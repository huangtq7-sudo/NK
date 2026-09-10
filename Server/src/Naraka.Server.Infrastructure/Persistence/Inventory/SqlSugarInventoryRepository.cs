using Naraka.Server.Application.Inventory;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Inventory;

[SugarTable("account_inventory")]
internal sealed class AccountInventoryRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "item_id", IsPrimaryKey = true, Length = 64)]
    public string ItemId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "quantity")]
    public long Quantity { get; set; }

    [SugarColumn(ColumnName = "slot_index")]
    public int SlotIndex { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

[SugarTable("account_equipment")]
internal sealed class AccountEquipmentRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "slot_kind", IsPrimaryKey = true, Length = 16)]
    public string SlotKind { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "slot_index", IsPrimaryKey = true)]
    public int SlotIndex { get; set; }

    [SugarColumn(ColumnName = "item_id", Length = 64)]
    public string ItemId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// 堆叠仓库的 MySQL 实现。
///
/// 每次写入都在一个事务里重新读取当前数量再计算结果，绝不信任调用方给出的旧值：
/// 两个并发请求各自基于陈旧读数写回，就会凭空造出或抹掉物品。
/// </summary>
public sealed class SqlSugarInventoryRepository(SqlSugarClientFactory factory) : IInventoryRepository
{
    public async Task<IReadOnlyList<InventorySlot>> ListSlotsAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<InventorySlot>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var rows = await database.Queryable<AccountInventoryRow>()
                .Where(row => row.AccountId == accountId)
                .OrderBy(row => row.SlotIndex)
                .ToListAsync();

            return rows.Select(row => new InventorySlot(row.ItemId, row.Quantity, row.SlotIndex)).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InventoryStorageException("Inventory read failed.", exception);
        }
    }

    public async Task<IReadOnlyList<EquipmentSlot>> ListEquipmentAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<EquipmentSlot>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var rows = await database.Queryable<AccountEquipmentRow>()
                .Where(row => row.AccountId == accountId)
                .ToListAsync();

            return rows
                .Where(row => !string.IsNullOrEmpty(row.ItemId))
                .Select(row => new EquipmentSlot(row.SlotKind, row.SlotIndex, row.ItemId))
                .OrderBy(slot => slot.SlotKind, StringComparer.Ordinal)
                .ThenBy(slot => slot.SlotIndex)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InventoryStorageException("Equipment read failed.", exception);
        }
    }

    public async Task<bool> TryApplyAsync(
        long accountId,
        IReadOnlyList<InventoryDelta> deltas,
        IReadOnlyDictionary<string, int> stackLimits,
        int capacity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        ArgumentNullException.ThrowIfNull(stackLimits);
        if (accountId <= 0 || deltas.Count == 0)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var rows = await database.Queryable<AccountInventoryRow>()
                .Where(row => row.AccountId == accountId)
                .ToListAsync();

            var byItem = rows.ToDictionary(row => row.ItemId, StringComparer.Ordinal);
            var now = DateTime.UtcNow;
            var nextSlot = rows.Count == 0 ? 0 : rows.Max(row => row.SlotIndex) + 1;
            var inserts = new List<AccountInventoryRow>();
            var updates = new List<AccountInventoryRow>();
            var deletes = new List<AccountInventoryRow>();

            // 合并同一物品的多条变动，避免同一格在一次事务里被写两次。
            foreach (var group in deltas.GroupBy(delta => delta.ItemId, StringComparer.Ordinal))
            {
                var itemId = group.Key;
                var amount = group.Sum(delta => delta.Amount);
                if (amount == 0)
                {
                    continue;
                }

                if (!stackLimits.TryGetValue(itemId, out var stackLimit) || stackLimit <= 0)
                {
                    database.Ado.RollbackTran();
                    return false;
                }

                byItem.TryGetValue(itemId, out var row);
                var current = row?.Quantity ?? 0L;
                var next = current + amount;

                if (next < 0 || next > stackLimit)
                {
                    // 数量不足或超过堆叠上限：整体拒绝，绝不写入一半。
                    database.Ado.RollbackTran();
                    return false;
                }

                if (row is null)
                {
                    inserts.Add(new AccountInventoryRow
                    {
                        AccountId = accountId,
                        ItemId = itemId,
                        Quantity = next,
                        SlotIndex = nextSlot++,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    });
                    continue;
                }

                row.Quantity = next;
                row.UpdatedUtc = now;
                if (next == 0)
                {
                    deletes.Add(row);
                }
                else
                {
                    updates.Add(row);
                }
            }

            // 容量按"占用的格子数"判定：一种物品占一格，数量归零的格子会被释放。
            var occupied = rows.Count - deletes.Count + inserts.Count;
            if (occupied > capacity)
            {
                database.Ado.RollbackTran();
                return false;
            }

            if (inserts.Count > 0)
            {
                await database.Insertable(inserts).ExecuteCommandAsync();
            }

            foreach (var row in updates)
            {
                await database.Updateable(row)
                    .UpdateColumns(target => new { target.Quantity, target.UpdatedUtc })
                    .ExecuteCommandAsync();
            }

            foreach (var row in deletes)
            {
                await database.Deleteable<AccountInventoryRow>()
                    .Where(target => target.AccountId == accountId && target.ItemId == row.ItemId)
                    .ExecuteCommandAsync();
            }

            database.Ado.CommitTran();
            return true;
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new InventoryStorageException("Inventory write failed.", exception);
        }
    }

    public async Task<bool> ReorderAsync(
        long accountId,
        IReadOnlyList<string> itemIdsInSlotOrder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemIdsInSlotOrder);
        if (accountId <= 0)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var rows = await database.Queryable<AccountInventoryRow>()
                .Where(row => row.AccountId == accountId)
                .ToListAsync();

            // 请求的排列必须与当前持有的物品集合完全一致，否则重排会静默丢格。
            if (rows.Count != itemIdsInSlotOrder.Count)
            {
                database.Ado.RollbackTran();
                return false;
            }

            var byItem = rows.ToDictionary(row => row.ItemId, StringComparer.Ordinal);
            var now = DateTime.UtcNow;
            for (var index = 0; index < itemIdsInSlotOrder.Count; index++)
            {
                if (!byItem.TryGetValue(itemIdsInSlotOrder[index], out var row))
                {
                    database.Ado.RollbackTran();
                    return false;
                }

                row.SlotIndex = index;
                row.UpdatedUtc = now;
                await database.Updateable(row)
                    .UpdateColumns(target => new { target.SlotIndex, target.UpdatedUtc })
                    .ExecuteCommandAsync();
            }

            database.Ado.CommitTran();
            return true;
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new InventoryStorageException("Inventory reorder failed.", exception);
        }
    }

    public async Task<bool> SetEquipmentAsync(
        long accountId,
        string slotKind,
        int slotIndex,
        string? itemId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotKind);
        if (accountId <= 0)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            if (string.IsNullOrWhiteSpace(itemId))
            {
                await database.Deleteable<AccountEquipmentRow>()
                    .Where(row => row.AccountId == accountId &&
                                  row.SlotKind == slotKind &&
                                  row.SlotIndex == slotIndex)
                    .ExecuteCommandAsync();
                return true;
            }

            var row = new AccountEquipmentRow
            {
                AccountId = accountId,
                SlotKind = slotKind,
                SlotIndex = slotIndex,
                ItemId = itemId,
                UpdatedUtc = DateTime.UtcNow
            };

            // 主键冲突时更新同一格，因此换装不需要先删后插这种会留下空窗的两步操作。
            await database.Storageable(row).ExecuteCommandAsync();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InventoryStorageException("Equipment write failed.", exception);
        }
    }

    private static void SafeRollback(ISqlSugarClient database)
    {
        try
        {
            database.Ado.RollbackTran();
        }
        catch (Exception)
        {
            // 连接已经断开时回滚必然失败；事务会由服务器自行终止。
        }
    }
}
