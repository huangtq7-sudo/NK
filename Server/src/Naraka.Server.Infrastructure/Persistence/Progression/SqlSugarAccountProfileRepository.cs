using Naraka.Server.Application.Progression;
using Naraka.Server.Infrastructure.Persistence.Lobby;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Progression;

/// <summary>
/// 账号资料与初始赠送的 MySQL 实现。
///
/// 余额仍然保存在 P1.1-A 建立的 <c>account_progression</c> 三个列上，因此本轮不需要迁移既有数据；
/// <see cref="SupportedCurrencies"/> 明确记录了这一限制，配置里出现第四种货币时会被显式拒绝，
/// 而不是悄悄丢弃它的余额。
/// </summary>
public sealed class SqlSugarAccountProfileRepository(SqlSugarClientFactory factory) : IAccountProfileRepository
{
    /// <summary>数据库当前能保存余额的货币。与 currencies.csv 的集合必须完全一致。</summary>
    public static readonly string[] SupportedCurrencies = { "Copper", "Silk", "Gold" };

    public async Task<AccountProfileRecord?> FindProfileAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var row = await database.Queryable<AccountProfileRow>()
                .Where(profile => profile.AccountId == accountId)
                .SingleAsync();
            cancellationToken.ThrowIfCancellationRequested();

            return row is null ? null : ToRecord(row);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AccountProfileStorageException("Account profile read failed.", exception);
        }
    }

    public async Task<CurrencyBalances?> FindBalancesAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var row = await database.Queryable<AccountProgressionRow>()
                .Where(progression => progression.AccountId == accountId)
                .SingleAsync();
            cancellationToken.ThrowIfCancellationRequested();

            return row is null ? null : ToBalances(row);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AccountProfileStorageException("Currency balance read failed.", exception);
        }
    }

    /// <summary>
    /// 开通账号并在需要时发放初始赠送。
    ///
    /// 幂等的落点是 <c>account_grants</c> 的主键：赠送记录、余额增加与货币流水在同一个事务里提交，
    /// 因此并发的第二次调用要么看到已存在的赠送记录而跳过，要么在插入时撞主键并整体回滚。
    /// 余额是<b>累加</b>而不是覆盖，既有账号的资产不会被重置成 1000。
    /// </summary>
    public async Task<AccountProvisionResult> ProvisionAsync(
        long accountId,
        AccountProvisionDefaults defaults,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        if (accountId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }

        RequireSupportedCurrencies(defaults.StarterGrants);
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var now = DateTime.UtcNow;
            var profileCreated = false;
            var profile = await database.Queryable<AccountProfileRow>()
                .Where(row => row.AccountId == accountId)
                .SingleAsync();

            if (profile is null)
            {
                profile = new AccountProfileRow
                {
                    AccountId = accountId,
                    AvatarId = defaults.AvatarId,
                    AvatarFrameId = defaults.AvatarFrameId,
                    SelectedHeroId = defaults.HeroId,
                    SelectedWeaponId = defaults.WeaponId,
                    SelectedPetId = defaults.PetId,
                    AccountXp = 0,
                    InventoryTier = 0,
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                await database.Insertable(profile).ExecuteCommandAsync();
                profileCreated = true;
            }

            var progression = await database.Queryable<AccountProgressionRow>()
                .Where(row => row.AccountId == accountId)
                .SingleAsync()
                ?? throw new AccountProfileStorageException(
                    "Account has no progression row; migration 0002 backfill is missing.");

            var alreadyGranted = await database.Queryable<AccountGrantRow>()
                .Where(row => row.AccountId == accountId && row.GrantKey == AccountGrantKey.StarterGrant)
                .AnyAsync();

            var starterGrantApplied = false;
            if (!alreadyGranted && defaults.StarterGrants.Count > 0)
            {
                await database.Insertable(new AccountGrantRow
                {
                    AccountId = accountId,
                    GrantKey = AccountGrantKey.StarterGrant,
                    GrantedUtc = now
                }).ExecuteCommandAsync();

                var ledger = new List<CurrencyLedgerRow>(defaults.StarterGrants.Count);
                foreach (var grant in defaults.StarterGrants)
                {
                    var balanceAfter = AddBalance(progression, grant.CurrencyId, grant.Amount);
                    ledger.Add(new CurrencyLedgerRow
                    {
                        AccountId = accountId,
                        CurrencyId = grant.CurrencyId,
                        Delta = grant.Amount,
                        BalanceAfter = balanceAfter,
                        Reason = CurrencyLedgerReason.StarterGrant,
                        ReferenceId = AccountGrantKey.StarterGrant,
                        CreatedUtc = now
                    });
                }

                progression.UpdatedUtc = now;
                await database.Updateable(progression)
                    .UpdateColumns(row => new { row.Copper, row.Silk, row.Gold, row.UpdatedUtc })
                    .ExecuteCommandAsync();
                await database.Insertable(ledger).ExecuteCommandAsync();
                starterGrantApplied = true;
            }

            database.Ado.CommitTran();
            cancellationToken.ThrowIfCancellationRequested();

            return new AccountProvisionResult(
                ToRecord(profile),
                ToBalances(progression),
                profileCreated,
                starterGrantApplied);
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (AccountProfileStorageException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new AccountProfileStorageException("Account provisioning failed.", exception);
        }
    }

    public async Task<bool> UpdateAppearanceAsync(
        long accountId,
        string avatarId,
        string avatarFrameId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(avatarId);
        ArgumentException.ThrowIfNullOrWhiteSpace(avatarFrameId);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var now = DateTime.UtcNow;
            var affected = await database.Updateable<AccountProfileRow>()
                .SetColumns(row => new AccountProfileRow
                {
                    AvatarId = avatarId,
                    AvatarFrameId = avatarFrameId,
                    UpdatedUtc = now
                })
                .Where(row => row.AccountId == accountId)
                .ExecuteCommandAsync();

            return affected > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AccountProfileStorageException("Account appearance update failed.", exception);
        }
    }

    /// <summary>
    /// 比较并交换仓库容量档位。<c>WHERE inventory_tier = expectedTier</c> 让并发扩容中
    /// 只有一个请求能推进档位，另一个得到 0 行影响并按已扣费的事实返回当前状态。
    /// </summary>
    public async Task<bool> TryAdvanceInventoryTierAsync(
        long accountId,
        int expectedTier,
        int nextTier,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || nextTier <= expectedTier)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var now = DateTime.UtcNow;
            var affected = await database.Updateable<AccountProfileRow>()
                .SetColumns(row => new AccountProfileRow
                {
                    InventoryTier = nextTier,
                    UpdatedUtc = now
                })
                .Where(row => row.AccountId == accountId && row.InventoryTier == expectedTier)
                .ExecuteCommandAsync();

            return affected > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AccountProfileStorageException("Inventory tier update failed.", exception);
        }
    }

    public async Task<bool> UpdateLoadoutAsync(
        long accountId,
        string heroId,
        string weaponId,
        string petId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(heroId);
        ArgumentException.ThrowIfNullOrWhiteSpace(weaponId);
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var now = DateTime.UtcNow;
            var affected = await database.Updateable<AccountProfileRow>()
                .SetColumns(row => new AccountProfileRow
                {
                    SelectedHeroId = heroId,
                    SelectedWeaponId = weaponId,
                    SelectedPetId = petId,
                    UpdatedUtc = now
                })
                .Where(row => row.AccountId == accountId)
                .ExecuteCommandAsync();

            return affected > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AccountProfileStorageException("Account loadout update failed.", exception);
        }
    }

    /// <summary>
    /// 余额列是固定的三列，因此配置里出现未知货币必须立刻失败。
    /// 静默忽略会让玩家看到"赠送成功"却查不到余额。
    /// </summary>
    private static void RequireSupportedCurrencies(IReadOnlyList<CurrencyGrant> grants)
    {
        foreach (var grant in grants)
        {
            if (Array.IndexOf(SupportedCurrencies, grant.CurrencyId) < 0)
            {
                throw new AccountProfileStorageException(
                    $"Currency '{grant.CurrencyId}' has no balance column; a schema migration is required.");
            }

            if (grant.Amount < 0)
            {
                throw new AccountProfileStorageException(
                    $"Starter grant for '{grant.CurrencyId}' must not be negative.");
            }
        }
    }

    private static long AddBalance(AccountProgressionRow row, string currencyId, long amount)
    {
        switch (currencyId)
        {
            case "Copper":
                row.Copper = checked(row.Copper + amount);
                return row.Copper;
            case "Silk":
                row.Silk = checked(row.Silk + amount);
                return row.Silk;
            case "Gold":
                row.Gold = checked(row.Gold + amount);
                return row.Gold;
            default:
                throw new AccountProfileStorageException($"Currency '{currencyId}' has no balance column.");
        }
    }

    private static AccountProfileRecord ToRecord(AccountProfileRow row) => new(
        row.AvatarId,
        row.AvatarFrameId,
        row.SelectedHeroId,
        row.SelectedWeaponId,
        row.SelectedPetId,
        row.AccountXp,
        row.InventoryTier);

    private static CurrencyBalances ToBalances(AccountProgressionRow row) => new(
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Copper"] = row.Copper,
            ["Silk"] = row.Silk,
            ["Gold"] = row.Gold
        });

    /// <summary>回滚本身失败不能掩盖原始异常，否则真正的故障原因会永远丢失。</summary>
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
