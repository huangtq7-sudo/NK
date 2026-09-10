using Naraka.Core.Application.MVC;

namespace Naraka.Features.Gacha.Controller
{
    /// <summary>
    /// 抽奖界面的只读展示状态。
    ///
    /// <see cref="PendingOrder"/> 是"还没让玩家看过"的订单：它可能来自刚刚的一次抽奖，
    /// 也可能是上次断线遗留下来的。无论哪种，奖励都已经入库，界面只是补上展示。
    /// </summary>
    public readonly struct GachaPresentationState : IPresentationState
    {
        public const int TenPullCount = 10;

        public GachaPresentationState(
            bool isOpen,
            bool isLoading,
            bool isBusy,
            bool hasServerState,
            string poolId,
            int pityCounter,
            long totalPulls,
            long copper,
            long silk,
            long gold,
            GachaOrderSnapshot pendingOrder,
            bool isAnimating,
            string statusMessage)
        {
            IsOpen = isOpen;
            IsLoading = isLoading;
            IsBusy = isBusy;
            HasServerState = hasServerState;
            PoolId = poolId ?? string.Empty;
            PityCounter = pityCounter;
            TotalPulls = totalPulls;
            Copper = copper;
            Silk = silk;
            Gold = gold;
            PendingOrder = pendingOrder;
            IsAnimating = isAnimating && pendingOrder != null;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public static GachaPresentationState Initial => new GachaPresentationState(
            false, false, false, false, string.Empty, 0, 0, 0, 0, 0, null, false, string.Empty);

        public bool IsOpen { get; }

        public bool IsLoading { get; }

        /// <summary>抽奖请求在途。期间禁用两个抽奖按钮，避免重复点击产生两笔订单。</summary>
        public bool IsBusy { get; }

        public bool HasServerState { get; }

        public string PoolId { get; }

        /// <summary>距离上一次保底品质已经抽了多少次。</summary>
        public int PityCounter { get; }

        public long TotalPulls { get; }

        public long Copper { get; }

        public long Silk { get; }

        public long Gold { get; }

        /// <summary>尚未确认展示的订单。为 null 表示没有待展示结果。</summary>
        public GachaOrderSnapshot PendingOrder { get; }

        /// <summary>动画正在播放。跳过只会把它置为 false，不改变结果。</summary>
        public bool IsAnimating { get; }

        public string StatusMessage { get; }

        /// <summary>是否存在需要玩家确认的结果。断线恢复时也为 true。</summary>
        public bool HasPendingResult => PendingOrder != null;

        public long BalanceOf(string currencyId)
        {
            switch (currencyId)
            {
                case "Copper": return Copper;
                case "Silk": return Silk;
                case "Gold": return Gold;
                default: return 0L;
            }
        }

        public GachaPresentationState WithOpen(bool isOpen) => Copy(isOpen: isOpen);

        public GachaPresentationState WithLoading(bool isLoading) => Copy(isLoading: isLoading);

        public GachaPresentationState WithBusy(bool isBusy) => Copy(isBusy: isBusy);

        public GachaPresentationState WithServerState(
            string poolId, int pityCounter, long totalPulls, long copper, long silk, long gold) =>
            Copy(
                hasServerState: true,
                isLoading: false,
                isBusy: false,
                poolId: poolId,
                pityCounter: pityCounter,
                totalPulls: totalPulls,
                copper: copper,
                silk: silk,
                gold: gold);

        public GachaPresentationState WithPendingOrder(GachaOrderSnapshot order) =>
            new GachaPresentationState(
                IsOpen, IsLoading, IsBusy, HasServerState, PoolId, PityCounter, TotalPulls,
                Copper, Silk, Gold, order, IsAnimating, StatusMessage);

        public GachaPresentationState WithAnimating(bool isAnimating) => Copy(isAnimating: isAnimating);

        public GachaPresentationState WithStatusMessage(string statusMessage) =>
            Copy(statusMessage: statusMessage);

        private GachaPresentationState Copy(
            bool? isOpen = null,
            bool? isLoading = null,
            bool? isBusy = null,
            bool? hasServerState = null,
            string poolId = null,
            int? pityCounter = null,
            long? totalPulls = null,
            long? copper = null,
            long? silk = null,
            long? gold = null,
            bool? isAnimating = null,
            string statusMessage = null) =>
            new GachaPresentationState(
                isOpen ?? IsOpen,
                isLoading ?? IsLoading,
                isBusy ?? IsBusy,
                hasServerState ?? HasServerState,
                poolId ?? PoolId,
                pityCounter ?? PityCounter,
                totalPulls ?? TotalPulls,
                copper ?? Copper,
                silk ?? Silk,
                gold ?? Gold,
                PendingOrder,
                isAnimating ?? IsAnimating,
                statusMessage ?? StatusMessage);
    }
}
