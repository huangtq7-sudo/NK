using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Messaging;
using Naraka.Features.Account.Controller;
using Naraka.Features.Bootstrap.Controller;
using Naraka.Features.Lobby.Controller;
using VContainer.Unity;

namespace Naraka.Boot
{
    public sealed class P0FlowCoordinator : IStartable, IDisposable
    {
        private readonly ConfigVersionController _configVersion;
        private readonly IDomainEventBus _events;
        private readonly LobbyController _lobby;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private IDisposable _subscription;

        public P0FlowCoordinator(
            ConfigVersionController configVersion,
            IDomainEventBus events,
            LobbyController lobby)
        {
            _configVersion = configVersion;
            _events = events;
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
            _lobby.Enter(account.Username, account.AccountId);
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
