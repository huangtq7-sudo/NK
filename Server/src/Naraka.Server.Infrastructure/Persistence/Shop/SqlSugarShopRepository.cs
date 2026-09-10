using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Shop;
using Naraka.Server.Infrastructure.Persistence.Inventory;
using Naraka.Server.Infrastructure.Persistence.Lobby;
using Naraka.Server.Infrastructure.Persistence.Progression;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Shop;

[SugarTable("account_shop_purchases")]
internal sealed class AccountShopPurchaseRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "product_id", IsPrimaryKey = true, Length = 64)]
    public string ProductId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "purchased_total")]
    public long PurchasedTotal { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// 商店购买的 MySQL 实现。
///
/// 整笔购买——限购检查、扣费、货币流水、物品入库与计数递增——在同一个事务里完成。
/// 任何一步失败都整体回滚，因此不可能出现"扣了钱没发货"或"发了货没扣钱"。
/// </summary>
public sealed class SqlSugarShopRepository(SqlSugarClientFactory factory) : IShopRepository
{
    public async Task<IReadOnlyList<ShopPurchaseCount>> ListPurchasesAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<ShopPurchaseCount>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var rows = await database.Queryable<AccountShopPurchaseRow>()
                .Where(row => row.AccountId == accountId)
                .ToListAsync();

            return rows
                .Select(row => new ShopPurchaseCount(row.ProductId, row.PurchasedTotal))
                .OrderBy(count => count.ProductId, StringComparer.Ordinal)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ShopStorageException("Shop purchase read failed.", exception);
        }
    }

    public async Task<ShopPurchaseResult> TryPurchaseAsync(
        ShopPurchaseCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            // 幂等首先判定：同一个 RequestId 已经记过账就直接重放，绝不再扣一次费。
            var alreadyApplied = await database.Queryable<CurrencyLedgerRow>()
                .Where(row => row.AccountId == command.AccountId &&
                              row.Reason == CurrencyLedgerReason.ShopPurchase &&
                              row.ReferenceId == command.RequestId)
                .AnyAsync();

            var purchase = await database.Queryable<AccountShopPurchaseRow>()
                .Where(row => row.AccountId == command.AccountId && row.ProductId == command.ProductId)
                .SingleAsync();

            if (alreadyApplied)
            {
                var replayBalances = await ReadBalancesAsync(database, command.AccountId);
                database.Ado.CommitTran();
                return new ShopPurchaseResult(
                    ShopPurchaseOutcome.AlreadyApplied, replayBalances, purchase?.PurchasedTotal ?? 0);
            }

            var progression = await database.Queryable<AccountProgressionRow>()
                .Where(row => row.AccountId == command.AccountId)
                .SingleAsync();

            if (progression is null)
            {
                database.Ado.RollbackTran();
                return new ShopPurchaseResult(ShopPurchaseOutcome.AccountMissing, null, 0);
            }

            var purchasedTotal = purchase?.PurchasedTotal ?? 0;
            if (command.PurchaseLimit > 0 && purchasedTotal + command.Quantity > command.PurchaseLimit)
            {
                database.Ado.RollbackTran();
                return new ShopPurchaseResult(ShopPurchaseOutcome.LimitReached, null, purchasedTotal);
            }

            if (!TrySpend(progression, command.CurrencyId, command.TotalPrice, out var balanceAfter))
            {
                database.Ado.RollbackTran();
                return new ShopPurchaseResult(ShopPurchaseOutcome.InsufficientCurrency, null, purchasedTotal);
            }

            var inventoryRows = await database.Queryable<AccountInventoryRow>()
                .Where(row => row.AccountId == command.AccountId)
                .ToListAsync();

            var slot = inventoryRows.FirstOrDefault(row => row.ItemId == command.ItemId);
            var nextQuantity = (slot?.Quantity ?? 0) + command.Quantity;
            var occupiedAfter = slot is null ? inventoryRows.Count + 1 : inventoryRows.Count;

            if (nextQuantity > command.StackLimit || occupiedAfter > command.Capacity)
            {
                // 堆叠上限与容量都算"装不下"。整笔回滚，玩家不会被扣钱。
                database.Ado.RollbackTran();
                return new ShopPurchaseResult(ShopPurchaseOutcome.InventoryFull, null, purchasedTotal);
            }

            var now = DateTime.UtcNow;
            progression.UpdatedUtc = now;
            await database.Updateable(progression)
                .UpdateColumns(row => new { row.Copper, row.Silk, row.Gold, row.UpdatedUtc })
                .ExecuteCommandAsync();

            await database.Insertable(new CurrencyLedgerRow
            {
                AccountId = command.AccountId,
                CurrencyId = command.CurrencyId,
                Delta = -command.TotalPrice,
                BalanceAfter = balanceAfter,
                Reason = CurrencyLedgerReason.ShopPurchase,
                ReferenceId = command.RequestId,
                CreatedUtc = now
            }).ExecuteCommandAsync();

            if (slot is null)
            {
                await database.Insertable(new AccountInventoryRow
                {
                    AccountId = command.AccountId,
                    ItemId = command.ItemId,
                    Quantity = nextQuantity,
                    SlotIndex = inventoryRows.Count == 0 ? 0 : inventoryRows.Max(row => row.SlotIndex) + 1,
                    CreatedUtc = now,
                    UpdatedUtc = now
                }).ExecuteCommandAsync();
            }
            else
            {
                slot.Quantity = nextQuantity;
                slot.UpdatedUtc = now;
                await database.Updateable(slot)
                    .UpdateColumns(row => new { row.Quantity, row.UpdatedUtc })
                    .ExecuteCommandAsync();
            }

            var updatedTotal = purchasedTotal + command.Quantity;
            if (purchase is null)
            {
                await database.Insertable(new AccountShopPurchaseRow
                {
                    AccountId = command.AccountId,
                    ProductId = command.ProductId,
                    PurchasedTotal = updatedTotal,
                    CreatedUtc = now,
                    UpdatedUtc = now
                }).ExecuteCommandAsync();
            }
            else
            {
                purchase.PurchasedTotal = updatedTotal;
                purchase.UpdatedUtc = now;
                await database.Updateable(purchase)
                    .UpdateColumns(row => new { row.PurchasedTotal, row.UpdatedUtc })
                    .ExecuteCommandAsync();
            }

            database.Ado.CommitTran();
            return new ShopPurchaseResult(ShopPurchaseOutcome.Applied, ToBalances(progression), updatedTotal);
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ShopStorageException("Shop purchase failed.", exception);
        }
    }

    private static async Task<CurrencyBalances?> ReadBalancesAsync(ISqlSugarClient database, long accountId)
    {
        var row = await database.Queryable<AccountProgressionRow>()
            .Where(progression => progression.AccountId == accountId)
            .SingleAsync();
        return row is null ? null : ToBalances(row);
    }

    private static bool TrySpend(AccountProgressionRow row, string currencyId, long amount, out long balanceAfter)
    {
        balanceAfter = 0;
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

    private static CurrencyBalances ToBalances(AccountProgressionRow row) => new(
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Copper"] = row.Copper,
            ["Silk"] = row.Silk,
            ["Gold"] = row.Gold
        });

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
