using System;
using System.Collections.Generic;
using System.Globalization;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Features.Gacha.Controller;
using Naraka.Features.Loadout.View;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Gacha.View
{
    /// <summary>
    /// 抽奖界面。
    ///
    /// 结果卡片画的永远是服务端已经固化的订单，动画只是表现：跳过、关闭甚至断线都不会改变
    /// 任何一个奖励。有未确认结果时禁止再次抽奖，玩家因此不会漏看任何一次奖励。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GachaPanelView :
        MonoBehaviour,
        IView<GachaPresentationState>,
        IObserver<GachaPresentationState>
    {
        /// <summary>品质到卡背贴图的映射。红色暂用玄夜卡背，正式素材到位后替换。</summary>
        private static readonly Dictionary<string, string> QualityCardKeys =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ConfigQuality.White] = "gacha_card_white",
                [ConfigQuality.Blue] = "gacha_card_blue",
                [ConfigQuality.Purple] = "gacha_card_purple",
                [ConfigQuality.Gold] = "gacha_card_gold",
                [ConfigQuality.Red] = "gacha_card_dark"
            };

        [SerializeField] private VisualTreeAsset gachaLayout;
        [SerializeField] private LoadoutIconCatalog icons;

        private readonly List<KeyValuePair<Button, Action>> _handlers =
            new List<KeyValuePair<Button, Action>>();

        private IGachaController _controller;
        private IGameConfigProvider _config;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _previewList;
        private VisualElement _resultOverlay;
        private VisualElement _resultList;
        private Label _poolName;
        private Label _pity;
        private Label _totalPulls;
        private Label _pendingNotice;
        private Label _singlePrice;
        private Label _tenPrice;
        private Label _resultTitle;
        private Label _status;
        private Button _pullOnce;
        private Button _pullTen;
        private Button _skip;
        private Button _confirm;
        private Button _close;
        private string _builtPreviewPoolId = string.Empty;
        private string _builtResultOrderId = string.Empty;

        [Inject]
        public void Construct(IGachaController controller, IGameConfigProvider config)
        {
            _controller = controller;
            _config = config;
        }

        private void Start()
        {
            if (!TryBuild(GetComponent<UIDocument>().rootVisualElement))
            {
                return;
            }

            _subscription = _controller.Subscribe(this);
        }

        public void Render(GachaPresentationState state)
        {
            if (_screen == null)
            {
                return;
            }

            _screen.style.display = state.IsOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!state.IsOpen)
            {
                return;
            }

            _status.text = state.StatusMessage;
            _status.style.display = string.IsNullOrEmpty(state.StatusMessage)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            var pool = ResolvePool(state);
            if (pool == null)
            {
                _poolName.text = state.IsLoading ? "正在读取抽奖数据…" : "抽奖数据不可用";
                _pity.text = string.Empty;
                _totalPulls.text = string.Empty;
                _singlePrice.text = string.Empty;
                _tenPrice.text = string.Empty;
                _pullOnce.SetEnabled(false);
                _pullTen.SetEnabled(false);
                _resultOverlay.style.display = DisplayStyle.None;
                return;
            }

            _poolName.text = pool.DisplayName;
            _pity.text = pool.PityCount > 0
                ? "距离" + DescribeQuality(pool.PityQuality) + "保底还差 " +
                  _controller.PullsUntilPity.ToString(CultureInfo.InvariantCulture) + " 抽"
                : string.Empty;
            _totalPulls.text = "累计抽取 " + state.TotalPulls.ToString(CultureInfo.InvariantCulture) + " 次";

            var currency = _config.Catalog.GetCurrencyDisplayName(pool.CurrencyId);
            _singlePrice.text = pool.SinglePrice.ToString(CultureInfo.InvariantCulture) + " " + currency;
            _tenPrice.text = pool.TenPullPrice.ToString(CultureInfo.InvariantCulture) + " " + currency;

            _pendingNotice.text = state.HasPendingResult ? "有尚未查看的抽奖结果" : string.Empty;
            _pendingNotice.style.display = state.HasPendingResult ? DisplayStyle.Flex : DisplayStyle.None;

            // 有未确认结果时禁止再抽：先让玩家看完上一次的奖励。
            var canPull = state.HasServerState && !state.IsBusy && !state.HasPendingResult;
            _pullOnce.SetEnabled(canPull);
            _pullTen.SetEnabled(canPull);

            RebuildPreview(pool);
            RenderResult(state);
        }

        public void OnNext(GachaPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
            if (_status != null)
            {
                _status.text = "抽奖界面发生错误。";
                _status.style.display = DisplayStyle.Flex;
            }
        }

        public void OnCompleted()
        {
        }

        private GachaPoolConfig ResolvePool(GachaPresentationState state)
        {
            if (_config == null || !_config.IsLoaded)
            {
                return null;
            }

            var poolId = state.PoolId.Length > 0
                ? state.PoolId
                : _config.Catalog.Catalog.GachaPools.Length > 0
                    ? _config.Catalog.Catalog.GachaPools[0].PoolId
                    : string.Empty;

            return _config.Catalog.TryGetGachaPool(poolId, out var pool) ? pool : null;
        }

        /// <summary>奖励预览来自配置，只在切换奖池时重建。</summary>
        private void RebuildPreview(GachaPoolConfig pool)
        {
            if (string.Equals(_builtPreviewPoolId, pool.PoolId, StringComparison.Ordinal))
            {
                return;
            }

            _previewList.Clear();
            foreach (var entry in _config.Catalog.Catalog.GachaEntries)
            {
                if (!string.Equals(entry.PoolId, pool.PoolId, StringComparison.Ordinal))
                {
                    continue;
                }

                var label = new Label(
                    DescribeQuality(entry.Quality) + "  " +
                    _config.Catalog.GetItemDisplayName(entry.ItemId) + " × " +
                    entry.Amount.ToString(CultureInfo.InvariantCulture));
                label.AddToClassList("gacha-preview-entry");
                label.pickingMode = PickingMode.Ignore;
                _previewList.Add(label);
            }

            _builtPreviewPoolId = pool.PoolId;
        }

        private void RenderResult(GachaPresentationState state)
        {
            var order = state.PendingOrder;
            if (order == null)
            {
                _resultOverlay.style.display = DisplayStyle.None;
                _builtResultOrderId = string.Empty;
                return;
            }

            _resultOverlay.style.display = DisplayStyle.Flex;
            _resultTitle.text = state.IsAnimating
                ? "抽奖中…"
                : order.PullCount >= GachaPresentationState.TenPullCount ? "十连结果" : "抽奖结果";

            if (!string.Equals(_builtResultOrderId, order.OrderId, StringComparison.Ordinal))
            {
                _resultList.Clear();
                foreach (var reward in order.Rewards)
                {
                    _resultList.Add(BuildCard(reward));
                }

                _builtResultOrderId = order.OrderId;
            }

            // 动画期间只允许跳过；跳过后才允许确认。结果本身两种情况下完全一致。
            _skip.style.display = state.IsAnimating ? DisplayStyle.Flex : DisplayStyle.None;
            _confirm.SetEnabled(!state.IsAnimating);
        }

        private VisualElement BuildCard(GachaRewardSnapshot reward)
        {
            var card = new VisualElement { name = "GachaCard_" + reward.RewardId };
            card.AddToClassList("gacha-result-card");
            card.pickingMode = PickingMode.Ignore;

            if (icons != null && QualityCardKeys.TryGetValue(reward.Quality, out var cardKey))
            {
                var background = icons.GetByKey(cardKey);
                if (background != null)
                {
                    card.style.backgroundImage = Background.FromTexture2D(background);
                }
            }

            var label = new Label(
                _config.Catalog.GetItemDisplayName(reward.ItemId) + " × " +
                reward.Amount.ToString(CultureInfo.InvariantCulture));
            label.AddToClassList("gacha-result-card-label");
            label.pickingMode = PickingMode.Ignore;
            card.Add(label);
            return card;
        }

        private static string DescribeQuality(string quality)
        {
            switch (quality)
            {
                case ConfigQuality.White: return "白";
                case ConfigQuality.Blue: return "蓝";
                case ConfigQuality.Purple: return "紫";
                case ConfigQuality.Gold: return "金";
                case ConfigQuality.Red: return "红";
                default: return quality;
            }
        }

        private bool TryBuild(VisualElement root)
        {
            if (gachaLayout == null)
            {
                Debug.LogError(
                    "GachaPanelView 未绑定 GachaPanel.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return false;
            }

            gachaLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("GachaScreen");
            _previewList = root.Q<VisualElement>("GachaPreviewList");
            _resultOverlay = root.Q<VisualElement>("GachaResultOverlay");
            _resultList = root.Q<VisualElement>("GachaResultList");
            _poolName = root.Q<Label>("GachaPoolNameLabel");
            _pity = root.Q<Label>("GachaPityLabel");
            _totalPulls = root.Q<Label>("GachaTotalPullsLabel");
            _pendingNotice = root.Q<Label>("GachaPendingNoticeLabel");
            _singlePrice = root.Q<Label>("GachaSinglePriceLabel");
            _tenPrice = root.Q<Label>("GachaTenPriceLabel");
            _resultTitle = root.Q<Label>("GachaResultTitle");
            _status = root.Q<Label>("GachaStatusLabel");
            _pullOnce = root.Q<Button>("GachaPullOnceButton");
            _pullTen = root.Q<Button>("GachaPullTenButton");
            _skip = root.Q<Button>("GachaSkipButton");
            _confirm = root.Q<Button>("GachaConfirmButton");
            _close = root.Q<Button>("GachaCloseButton");

            if (_screen == null || _previewList == null || _resultOverlay == null || _resultList == null ||
                _poolName == null || _pity == null || _totalPulls == null || _pendingNotice == null ||
                _singlePrice == null || _tenPrice == null || _resultTitle == null || _status == null ||
                _pullOnce == null || _pullTen == null || _skip == null || _confirm == null || _close == null)
            {
                Debug.LogError("GachaPanel.uxml 缺少必需的元素名称，抽奖界面未能装配。", this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;
            _resultOverlay.style.display = DisplayStyle.None;

            Bind(_pullOnce, () => _controller.PullOnce());
            Bind(_pullTen, () => _controller.PullTen());
            Bind(_skip, () => _controller.SkipAnimation());
            Bind(_confirm, () => _controller.ConfirmResult());
            Bind(_close, () => _controller.Close());
            return true;
        }

        private void Bind(Button button, Action handler)
        {
            button.clicked += handler;
            _handlers.Add(new KeyValuePair<Button, Action>(button, handler));
        }

        private void OnDestroy()
        {
            foreach (var entry in _handlers)
            {
                entry.Key.clicked -= entry.Value;
            }

            _handlers.Clear();
            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
        }
    }
}
