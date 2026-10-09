using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Messaging;
using Naraka.Features.Account.Controller;
using Naraka.Features.Account.Model;
using Naraka.Features.Bootstrap.Controller;
using Naraka.Features.Expedition.Controller;
using Naraka.Features.Loading.Controller;
using Naraka.Features.Lobby.Controller;
using VContainer.Unity;

namespace Naraka.Boot
{
    public sealed class P0FlowCoordinator : IStartable, IDisposable
    {
        private readonly ConfigVersionController _configVersion;
        private readonly IDomainEventBus _events;
        private readonly ILoadingController _loading;
        private readonly LobbyController _lobby;
        private readonly AccountSessionModel _session;
        private readonly IExpeditionController _expedition;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private IDisposable _subscription;
        private bool _isEnteringLobby;

        public P0FlowCoordinator(
            ConfigVersionController configVersion,
            IDomainEventBus events,
            ILoadingController loading,
            LobbyController lobby,
            AccountSessionModel session = null,
            IExpeditionController expedition = null)
        {
            _configVersion = configVersion;
            _events = events;
            _loading = loading;
            _lobby = lobby;
            _session = session;
            _expedition = expedition;
        }

        public void Start()
        {
            _subscription = _events.Subscribe<AccountAuthenticatedEvent>(OnAccountAuthenticated);
            CheckConfigVersionAsync().Forget();
        }

        public void Dispose()
        {
            _subscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private void OnAccountAuthenticated(AccountAuthenticatedEvent account)
        {
            EnterLobbyAsync(account).Forget();
        }

        // 登录成功后先播异步加载界面，加载结束才进入大厅。
        private async UniTaskVoid EnterLobbyAsync(AccountAuthenticatedEvent account)
        {
            if (_isEnteringLobby)
            {
                return;
            }

            _isEnteringLobby = true;
            try
            {
                await _loading.RunAsync(LoadExpeditionSnapshotAsync, _lifetime.Token);
                _lobby.Enter(account.Username, account.AccountId);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _isEnteringLobby = false;
            }
        }

        private async UniTask LoadExpeditionSnapshotAsync(CancellationToken cancellationToken)
        {
            if (_expedition != null)
            {
                await _expedition.LoadActiveAsync(cancellationToken);
            }
        }

        private async UniTaskVoid CheckConfigVersionAsync()
        {
            try
            {
                await _configVersion.CheckAsync(_lifetime.Token);
                // 从地图结算并重新加载Bootstrap时，认证连接与最小账号会话仍在持久根中。
                // 版本门禁重新通过后直接恢复大厅，不要求玩家再次输入密码。
                if (_configVersion.IsReady && _session != null && _session.IsAuthenticated)
                {
                    EnterLobbyAsync(new AccountAuthenticatedEvent(
                        _session.Username, _session.AccountId)).Forget();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
