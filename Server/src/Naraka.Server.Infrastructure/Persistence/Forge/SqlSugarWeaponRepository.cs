using Naraka.Server.Application.Forge;
using Naraka.Server.Application.Progression;
using Naraka.Server.Infrastructure.Persistence.Inventory;
using Naraka.Server.Infrastructure.Persistence.Lobby;
using Naraka.Server.Infrastructure.Persistence.Progression;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Forge;

[SugarTable("account_weapons")]
internal sealed class AccountWeaponRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "weapon_id", IsPrimaryKey = true, Length = 64)]
    public string WeaponId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "level")]
    public int Level { get; set; }

    [SugarColumn(ColumnName = "proficiency")]
    public long Proficiency { get; set; }

    [SugarColumn(ColumnName = "kill_count")]
    public long KillCount { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// 武器与强化的 MySQL 实现。
///
/// 整次强化——材料扣减、货币扣费、货币流水与等级 +1——在同一个事务里完成。
/// 材料不足时整体回滚，玩家不会被扣掉一半材料。
/// </summary>
public sealed class SqlSugarWeaponRepository(SqlSugarClientFactory factory) : IWeaponRepository
{
    public async Task<IReadOnlyList<AccountWeapon>> ListAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<AccountWeapon>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var rows = await database.Queryable<AccountWeaponRow>()
                .Where(row => row.AccountId == accountId)
                .ToListAsync();

            return rows
                .Select(row => new AccountWeapon(row.WeaponId, row.Level, row.Proficiency, row.KillCount))
                .OrderBy(weapon => weapon.WeaponId, StringComparer.Ordinal)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ForgeStorageException("Weapon read failed.", exception);
        }
    }

    public async Task EnsureWeaponsAsync(
        long accountId,
        IReadOnlyList<string> weaponIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(weaponIds);
        if (accountId <= 0 || weaponIds.Count == 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var existing = (await database.Queryable<AccountWeaponRow>()
                    .Where(row => row.AccountId == accountId)
                    .ToListAsync())
                .Select(row => row.WeaponId)
                .ToHashSet(StringComparer.Ordinal);

            var now = DateTime.UtcNow;
            var missing = weaponIds
                .Where(weaponId => !existing.Contains(weaponId))
                .Select(weaponId => new AccountWeaponRow
                {
                    AccountId = accountId,
                    WeaponId = weaponId,
                    Level = 1,
                    Proficiency = 0,
                    KillCount = 0,
                    CreatedUtc = now,
                    UpdatedUtc = now
                })
                .ToArray();

            if (missing.Length > 0)
            {
                // 只插入缺失的行。已有行绝不改写，否则重复调用会把强化进度重置成 1 级。
                await database.Insertable(missing).ExecuteCommandAsync();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ForgeStorageException("Weapon provisioning failed.", exception);
        }
    }

    public async Task<ForgeUpgradeResult> TryUpgradeAsync(
        ForgeUpgradeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var alreadyApplied = await database.Queryable<CurrencyLedgerRow>()
                .Where(row => row.AccountId == command.AccountId &&
                              row.Reason == CurrencyLedgerReason.Forge &&
                              row.ReferenceId == command.RequestId)
                .AnyAsync();

            var weapon = await database.Queryable<AccountWeaponRow>()
                .Where(row => row.AccountId == command.AccountId && row.WeaponId == command.WeaponId)
                .SingleAsync();

            if (weapon is null)
            {
                database.Ado.RollbackTran();
                return new ForgeUpgradeResult(ForgeUpgradeOutcome.AccountMissing, 0);
            }

            if (alreadyApplied)
            {
                database.Ado.CommitTran();
                return new ForgeUpgradeResult(ForgeUpgradeOutcome.AlreadyApplied, weapon.Level);
            }

            if (weapon.Level != command.FromLevel)
            {
                database.Ado.RollbackTran();
                return new ForgeUpgradeResult(ForgeUpgradeOutcome.LevelMismatch, weapon.Level);
            }

            var progression = await database.Queryable<AccountProgressionRow>()
                .Where(row => row.AccountId == command.AccountId)
                .SingleAsync();

            if (progression is null)
            {
                database.Ado.RollbackTran();
                return new ForgeUpgradeResult(ForgeUpgradeOutcome.AccountMissing, weapon.Level);
            }

            if (!TrySpend(progression, command.CurrencyId, command.CurrencyAmount, out var balanceAfter))
            {
                database.Ado.RollbackTran();
                return new ForgeUpgradeResult(ForgeUpgradeOutcome.InsufficientCurrency, weapon.Level);
            }

            var inventoryRows = await database.Queryable<AccountInventoryRow>()
                .Where(row => row.AccountId == command.AccountId)
                .ToListAsync();

            var byItem = inventoryRows.ToDictionary(row => row.ItemId, StringComparer.Ordinal);
            var updates = new List<AccountInventoryRow>();
            var deletes = new List<AccountInventoryRow>();

            foreach (var cost in command.MaterialCosts)
            {
                if (!byItem.TryGetValue(cost.ItemId, out var slot) || slot.Quantity + cost.Amount < 0)
                {
                    // 材料不足：整体回滚，货币也不会被扣。
                    database.Ado.RollbackTran();
                    return new ForgeUpgradeResult(ForgeUpgradeOutcome.InsufficientItems, weapon.Level);
                }

                slot.Quantity += cost.Amount;
                if (slot.Quantity == 0)
                {
                    deletes.Add(slot);
                }
                else
                {
                    updates.Add(slot);
                }
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
                Delta = -command.CurrencyAmount,
                BalanceAfter = balanceAfter,
                Reason = CurrencyLedgerReason.Forge,
                ReferenceId = command.RequestId,
                CreatedUtc = now
            }).ExecuteCommandAsync();

            foreach (var slot in updates)
            {
                slot.UpdatedUtc = now;
                await database.Updateable(slot)
                    .UpdateColumns(row => new { row.Quantity, row.UpdatedUtc })
                    .ExecuteCommandAsync();
            }

            foreach (var slot in deletes)
            {
                await database.Deleteable<AccountInventoryRow>()
                    .Where(row => row.AccountId == command.AccountId && row.ItemId == slot.ItemId)
                    .ExecuteCommandAsync();
            }

            weapon.Level = command.ToLevel;
            weapon.UpdatedUtc = now;
            await database.Updateable(weapon)
                .UpdateColumns(row => new { row.Level, row.UpdatedUtc })
                .ExecuteCommandAsync();

            database.Ado.CommitTran();
            return new ForgeUpgradeResult(ForgeUpgradeOutcome.Applied, weapon.Level);
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ForgeStorageException("Weapon upgrade failed.", exception);
        }
    }

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
