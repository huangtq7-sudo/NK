using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Config;
using Naraka.Core.Application.MVC;
using Naraka.Features.Lobby.Controller;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;
using VContainer;

namespace Naraka.Features.Lobby.View
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyView : MonoBehaviour, IView<LobbyPresentationState>, IObserver<LobbyPresentationState>
    {
        private const string StartingModifierClass = "btn-start-game--loading";

        [SerializeField] private VisualTreeAsset lobbyLayout;
        [SerializeField] private LobbyAppearanceCatalog catalog;

        /// <summary>大厅循环背景视频。未入库时保持为空，界面回退到UXML/USS里的静态底图。</summary>
        [SerializeField] private VideoClip backgroundClip;

        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<KeyValuePair<Button, Action>> _featureHandlers =
            new List<KeyValuePair<Button, Action>>();

        private ILobbyController _controller;
        private IGameConfigProvider _config;
        private IDisposable _subscription;
        private VisualElement _screen;
        private Label _playerName;
        private Label _playerLevel;
        private Label _copperAmount;
        private Label _silkAmount;
        private Label _goldAmount;
        private Label _playerAccountId;
        private Label _status;
        private Button _startGame;
        private Button _avatarButton;
        private VisualElement _avatarFrame;
        private VisualElement _background;
        private VideoPlayer _videoPlayer;
        private RenderTexture _videoTexture;

        [Inject]
        public void Construct(ILobbyController controller, IGameConfigProvider config)
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

        public void Render(LobbyPresentationState state)
        {
            if (_screen == null)
            {
                return;
            }

            _screen.style.display = state.IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!state.IsVisible)
            {
                return;
            }

            _playerName.text = state.Username;
            _playerAccountId.text = state.AccountId > 0 ? $"ID {state.AccountId}" : string.Empty;
            _status.text = state.StatusMessage;
            _status.style.display = string.IsNullOrEmpty(state.StatusMessage)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            _startGame.SetEnabled(!state.IsStartingGame);
            _startGame.EnableInClassList(StartingModifierClass, state.IsStartingGame);
            ApplyAppearance(state);
            ApplyAccountSummary(state);
            foreach (var entry in _featureHandlers)
            {
                entry.Key.SetEnabled(!state.IsStartingGame);
            }
        }

        public void OnNext(LobbyPresentationState value) => Render(value);

        public void OnError(Exception error)
        {
            if (_status != null)
            {
                _status.text = "大厅界面发生错误。";
                _status.style.display = DisplayStyle.Flex;
            }
        }

        public void OnCompleted()
        {
        }

        private bool TryBuild(VisualElement root)
        {
            if (lobbyLayout == null)
            {
                Debug.LogError(
                    "LobbyView 未绑定 LobbyMain.uxml；请运行菜单 NARAKA/Setup/Apply P0 Project Settings 重新装配启动场景。",
                    this);
                return false;
            }

            lobbyLayout.CloneTree(root);
            _screen = root.Q<VisualElement>("LobbyScreen");
            if (_screen == null)
            {
                Debug.LogError("LobbyMain.uxml 缺少 LobbyScreen 根元素，大厅界面未能装配。", this);
                return false;
            }

            _playerName = _screen.Q<Label>("PlayerNameLabel");
            // 这四个 Label 已经存在于用户完成的 LobbyMain.uxml 中，这里只查询和更新，不改布局。
            _playerLevel = _screen.Q<Label>("PlayerLevelLabel");
            _copperAmount = _screen.Q<Label>("CopperCurrencyAmount");
            _silkAmount = _screen.Q<Label>("SilkCurrencyAmount");
            _goldAmount = _screen.Q<Label>("GoldCurrencyAmount");
            _playerAccountId = _screen.Q<Label>("PlayerAccountIdLabel");
            _status = _screen.Q<Label>("LobbyStatusLabel");
            _startGame = _screen.Q<Button>("StartGameButton");
            _avatarButton = _screen.Q<Button>("PlayerAvatarButton");
            _avatarFrame = _screen.Q<VisualElement>("PlayerAvatarFrame");
            _background = _screen.Q<VisualElement>("LobbyBackground");
            if (_playerName == null || _playerAccountId == null || _status == null ||
                _startGame == null || _avatarButton == null || _avatarFrame == null)
            {
                Debug.LogError("LobbyMain.uxml 缺少大厅界面必需的元素名称，大厅界面未能装配。", this);
                _screen = null;
                return false;
            }

            _screen.style.display = DisplayStyle.None;
            _startGame.clicked += OnStartGameClicked;
            _avatarButton.clicked += OnAvatarClicked;
            SetupBackgroundVideo();
            BindFeature("HeroButton", LobbyFeature.Hero);
            BindFeature("WeaponButton", LobbyFeature.Weapon);
            BindFeature("ForgeButton", LobbyFeature.Forge);
            BindFeature("ShopButton", LobbyFeature.Shop);
            BindFeature("InventoryButton", LobbyFeature.Inventory);
            BindFeature("CheckInButton", LobbyFeature.CheckIn);
            BindFeature("DrawButton", LobbyFeature.Draw);
            BindFeature("AccountLevelRewardButton", LobbyFeature.AccountLevelReward);
            BindFeature("FriendsButton", LobbyFeature.Friends);
            BindFeature("ChatButton", LobbyFeature.Chat);
            return true;
        }

        private void BindFeature(string elementName, LobbyFeature feature)
        {
            var button = _screen.Q<Button>(elementName);
            if (button == null)
            {
                Debug.LogError($"LobbyMain.uxml 缺少大厅入口按钮 {elementName}。", this);
                return;
            }

            // 保存委托实例，否则 OnDestroy 里用新建的 lambda 无法解除订阅。
            Action handler = () => _controller.RequestFeature(feature);
            button.clicked += handler;
            _featureHandlers.Add(new KeyValuePair<Button, Action>(button, handler));
        }

        private void OnAvatarClicked() => _controller.OpenAppearance();

        /// <summary>
        /// 只有服务端返回过一次成功数据才显示真实数值；其余情况一律保持 UXML 里的占位，
        /// 既不伪造余额，也不把上一次成功的数据清成 0。
        /// </summary>
        private void ApplyAccountSummary(LobbyPresentationState state)
        {
            if (!state.HasAccountSummary)
            {
                return;
            }

            var summary = state.AccountSummary;
            if (_playerLevel != null)
            {
                _playerLevel.text = "Lv." + summary.AccountLevel;
            }

            SetCurrency(_copperAmount, summary.Copper);
            SetCurrency(_silkAmount, summary.Silk);
            SetCurrency(_goldAmount, summary.Gold);
        }

        private static void SetCurrency(Label label, long amount)
        {
            if (label != null)
            {
                label.text = amount.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 左下角头像与头像框。
        ///
        /// 资料尚未返回时回落到<b>配置里的默认外观</b>，而不是留空。此前这里在
        /// <c>HasProfile == false</c> 时直接返回，注释声称"保留 UXML 里的占位"——但 UXML 与 USS
        /// 都没有给这两个元素设过 <c>background-image</c>，于是实际表现是彻底空白：
        /// 玩家看不到头像，也看不出那里是一个可以点击更换外观的按钮。
        ///
        /// 空白窗口并不短：进入大厅时资料请求是异步的；而在旧云端兼容模式下资料协议根本没有部署，
        /// 资料永远不会到达，头像也就永远空着。
        ///
        /// 回落用的默认 ID 与服务端发给新账号的默认值是同一条规则，因此它不是在替玩家编造一个选择，
        /// 而是先画出"还没读到玩家资料时本来就该是的样子"；资料一旦到达立即被真实选择覆盖。
        /// </summary>
        private void ApplyAppearance(LobbyPresentationState state)
        {
            if (catalog == null || _config == null || !_config.IsLoaded)
            {
                return;
            }

            var avatarId = state.HasProfile ? state.SelectedAvatarId : _config.Catalog.DefaultAvatarId;
            var frameId = state.HasProfile
                ? state.SelectedAvatarFrameId
                : _config.Catalog.DefaultAvatarFrameId;

            if (_config.Catalog.TryGetAvatar(avatarId, out var avatarConfig))
            {
                ApplyTexture(_avatarButton, avatarConfig.IconKey);
            }

            if (_config.Catalog.TryGetAvatarFrame(frameId, out var frameConfig))
            {
                ApplyTexture(_avatarFrame, frameConfig.IconKey);
            }
        }

        /// <summary>
        /// 按 IconKey 取贴图并贴到元素上。
        ///
        /// 目录里没有这张贴图时保持元素原样：宁可少画一层，也不要把已经显示正确的头像清成空白。
        /// </summary>
        private void ApplyTexture(VisualElement element, string iconKey)
        {
            if (element == null)
            {
                return;
            }

            var texture = catalog.GetByKey(iconKey);
            if (texture != null)
            {
                element.style.backgroundImage = Background.FromTexture2D(texture);
            }
        }

        // 视频准备完成后才把背景切到RenderTexture，避免首帧出现未初始化的画面。
        private void SetupBackgroundVideo()
        {
            if (backgroundClip == null || _background == null)
            {
                return;
            }

            // RenderTexture 尺寸跟随片源，避免多一次缩放采样。
            var width = backgroundClip.width > 0 ? (int)backgroundClip.width : 1920;
            var height = backgroundClip.height > 0 ? (int)backgroundClip.height : 1080;
            _videoTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "LobbyBackgroundVideo"
            };
            _videoTexture.Create();
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.clip = backgroundClip;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _videoTexture;
            _videoPlayer.isLooping = true;
            _videoPlayer.waitForFirstFrame = true;
            // 无音轨的循环背景不需要与时钟对齐，逐帧播放比丢帧追赶更平滑。
            _videoPlayer.skipOnDrop = false;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.prepareCompleted += OnBackgroundVideoPrepared;
            _videoPlayer.Prepare();
        }

        private void OnBackgroundVideoPrepared(VideoPlayer source)
        {
            if (_background != null && _videoTexture != null)
            {
                _background.style.backgroundImage = Background.FromRenderTexture(_videoTexture);
            }

            source.Play();
        }

        private void OnStartGameClicked() => StartGameAsync().Forget();

        private async UniTaskVoid StartGameAsync()
        {
            await _controller.StartGameAsync(_lifetime.Token);
        }

        private void OnDestroy()
        {
            foreach (var entry in _featureHandlers)
            {
                entry.Key.clicked -= entry.Value;
            }

            _featureHandlers.Clear();
            if (_startGame != null)
            {
                _startGame.clicked -= OnStartGameClicked;
            }

            if (_avatarButton != null)
            {
                _avatarButton.clicked -= OnAvatarClicked;
            }

            if (_videoPlayer != null)
            {
                _videoPlayer.prepareCompleted -= OnBackgroundVideoPrepared;
                _videoPlayer.Stop();
            }

            if (_videoTexture != null)
            {
                _videoTexture.Release();
                Destroy(_videoTexture);
                _videoTexture = null;
            }

            _screen?.RemoveFromHierarchy();
            _subscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
