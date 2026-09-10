using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;
using Naraka.Server.Infrastructure.Persistence.Inventory;
using Naraka.Server.Infrastructure.Persistence.Lobby;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Progression;

[SugarTable("account_signin")]
internal sealed class AccountSignInRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "cycle_start_day")]
    public long CycleStartDay { get; set; }

    [SugarColumn(ColumnName = "consecutive_days")]
    public int ConsecutiveDays { get; set; }

    [SugarColumn(ColumnName = "last_claim_day")]
    public long LastClaimDay { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

[SugarTable("account_signin_claims")]
internal sealed class AccountSignInClaimRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "cycle_start_day", IsPrimaryKey = true)]
    public long CycleStartDay { get; set; }

    [SugarColumn(ColumnName = "day_index", IsPrimaryKey = true)]
    public int DayIndex { get; set; }

    [SugarColumn(ColumnName = "is_makeup")]
    public int IsMakeup { get; set; }

    [SugarColumn(ColumnName = "claimed_utc")]
    public DateTime ClaimedUtc { get; set; }
}

[SugarTable("account_reward_claims")]
internal sealed class AccountRewardClaimRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "reward_kind", IsPrimaryKey = true, Length = 32)]
    public string RewardKind { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "reward_key", IsPrimaryKey = true, Length = 96)]
    public string RewardKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "claimed_utc")]
    public DateTime ClaimedUtc { get; set; }
}

[SugarTable("account_achievements")]
internal sealed class AccountAchievementRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "achievement_id", IsPrimaryKey = true, Length = 64)]
    public string AchievementId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "progress")]
    public long Progress { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

[SugarTable("account_achievement_state")]
internal sealed class AccountAchievementStateRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "achievement_xp")]
    public long AchievementXp { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// 签到、一次性奖励与成就的 MySQL 实现。
///
/// 每一次领取——记录、物品入库、货币与流水、成就经验——都在同一个事务里完成。
/// 重复领取由主键拒绝并整体回滚，因此"领两次"在结构上就不可能发生。
/// </summary>
public sealed class SqlSugarProgressionRepository(SqlSugarClientFactory factory)
    : ISignInRepository, IAchievementRepository
{
    public async Task<SignInState?> FindStateAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var state = await database.Queryable<AccountSignInRow>()
                .Where(row => row.AccountId == accountId)
                .SingleAsync();

            if (state is null)
            {
                return null;
            }

            var claims = await database.Queryable<AccountSignInClaimRow>()
                .Where(row => row.AccountId == accountId && row.CycleStartDay == state.CycleStartDay)
                .ToListAsync();

            return new SignInState(
                state.CycleStartDay,
                state.ConsecutiveDays,
                state.LastClaimDay,
                claims
                    .Select(row => new SignInClaim(row.DayIndex, row.IsMakeup != 0))
                    .OrderBy(claim => claim.DayIndex)
                    .ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Sign-in state read failed.", exception);
        }
    }

    public async Task<IReadOnlyList<string>> ListClaimedRewardsAsync(
        long accountId,
        string rewardKind,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || string.IsNullOrWhiteSpace(rewardKind))
        {
            return Array.Empty<string>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var rows = await database.Queryable<AccountRewardClaimRow>()
                .Where(row => row.AccountId == accountId && row.RewardKind == rewardKind)
                .ToListAsync();

            return rows
                .Select(row => row.RewardKey)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Reward claim read failed.", exception);
        }
    }

    public async Task<ClaimOutcome> TryClaimSignInAsync(
        SignInClaimCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var alreadyClaimed = await database.Queryable<AccountSignInClaimRow>()
                .Where(row => row.AccountId == command.AccountId &&
                              row.CycleStartDay == command.CycleStartDay &&
                              row.DayIndex == command.DayIndex)
                .AnyAsync();

            if (alreadyClaimed)
            {
                database.Ado.RollbackTran();
                return ClaimOutcome.AlreadyClaimed;
            }

            var now = DateTime.UtcNow;
            var costs = command.MakeupCardCost.Concat(command.Reward.Items).ToArray();
            var inventoryOutcome = await ApplyInventoryAsync(
                database, command.AccountId, costs, command.StackLimits, command.Capacity, now);
            if (inventoryOutcome != ClaimOutcome.Applied)
            {
                database.Ado.RollbackTran();
                return inventoryOutcome;
            }

            var currencyOutcome = await ApplyCurrencyAsync(
                database, command.AccountId, command.Reward, now);
            if (currencyOutcome != ClaimOutcome.Applied)
            {
                database.Ado.RollbackTran();
                return currencyOutcome;
            }

            await database.Insertable(new AccountSignInClaimRow
            {
                AccountId = command.AccountId,
                CycleStartDay = command.CycleStartDay,
                DayIndex = command.DayIndex,
                IsMakeup = command.IsMakeup ? 1 : 0,
                ClaimedUtc = now
            }).ExecuteCommandAsync();

            var state = await database.Queryable<AccountSignInRow>()
                .Where(row => row.AccountId == command.AccountId)
                .SingleAsync();

            if (state is null)
            {
                await database.Insertable(new AccountSignInRow
                {
                    AccountId = command.AccountId,
                    CycleStartDay = command.CycleStartDay,
                    ConsecutiveDays = command.ConsecutiveDaysAfter,
                    LastClaimDay = command.LastClaimDayAfter,
                    CreatedUtc = now,
                    UpdatedUtc = now
                }).ExecuteCommandAsync();
            }
            else
            {
                state.CycleStartDay = command.CycleStartDay;
                state.ConsecutiveDays = command.ConsecutiveDaysAfter;
                state.LastClaimDay = command.LastClaimDayAfter;
                state.UpdatedUtc = now;
                await database.Updateable(state)
                    .UpdateColumns(row => new
                    {
                        row.CycleStartDay,
                        row.ConsecutiveDays,
                        row.LastClaimDay,
                        row.UpdatedUtc
                    })
                    .ExecuteCommandAsync();
            }

            database.Ado.CommitTran();
            return ClaimOutcome.Applied;
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ProgressionStorageException("Sign-in claim failed.", exception);
        }
    }

    public Task<ClaimOutcome> TryClaimRewardAsync(
        RewardClaimCommand command,
        CancellationToken cancellationToken) =>
        ClaimRewardCoreAsync(command, achievementXpGain: 0, cancellationToken);

    public Task<ClaimOutcome> TryClaimAchievementAsync(
        RewardClaimCommand command,
        long achievementXpGain,
        CancellationToken cancellationToken) =>
        ClaimRewardCoreAsync(command, achievementXpGain, cancellationToken);

    private async Task<ClaimOutcome> ClaimRewardCoreAsync(
        RewardClaimCommand command,
        long achievementXpGain,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var alreadyClaimed = await database.Queryable<AccountRewardClaimRow>()
                .Where(row => row.AccountId == command.AccountId &&
                              row.RewardKind == command.RewardKind &&
                              row.RewardKey == command.RewardKey)
                .AnyAsync();

            if (alreadyClaimed)
            {
                database.Ado.RollbackTran();
                return ClaimOutcome.AlreadyClaimed;
            }

            var now = DateTime.UtcNow;
            var inventoryOutcome = await ApplyInventoryAsync(
                database, command.AccountId, command.Reward.Items,
                command.StackLimits, command.Capacity, now);
            if (inventoryOutcome != ClaimOutcome.Applied)
            {
                database.Ado.RollbackTran();
                return inventoryOutcome;
            }

            var currencyOutcome = await ApplyCurrencyAsync(database, command.AccountId, command.Reward, now);
            if (currencyOutcome != ClaimOutcome.Applied)
            {
                database.Ado.RollbackTran();
                return currencyOutcome;
            }

            await database.Insertable(new AccountRewardClaimRow
            {
                AccountId = command.AccountId,
                RewardKind = command.RewardKind,
                RewardKey = command.RewardKey,
                ClaimedUtc = now
            }).ExecuteCommandAsync();

            if (achievementXpGain > 0)
            {
                // 成就经验只写 account_achievement_state，绝不触碰 account_profile.account_xp。
                var state = await database.Queryable<AccountAchievementStateRow>()
                    .Where(row => row.AccountId == command.AccountId)
                    .SingleAsync();

                if (state is null)
                {
                    await database.Insertable(new AccountAchievementStateRow
                    {
                        AccountId = command.AccountId,
                        AchievementXp = achievementXpGain,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    }).ExecuteCommandAsync();
                }
                else
                {
                    state.AchievementXp += achievementXpGain;
                    state.UpdatedUtc = now;
                    await database.Updateable(state)
                        .UpdateColumns(row => new { row.AchievementXp, row.UpdatedUtc })
                        .ExecuteCommandAsync();
                }
            }

            database.Ado.CommitTran();
            return ClaimOutcome.Applied;
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ProgressionStorageException("Reward claim failed.", exception);
        }
    }

    public async Task<IReadOnlyList<AchievementProgress>> ListProgressAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<AchievementProgress>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var rows = await database.Queryable<AccountAchievementRow>()
                .Where(row => row.AccountId == accountId)
                .ToListAsync();

            return rows
                .Select(row => new AchievementProgress(row.AchievementId, row.Progress))
                .OrderBy(entry => entry.AchievementId, StringComparer.Ordinal)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Achievement progress read failed.", exception);
        }
    }

    public async Task<long> GetAchievementXpAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return 0;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var row = await database.Queryable<AccountAchievementStateRow>()
                .Where(entry => entry.AccountId == accountId)
                .SingleAsync();

            return row?.AchievementXp ?? 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Achievement experience read failed.", exception);
        }
    }

    public async Task<long> AddProgressAsync(
        long accountId,
        string achievementId,
        long delta,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(achievementId);
        if (accountId <= 0 || delta <= 0)
        {
            return 0;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var row = await database.Queryable<AccountAchievementRow>()
                .Where(entry => entry.AccountId == accountId && entry.AchievementId == achievementId)
                .SingleAsync();

            var now = DateTime.UtcNow;
            long progress;
            if (row is null)
            {
                progress = delta;
                await database.Insertable(new AccountAchievementRow
                {
                    AccountId = accountId,
                    AchievementId = achievementId,
                    Progress = progress,
                    UpdatedUtc = now
                }).ExecuteCommandAsync();
            }
            else
            {
                progress = row.Progress + delta;
                row.Progress = progress;
                row.UpdatedUtc = now;
                await database.Updateable(row)
                    .UpdateColumns(entry => new { entry.Progress, entry.UpdatedUtc })
                    .ExecuteCommandAsync();
            }

            database.Ado.CommitTran();
            return progress;
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ProgressionStorageException("Achievement progress write failed.", exception);
        }
    }

    /// <summary>
    /// 在当前事务内应用一组库存变动。数量不足或超出上限时返回失败，由调用方回滚。
    /// </summary>
    private static async Task<ClaimOutcome> ApplyInventoryAsync(
        ISqlSugarClient database,
        long accountId,
        IReadOnlyList<InventoryDelta> deltas,
        IReadOnlyDictionary<string, int> stackLimits,
        int capacity,
        DateTime now)
    {
        if (deltas.Count == 0)
        {
            return ClaimOutcome.Applied;
        }

        var rows = await database.Queryable<AccountInventoryRow>()
            .Where(row => row.AccountId == accountId)
            .ToListAsync();

        var byItem = rows.ToDictionary(row => row.ItemId, StringComparer.Ordinal);
        var nextSlot = rows.Count == 0 ? 0 : rows.Max(row => row.SlotIndex) + 1;
        var inserts = new List<AccountInventoryRow>();
        var updates = new List<AccountInventoryRow>();
        var deletes = new List<AccountInventoryRow>();

        foreach (var group in deltas.GroupBy(delta => delta.ItemId, StringComparer.Ordinal))
        {
            var amount = group.Sum(delta => delta.Amount);
            if (amount == 0)
            {
                continue;
            }

            if (!stackLimits.TryGetValue(group.Key, out var stackLimit) || stackLimit <= 0)
            {
                return ClaimOutcome.InventoryFull;
            }

            byItem.TryGetValue(group.Key, out var slot);
            var next = (slot?.Quantity ?? 0) + amount;
            if (next < 0)
            {
                return ClaimOutcome.InsufficientItems;
            }

            if (next > stackLimit)
            {
                return ClaimOutcome.InventoryFull;
            }

            if (slot is null)
            {
                inserts.Add(new AccountInventoryRow
                {
                    AccountId = accountId,
                    ItemId = group.Key,
                    Quantity = next,
                    SlotIndex = nextSlot++,
                    CreatedUtc = now,
                    UpdatedUtc = now
                });
                continue;
            }

            slot.Quantity = next;
            slot.UpdatedUtc = now;
            if (next == 0)
            {
                deletes.Add(slot);
            }
            else
            {
                updates.Add(slot);
            }
        }

        if (rows.Count - deletes.Count + inserts.Count > capacity)
        {
            return ClaimOutcome.InventoryFull;
        }

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

        foreach (var slot in deletes)
        {
            await database.Deleteable<AccountInventoryRow>()
                .Where(row => row.AccountId == accountId && row.ItemId == slot.ItemId)
                .ExecuteCommandAsync();
        }

        return ClaimOutcome.Applied;
    }

    private static async Task<ClaimOutcome> ApplyCurrencyAsync(
        ISqlSugarClient database,
        long accountId,
        RewardGrant reward,
        DateTime now)
    {
        if (reward.Currencies.Count == 0)
        {
            return ClaimOutcome.Applied;
        }

        var progression = await database.Queryable<AccountProgressionRow>()
            .Where(row => row.AccountId == accountId)
            .SingleAsync();

        if (progression is null)
        {
            return ClaimOutcome.AccountMissing;
        }

        var ledger = new List<CurrencyLedgerRow>(reward.Currencies.Count);
        foreach (var grant in reward.Currencies)
        {
            if (!TryAdjust(progression, grant.CurrencyId, grant.Amount, out var balanceAfter))
            {
                return ClaimOutcome.AccountMissing;
            }

            ledger.Add(new CurrencyLedgerRow
            {
                AccountId = accountId,
                CurrencyId = grant.CurrencyId,
                Delta = grant.Amount,
                BalanceAfter = balanceAfter,
                Reason = reward.Reason,
                ReferenceId = reward.ReferenceId,
                CreatedUtc = now
            });
        }

        progression.UpdatedUtc = now;
        await database.Updateable(progression)
            .UpdateColumns(row => new { row.Copper, row.Silk, row.Gold, row.UpdatedUtc })
            .ExecuteCommandAsync();
        await database.Insertable(ledger).ExecuteCommandAsync();
        return ClaimOutcome.Applied;
    }

    private static bool TryAdjust(
        AccountProgressionRow row,
        string currencyId,
        long amount,
        out long balanceAfter)
    {
        balanceAfter = 0;
        switch (currencyId)
        {
            case "Copper" when row.Copper + amount >= 0:
                row.Copper += amount;
                balanceAfter = row.Copper;
                return true;
            case "Silk" when row.Silk + amount >= 0:
                row.Silk += amount;
                balanceAfter = row.Silk;
                return true;
            case "Gold" when row.Gold + amount >= 0:
                row.Gold += amount;
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
