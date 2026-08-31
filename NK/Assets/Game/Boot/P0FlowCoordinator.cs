using System;
using Naraka.Features.Account.Controller;
using Naraka.Features.Lobby.Controller;
using VContainer.Unity;

namespace Naraka.Boot
{
    public sealed class P0FlowCoordinator : IStartable, IDisposable, IObserver<AccountPresentationState>
    {
        private readonly AccountController _account;
        private readonly LobbyController _lobby;
        private IDisposable _subscription;

        public P0FlowCoordinator(AccountController account, LobbyController lobby)
        {
            _account = account;
            _lobby = lobby;
        }

        public void Start()
        {
            _subscription = _account.Subscribe(this);
        }

        public void OnNext(AccountPresentationState value)
        {
            if (value.Phase == AccountFlowPhase.Authenticated)
            {
                _lobby.Enter(value.Username, value.AccountId);
            }
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        public void Dispose()
        {
            _subscription?.Dispose();
        }
    }
}
