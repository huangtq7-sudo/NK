using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Messaging;
using Naraka.Features.Account.Controller;
using Naraka.Features.Bootstrap.Controller;
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
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private IDisposable _subscription;

        public P0FlowCoordinator(
            ConfigVersionController configVersion,
            IDomainEventBus events,
            ILoadingController loading,
            LobbyController lobby)
        {
            _configVersion = configVersion;
            _events = events;
            _loading = loading;
            _lobby = lobby;
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
            try
            {
                await _loading.RunAsync(_lifetime.Token);
                _lobby.Enter(account.Username, account.AccountId);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async UniTaskVoid CheckConfigVersionAsync()
        {
            try
            {
                await _configVersion.CheckAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
