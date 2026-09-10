using System;
using System.Collections.Generic;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Features.Lobby.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Naraka.Features.Lobby.View
{
    /// <summary>
    /// 头像与头像框选择面板。
    ///
    /// 单元格由配置驱动：顺序来自配置的 SortOrder，贴图通过配置的 IconKey 在
    /// <see cref="LobbyAppearanceCatalog"/> 中查表得到。选中状态与提交都使用稳定配置 ID，
    /// 因此配置中插入一个头像不会让玩家已保存的选择错位。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyAppearanceView :
        MonoBehaviour,
        IView<LobbyPresentationState>,
        IObserver<LobbyPresentationState>
    {
        private const string CellClass = "appearance-cell";
        private const string CellSelectedClass = "appearance-cell--selected";
        private const string TabActiveClass = "appearance-tab--active";

        [SerializeField] private VisualTreeAsset appearanceLayout;
        [SerializeField] private LobbyAppearanceCatalog catalog;

        /// <summary>滚轮每一格滚动的像素量。默认约两行单元格，值越大滚动越快。</summary>
        [SerializeField] private float mouseWheelScrollSize = 260f;

        private readonly List<KeyValuePair<Button, Action>> _handlers =
            new List<KeyValuePair<Button, Action>>();

        private ILobbyController _controller;
        private IGameConfigProvider _config;
        private IDisposable _subscription;
        private VisualElement _screen;
        private VisualElement _window;
        private Label _title;
        private Label _status;
        private Button _closeButton;
        private Button _avatarTab;
        private Button _frameTab;
        private AppearanceCell[] _avatarCells = Array.Empty<AppearanceCell>();
        private AppearanceCell[] _frameCells = Array.Empty<AppearanceCell>();
        private bool _wasOpen;

        [Inject]
        public void Construct(ILobbyController controller, IGameConfigProvider config)
        {
            _controller = controller;
            _config = config;
        }

        /// <summary>一个单元格与它代表的稳定配置 ID。ID 而不是下标才是选中判定的依据。</summary>
        private readonly struct AppearanceCell
        {
            public AppearanceCell(Button button, string id)
            {
                Button = button;
                Id = id;
            }

            public Button Button { get; }

            public string Id { get; }
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

            _screen.style.display = state.IsAppearanceOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!state.IsAppearanceOpen)
            {
                _wasOpen = false;
                return;
            }

            if (!_wasOpen)
            {
                _wasOpen = true;
                LobbyPanelAnimation.PlayDrop(_window);
            }

            var showAvatars = state.AppearanceTab == LobbyAppearanceTab.Avatar;
            _title.text = showAvatars ? "头像" : "头像框";
            _avatarTab.EnableInClassList(TabActiveClass, showAvatars);
            _frameTab.EnableInClassList(TabActiveClass, !showAvatars);

            ApplyStatus(state);

            // 尚未取得服务端资料时不高亮任何一格，也不允许提交：
            // 没有权威数据就显示一个"已选中"是在伪造状态；
            // 而且资料未知时提交会把玩家原本的头像框意外改成默认框。
            ApplyCells(_avatarCells, showAvatars, state.SelectedAvatarId, state);
            ApplyCells(_frameCells, !showAvatars, state.SelectedAvatarFrameId, state);
        }

        public void OnNext(LobbyPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        /// <summary>
        /// 显示"为什么现在不能换"。
        ///
        /// 单元格在资料未到时是禁用的，如果面板上不说明原因，玩家只会看到一个点不动的界面。
        /// 资料链路已经写下了原因（旧云端未部署、传输失败等），这里只负责把它摊开；
        /// 其它链路（账号概要、开始游戏）的消息不属于这个面板，因此按来源过滤。
        /// </summary>
        private void ApplyStatus(LobbyPresentationState state)
        {
            if (_status == null)
            {
                return;
            }

            var message = state.StatusSource == LobbyStatusSource.Profile
                ? state.StatusMessage
                : string.Empty;

            if (message.Length == 0 && !state.HasProfile)
            {
                message = state.IsAppearanceSaving ? "正在保存…" : "正在读取账号资料，暂时无法更换外观。";
            }

            _status.text = message;
            _status.style.display = message.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static void ApplyCells(
            AppearanceCell[] cells,
            bool visible,
            string selectedId,
            LobbyPresentationState state)
        {
            var interactable = state.HasProfile && !state.IsAppearanceSaving;
            foreach (var cell in cells)
            {
                cell.Button.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                cell.Button.EnableInClassList(
                    CellSelectedClass,
                    visible && !string.IsNullOrEmpty(selectedId) &&
                    string.Equals(cell.Id, selectedId, StringComparison.Ordinal));
                cell.Button.SetEnabled(interactable);
            }
        }

        private bool TryBuild(VisualElement root)
        {
            if (appearanceLayout == null)
            {
                Debug.LogError(
                    "LobbyAppearanceView 未绑定 LobbyAppearance.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                return false;
            }

            appearanceLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("AppearanceScreen");
            _window = root.Q<VisualElement>("AppearanceWindow");
            _title = root.Q<Label>("AppearanceTitle");
            // 状态标签是后补的说明位；缺失时只是没有解释，不该让整个面板装配失败。
            _status = root.Q<Label>("AppearanceStatusLabel");
            _closeButton = root.Q<Button>("AppearanceCloseButton");
            _avatarTab = root.Q<Button>("AppearanceTabAvatar");
            _frameTab = root.Q<Button>("AppearanceTabFrame");
            var grid = root.Q<VisualElement>("AppearanceGrid");
            var scroll = root.Q<ScrollView>("AppearanceScroll");
            if (_screen == null || _window == null || _title == null || _closeButton == null ||
                _avatarTab == null || _frameTab == null || grid == null || scroll == null)
            {
                Debug.LogError("LobbyAppearance.uxml 缺少必需的元素名称，外观面板未能装配。", this);
                _screen = null;
                return false;
            }

            // 隐藏滚动条本身，但保留滚轮滚动能力；同时加大每格滚动量让下滑更跟手。
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.mouseWheelScrollSize = mouseWheelScrollSize;

            _screen.style.display = DisplayStyle.None;
            Bind(_closeButton, () => _controller.CloseAppearance());
            Bind(_avatarTab, () => _controller.SelectAppearanceTab(LobbyAppearanceTab.Avatar));
            Bind(_frameTab, () => _controller.SelectAppearanceTab(LobbyAppearanceTab.Frame));
            if (catalog == null)
            {
                Debug.LogError(
                    "LobbyAppearanceView 未绑定 LobbyAppearanceCatalog；请运行菜单 NARAKA/Setup/Apply P0 Project Settings。",
                    this);
                _screen = null;
                return false;
            }

            if (_config == null || !_config.IsLoaded)
            {
                var reason = _config == null ? "配置未注入" : _config.LoadError;
                Debug.LogError("外观面板需要客户端配置，但配置不可用：" + reason, this);
                _title.text = "配置不可用";
                return true;
            }

            var catalogData = _config.Catalog;
            _avatarCells = BuildCells(
                grid,
                catalogData.AvatarsInDisplayOrder,
                avatar => avatar.AvatarId,
                avatar => avatar.IconKey,
                id => _controller.SelectAvatar(id));
            _frameCells = BuildCells(
                grid,
                catalogData.AvatarFramesInDisplayOrder,
                frame => frame.AvatarFrameId,
                frame => frame.IconKey,
                id => _controller.SelectFrame(id));

            if (_avatarCells.Length == 0 && _frameCells.Length == 0)
            {
                Debug.LogWarning(
                    "外观面板没有可显示的头像或头像框；请检查配置的 IconKey 与 " +
                    "NARAKA/Setup/Rescan Appearance Catalog 扫描结果是否匹配。",
                    this);
            }

            return true;
        }

        /// <summary>
        /// 按配置顺序生成单元格。缺少贴图的条目会被跳过并留下警告：
        /// 一个没有图的按钮在界面上等同于不存在，静默保留只会让人以为图挂掉了。
        /// </summary>
        private AppearanceCell[] BuildCells<T>(
            VisualElement grid,
            IReadOnlyList<T> entries,
            Func<T, string> idSelector,
            Func<T, string> iconSelector,
            Action<string> onSelected)
        {
            var cells = new List<AppearanceCell>(entries.Count);
            foreach (var entry in entries)
            {
                var id = idSelector(entry);
                var texture = catalog.GetByKey(iconSelector(entry));
                if (texture == null)
                {
                    Debug.LogWarning(
                        "外观目录缺少贴图 " + iconSelector(entry) + "（配置 ID " + id + "）。",
                        this);
                    continue;
                }

                var cell = new Button { name = "AppearanceCell_" + id };
                cell.AddToClassList(CellClass);
                cell.style.backgroundImage = Background.FromTexture2D(texture);
                Bind(cell, () => onSelected(id));
                grid.Add(cell);
                cells.Add(new AppearanceCell(cell, id));
            }

            return cells.ToArray();
        }

        // 保存委托实例，否则 OnDestroy 里用新建的 lambda 无法解除订阅。
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
