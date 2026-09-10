using Naraka.Config;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Inventory;

namespace Naraka.Server.Application.Progression;

/// <summary>一次签到领取记录。</summary>
public sealed record SignInClaim(int DayIndex, bool IsMakeup);

/// <summary>账号的签到周期状态。</summary>
public sealed record SignInState(
    long CycleStartDay,
    int ConsecutiveDays,
    long LastClaimDay,
    IReadOnlyList<SignInClaim> Claims);

/// <summary>领取尝试的结果。</summary>
public enum ClaimOutcome
{
    Applied = 0,
    AlreadyClaimed = 1,
    NotAvailable = 2,
    LimitReached = 3,
    InsufficientItems = 4,
    InventoryFull = 5,
    AccountMissing = 6
}

/// <summary>一次奖励发放的内容。物品与货币二选一，由配置决定。</summary>
public sealed record RewardGrant(
    IReadOnlyList<InventoryDelta> Items,
    IReadOnlyList<CurrencyGrant> Currencies,
    string Reason,
    string ReferenceId);

/// <summary>一次签到写入的完整参数。</summary>
public sealed record SignInClaimCommand(
    long AccountId,
    long CycleStartDay,
    int DayIndex,
    bool IsMakeup,
    int ConsecutiveDaysAfter,
    long LastClaimDayAfter,
    RewardGrant Reward,
    IReadOnlyList<InventoryDelta> MakeupCardCost,
    IReadOnlyDictionary<string, int> StackLimits,
    int Capacity);

/// <summary>领取一次性奖励（连续签到节点、账号等级奖励、成就奖励）的参数。</summary>
public sealed record RewardClaimCommand(
    long AccountId,
    string RewardKind,
    string RewardKey,
    RewardGrant Reward,
    IReadOnlyDictionary<string, int> StackLimits,
    int Capacity);

/// <summary>一次性奖励的种类。写入 account_reward_claims 的 reward_kind 列。</summary>
public static class RewardClaimKind
{
    public const string SignInMilestone = "SignInMilestone";
    public const string AccountLevel = "AccountLevel";
    public const string Achievement = "Achievement";
}

public sealed class ProgressionStorageException : Exception
{
    public ProgressionStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public interface ISignInRepository
{
    Task<SignInState?> FindStateAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>已经领取过的一次性奖励键。用于判定"已领取"而不必逐个查询。</summary>
    Task<IReadOnlyList<string>> ListClaimedRewardsAsync(
        long accountId,
        string rewardKind,
        CancellationToken cancellationToken);

    /// <summary>
    /// 在<b>一个</b>事务内完成签到记录、补签卡消耗、奖励发放与周期状态更新。
    /// 重复领取由 (账号, 周期, 天序) 主键拒绝，因此不可能领两次。
    /// </summary>
    Task<ClaimOutcome> TryClaimSignInAsync(SignInClaimCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// 在<b>一个</b>事务内完成一次性奖励的领取记录与发放。
    /// 重复领取由 (账号, 种类, 键) 主键拒绝。
    /// </summary>
    Task<ClaimOutcome> TryClaimRewardAsync(RewardClaimCommand command, CancellationToken cancellationToken);
}

public sealed record SignInView(
    SignInState State,
    long ServerDay,
    IReadOnlyList<string> ClaimedMilestones,
    long MakeupCardCount);

public readonly struct SignInResult
{
    private SignInResult(LobbyOperationStatus status, SignInView? view)
    {
        Status = status;
        View = view;
    }

    public LobbyOperationStatus Status { get; }

    public SignInView? View { get; }

    public static SignInResult Success(SignInView view) => new(LobbyOperationStatus.Success, view);

    public static SignInResult Failed(LobbyOperationStatus status) => new(status, null);
}

/// <summary>
/// 签到业务。
///
/// 规则来自本轮确认的玩法决策：
/// - 七日循环，以服务器每日 05:00 为日期边界；
/// - 每个周期最多补签<b>一个</b>漏签日，且必须消耗一张补签卡；
/// - 主七日进度与连续签到次数<b>分开</b>保存，互不影响；
/// - 签到奖励与连续奖励都必须手动领取，且都是幂等的。
/// </summary>
public sealed class SignInService(
    ISignInRepository signIn,
    IInventoryRepository inventory,
    IAccountProfileRepository profiles,
    GameConfig config,
    TimeProvider? timeProvider = null)
{
    /// <summary>七日循环长度。</summary>
    public const int CycleLength = 7;

    /// <summary>每个周期允许的补签次数。</summary>
    public const int MakeupPerCycle = 1;

    /// <summary>补签卡物品。产出渠道与价格都在配置里，这里只引用它的 ID。</summary>
    public const string MakeupCardItemId = "special_makeup_card";

    private readonly ISignInRepository _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));

    private readonly IInventoryRepository _inventory =
        inventory ?? throw new ArgumentNullException(nameof(inventory));

    private readonly IAccountProfileRepository _profiles =
        profiles ?? throw new ArgumentNullException(nameof(profiles));

    private readonly GameConfig _config = config ?? throw new ArgumentNullException(nameof(config));

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<SignInResult> GetAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return SignInResult.Failed(LobbyOperationStatus.InvalidRequest);
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
            return SignInResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return SignInResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 领取今天的签到。
    ///
    /// "今天是周期第几天"由当前服务器日与周期起始日之差决定，因此漏签不会让进度倒退，
    /// 也不会因为一次补签而把主进度往前推。
    /// </summary>
    public Task<SignInResult> ClaimTodayAsync(long accountId, CancellationToken cancellationToken) =>
        ClaimAsync(accountId, dayIndex: -1, isMakeup: false, cancellationToken);

    /// <summary>
    /// 补签一个漏签日。必须消耗一张补签卡，每个周期最多一次。
    /// </summary>
    public Task<SignInResult> MakeUpAsync(long accountId, int dayIndex, CancellationToken cancellationToken) =>
        ClaimAsync(accountId, dayIndex, isMakeup: true, cancellationToken);

    private async Task<SignInResult> ClaimAsync(
        long accountId,
        int dayIndex,
        bool isMakeup,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return SignInResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var profile = await _profiles.FindProfileAsync(accountId, cancellationToken);
            if (profile is null)
            {
                return SignInResult.Failed(LobbyOperationStatus.NotFound);
            }

            var today = ServerDay.FromUtc(_time.GetUtcNow().UtcDateTime);
            var state = await _signIn.FindStateAsync(accountId, cancellationToken)
                        ?? new SignInState(today, 0, long.MinValue, Array.Empty<SignInClaim>());

            // 周期已经走完就开新周期：主进度与连续次数分别处理。
            var cycleStart = today - state.CycleStartDay >= CycleLength ? today : state.CycleStartDay;
            var claims = cycleStart == state.CycleStartDay ? state.Claims : Array.Empty<SignInClaim>();

            var targetDay = isMakeup ? dayIndex : (int)(today - cycleStart) + 1;
            if (targetDay < 1 || targetDay > CycleLength)
            {
                return SignInResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            if (claims.Any(claim => claim.DayIndex == targetDay))
            {
                return SignInResult.Failed(LobbyOperationStatus.AlreadyClaimed);
            }

            if (isMakeup)
            {
                var todayIndex = (int)(today - cycleStart) + 1;
                if (targetDay >= todayIndex)
                {
                    // 只能补"已经过去"的日子，不能提前领未来的奖励。
                    return SignInResult.Failed(LobbyOperationStatus.NotAvailable);
                }

                if (claims.Count(claim => claim.IsMakeup) >= MakeupPerCycle)
                {
                    return SignInResult.Failed(LobbyOperationStatus.LimitReached);
                }

                if (await CountMakeupCardsAsync(accountId, cancellationToken) < 1)
                {
                    return SignInResult.Failed(LobbyOperationStatus.InsufficientItems);
                }
            }

            if (!_config.TryGetSignInReward(targetDay, out var reward) || reward is null)
            {
                return SignInResult.Failed(LobbyOperationStatus.InternalError);
            }

            // 连续签到次数只随"当天签到"增长；补签不改变它，否则补签就变成了连续奖励的捷径。
            var consecutive = isMakeup
                ? state.ConsecutiveDays
                : today - state.LastClaimDay == 1
                    ? state.ConsecutiveDays + 1
                    : 1;
            var lastClaimDay = isMakeup ? state.LastClaimDay : today;

            var command = new SignInClaimCommand(
                accountId,
                cycleStart,
                targetDay,
                isMakeup,
                consecutive,
                lastClaimDay,
                ToGrant(reward.RewardKind, reward.ItemId, reward.ItemAmount,
                    reward.CurrencyId, reward.CurrencyAmount,
                    CurrencyLedgerReason.SignInReward,
                    BuildReference(cycleStart, targetDay)),
                isMakeup
                    ? new[] { new InventoryDelta(MakeupCardItemId, -1) }
                    : Array.Empty<InventoryDelta>(),
                StackLimits(),
                _config.GetInventoryCapacity(profile.InventoryTier));

            var outcome = await _signIn.TryClaimSignInAsync(command, cancellationToken);
            return outcome switch
            {
                ClaimOutcome.Applied => await ReadViewAsync(accountId, cancellationToken),
                ClaimOutcome.AlreadyClaimed => SignInResult.Failed(LobbyOperationStatus.AlreadyClaimed),
                ClaimOutcome.InsufficientItems => SignInResult.Failed(LobbyOperationStatus.InsufficientItems),
                ClaimOutcome.InventoryFull => SignInResult.Failed(LobbyOperationStatus.InventoryFull),
                ClaimOutcome.LimitReached => SignInResult.Failed(LobbyOperationStatus.LimitReached),
                ClaimOutcome.NotAvailable => SignInResult.Failed(LobbyOperationStatus.NotAvailable),
                ClaimOutcome.AccountMissing => SignInResult.Failed(LobbyOperationStatus.NotFound),
                _ => SignInResult.Failed(LobbyOperationStatus.InternalError)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return SignInResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return SignInResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    /// <summary>
    /// 领取一个连续签到节点奖励。要求连续签到次数已经达到该节点，且尚未领取过。
    /// </summary>
    public async Task<SignInResult> ClaimMilestoneAsync(
        long accountId,
        int milestoneDays,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return SignInResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        var milestone = _config.SignInMilestones
            .FirstOrDefault(entry => entry.MilestoneDays == milestoneDays);
        if (milestone is null)
        {
            return SignInResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            var profile = await _profiles.FindProfileAsync(accountId, cancellationToken);
            if (profile is null)
            {
                return SignInResult.Failed(LobbyOperationStatus.NotFound);
            }

            var state = await _signIn.FindStateAsync(accountId, cancellationToken);
            if (state is null || state.ConsecutiveDays < milestoneDays)
            {
                return SignInResult.Failed(LobbyOperationStatus.NotAvailable);
            }

            var key = milestone.RewardId;
            var command = new RewardClaimCommand(
                accountId,
                RewardClaimKind.SignInMilestone,
                key,
                ToGrant(milestone.RewardKind, milestone.ItemId, milestone.ItemAmount,
                    milestone.CurrencyId, milestone.CurrencyAmount,
                    CurrencyLedgerReason.SignInMilestone, key),
                StackLimits(),
                _config.GetInventoryCapacity(profile.InventoryTier));

            var outcome = await _signIn.TryClaimRewardAsync(command, cancellationToken);
            return outcome switch
            {
                ClaimOutcome.Applied => await ReadViewAsync(accountId, cancellationToken),
                ClaimOutcome.AlreadyClaimed => SignInResult.Failed(LobbyOperationStatus.AlreadyClaimed),
                ClaimOutcome.InventoryFull => SignInResult.Failed(LobbyOperationStatus.InventoryFull),
                ClaimOutcome.AccountMissing => SignInResult.Failed(LobbyOperationStatus.NotFound),
                _ => SignInResult.Failed(LobbyOperationStatus.InternalError)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFault(exception))
        {
            return SignInResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return SignInResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    internal static string BuildReference(long cycleStartDay, int dayIndex) =>
        "signin-" + cycleStartDay.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        "-" + dayIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static RewardGrant ToGrant(
        string rewardKind,
        string itemId,
        int itemAmount,
        string currencyId,
        long currencyAmount,
        string reason,
        string referenceId)
    {
        if (string.Equals(rewardKind, ConfigRewardKind.Item, StringComparison.Ordinal))
        {
            return new RewardGrant(
                new[] { new InventoryDelta(itemId, itemAmount) },
                Array.Empty<CurrencyGrant>(),
                reason,
                referenceId);
        }

        if (string.Equals(rewardKind, ConfigRewardKind.Currency, StringComparison.Ordinal))
        {
            return new RewardGrant(
                Array.Empty<InventoryDelta>(),
                new[] { new CurrencyGrant(currencyId, currencyAmount) },
                reason,
                referenceId);
        }

        return new RewardGrant(
            Array.Empty<InventoryDelta>(), Array.Empty<CurrencyGrant>(), reason, referenceId);
    }

    private async Task<long> CountMakeupCardsAsync(long accountId, CancellationToken cancellationToken)
    {
        var slots = await _inventory.ListSlotsAsync(accountId, cancellationToken);
        return slots.FirstOrDefault(slot => slot.ItemId == MakeupCardItemId)?.Quantity ?? 0;
    }

    private IReadOnlyDictionary<string, int> StackLimits() =>
        _config.Catalog.Items.ToDictionary(item => item.ItemId, item => item.StackLimit, StringComparer.Ordinal);

    private async Task<SignInResult> ReadViewAsync(long accountId, CancellationToken cancellationToken)
    {
        var today = ServerDay.FromUtc(_time.GetUtcNow().UtcDateTime);
        var state = await _signIn.FindStateAsync(accountId, cancellationToken)
                    ?? new SignInState(today, 0, long.MinValue, Array.Empty<SignInClaim>());

        // 周期已经走完时对外呈现新周期，界面因此不会显示一堆早已过期的"可补签"。
        if (today - state.CycleStartDay >= CycleLength)
        {
            state = new SignInState(today, state.ConsecutiveDays, state.LastClaimDay, Array.Empty<SignInClaim>());
        }

        return SignInResult.Success(new SignInView(
            state,
            today,
            await _signIn.ListClaimedRewardsAsync(
                accountId, RewardClaimKind.SignInMilestone, cancellationToken),
            await CountMakeupCardsAsync(accountId, cancellationToken)));
    }

    private static bool IsStorageFault(Exception exception) =>
        exception is ProgressionStorageException or InventoryStorageException or AccountProfileStorageException;
}
