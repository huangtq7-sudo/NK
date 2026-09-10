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

namespace Naraka.Features.Achievement.Controller
{
    /// <summary>服务端记录的一条成就进度。</summary>
    public readonly struct AchievementProgressRecord
    {
        public AchievementProgressRecord(string achievementId, long progress)
        {
            AchievementId = achievementId ?? string.Empty;
            Progress = progress;
        }

        public string AchievementId { get; }

        public long Progress { get; }
    }

    /// <summary>成就与账号等级请求的结果。</summary>
    public readonly struct AchievementResult
    {
        private AchievementResult(
            LobbyOperationStatus status,
            bool hasState,
            long accountXp,
            int accountLevel,
            long achievementXp,
            int achievementLevel,
            IReadOnlyList<AchievementProgressRecord> progress,
            IReadOnlyList<string> claimedAchievements,
            IReadOnlyList<string> claimedAccountLevels)
        {
            Status = status;
            HasState = hasState;
            AccountXp = accountXp;
            AccountLevel = accountLevel;
            AchievementXp = achievementXp;
            AchievementLevel = achievementLevel;
            Progress = progress ?? Array.Empty<AchievementProgressRecord>();
            ClaimedAchievements = claimedAchievements ?? Array.Empty<string>();
            ClaimedAccountLevels = claimedAccountLevels ?? Array.Empty<string>();
        }

        public LobbyOperationStatus Status { get; }

        public bool HasState { get; }

        public long AccountXp { get; }

        public int AccountLevel { get; }

        public long AchievementXp { get; }

        public int AchievementLevel { get; }

        public IReadOnlyList<AchievementProgressRecord> Progress { get; }

        public IReadOnlyList<string> ClaimedAchievements { get; }

        public IReadOnlyList<string> ClaimedAccountLevels { get; }

        public bool IsSuccess => Status == LobbyOperationStatus.Success;

        public static AchievementResult Success(
            long accountXp,
            int accountLevel,
            long achievementXp,
            int achievementLevel,
            IReadOnlyList<AchievementProgressRecord> progress,
            IReadOnlyList<string> claimedAchievements,
            IReadOnlyList<string> claimedAccountLevels) =>
            new AchievementResult(
                LobbyOperationStatus.Success, true, accountXp, accountLevel, achievementXp,
                achievementLevel, progress, claimedAchievements, claimedAccountLevels);

        public static AchievementResult Failed(LobbyOperationStatus status) =>
            new AchievementResult(status, false, 0, 0, 0, 0, null, null, null);
    }

    /// <summary>成就与账号等级奖励网关。</summary>
    public interface IAchievementGateway
    {
        UniTask<AchievementResult> RequestAchievementsAsync(CancellationToken cancellationToken);

        UniTask<AchievementResult> ClaimAchievementAsync(
            string achievementId,
            CancellationToken cancellationToken);

        UniTask<AchievementResult> ClaimAccountLevelRewardAsync(
            int level,
            CancellationToken cancellationToken);
    }

    public interface IAchievementController : IReadOnlyState<AchievementPresentationState>
    {
        void Close();

        void SelectTab(AchievementTab tab);

        void SelectCategory(string category);

        void ClaimAchievement(string achievementId);

        void ClaimAccountLevelReward(int level);

        UniTask ReloadAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 成就与账号等级奖励界面的编排。
    ///
    /// 两套系统的数据在同一个响应里返回，但在展示状态里始终分开存放：成就进度只影响
    /// 成就经验与成就等级，账号等级奖励只看账号等级。任何一侧的领取都不会改写另一侧。
    /// </summary>
    public sealed class AchievementController : IController, IAchievementController, IDisposable
    {
        private readonly IGameConfigProvider _config;
        private readonly IAchievementGateway _gateway;
        private readonly ILobbyController _lobby;
        private readonly IServerCapabilities _capabilities;
        private readonly IDomainEventBus _bus;
        private readonly ReactiveState<AchievementPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly IDisposable _lobbySubscription;
        private bool _isLoading;
        private bool _isClaiming;
        private bool _wasOpen;

        public AchievementController(
            IGameConfigProvider config,
            IAchievementGateway gateway,
            ILobbyController lobby,
            IServerCapabilities capabilities,
            IDomainEventBus bus)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));

            _state = new ReactiveState<AchievementPresentationState>(
                AchievementPresentationState.Initial.WithCategory(ConfigAchievementCategory.Adventure));
            _lobbySubscription = _lobby.Subscribe(new LobbyStateObserver(this));
            ApplyLobbyState(_lobby.Current);
        }

        public AchievementPresentationState Current => _state.Current;

        public IReadOnlyList<AchievementConfig> Achievements =>
            _config.IsLoaded ? _config.Catalog.AchievementsInDisplayOrder : Array.Empty<AchievementConfig>();

        public IReadOnlyList<AccountLevelRewardConfig> AccountLevelRewards =>
            _config.IsLoaded
                ? _config.Catalog.AccountLevelRewardsInLevelOrder
                : Array.Empty<AccountLevelRewardConfig>();

        /// <summary>成就等级每级所需经验。与服务端 AchievementService 的常量一致。</summary>
        public const long AchievementXpPerLevel = 500;

        public void Close() => _lobby.CloseFeature();

        public void SelectTab(AchievementTab tab)
        {
            if (Current.Tab == tab)
            {
                return;
            }

            _state.Set(Current.WithTab(tab).WithStatusMessage(string.Empty));
        }

        public void SelectCategory(string category)
        {
            if (!ConfigAchievementCategory.IsDefined(category) ||
                string.Equals(Current.Category, category, StringComparison.Ordinal))
            {
                return;
            }

            _state.Set(Current.WithCategory(category).WithStatusMessage(string.Empty));
        }

        public void ClaimAchievement(string achievementId)
        {
            if (string.IsNullOrEmpty(achievementId))
            {
                return;
            }

            ClaimAsync(achievementId, 0).Forget();
        }

        public void ClaimAccountLevelReward(int level)
        {
            if (level <= 0)
            {
                return;
            }

            ClaimAsync(null, level).Forget();
        }

        public async UniTask ReloadAsync(CancellationToken cancellationToken)
        {
            if (_isLoading)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Achievement))
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
                    Apply(await _gateway.RequestAchievementsAsync(linked.Token));
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

        public IDisposable Subscribe(IObserver<AchievementPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose()
        {
            _lobbySubscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        private async UniTaskVoid ClaimAsync(string achievementId, int level)
        {
            var state = Current;
            if (_isClaiming || !state.IsOpen)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Achievement))
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
                    var result = achievementId != null
                        ? await _gateway.ClaimAchievementAsync(achievementId, linked.Token)
                        : await _gateway.ClaimAccountLevelRewardAsync(level, linked.Token);

                    Apply(result);

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

        private void Apply(AchievementResult result)
        {
            if (!result.IsSuccess)
            {
                _state.Set(Current
                    .WithLoading(false)
                    .WithBusy(false)
                    .WithStatusMessage(LobbyOperationMessages.Describe(result.Status)));
                return;
            }

            _state.Set(Current
                .WithServerState(
                    result.AccountXp,
                    result.AccountLevel,
                    result.AchievementXp,
                    result.AchievementLevel,
                    BuildAchievements(result),
                    BuildAccountLevelRewards(result))
                .WithStatusMessage(string.Empty));

            // 两套系统各自拥有一个红点叶子，不合并：领完成就奖励不会把账号等级的红点也抹掉。
            _bus.Publish(new RedDotSourceChanged(
                RedDotPath.AchievementClaimableReward, Current.HasClaimableAchievement));
            _bus.Publish(new RedDotSourceChanged(
                RedDotPath.AccountLevelClaimableReward, Current.HasClaimableAccountLevel));
        }

        /// <summary>
        /// 合并配置与服务端进度。
        ///
        /// 事件源尚未接入的成就（<c>IsActiveInP1 == false</c>）一律显示 0 进度：
        /// 服务端也不会给它们累加，这里只是把这个事实照实呈现，不做任何本地推算。
        /// </summary>
        private IReadOnlyList<AchievementSnapshot> BuildAchievements(AchievementResult result)
        {
            var configured = Achievements;
            var rows = new List<AchievementSnapshot>(configured.Count);
            foreach (var achievement in configured)
            {
                long progress = 0;
                foreach (var record in result.Progress)
                {
                    if (string.Equals(record.AchievementId, achievement.AchievementId, StringComparison.Ordinal))
                    {
                        progress = record.Progress;
                        break;
                    }
                }

                var isClaimed = false;
                foreach (var claimed in result.ClaimedAchievements)
                {
                    if (string.Equals(claimed, achievement.AchievementId, StringComparison.Ordinal))
                    {
                        isClaimed = true;
                        break;
                    }
                }

                rows.Add(new AchievementSnapshot(
                    achievement.AchievementId,
                    achievement.Category,
                    achievement.IsActiveInP1 ? progress : 0,
                    achievement.TargetProgress,
                    isClaimed,
                    achievement.IsActiveInP1));
            }

            return rows;
        }

        private IReadOnlyList<AccountLevelRewardSnapshot> BuildAccountLevelRewards(AchievementResult result)
        {
            var configured = AccountLevelRewards;
            var rows = new List<AccountLevelRewardSnapshot>(configured.Count);
            foreach (var reward in configured)
            {
                // 服务端以 RewardId 作为领取键写入 account_reward_claims，这里用同一个键对账。
                var isClaimed = false;
                foreach (var claimed in result.ClaimedAccountLevels)
                {
                    if (string.Equals(claimed, reward.RewardId, StringComparison.Ordinal))
                    {
                        isClaimed = true;
                        break;
                    }
                }

                var hasReward = !string.Equals(
                    reward.RewardKind, ConfigRewardKind.None, StringComparison.Ordinal);
                rows.Add(new AccountLevelRewardSnapshot(
                    reward.Level, hasReward, result.AccountLevel >= reward.Level, isClaimed));
            }

            return rows;
        }

        private void ApplyLobbyState(LobbyPresentationState lobby)
        {
            var isOpen = lobby.IsFeatureOpen && lobby.OpenFeature == LobbyFeature.AccountLevelReward;
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
            private readonly AchievementController _owner;

            public LobbyStateObserver(AchievementController owner) => _owner = owner;

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
