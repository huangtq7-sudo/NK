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

namespace Naraka.Features.Gacha.Controller
{
    /// <summary>抽出的一个奖励。</summary>
    public readonly struct GachaRewardSnapshot
    {
        public GachaRewardSnapshot(string rewardId, string itemId, int amount, string quality)
        {
            RewardId = rewardId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
            Amount = amount;
            Quality = quality ?? string.Empty;
        }

        public string RewardId { get; }

        public string ItemId { get; }

        public int Amount { get; }

        public string Quality { get; }
    }

    /// <summary>一张已经固化的抽奖订单。服务端先写下它，客户端再播动画。</summary>
    public sealed class GachaOrderSnapshot
    {
        public GachaOrderSnapshot(
            string orderId,
            string poolId,
            int pullCount,
            bool isShown,
            IReadOnlyList<GachaRewardSnapshot> rewards)
        {
            OrderId = orderId ?? string.Empty;
            PoolId = poolId ?? string.Empty;
            PullCount = pullCount;
            IsShown = isShown;
            Rewards = rewards ?? Array.Empty<GachaRewardSnapshot>();
        }

        public string OrderId { get; }

        public string PoolId { get; }

        public int PullCount { get; }

        public bool IsShown { get; }

        public IReadOnlyList<GachaRewardSnapshot> Rewards { get; }

        public bool IsValid => OrderId.Length > 0 && Rewards.Count > 0;
    }

    public readonly struct GachaResult
    {
        private GachaResult(
            LobbyOperationStatus status,
            bool hasState,
            string poolId,
            int pityCounter,
            long totalPulls,
            IReadOnlyList<GachaOrderSnapshot> unshownOrders,
            GachaOrderSnapshot order,
            long copper,
            long silk,
            long gold)
        {
            Status = status;
            HasState = hasState;
            PoolId = poolId ?? string.Empty;
            PityCounter = pityCounter;
            TotalPulls = totalPulls;
            UnshownOrders = unshownOrders ?? Array.Empty<GachaOrderSnapshot>();
            Order = order;
            Copper = copper;
            Silk = silk;
            Gold = gold;
        }

        public LobbyOperationStatus Status { get; }

        public bool HasState { get; }

        public string PoolId { get; }

        public int PityCounter { get; }

        public long TotalPulls { get; }

        public IReadOnlyList<GachaOrderSnapshot> UnshownOrders { get; }

        /// <summary>本次抽奖固化的订单。只有抽奖响应会带它。</summary>
        public GachaOrderSnapshot Order { get; }

        public long Copper { get; }

        public long Silk { get; }

        public long Gold { get; }

        public bool IsSuccess => Status == LobbyOperationStatus.Success;

        public static GachaResult Success(
            string poolId,
            int pityCounter,
            long totalPulls,
            IReadOnlyList<GachaOrderSnapshot> unshownOrders,
            GachaOrderSnapshot order,
            long copper,
            long silk,
            long gold) =>
            new GachaResult(
                LobbyOperationStatus.Success, true, poolId, pityCounter, totalPulls,
                unshownOrders, order, copper, silk, gold);

        public static GachaResult Failed(LobbyOperationStatus status) =>
            new GachaResult(status, false, string.Empty, 0, 0, null, null, 0, 0, 0);
    }

    /// <summary>
    /// 抽奖网关。奖池、价格与权重都在两端共享的配置里，网络上只传保底计数、余额与固化后的订单。
    /// 客户端不生成任何随机结果。
    /// </summary>
    public interface IGachaGateway
    {
        UniTask<GachaResult> RequestGachaAsync(string poolId, CancellationToken cancellationToken);

        UniTask<GachaResult> PullAsync(
            string poolId,
            int pullCount,
            string orderId,
            CancellationToken cancellationToken);

        /// <summary>确认订单已经展示过。确认之后它才不再出现在未展示列表里。</summary>
        UniTask<GachaResult> AcknowledgeAsync(
            string poolId,
            string orderId,
            CancellationToken cancellationToken);
    }

    public interface IGachaController : IReadOnlyState<GachaPresentationState>
    {
        void Close();

        void PullOnce();

        void PullTen();

        /// <summary>跳过动画。结果不变，只是立刻显示最终画面。</summary>
        void SkipAnimation();

        /// <summary>玩家确认看过结果。确认后才向服务端标记已展示。</summary>
        void ConfirmResult();

        UniTask ReloadAsync(CancellationToken cancellationToken);

        /// <summary>距离硬保底还差多少抽。奖池未配置保底时为 0。</summary>
        int PullsUntilPity { get; }
    }

    /// <summary>
    /// 抽奖界面的编排。
    ///
    /// 流程是刻意的：请求服务端 → 服务端完成订单 → 客户端收到固化结果 → 播放动画 →
    /// 显示结果 → 用户确认 → 标记已展示。动画可以跳过，但<b>不会改变结果</b>；
    /// 中断后重新登录时，未确认的订单会被重新取回并继续展示。
    /// </summary>
    public sealed class GachaController : IController, IGachaController, IDisposable
    {
        private readonly IGameConfigProvider _config;
        private readonly IGachaGateway _gateway;
        private readonly ILobbyController _lobby;
        private readonly IServerCapabilities _capabilities;
        private readonly IDomainEventBus _bus;
        private readonly ReactiveState<GachaPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly IDisposable _lobbySubscription;
        private bool _isLoading;
        private bool _isPulling;
        private bool _wasOpen;

        public GachaController(
            IGameConfigProvider config,
            IGachaGateway gateway,
            ILobbyController lobby,
            IServerCapabilities capabilities,
            IDomainEventBus bus)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));

            _state = new ReactiveState<GachaPresentationState>(GachaPresentationState.Initial);
            _lobbySubscription = _lobby.Subscribe(new LobbyStateObserver(this));
            ApplyLobbyState(_lobby.Current);
        }

        public GachaPresentationState Current => _state.Current;

        /// <summary>当前奖池配置。配置未加载或奖池缺失时为 null。</summary>
        public GachaPoolConfig Pool
        {
            get
            {
                if (!_config.IsLoaded)
                {
                    return null;
                }

                var poolId = Current.PoolId.Length > 0 ? Current.PoolId : DefaultPoolId;
                return _config.Catalog.TryGetGachaPool(poolId, out var pool) ? pool : null;
            }
        }

        public int PullsUntilPity
        {
            get
            {
                var pool = Pool;
                if (pool == null || pool.PityCount <= 0)
                {
                    return 0;
                }

                var remaining = pool.PityCount - Current.PityCounter;
                return remaining < 0 ? 0 : remaining;
            }
        }

        public void Close() => _lobby.CloseFeature();

        public void PullOnce() => PullAsync(1).Forget();

        public void PullTen() => PullAsync(GachaPresentationState.TenPullCount).Forget();

        public void SkipAnimation()
        {
            if (!Current.IsAnimating)
            {
                return;
            }

            // 跳过只影响表现：结果早已由服务端固化，这里不重新计算任何东西。
            _state.Set(Current.WithAnimating(false));
        }

        public void ConfirmResult()
        {
            var state = Current;
            if (state.PendingOrder == null)
            {
                return;
            }

            AcknowledgeAsync(state.PendingOrder.OrderId).Forget();
        }

        public async UniTask ReloadAsync(CancellationToken cancellationToken)
        {
            if (_isLoading)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Gacha))
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
                    Apply(await _gateway.RequestGachaAsync(DefaultPoolId, linked.Token), fromPull: false);
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

        public IDisposable Subscribe(IObserver<GachaPresentationState> observer) => _state.Subscribe(observer);

        public void Dispose()
        {
            _lobbySubscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        private string DefaultPoolId
        {
            get
            {
                if (Current.PoolId.Length > 0)
                {
                    return Current.PoolId;
                }

                return _config.IsLoaded && _config.Catalog.Catalog.GachaPools.Length > 0
                    ? _config.Catalog.Catalog.GachaPools[0].PoolId
                    : string.Empty;
            }
        }

        private async UniTaskVoid PullAsync(int pullCount)
        {
            var state = Current;
            if (_isPulling || !state.IsOpen)
            {
                return;
            }

            // 还有未确认展示的结果时不允许再抽：否则玩家会永远看不到上一次的奖励。
            if (state.PendingOrder != null)
            {
                _state.Set(state.WithStatusMessage("请先确认上一次的抽奖结果。"));
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Gacha))
            {
                _state.Set(state.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing)));
                return;
            }

            _isPulling = true;
            _state.Set(state.WithBusy(true).WithStatusMessage(string.Empty));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           CancellationToken.None, _lifetime.Token))
                {
                    var result = await _gateway.PullAsync(
                        DefaultPoolId, pullCount, Guid.NewGuid().ToString("N"), linked.Token);
                    Apply(result, fromPull: true);

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
                // 传输失败时结果可能已经在服务端固化：提示玩家重新打开界面即可取回，
                // 绝不在客户端伪造一个结果。
                _state.Set(Current
                    .WithBusy(false)
                    .WithStatusMessage("抽奖结果暂时无法确认，请重新打开抽奖界面查看。"));
            }
            finally
            {
                _isPulling = false;
            }
        }

        private async UniTaskVoid AcknowledgeAsync(string orderId)
        {
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           CancellationToken.None, _lifetime.Token))
                {
                    Apply(await _gateway.AcknowledgeAsync(DefaultPoolId, orderId, linked.Token), fromPull: false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // 确认失败不影响奖励归属：奖励早已入库，下次登录会再次展示。
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.TransportFailure)));
            }
        }

        /// <summary>
        /// 应用服务端结果。
        ///
        /// 待展示订单的来源只有两个：本次抽奖返回的订单，或服务端回传的未展示列表。
        /// 两者都来自数据库，因此界面上出现的每一个奖励都已经属于玩家。
        /// </summary>
        private void Apply(GachaResult result, bool fromPull)
        {
            if (!result.IsSuccess)
            {
                _state.Set(Current
                    .WithLoading(false)
                    .WithBusy(false)
                    .WithStatusMessage(LobbyOperationMessages.Describe(result.Status)));
                return;
            }

            var pending = fromPull && result.Order != null && result.Order.IsValid
                ? result.Order
                : FirstUnshown(result.UnshownOrders);

            var next = Current
                .WithServerState(result.PoolId, result.PityCounter, result.TotalPulls,
                    result.Copper, result.Silk, result.Gold)
                .WithPendingOrder(pending)
                .WithStatusMessage(string.Empty);

            // 只有刚刚拿到一个新订单才播放动画；恢复出来的旧订单直接显示结果，
            // 玩家已经等过一次动画了。
            _state.Set(next.WithAnimating(fromPull && pending != null));

            // 断线遗留的未展示订单会在重新登录后点亮抽奖入口的红点。
            _bus.Publish(new RedDotSourceChanged(
                RedDotPath.GachaUnshownResult, pending != null));
        }

        private static GachaOrderSnapshot FirstUnshown(IReadOnlyList<GachaOrderSnapshot> orders)
        {
            foreach (var order in orders)
            {
                if (order != null && order.IsValid && !order.IsShown)
                {
                    return order;
                }
            }

            return null;
        }

        private void ApplyLobbyState(LobbyPresentationState lobby)
        {
            var isOpen = lobby.IsFeatureOpen && lobby.OpenFeature == LobbyFeature.Draw;
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
            private readonly GachaController _owner;

            public LobbyStateObserver(GachaController owner) => _owner = owner;

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
