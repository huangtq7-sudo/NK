using Naraka.Core.Application.MVC;
using Naraka.Features.Lobby.Model;

namespace Naraka.Features.Lobby.Controller
{
    public enum LobbyFeature
    {
        Hero,
        Weapon,
        Forge,
        Shop,
        Inventory,
        CheckIn,
        Draw,
        AccountLevelReward,
        Friends,
        Chat
    }

    /// <summary>外观面板的侧边分页。</summary>
    public enum LobbyAppearanceTab
    {
        Avatar,
        Frame
    }

    /// <summary>
    /// 状态栏消息的来源。
    ///
    /// 大厅同时有多条异步链路（账号概要、账号资料、开始游戏、功能入口），它们共用一个状态栏。
    /// 没有来源标记时，一条后完成的成功回调会把另一条链路刚写下的错误提示清掉，
    /// 玩家就再也看不到"服务器数据暂时不可用"。因此每条消息都记住是谁写的，
    /// 只有同一来源成功时才允许清除。
    /// </summary>
    public enum LobbyStatusSource
    {
        None,
        AccountSummary,
        Profile,
        Feature,
        StartGame
    }

    /// <summary>
    /// 当前打开的弹窗。用单一枚举而不是多个bool，结构上保证同一时刻只有一个模态，
    /// 避免外观面板与功能面板叠在一起。
    /// </summary>
    public enum LobbyModal
    {
        None,
        Appearance,
        Feature
    }

    public readonly struct LobbyPresentationState : IPresentationState
    {
        public LobbyPresentationState(
            bool isVisible,
            string username,
            long accountId,
            string statusMessage,
            LobbyStatusSource statusSource,
            bool isStartingGame,
            LobbyModal openModal,
            LobbyFeature openFeature,
            LobbyAppearanceTab appearanceTab,
            bool hasAccountSummary,
            bool isAccountSummaryLoading,
            LobbyAccountSnapshot accountSummary,
            bool hasProfile,
            bool isAppearanceSaving,
            LobbyProfileSnapshot profile)
        {
            IsVisible = isVisible;
            Username = username ?? string.Empty;
            AccountId = accountId;
            StatusMessage = statusMessage ?? string.Empty;
            StatusSource = StatusMessage.Length == 0 ? LobbyStatusSource.None : statusSource;
            IsStartingGame = isStartingGame;
            OpenModal = openModal;
            OpenFeature = openFeature;
            AppearanceTab = appearanceTab;
            HasAccountSummary = hasAccountSummary;
            IsAccountSummaryLoading = isAccountSummaryLoading;
            AccountSummary = accountSummary;
            HasProfile = hasProfile;
            IsAppearanceSaving = isAppearanceSaving;
            Profile = profile;
        }

        public bool IsVisible { get; }

        public string Username { get; }

        public long AccountId { get; }

        public string StatusMessage { get; }

        /// <summary>写下当前消息的链路。消息为空时恒为 <see cref="LobbyStatusSource.None"/>。</summary>
        public LobbyStatusSource StatusSource { get; }

        public bool IsStartingGame { get; }

        public LobbyModal OpenModal { get; }

        /// <summary>OpenModal 为 Feature 时才有意义。</summary>
        public LobbyFeature OpenFeature { get; }

        public LobbyAppearanceTab AppearanceTab { get; }

        /// <summary>是否已经拿到过一次服务端账号概要。为 false 时界面保持占位。</summary>
        public bool HasAccountSummary { get; }

        public bool IsAccountSummaryLoading { get; }

        /// <summary>仅在 HasAccountSummary 为 true 时有意义。</summary>
        public LobbyAccountSnapshot AccountSummary { get; }

        /// <summary>是否已经拿到过一次服务端账号资料。为 false 时外观面板保持占位。</summary>
        public bool HasProfile { get; }

        /// <summary>头像修改请求在途。期间禁用所有单元格，避免重复点击产生多次写请求。</summary>
        public bool IsAppearanceSaving { get; }

        /// <summary>仅在 HasProfile 为 true 时有意义。</summary>
        public LobbyProfileSnapshot Profile { get; }

        /// <summary>当前头像的稳定配置 ID。尚未取得资料时为空字符串。</summary>
        public string SelectedAvatarId => HasProfile ? Profile.AvatarId : string.Empty;

        /// <summary>当前头像框的稳定配置 ID。尚未取得资料时为空字符串。</summary>
        public string SelectedAvatarFrameId => HasProfile ? Profile.AvatarFrameId : string.Empty;

        public bool IsAppearanceOpen => OpenModal == LobbyModal.Appearance;

        public bool IsFeatureOpen => OpenModal == LobbyModal.Feature;

        public LobbyPresentationState WithStatusMessage(string statusMessage, LobbyStatusSource source) =>
            Copy(statusMessage: statusMessage, statusSource: source);

        /// <summary>
        /// 清除状态栏，但只清除本链路自己写下的消息。
        /// 别的链路正在展示错误时保持原样，避免一次成功掩盖另一处失败。
        /// </summary>
        public LobbyPresentationState WithClearedStatus(LobbyStatusSource source) =>
            StatusSource == source || StatusSource == LobbyStatusSource.None
                ? Copy(statusMessage: string.Empty, statusSource: LobbyStatusSource.None)
                : this;

        public LobbyPresentationState WithStartingGame(bool isStartingGame) =>
            Copy(isStartingGame: isStartingGame);

        public LobbyPresentationState WithAppearanceOpen(bool isAppearanceOpen) =>
            Copy(openModal: isAppearanceOpen ? LobbyModal.Appearance : LobbyModal.None);

        public LobbyPresentationState WithFeatureOpen(LobbyFeature feature) =>
            Copy(openModal: LobbyModal.Feature, openFeature: feature);

        public LobbyPresentationState WithModalClosed() => Copy(openModal: LobbyModal.None);

        public LobbyPresentationState WithAppearanceTab(LobbyAppearanceTab appearanceTab) =>
            Copy(appearanceTab: appearanceTab);

        public LobbyPresentationState WithAppearanceSaving(bool isSaving) =>
            Copy(isAppearanceSaving: isSaving);

        public LobbyPresentationState WithProfile(LobbyProfileSnapshot profile) =>
            Copy(hasProfile: true, isAppearanceSaving: false, profile: profile);

        /// <summary>清空资料只在切换账号时使用；加载失败不得调用，否则会抹掉上一次成功的数据。</summary>
        public LobbyPresentationState WithoutProfile() =>
            Copy(hasProfile: false, isAppearanceSaving: false, profile: default(LobbyProfileSnapshot));

        public LobbyPresentationState WithAccountSummaryLoading(bool isLoading) =>
            Copy(isAccountSummaryLoading: isLoading);

        public LobbyPresentationState WithAccountSummary(LobbyAccountSnapshot snapshot) =>
            Copy(hasAccountSummary: true, isAccountSummaryLoading: false, accountSummary: snapshot);

        /// <summary>清空概要只在切换账号时使用；加载失败不得调用，否则会抹掉上一次成功的数据。</summary>
        public LobbyPresentationState WithoutAccountSummary() =>
            Copy(hasAccountSummary: false, isAccountSummaryLoading: false, accountSummary: default(LobbyAccountSnapshot));

        // 单一复制入口，避免每个 With 方法都重复列出全部字段而写错顺序。
        private LobbyPresentationState Copy(
            bool? isVisible = null,
            string username = null,
            long? accountId = null,
            string statusMessage = null,
            LobbyStatusSource? statusSource = null,
            bool? isStartingGame = null,
            LobbyModal? openModal = null,
            LobbyFeature? openFeature = null,
            LobbyAppearanceTab? appearanceTab = null,
            bool? hasAccountSummary = null,
            bool? isAccountSummaryLoading = null,
            LobbyAccountSnapshot? accountSummary = null,
            bool? hasProfile = null,
            bool? isAppearanceSaving = null,
            LobbyProfileSnapshot? profile = null) =>
            new LobbyPresentationState(
                isVisible ?? IsVisible,
                username ?? Username,
                accountId ?? AccountId,
                statusMessage ?? StatusMessage,
                statusSource ?? StatusSource,
                isStartingGame ?? IsStartingGame,
                openModal ?? OpenModal,
                openFeature ?? OpenFeature,
                appearanceTab ?? AppearanceTab,
                hasAccountSummary ?? HasAccountSummary,
                isAccountSummaryLoading ?? IsAccountSummaryLoading,
                accountSummary ?? AccountSummary,
                hasProfile ?? HasProfile,
                isAppearanceSaving ?? IsAppearanceSaving,
                profile ?? Profile);
    }
}
