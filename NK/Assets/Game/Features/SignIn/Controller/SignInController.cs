using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.Messaging;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Core.Application.RedDot;
using Naraka.Features.Lobby.Controller;

namespace Naraka.Features.SignIn.Controller
{
    /// <summary>服务端记录的一次领取。</summary>
    public readonly struct SignInClaimRecord
    {
        public SignInClaimRecord(int dayIndex, bool isMakeup)
        {
            DayIndex = dayIndex;
            IsMakeup = isMakeup;
        }

        public int DayIndex { get; }

        public bool IsMakeup { get; }
    }

    /// <summary>签到请求的结果。数据全部来自服务端，客户端不推导任何领取资格。</summary>
    public readonly struct SignInResult
    {
        private SignInResult(
            LobbyOperationStatus status,
            bool hasState,
            long cycleStartDay,
            long serverDay,
            int consecutiveDays,
            long makeupCardCount,
            IReadOnlyList<SignInClaimRecord> claims,
            IReadOnlyList<string> claimedMilestones)
        {
            Status = status;
            HasState = hasState;
            CycleStartDay = cycleStartDay;
            ServerDay = serverDay;
            ConsecutiveDays = consecutiveDays;
            MakeupCardCount = makeupCardCount;
            Claims = claims ?? Array.Empty<SignInClaimRecord>();
            ClaimedMilestones = claimedMilestones ?? Array.Empty<string>();
        }

        public LobbyOperationStatus Status { get; }

        public bool HasState { get; }

        public long CycleStartDay { get; }

        public long ServerDay { get; }

        public int ConsecutiveDays { get; }

        public long MakeupCardCount { get; }

        public IReadOnlyList<SignInClaimRecord> Claims { get; }

        public IReadOnlyList<string> ClaimedMilestones { get; }

        public bool IsSuccess => Status == LobbyOperationStatus.Success;

        public static SignInResult Success(
            long cycleStartDay,
            long serverDay,
            int consecutiveDays,
            long makeupCardCount,
            IReadOnlyList<SignInClaimRecord> claims,
            IReadOnlyList<string> claimedMilestones) =>
            new SignInResult(
                LobbyOperationStatus.Success, true, cycleStartDay, serverDay, consecutiveDays,
                makeupCardCount, claims, claimedMilestones);

        public static SignInResult Failed(LobbyOperationStatus status) =>
            new SignInResult(status, false, 0, 0, 0, 0, null, null);
    }

    /// <summary>签到网关。奖励内容在配置里，网络上只传领取记录与周期状态。</summary>
    public interface ISignInGateway
    {
        UniTask<SignInResult> RequestSignInAsync(CancellationToken cancellationToken);

        UniTask<SignInResult> ClaimTodayAsync(CancellationToken cancellationToken);

        /// <summary>补签一个漏签日。消耗一张补签卡。</summary>
        UniTask<SignInResult> MakeUpAsync(int dayIndex, CancellationToken cancellationToken);

        /// <summary>领取一个连续签到节点奖励。</summary>
        UniTask<SignInResult> ClaimMilestoneAsync(int milestoneDays, CancellationToken cancellationToken);
    }

    public interface ISignInController : IReadOnlyState<SignInPresentationState>
    {
        void Close();

        void ClaimToday();

        void MakeUp(int dayIndex);

        void ClaimMilestone(int milestoneDays);

        UniTask ReloadAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 签到界面的编排。
    ///
    /// 客户端<b>不判断</b>今天能不能签、补签卡够不够、周期有没有翻篇：这些都由服务端回答，
    /// 这里只把服务端返回的领取记录翻译成七个格子的显示状态。因此界面永远不会先于服务端
    /// 显示一个"已领取"。
    /// </summary>
    public sealed class SignInController : IController, ISignInController, IDisposable
    {
        private readonly IGameConfigProvider _config;
        private readonly ISignInGateway _gateway;
        private readonly ILobbyController _lobby;
        private readonly IServerCapabilities _capabilities;
        private readonly IDomainEventBus _bus;
        private readonly ReactiveState<SignInPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly IDisposable _lobbySubscription;
        private bool _isLoading;
        private bool _isClaiming;
        private bool _wasOpen;

        public SignInController(
            IGameConfigProvider config,
            ISignInGateway gateway,
            ILobbyController lobby,
            IServerCapabilities capabilities,
            IDomainEventBus bus)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));

            _state = new ReactiveState<SignInPresentationState>(SignInPresentationState.Initial);
            _lobbySubscription = _lobby.Subscribe(new LobbyStateObserver(this));
            ApplyLobbyState(_lobby.Current);
        }

        public SignInPresentationState Current => _state.Current;

        /// <summary>七日奖励配置。仅用于显示奖励图标与数量。</summary>
        public IReadOnlyList<SignInRewardConfig> Rewards =>
            _config.IsLoaded ? _config.Catalog.SignInRewardsInDayOrder : Array.Empty<SignInRewardConfig>();

        public IReadOnlyList<SignInMilestoneConfig> MilestoneRewards =>
            _config.IsLoaded
                ? _config.Catalog.SignInMilestonesInDayOrder
                : Array.Empty<SignInMilestoneConfig>();

        public void Close() => _lobby.CloseFeature();

        public void ClaimToday() => ClaimAsync(SignInClaimKind.Today, 0).Forget();

        public void MakeUp(int dayIndex) => ClaimAsync(SignInClaimKind.MakeUp, dayIndex).Forget();

        public void ClaimMilestone(int milestoneDays) =>
            ClaimAsync(SignInClaimKind.Milestone, milestoneDays).Forget();

        public async UniTask ReloadAsync(CancellationToken cancellationToken)
        {
            if (_isLoading)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.SignIn))
            {
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing)));
                return;
            }

            _isLoading = true;
            _state.Set(Current.WithLoading(true).WithStatusMessage(string.Empty));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, _lifetime.Token))
                {
                    Apply(await _gateway.RequestSignInAsync(linked.Token));
                }
            }
            catch (OperationCanceledException)
            {
                _state.Set(Current.WithLoading(false));
            }
            catch (Exception)
            {
                _state.Set(Current
                    .WithLoading(false)
                    .WithStatusMessage(LobbyOperationMessages.Describe(LobbyOperationStatus.TransportFailure)));
            }
            finally
            {
                _isLoading = false;
            }
        }

        public IDisposable Subscribe(IObserver<SignInPresentationState> observer) => _state.Subscribe(observer);

        public void Dispose()
        {
            _lobbySubscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        private enum SignInClaimKind
        {
            Today,
            MakeUp,
            Milestone
        }

        private async UniTaskVoid ClaimAsync(SignInClaimKind kind, int target)
        {
            var state = Current;
            if (_isClaiming || !state.IsOpen)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.SignIn))
            {
                _state.Set(state.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing)));
                return;
            }

            _isClaiming = true;
            _state.Set(state.WithBusy(true).WithStatusMessage(string.Empty));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           CancellationToken.None, _lifetime.Token))
                {
                    SignInResult result;
                    switch (kind)
                    {
                        case SignInClaimKind.MakeUp:
                            result = await _gateway.MakeUpAsync(target, linked.Token);
                            break;
                        case SignInClaimKind.Milestone:
                            result = await _gateway.ClaimMilestoneAsync(target, linked.Token);
                            break;
                        default:
                            result = await _gateway.ClaimTodayAsync(linked.Token);
                            break;
                    }

                    Apply(result);

                    // 奖励可能是货币，也可能是物品：两种都要让大厅重新读一次余额。
                    if (result.IsSuccess)
                    {
                        _lobby.LoadAccountSummaryAsync(linked.Token).Forget();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _state.Set(Current.WithBusy(false));
            }
            catch (Exception)
            {
                _state.Set(Current
                    .WithBusy(false)
                    .WithStatusMessage(LobbyOperationMessages.Describe(LobbyOperationStatus.TransportFailure)));
            }
            finally
            {
                _isClaiming = false;
            }
        }

        private void Apply(SignInResult result)
        {
            if (!result.IsSuccess)
            {
                _state.Set(Current
                    .WithLoading(false)
                    .WithBusy(false)
                    .WithStatusMessage(LobbyOperationMessages.Describe(result.Status)));
                return;
            }

            var makeupUsed = false;
            foreach (var claim in result.Claims)
            {
                if (claim.IsMakeup)
                {
                    makeupUsed = true;
                    break;
                }
            }

            var days = BuildDays(result, makeupUsed);
            var milestones = BuildMilestones(result);

            _state.Set(Current
                .WithServerState(
                    result.ServerDay,
                    result.CycleStartDay,
                    result.ConsecutiveDays,
                    result.MakeupCardCount,
                    makeupUsed,
                    days,
                    milestones)
                .WithStatusMessage(string.Empty));

            PublishRedDots();
        }

        /// <summary>
        /// 声明两个红点叶子是否有内容。
        ///
        /// 领完之后服务端返回的新状态会把它们置为 false，红点因此立刻熄灭，
        /// 不需要任何额外的"清除红点"调用。
        /// </summary>
        private void PublishRedDots()
        {
            var state = Current;
            _bus.Publish(new RedDotSourceChanged(RedDotPath.SignInDailyClaim, state.CanClaimToday));

            var hasMilestone = false;
            foreach (var milestone in state.Milestones)
            {
                if (milestone.IsClaimable)
                {
                    hasMilestone = true;
                    break;
                }
            }

            _bus.Publish(new RedDotSourceChanged(RedDotPath.SignInMilestoneClaim, hasMilestone));
        }

        /// <summary>
        /// 把服务端的领取记录翻译成七个格子。
        ///
        /// 补签能力的判定顺序与服务端一致：先看这一天是否真的漏签，再看周期次数，最后看补签卡。
        /// 任何一项不满足都显示为不可补签，避免玩家点了才被拒绝。
        /// </summary>
        private static IReadOnlyList<SignInDaySnapshot> BuildDays(SignInResult result, bool makeupUsed)
        {
            var offset = result.ServerDay - result.CycleStartDay;
            var todayIndex = offset >= 0 && offset < SignInPresentationState.CycleLength
                ? (int)offset + 1
                : 0;

            var days = new List<SignInDaySnapshot>(SignInPresentationState.CycleLength);
            for (var day = 1; day <= SignInPresentationState.CycleLength; day++)
            {
                var status = SignInDayStatus.Upcoming;
                var claimed = false;
                foreach (var claim in result.Claims)
                {
                    if (claim.DayIndex != day)
                    {
                        continue;
                    }

                    claimed = true;
                    status = claim.IsMakeup ? SignInDayStatus.MadeUp : SignInDayStatus.Claimed;
                    break;
                }

                if (!claimed)
                {
                    if (todayIndex > 0 && day == todayIndex)
                    {
                        status = SignInDayStatus.Claimable;
                    }
                    else if (todayIndex > 0 && day < todayIndex)
                    {
                        var canMakeUp = !makeupUsed && result.MakeupCardCount > 0;
                        status = canMakeUp ? SignInDayStatus.Missed : SignInDayStatus.MissedLocked;
                    }
                }

                days.Add(new SignInDaySnapshot(day, status));
            }

            return days;
        }

        private IReadOnlyList<SignInMilestoneSnapshot> BuildMilestones(SignInResult result)
        {
            var configured = MilestoneRewards;
            var milestones = new List<SignInMilestoneSnapshot>(configured.Count);
            foreach (var milestone in configured)
            {
                var isClaimed = false;
                foreach (var claimed in result.ClaimedMilestones)
                {
                    if (string.Equals(claimed, milestone.RewardId, StringComparison.Ordinal))
                    {
                        isClaimed = true;
                        break;
                    }
                }

                milestones.Add(new SignInMilestoneSnapshot(
                    milestone.MilestoneDays,
                    milestone.RewardId,
                    result.ConsecutiveDays >= milestone.MilestoneDays,
                    isClaimed));
            }

            return milestones;
        }

        private void ApplyLobbyState(LobbyPresentationState lobby)
        {
            var isOpen = lobby.IsFeatureOpen && lobby.OpenFeature == LobbyFeature.CheckIn;
            if (isOpen == _wasOpen)
            {
                return;
            }

            _wasOpen = isOpen;
            _state.Set(Current.WithOpen(isOpen));
            if (isOpen)
            {
                ReloadAsync(CancellationToken.None).Forget();
            }
        }

        private sealed class LobbyStateObserver : IObserver<LobbyPresentationState>
        {
            private readonly SignInController _owner;

            public LobbyStateObserver(SignInController owner) => _owner = owner;

            public void OnNext(LobbyPresentationState value) => _owner.ApplyLobbyState(value);

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }
    }
}
