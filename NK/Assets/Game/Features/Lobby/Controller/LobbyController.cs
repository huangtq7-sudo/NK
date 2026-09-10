using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Lobby.Model;

namespace Naraka.Features.Lobby.Controller
{
    public interface ILobbyController : IReadOnlyState<LobbyPresentationState>
    {
        void Enter(string username, long accountId);

        /// <summary>重新向服务端拉取账号概要。未进入大厅或已有请求在途时直接返回。</summary>
        UniTask LoadAccountSummaryAsync(CancellationToken cancellationToken);

        /// <summary>拉取账号资料（头像、头像框、出战选择、经验与容量档位）。</summary>
        UniTask LoadProfileAsync(CancellationToken cancellationToken);

        void RequestFeature(LobbyFeature feature);

        UniTask StartGameAsync(CancellationToken cancellationToken);

        void OpenAppearance();

        void CloseAppearance();

        void CloseFeature();

        void SelectAppearanceTab(LobbyAppearanceTab tab);

        /// <summary>提交头像。参数是稳定配置 ID，不是界面下标。</summary>
        void SelectAvatar(string avatarId);

        /// <summary>提交头像框。参数是稳定配置 ID，不是界面下标。</summary>
        void SelectFrame(string avatarFrameId);
    }

    public sealed class LobbyController : IController, ILobbyController, IDisposable
    {
        private readonly LobbyModel _model;
        private readonly ILobbySceneGateway _sceneGateway;
        private readonly ILobbyAccountGateway _accountGateway;
        private readonly ILobbyProfileGateway _profileGateway;
        private readonly IServerCapabilities _capabilities;
        private readonly string _mapSceneName;
        private readonly ReactiveState<LobbyPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _isStartingGame;
        private bool _isLoadingAccountSummary;
        private bool _isLoadingProfile;
        private bool _isSavingAppearance;

        public LobbyController(
            LobbyModel model,
            ILobbySceneGateway sceneGateway,
            string mapSceneName,
            ILobbyAccountGateway accountGateway,
            ILobbyProfileGateway profileGateway,
            IServerCapabilities capabilities)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _sceneGateway = sceneGateway ?? throw new ArgumentNullException(nameof(sceneGateway));
            _accountGateway = accountGateway ?? throw new ArgumentNullException(nameof(accountGateway));
            _profileGateway = profileGateway ?? throw new ArgumentNullException(nameof(profileGateway));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            if (string.IsNullOrWhiteSpace(mapSceneName))
            {
                throw new ArgumentException("Map scene name cannot be empty.", nameof(mapSceneName));
            }

            _mapSceneName = mapSceneName;
            _state = new ReactiveState<LobbyPresentationState>(
                new LobbyPresentationState(
                    false, string.Empty, 0, string.Empty, LobbyStatusSource.None, false,
                    LobbyModal.None, LobbyFeature.Hero, LobbyAppearanceTab.Avatar,
                    false, false, default(LobbyAccountSnapshot),
                    false, false, default(LobbyProfileSnapshot)));
        }

        public LobbyPresentationState Current => _state.Current;

        public void Enter(string username, long accountId)
        {
            if (_model.IsEntered && _model.AccountId == accountId)
            {
                // 重新进入同一个账号的大厅：布局与选择保持不变，但概要与资料要重新拉一次。
                LoadAccountSummaryAsync(CancellationToken.None).Forget();
                LoadProfileAsync(CancellationToken.None).Forget();
                return;
            }

            _model.Enter(username, accountId);
            var current = Current;
            _state.Set(new LobbyPresentationState(
                true,
                _model.Username,
                _model.AccountId,
                string.Empty,
                LobbyStatusSource.None,
                false,
                LobbyModal.None,
                current.OpenFeature,
                current.AppearanceTab,
                _model.HasAccountSnapshot,
                false,
                _model.AccountSnapshot,
                _model.HasProfile,
                false,
                _model.Profile));

            LoadAccountSummaryAsync(CancellationToken.None).Forget();
            LoadProfileAsync(CancellationToken.None).Forget();
        }

        public void RequestFeature(LobbyFeature feature)
        {
            if (!Current.IsVisible)
            {
                return;
            }

            if (Current.IsFeatureOpen && Current.OpenFeature == feature)
            {
                return;
            }

            // 打开功能面板会顶掉外观面板：OpenModal 是单值，结构上不可能同时开两个。
            // 服务器没有部署该功能时只显示提示，绝不发送它不认识的协议——那会直接触发断线。
            //
            // 能力具备时不写任何状态栏文案：十个入口现在都有专属面板，
            // 面板自己会显示加载、空数据与失败状态，大厅再叠一句提示只会互相打架。
            if (_capabilities.Has(ToCapability(feature)))
            {
                _state.Set(Current.WithFeatureOpen(feature)
                    .WithClearedStatus(LobbyStatusSource.Feature));
                return;
            }

            _state.Set(Current.WithFeatureOpen(feature).WithStatusMessage(
                LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing),
                LobbyStatusSource.Feature));
        }

        public void CloseFeature()
        {
            if (!Current.IsFeatureOpen)
            {
                return;
            }

            _state.Set(Current.WithModalClosed().WithClearedStatus(LobbyStatusSource.Feature));
        }

        public async UniTask StartGameAsync(CancellationToken cancellationToken)
        {
            if (_isStartingGame || !Current.IsVisible)
            {
                return;
            }

            _isStartingGame = true;
            _state.Set(Current.WithStartingGame(true)
                .WithStatusMessage("正在进入地图…", LobbyStatusSource.StartGame));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, _lifetime.Token))
                {
                    await _sceneGateway.LoadMapAsync(_mapSceneName, linked.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 取消属于正常流程，只恢复按钮状态，不写错误消息。
                _state.Set(Current.WithStartingGame(false).WithClearedStatus(LobbyStatusSource.StartGame));
            }
            catch (Exception)
            {
                _state.Set(Current.WithStartingGame(false)
                    .WithStatusMessage("加载地图失败，请重试。", LobbyStatusSource.StartGame));
            }
            finally
            {
                _isStartingGame = false;
            }
        }

        public void OpenAppearance()
        {
            if (!Current.IsVisible || Current.IsAppearanceOpen)
            {
                return;
            }

            _state.Set(Current.WithAppearanceOpen(true).WithClearedStatus(LobbyStatusSource.Profile));
        }

        public void CloseAppearance()
        {
            if (!Current.IsAppearanceOpen)
            {
                return;
            }

            _state.Set(Current.WithAppearanceOpen(false));
        }

        public void SelectAppearanceTab(LobbyAppearanceTab tab)
        {
            if (!Current.IsAppearanceOpen || Current.AppearanceTab == tab)
            {
                return;
            }

            _state.Set(Current.WithAppearanceTab(tab));
        }

        public void SelectAvatar(string avatarId) =>
            SaveAppearanceAsync(avatarId, Current.SelectedAvatarFrameId).Forget();

        public void SelectFrame(string avatarFrameId) =>
            SaveAppearanceAsync(Current.SelectedAvatarId, avatarFrameId).Forget();

        /// <summary>
        /// 提交外观修改。界面显示的头像始终来自服务端返回值，绝不做乐观更新：
        /// 服务端可能拒绝一个配置里不存在的 ID，先改界面会让玩家看到一个数据库里并不存在的头像。
        /// </summary>
        private async UniTaskVoid SaveAppearanceAsync(string avatarId, string avatarFrameId)
        {
            if (!_model.IsEntered || _isSavingAppearance ||
                string.IsNullOrEmpty(avatarId) || string.IsNullOrEmpty(avatarFrameId))
            {
                return;
            }

            if (avatarId == Current.SelectedAvatarId && avatarFrameId == Current.SelectedAvatarFrameId)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.AccountProfile))
            {
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing),
                    LobbyStatusSource.Profile));
                return;
            }

            _isSavingAppearance = true;
            _state.Set(Current.WithAppearanceSaving(true).WithClearedStatus(LobbyStatusSource.Profile));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           CancellationToken.None, _lifetime.Token))
                {
                    var result = await _profileGateway.SetAppearanceAsync(avatarId, avatarFrameId, linked.Token);
                    if (!_model.IsEntered)
                    {
                        return;
                    }

                    ApplyProfileResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                _state.Set(Current.WithAppearanceSaving(false));
            }
            catch (Exception)
            {
                _state.Set(Current
                    .WithAppearanceSaving(false)
                    .WithStatusMessage(
                        LobbyOperationMessages.Describe(LobbyOperationStatus.TransportFailure),
                        LobbyStatusSource.Profile));
            }
            finally
            {
                _isSavingAppearance = false;
            }
        }

        /// <summary>
        /// 拉取账号资料。与账号概要一样：失败只更新状态消息，绝不覆盖上一次成功的数据。
        /// </summary>
        public async UniTask LoadProfileAsync(CancellationToken cancellationToken)
        {
            if (!_model.IsEntered || _isLoadingProfile)
            {
                return;
            }

            // 旧云端没有部署账号资料协议，此时连发都不能发，否则会被对端当成非法帧直接断线。
            //
            // 但不能就这么静默返回：资料拿不到会连带让外观面板的全部单元格失效，
            // 玩家点开面板、点了头像却毫无反应，界面上又没有任何解释。
            // 因此这里显式写下原因，交给大厅与外观面板显示。
            if (!_capabilities.Has(NarakaServerCapabilities.AccountProfile))
            {
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing),
                    LobbyStatusSource.Profile));
                return;
            }

            _isLoadingProfile = true;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, _lifetime.Token))
                {
                    var result = await _profileGateway.RequestProfileAsync(linked.Token);
                    if (!_model.IsEntered)
                    {
                        return;
                    }

                    ApplyProfileResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                // 取消属于正常流程，保留已有数据，不写错误消息。
            }
            catch (Exception)
            {
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.TransportFailure),
                    LobbyStatusSource.Profile));
            }
            finally
            {
                _isLoadingProfile = false;
            }
        }

        private void ApplyProfileResult(LobbyProfileResult result)
        {
            if (result.IsSuccess)
            {
                _model.ApplyProfile(result.Snapshot);
                // 只清掉资料链路自己的错误：账号概要可能同时在展示它的失败提示。
                _state.Set(Current.WithProfile(result.Snapshot).WithClearedStatus(LobbyStatusSource.Profile));
                return;
            }

            _state.Set(Current
                .WithAppearanceSaving(false)
                .WithStatusMessage(
                    LobbyOperationMessages.Describe(result.Status), LobbyStatusSource.Profile));
        }

        /// <summary>
        /// 拉取账号概要。失败时只更新状态消息，绝不覆盖上一次成功的余额，也不伪造 0。
        /// 请求本身不携带 accountId：服务端从已认证连接会话决定读谁的数据。
        /// </summary>
        public async UniTask LoadAccountSummaryAsync(CancellationToken cancellationToken)
        {
            if (!_model.IsEntered || _isLoadingAccountSummary)
            {
                return;
            }

            _isLoadingAccountSummary = true;
            _state.Set(Current.WithAccountSummaryLoading(true)
                .WithClearedStatus(LobbyStatusSource.AccountSummary));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, _lifetime.Token))
                {
                    var result = await _accountGateway.RequestAccountSummaryAsync(linked.Token);
                    if (!_model.IsEntered)
                    {
                        return;
                    }

                    if (result.IsSuccess)
                    {
                        _model.ApplyAccountSnapshot(result.Snapshot);
                        _state.Set(Current
                            .WithAccountSummary(result.Snapshot)
                            .WithClearedStatus(LobbyStatusSource.AccountSummary));
                    }
                    else
                    {
                        _state.Set(Current
                            .WithAccountSummaryLoading(false)
                            .WithStatusMessage(
                                ToAccountSummaryMessage(result.Status), LobbyStatusSource.AccountSummary));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 取消属于正常流程，保留已有数据，不写错误消息。
                _state.Set(Current.WithAccountSummaryLoading(false));
            }
            catch (Exception)
            {
                _state.Set(Current
                    .WithAccountSummaryLoading(false)
                    .WithStatusMessage(
                        ToAccountSummaryMessage(LobbyAccountSummaryStatus.TransportFailure),
                        LobbyStatusSource.AccountSummary));
            }
            finally
            {
                _isLoadingAccountSummary = false;
            }
        }

        private static string ToAccountSummaryMessage(LobbyAccountSummaryStatus status) => status switch
        {
            LobbyAccountSummaryStatus.Unauthenticated => "登录状态已失效，请重新登录。",
            LobbyAccountSummaryStatus.InvalidRequest => "账号数据请求无效，请重试。",
            LobbyAccountSummaryStatus.NotFound => "未找到该账号的资料。",
            LobbyAccountSummaryStatus.DatabaseUnavailable => "服务器数据暂时不可用，请稍后重试。",
            LobbyAccountSummaryStatus.TransportFailure => "无法连接服务器，请检查网络后重试。",
            _ => "读取账号数据失败，请稍后重试。"
        };

        public IDisposable Subscribe(IObserver<LobbyPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        /// <summary>大厅入口与服务器能力的对应关系。新增入口时必须同步登记，否则会漏掉能力门禁。</summary>
        private static string ToCapability(LobbyFeature feature) => feature switch
        {
            LobbyFeature.Hero => NarakaServerCapabilities.Loadout,
            LobbyFeature.Weapon => NarakaServerCapabilities.Loadout,
            LobbyFeature.Forge => NarakaServerCapabilities.Forge,
            LobbyFeature.Shop => NarakaServerCapabilities.Shop,
            LobbyFeature.Inventory => NarakaServerCapabilities.Inventory,
            LobbyFeature.CheckIn => NarakaServerCapabilities.SignIn,
            LobbyFeature.Draw => NarakaServerCapabilities.Gacha,
            LobbyFeature.AccountLevelReward => NarakaServerCapabilities.AccountReward,
            LobbyFeature.Friends => NarakaServerCapabilities.Social,
            LobbyFeature.Chat => NarakaServerCapabilities.Social,
            _ => string.Empty
        };

    }
}
