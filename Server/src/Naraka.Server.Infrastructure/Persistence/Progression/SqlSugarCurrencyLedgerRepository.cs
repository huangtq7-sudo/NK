using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;
using Naraka.Server.Infrastructure.Persistence.Lobby;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Progression;

/// <summary>
/// 货币变动与不可变流水的原子写入。
///
/// 所有经济操作都必须经过这里，因此"余额变化必然伴随一条流水"是结构性保证。
/// 幂等落在 <c>uq_currency_ledger_reference</c> 唯一键上：同一个 (账号, 货币, 原因, 引用号)
/// 只能写入一次，重复提交在数据库层被拒绝并整体回滚，不会重复扣费或重复发放。
/// </summary>
public sealed class SqlSugarCurrencyLedgerRepository(SqlSugarClientFactory factory) : ICurrencyLedgerRepository
{
    public async Task<CurrencyWriteResult> TryApplyAsync(
        long accountId,
        IReadOnlyList<CurrencyGrant> deltas,
        string reason,
        string referenceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);
        if (accountId <= 0)
        {
            return new CurrencyWriteResult(false, false, null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            // 先看这笔引用号是否已经记过账。命中即为重复请求：返回当前余额，不再写任何东西。
            var alreadyApplied = await database.Queryable<CurrencyLedgerRow>()
                .Where(row => row.AccountId == accountId &&
                              row.Reason == reason &&
                              row.ReferenceId == referenceId)
                .AnyAsync();

            var progression = await database.Queryable<AccountProgressionRow>()
                .Where(row => row.AccountId == accountId)
                .SingleAsync();

            if (progression is null)
            {
                database.Ado.RollbackTran();
                return new CurrencyWriteResult(false, false, null);
            }

            if (alreadyApplied)
            {
                database.Ado.CommitTran();
                return new CurrencyWriteResult(false, true, ToBalances(progression));
            }

            var now = DateTime.UtcNow;
            var ledger = new List<CurrencyLedgerRow>(deltas.Count);
            foreach (var group in deltas.GroupBy(delta => delta.CurrencyId, StringComparer.Ordinal))
            {
                var amount = group.Sum(delta => delta.Amount);
                if (amount == 0)
                {
                    continue;
                }

                if (!TryAdjust(progression, group.Key, amount, out var balanceAfter))
                {
                    // 余额不足或货币未知：整笔回滚，绝不写入一半。
                    database.Ado.RollbackTran();
                    return new CurrencyWriteResult(false, false, null);
                }

                ledger.Add(new CurrencyLedgerRow
                {
                    AccountId = accountId,
                    CurrencyId = group.Key,
                    Delta = amount,
                    BalanceAfter = balanceAfter,
                    Reason = reason,
                    ReferenceId = referenceId,
                    CreatedUtc = now
                });
            }

            if (ledger.Count == 0)
            {
                database.Ado.CommitTran();
                return new CurrencyWriteResult(true, false, ToBalances(progression));
            }

            progression.UpdatedUtc = now;
            await database.Updateable(progression)
                .UpdateColumns(row => new { row.Copper, row.Silk, row.Gold, row.UpdatedUtc })
                .ExecuteCommandAsync();
            await database.Insertable(ledger).ExecuteCommandAsync();

            database.Ado.CommitTran();
            return new CurrencyWriteResult(true, false, ToBalances(progression));
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new InventoryStorageException("Currency write failed.", exception);
        }
    }

    /// <summary>
    /// 余额列固定为三种货币。未知货币立即失败，而不是静默丢弃一次扣费或发放。
    /// </summary>
    private static bool TryAdjust(AccountProgressionRow row, string currencyId, long amount, out long balanceAfter)
    {
        balanceAfter = 0;
        switch (currencyId)
        {
            case "Copper":
                if (row.Copper + amount < 0)
                {
                    return false;
                }

                row.Copper = checked(row.Copper + amount);
                balanceAfter = row.Copper;
                return true;

            case "Silk":
                if (row.Silk + amount < 0)
                {
                    return false;
                }

                row.Silk = checked(row.Silk + amount);
                balanceAfter = row.Silk;
                return true;

            case "Gold":
                if (row.Gold + amount < 0)
                {
                    return false;
                }

                row.Gold = checked(row.Gold + amount);
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
