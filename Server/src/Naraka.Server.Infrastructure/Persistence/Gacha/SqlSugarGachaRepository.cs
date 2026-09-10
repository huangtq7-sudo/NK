using Naraka.Server.Application.Gacha;
using Naraka.Server.Application.Progression;
using Naraka.Server.Infrastructure.Persistence.Inventory;
using Naraka.Server.Infrastructure.Persistence.Lobby;
using Naraka.Server.Infrastructure.Persistence.Progression;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Gacha;

[SugarTable("account_gacha")]
internal sealed class AccountGachaRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "pool_id", IsPrimaryKey = true, Length = 64)]
    public string PoolId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "pity_counter")]
    public int PityCounter { get; set; }

    [SugarColumn(ColumnName = "total_pulls")]
    public long TotalPulls { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

[SugarTable("gacha_orders")]
internal sealed class GachaOrderRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "order_id", IsPrimaryKey = true, Length = 64)]
    public string OrderId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "pool_id", Length = 64)]
    public string PoolId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "pull_count")]
    public int PullCount { get; set; }

    [SugarColumn(ColumnName = "currency_id", Length = 32)]
    public string CurrencyId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "price")]
    public long Price { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    /// <summary>null 表示客户端尚未确认展示。断线恢复正是靠它。</summary>
    [SugarColumn(ColumnName = "shown_utc", IsNullable = true)]
    public DateTime? ShownUtc { get; set; }
}

[SugarTable("gacha_order_results")]
internal sealed class GachaOrderResultRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "order_id", IsPrimaryKey = true, Length = 64)]
    public string OrderId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "sequence", IsPrimaryKey = true)]
    public int Sequence { get; set; }

    [SugarColumn(ColumnName = "reward_id", Length = 64)]
    public string RewardId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "item_id", Length = 64)]
    public string ItemId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "amount")]
    public int Amount { get; set; }

    [SugarColumn(ColumnName = "quality", Length = 16)]
    public string Quality { get; set; } = string.Empty;
}

/// <summary>
/// 抽奖的 MySQL 实现。
///
/// 一次抽奖的全部写入——扣费、货币流水、奖励入库、订单与结果固化、保底计数——都在同一个事务里。
/// 因此"客户端看到的结果"与"数据库里的结果"永远是同一份，动画中断也不会造成任何差异。
/// </summary>
public sealed class SqlSugarGachaRepository(SqlSugarClientFactory factory) : IGachaRepository
{
    public async Task<GachaAccountState?> FindStateAsync(
        long accountId,
        string poolId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || string.IsNullOrWhiteSpace(poolId))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var row = await database.Queryable<AccountGachaRow>()
                .Where(entry => entry.AccountId == accountId && entry.PoolId == poolId)
                .SingleAsync();

            return row is null ? null : new GachaAccountState(row.PoolId, row.PityCounter, row.TotalPulls);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GachaStorageException("Gacha state read failed.", exception);
        }
    }

    public async Task<IReadOnlyList<GachaOrder>> ListUnshownOrdersAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<GachaOrder>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var orders = await database.Queryable<GachaOrderRow>()
                .Where(row => row.AccountId == accountId && row.ShownUtc == null)
                .OrderBy(row => row.CreatedUtc)
                .ToListAsync();

            if (orders.Count == 0)
            {
                return Array.Empty<GachaOrder>();
            }

            var orderIds = orders.Select(order => order.OrderId).ToArray();
            var results = await database.Queryable<GachaOrderResultRow>()
                .Where(row => row.AccountId == accountId && orderIds.Contains(row.OrderId))
                .ToListAsync();

            var byOrder = results
                .GroupBy(row => row.OrderId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(row => row.Sequence).ToArray(),
                    StringComparer.Ordinal);

            return orders
                .Select(order => new GachaOrder(
                    order.OrderId,
                    order.PoolId,
                    order.PullCount,
                    false,
                    byOrder.TryGetValue(order.OrderId, out var rows)
                        ? rows.Select(ToReward).ToArray()
                        : Array.Empty<GachaRolledReward>()))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GachaStorageException("Gacha order read failed.", exception);
        }
    }

    public async Task<GachaPullResult> TryPullAsync(
        GachaPullCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            // 幂等：同一个 OrderId 已经存在就直接重放，绝不再抽一次也不再扣一次费。
            var existing = await database.Queryable<GachaOrderRow>()
                .Where(row => row.AccountId == command.AccountId && row.OrderId == command.OrderId)
                .SingleAsync();

            if (existing is not null)
            {
                var replayResults = await database.Queryable<GachaOrderResultRow>()
                    .Where(row => row.AccountId == command.AccountId && row.OrderId == command.OrderId)
                    .OrderBy(row => row.Sequence)
                    .ToListAsync();
                database.Ado.CommitTran();

                return new GachaPullResult(
                    GachaPullOutcome.AlreadyApplied,
                    new GachaOrder(
                        existing.OrderId,
                        existing.PoolId,
                        existing.PullCount,
                        existing.ShownUtc is not null,
                        replayResults.Select(ToReward).ToArray()));
            }

            var progression = await database.Queryable<AccountProgressionRow>()
                .Where(row => row.AccountId == command.AccountId)
                .SingleAsync();

            if (progression is null)
            {
                database.Ado.RollbackTran();
                return new GachaPullResult(GachaPullOutcome.AccountMissing, null);
            }

            if (!TrySpend(progression, command.CurrencyId, command.Price, out var balanceAfter))
            {
                database.Ado.RollbackTran();
                return new GachaPullResult(GachaPullOutcome.InsufficientCurrency, null);
            }

            var inventoryRows = await database.Queryable<AccountInventoryRow>()
                .Where(row => row.AccountId == command.AccountId)
                .ToListAsync();

            var byItem = inventoryRows.ToDictionary(row => row.ItemId, StringComparer.Ordinal);
            var now = DateTime.UtcNow;
            var nextSlot = inventoryRows.Count == 0 ? 0 : inventoryRows.Max(row => row.SlotIndex) + 1;
            var inserts = new List<AccountInventoryRow>();
            var updates = new List<AccountInventoryRow>();

            // 同一次十连可能抽到同一个物品多次；先合并再判定上限，避免逐个判定时错误地放行。
            foreach (var group in command.Rewards.GroupBy(reward => reward.ItemId, StringComparer.Ordinal))
            {
                var amount = group.Sum(reward => (long)reward.Amount);
                if (!command.StackLimits.TryGetValue(group.Key, out var stackLimit) || stackLimit <= 0)
                {
                    database.Ado.RollbackTran();
                    return new GachaPullResult(GachaPullOutcome.InventoryFull, null);
                }

                byItem.TryGetValue(group.Key, out var slot);
                var next = (slot?.Quantity ?? 0) + amount;
                if (next > stackLimit)
                {
                    database.Ado.RollbackTran();
                    return new GachaPullResult(GachaPullOutcome.InventoryFull, null);
                }

                if (slot is null)
                {
                    inserts.Add(new AccountInventoryRow
                    {
                        AccountId = command.AccountId,
                        ItemId = group.Key,
                        Quantity = next,
                        SlotIndex = nextSlot++,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    });
                }
                else
                {
                    slot.Quantity = next;
                    slot.UpdatedUtc = now;
                    updates.Add(slot);
                }
            }

            if (inventoryRows.Count + inserts.Count > command.Capacity)
            {
                database.Ado.RollbackTran();
                return new GachaPullResult(GachaPullOutcome.InventoryFull, null);
            }

            progression.UpdatedUtc = now;
            await database.Updateable(progression)
                .UpdateColumns(row => new { row.Copper, row.Silk, row.Gold, row.UpdatedUtc })
                .ExecuteCommandAsync();

            await database.Insertable(new CurrencyLedgerRow
            {
                AccountId = command.AccountId,
                CurrencyId = command.CurrencyId,
                Delta = -command.Price,
                BalanceAfter = balanceAfter,
                Reason = CurrencyLedgerReason.Gacha,
                ReferenceId = command.OrderId,
                CreatedUtc = now
            }).ExecuteCommandAsync();

            if (inserts.Count > 0)
            {
                await database.Insertable(inserts).ExecuteCommandAsync();
            }

            foreach (var slot in updates)
            {
                await database.Updateable(slot)
                    .UpdateColumns(row => new { row.Quantity, row.UpdatedUtc })
                    .ExecuteCommandAsync();
            }

            await database.Insertable(new GachaOrderRow
            {
                AccountId = command.AccountId,
                OrderId = command.OrderId,
                PoolId = command.PoolId,
                PullCount = command.PullCount,
                CurrencyId = command.CurrencyId,
                Price = command.Price,
                CreatedUtc = now,
                ShownUtc = null
            }).ExecuteCommandAsync();

            var resultRows = command.Rewards
                .Select((reward, index) => new GachaOrderResultRow
                {
                    AccountId = command.AccountId,
                    OrderId = command.OrderId,
                    Sequence = index,
                    RewardId = reward.RewardId,
                    ItemId = reward.ItemId,
                    Amount = reward.Amount,
                    Quality = reward.Quality
                })
                .ToArray();
            await database.Insertable(resultRows).ExecuteCommandAsync();

            var state = await database.Queryable<AccountGachaRow>()
                .Where(row => row.AccountId == command.AccountId && row.PoolId == command.PoolId)
                .SingleAsync();

            if (state is null)
            {
                await database.Insertable(new AccountGachaRow
                {
                    AccountId = command.AccountId,
                    PoolId = command.PoolId,
                    PityCounter = command.PityCounterAfter,
                    TotalPulls = command.PullCount,
                    CreatedUtc = now,
                    UpdatedUtc = now
                }).ExecuteCommandAsync();
            }
            else
            {
                state.PityCounter = command.PityCounterAfter;
                state.TotalPulls += command.PullCount;
                state.UpdatedUtc = now;
                await database.Updateable(state)
                    .UpdateColumns(row => new { row.PityCounter, row.TotalPulls, row.UpdatedUtc })
                    .ExecuteCommandAsync();
            }

            database.Ado.CommitTran();
            return new GachaPullResult(
                GachaPullOutcome.Applied,
                new GachaOrder(command.OrderId, command.PoolId, command.PullCount, false, command.Rewards));
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new GachaStorageException("Gacha pull failed.", exception);
        }
    }

    public async Task<bool> MarkShownAsync(long accountId, string orderId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        if (accountId <= 0)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var now = DateTime.UtcNow;
            // 只更新尚未标记的订单，重复确认因此不会改写第一次的展示时间。
            var affected = await database.Updateable<GachaOrderRow>()
                .SetColumns(row => new GachaOrderRow { ShownUtc = now })
                .Where(row => row.AccountId == accountId && row.OrderId == orderId && row.ShownUtc == null)
                .ExecuteCommandAsync();

            return affected > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GachaStorageException("Gacha order acknowledgement failed.", exception);
        }
    }

    private static GachaRolledReward ToReward(GachaOrderResultRow row) =>
        new(row.RewardId, row.ItemId, row.Amount, row.Quality);

    private static bool TrySpend(AccountProgressionRow row, string currencyId, long amount, out long balanceAfter)
    {
        balanceAfter = 0;
        if (amount < 0)
        {
            return false;
        }

        switch (currencyId)
        {
            case "Copper" when row.Copper >= amount:
                row.Copper -= amount;
                balanceAfter = row.Copper;
                return true;
            case "Silk" when row.Silk >= amount:
                row.Silk -= amount;
                balanceAfter = row.Silk;
                return true;
            case "Gold" when row.Gold >= amount:
                row.Gold -= amount;
                balanceAfter = row.Gold;
                return true;
            default:
                return false;
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
