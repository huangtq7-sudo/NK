using Naraka.Config;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Inventory;

namespace Naraka.Server.Application.Progression;

/// <summary>一条成就的账号进度。</summary>
public sealed record AchievementProgress(string AchievementId, long Progress);

/// <summary>成就等级状态。<b>AchievementXp 与账号经验完全分离</b>，成就永远不影响账号等级。</summary>
public sealed record AchievementState(long AchievementXp, int AchievementLevel);

public interface IAchievementRepository
{
    Task<IReadOnlyList<AchievementProgress>> ListProgressAsync(
        long accountId,
        CancellationToken cancellationToken);

    Task<long> GetAchievementXpAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>
    /// 在<b>一个</b>事务内记录领取、发放奖励并累加成就经验。
    /// 重复领取由 (账号, 种类, 成就 ID) 主键拒绝。
    /// </summary>
    Task<ClaimOutcome> TryClaimAchievementAsync(
        RewardClaimCommand command,
        long achievementXpGain,
        CancellationToken cancellationToken);

    /// <summary>
    /// 累加一条成就的进度并返回累加后的值。进度只增不减。
    /// </summary>
    Task<long> AddProgressAsync(
        long accountId,
        string achievementId,
        long delta,
        CancellationToken cancellationToken);
}

public sealed record AchievementView(
    AchievementState State,
    IReadOnlyList<AchievementProgress> Progress,
    IReadOnlyList<string> ClaimedAchievements,
    IReadOnlyList<string> ClaimedAccountLevels,
    long AccountXp,
    int AccountLevel);

public readonly struct AchievementResult
{
    private AchievementResult(LobbyOperationStatus status, AchievementView? view)
    {
        Status = status;
        View = view;
    }

    public LobbyOperationStatus Status { get; }

    public AchievementView? View { get; }

    public static AchievementResult Success(AchievementView view) => new(LobbyOperationStatus.Success, view);

    public static AchievementResult Failed(LobbyOperationStatus status) => new(status, null);
}

/// <summary>
/// 成就与账号等级奖励。
///
/// 两者是<b>两个独立系统</b>：
/// - 账号等级只由 AccountXp 决定，而 AccountXp 只能来自任务（P2 之后接入）；
/// - 成就等级只由 AchievementXp 决定，成就经验绝不写入 AccountXp。
///
/// 依赖 P2 战斗或 P3 远征的成就在配置里标记为 IsActiveInP1=false，
/// 它们的进度会一直保持 0，而不是用假数据凑出一个看起来在推进的进度条。
/// </summary>
public sealed class AchievementService(
    IAchievementRepository achievements,
    ISignInRepository rewards,
    IAccountProfileRepository profiles,
    GameConfig config)
{
    /// <summary>成就等级的每级经验需求。等级只用于展示，不解锁任何内容。</summary>
    public const long AchievementXpPerLevel = 500;

    private readonly IAchievementRepository _achievements =
        achievements ?? throw new ArgumentNullException(nameof(achievements));

    private readonly ISignInRepository _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));

    private readonly IAccountProfileRepository _profiles =
        profiles ?? throw new ArgumentNullException(nameof(profiles));

    private readonly GameConfig _config = config ?? throw new ArgumentNullException(nameof(config));

    public static int ResolveAchievementLevel(long achievementXp) =>
        (int)(achievementXp / AchievementXpPerLevel) + 1;

    public async Task<AchievementResult> GetAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return AchievementResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            return await ReadViewAsync(accountId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return AchievementResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return AchievementResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>领取一条已完成成就的奖励。奖励与成就经验在同一事务内发放。</summary>
    public async Task<AchievementResult> ClaimAchievementAsync(
        long accountId,
        string? achievementId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || string.IsNullOrWhiteSpace(achievementId))
        {
            return AchievementResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (!_config.TryGetAchievement(achievementId, out var achievement) || achievement is null)
        {
            return AchievementResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var profile = await _profiles.FindProfileAsync(accountId, cancellationToken);
            if (profile is null)
            {
                return AchievementResult.Failed(LobbyOperationStatus.NotFound);
            }

            var progress = await _achievements.ListProgressAsync(accountId, cancellationToken);
            var current = progress.FirstOrDefault(entry => entry.AchievementId == achievementId)?.Progress ?? 0;
            if (current < achievement.TargetProgress)
            {
                return AchievementResult.Failed(LobbyOperationStatus.NotAvailable);
            }

            var command = new RewardClaimCommand(
                accountId,
                RewardClaimKind.Achievement,
                achievement.AchievementId,
                ToGrant(achievement, CurrencyLedgerReason.AchievementReward),
                StackLimits(),
                _config.GetInventoryCapacity(profile.InventoryTier));

            var outcome = await _achievements.TryClaimAchievementAsync(
                command, achievement.AchievementXp, cancellationToken);

            return outcome switch
            {
                ClaimOutcome.Applied => await ReadViewAsync(accountId, cancellationToken),
                ClaimOutcome.AlreadyClaimed => AchievementResult.Failed(LobbyOperationStatus.AlreadyClaimed),
                ClaimOutcome.InventoryFull => AchievementResult.Failed(LobbyOperationStatus.InventoryFull),
                ClaimOutcome.AccountMissing => AchievementResult.Failed(LobbyOperationStatus.NotFound),
                _ => AchievementResult.Failed(LobbyOperationStatus.InternalError)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return AchievementResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return AchievementResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 领取一个账号等级奖励。要求账号等级已经达到该等级，且尚未领取过。
    /// 账号等级由 AccountXp 推导，而 AccountXp 只能来自任务。
    /// </summary>
    public async Task<AchievementResult> ClaimAccountLevelRewardAsync(
        long accountId,
        int level,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return AchievementResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (!_config.TryGetAccountLevel(level, out var reward) || reward is null)
        {
            return AchievementResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (string.Equals(reward.RewardKind, ConfigRewardKind.None, StringComparison.Ordinal))
        {
            return AchievementResult.Failed(LobbyOperationStatus.NotAvailable);
        }

        try
        {
            var profile = await _profiles.FindProfileAsync(accountId, cancellationToken);
            if (profile is null)
            {
                return AchievementResult.Failed(LobbyOperationStatus.NotFound);
            }

            if (_config.ResolveAccountLevel(profile.AccountXp) < level)
            {
                return AchievementResult.Failed(LobbyOperationStatus.NotAvailable);
            }

            var command = new RewardClaimCommand(
                accountId,
                RewardClaimKind.AccountLevel,
                reward.RewardId,
                new RewardGrant(
                    string.Equals(reward.RewardKind, ConfigRewardKind.Item, StringComparison.Ordinal)
                        ? new[] { new InventoryDelta(reward.ItemId, reward.ItemAmount) }
                        : Array.Empty<InventoryDelta>(),
                    string.Equals(reward.RewardKind, ConfigRewardKind.Currency, StringComparison.Ordinal)
                        ? new[] { new CurrencyGrant(reward.CurrencyId, reward.CurrencyAmount) }
                        : Array.Empty<CurrencyGrant>(),
                    CurrencyLedgerReason.AccountLevelReward,
                    reward.RewardId),
                StackLimits(),
                _config.GetInventoryCapacity(profile.InventoryTier));

            var outcome = await _rewards.TryClaimRewardAsync(command, cancellationToken);
            return outcome switch
            {
                ClaimOutcome.Applied => await ReadViewAsync(accountId, cancellationToken),
                ClaimOutcome.AlreadyClaimed => AchievementResult.Failed(LobbyOperationStatus.AlreadyClaimed),
                ClaimOutcome.InventoryFull => AchievementResult.Failed(LobbyOperationStatus.InventoryFull),
                ClaimOutcome.AccountMissing => AchievementResult.Failed(LobbyOperationStatus.NotFound),
                _ => AchievementResult.Failed(LobbyOperationStatus.InternalError)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return AchievementResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return AchievementResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 推进一条由 P1 事件驱动的成就。
    ///
    /// 只接受配置中 IsActiveInP1 为 true 的成就：P2/P3 依赖的成就没有事件源，
    /// 允许它们被推进就等于用假数据填进度条。
    /// </summary>
    public async Task<long> AddProgressAsync(
        long accountId,
        string sourceEvent,
        long delta,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || string.IsNullOrWhiteSpace(sourceEvent) || delta <= 0)
        {
            return 0;
        }

        var total = 0L;
        foreach (var achievement in _config.Achievements)
        {
            if (!achievement.IsActiveInP1 ||
                !string.Equals(achievement.SourceEvent, sourceEvent, StringComparison.Ordinal))
            {
                continue;
            }

            total = await _achievements.AddProgressAsync(
                accountId, achievement.AchievementId, delta, cancellationToken);
        }

        return total;
    }

    private RewardGrant ToGrant(AchievementConfig achievement, string reason) => new(
        string.Equals(achievement.RewardKind, ConfigRewardKind.Item, StringComparison.Ordinal)
            ? new[] { new InventoryDelta(achievement.ItemId, achievement.ItemAmount) }
            : Array.Empty<InventoryDelta>(),
        string.Equals(achievement.RewardKind, ConfigRewardKind.Currency, StringComparison.Ordinal)
            ? new[] { new CurrencyGrant(achievement.CurrencyId, achievement.CurrencyAmount) }
            : Array.Empty<CurrencyGrant>(),
        reason,
        achievement.AchievementId);

    private IReadOnlyDictionary<string, int> StackLimits() =>
        _config.Catalog.Items.ToDictionary(item => item.ItemId, item => item.StackLimit, StringComparer.Ordinal);

    private async Task<AchievementResult> ReadViewAsync(long accountId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.FindProfileAsync(accountId, cancellationToken);
        if (profile is null)
        {
            return AchievementResult.Failed(LobbyOperationStatus.NotFound);
        }

        var achievementXp = await _achievements.GetAchievementXpAsync(accountId, cancellationToken);

        return AchievementResult.Success(new AchievementView(
            new AchievementState(achievementXp, ResolveAchievementLevel(achievementXp)),
            await _achievements.ListProgressAsync(accountId, cancellationToken),
            await _rewards.ListClaimedRewardsAsync(accountId, RewardClaimKind.Achievement, cancellationToken),
            await _rewards.ListClaimedRewardsAsync(accountId, RewardClaimKind.AccountLevel, cancellationToken),
            profile.AccountXp,
            _config.ResolveAccountLevel(profile.AccountXp)));
    }

    private static bool IsStorageFault(Exception exception) =>
        exception is ProgressionStorageException or InventoryStorageException or AccountProfileStorageException;
}
