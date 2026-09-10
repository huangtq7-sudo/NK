using System;
using Naraka.Core.Application.MVC;
using Naraka.Features.Lobby.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Lobby.View
{
    /// <summary>
    /// 十个大厅功能入口共用的面板。打开方式与外观面板一致：从屏幕上方掉落并回弹。
    /// 面板目前只有背景框、标题与占位说明，具体功能在后续模块接入。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyFeaturePanelView :
        MonoBehaviour,
        IView<LobbyPresentationState>,
        IObserver<LobbyPresentationState>
    {
        [SerializeField] private VisualTreeAsset featurePanelLayout;
        [SerializeField] private LobbyFeaturePanelCatalog catalog;

        private ILobbyController _controller;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _window;
        private VisualElement _panel;
        private Label _title;
        private Label _message;
        private Button _closeButton;
        private Action _closeHandler;
        private bool _wasOpen;

        [Inject]
        public void Construct(ILobbyController controller)
        {
            _controller = controller;
        }

        private void Start()
        {
            if (!TryBuild(GetComponent<UIDocument>().rootVisualElement))
            {
                return;
            }

            _subscription = _controller.Subscribe(this);
        }

        public void Render(LobbyPresentationState state)
        {
            if (_screen == null)
            {
                return;
            }

            // 已经拥有专用界面的入口不再显示这个通用占位面板，否则两层面板会叠在一起。
            var showsPlaceholder = state.IsFeatureOpen && !HasDedicatedPanel(state.OpenFeature);
            _screen.style.display = showsPlaceholder ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showsPlaceholder)
            {
                _wasOpen = false;
                return;
            }

            var background = catalog == null ? null : catalog.GetBackground(state.OpenFeature);
            if (background != null)
            {
                _panel.style.backgroundImage = Background.FromTexture2D(background);
            }

            _title.text = ToTitle(state.OpenFeature);
            _message.text = state.StatusMessage;
            if (!_wasOpen)
            {
                _wasOpen = true;
                // 面板必须盖住大厅；只在转为可见时提层，避免每帧重排。
                _screen.BringToFront();
                LobbyPanelAnimation.PlayDrop(_window);
            }
        }

        /// <summary>
        /// 是否已有专用界面。新增专用界面时必须同步登记，否则通用占位面板会盖在它上面。
        /// </summary>
        /// <summary>
        /// P1.9 之后十个入口都有了专属面板，因此占位面板不再为任何功能显示。
        ///
        /// 保留这个组件而不删除，是因为专属面板都是可选视图：将来新增一个入口时，
        /// 它仍然是那个入口在拿到自己的面板之前的落脚点。
        /// </summary>
        private static bool HasDedicatedPanel(LobbyFeature feature) =>
            feature == LobbyFeature.Hero ||
            feature == LobbyFeature.Weapon ||
            feature == LobbyFeature.Inventory ||
            feature == LobbyFeature.Shop ||
            feature == LobbyFeature.Forge ||
            feature == LobbyFeature.Draw ||
            feature == LobbyFeature.CheckIn ||
            feature == LobbyFeature.AccountLevelReward ||
            feature == LobbyFeature.Friends ||
            feature == LobbyFeature.Chat;

        public void OnNext(LobbyPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        // 入口名称属于界面文案，与 LobbyMain.uxml 上的 tooltip 保持一致，由View负责。
        private static string ToTitle(LobbyFeature feature) => feature switch
        {
            LobbyFeature.Hero => "英雄",
            LobbyFeature.Weapon => "兵器",
            LobbyFeature.Forge => "锻造",
            LobbyFeature.Shop => "商店",
            LobbyFeature.Inventory => "仓库",
            LobbyFeature.CheckIn => "签到",
            LobbyFeature.Draw => "抽奖",
            LobbyFeature.AccountLevelReward => "账号等级奖励",
            LobbyFeature.Friends => "好友",
            LobbyFeature.Chat => "聊天",
            _ => string.Empty
        };

        private bool TryBuild(VisualElement root)
        {
            if (featurePanelLayout == null)
            {
                Debug.LogError(
                    "LobbyFeaturePanelView 未绑定 LobbyFeaturePanel.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return false;
            }

            featurePanelLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("FeatureScreen");
            _window = _screen?.Q<VisualElement>("FeatureWindow");
            _panel = _screen?.Q<VisualElement>("FeaturePanel");
            _title = _screen?.Q<Label>("FeatureTitle");
            _message = _screen?.Q<Label>("FeatureMessage");
            _closeButton = _screen?.Q<Button>("FeatureCloseButton");
            if (_screen == null || _window == null || _panel == null ||
                _title == null || _message == null || _closeButton == null)
            {
                Debug.LogError("LobbyFeaturePanel.uxml 缺少必需的元素名称，功能面板未能装配。", this);
                _screen = null;
                return false;
            }

            if (catalog == null)
            {
                Debug.LogError(
                    "LobbyFeaturePanelView 未绑定 LobbyFeaturePanelCatalog；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;
            // 保存委托实例，否则 OnDestroy 里用新建的 lambda 无法解除订阅。
            _closeHandler = () => _controller.CloseFeature();
            _closeButton.clicked += _closeHandler;
            return true;
        }

        private void OnDestroy()
        {
            if (_closeButton != null && _closeHandler != null)
            {
                _closeButton.clicked -= _closeHandler;
            }

            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
        }
    }
}
