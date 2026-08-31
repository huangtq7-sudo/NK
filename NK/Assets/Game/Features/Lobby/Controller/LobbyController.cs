using System;
using System.Collections.Generic;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Lobby.Model;

namespace Naraka.Features.Lobby.Controller
{
    public readonly struct LobbyPresentationState : IPresentationState
    {
        public LobbyPresentationState(bool isVisible, string username, long accountId)
        {
            IsVisible = isVisible;
            Username = username ?? string.Empty;
            AccountId = accountId;
        }

        public bool IsVisible { get; }

        public string Username { get; }

        public long AccountId { get; }
    }

    public sealed class LobbyController : IController, IReadOnlyState<LobbyPresentationState>
    {
        private readonly LobbyModel _model;
        private readonly List<IObserver<LobbyPresentationState>> _observers =
            new List<IObserver<LobbyPresentationState>>();

        public LobbyController(LobbyModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            Current = new LobbyPresentationState(false, string.Empty, 0);
        }

        public LobbyPresentationState Current { get; private set; }

        public void Enter(string username, long accountId)
        {
            if (_model.IsEntered && _model.AccountId == accountId)
            {
                return;
            }

            _model.Enter(username, accountId);
            Publish(new LobbyPresentationState(true, _model.Username, _model.AccountId));
        }

        public IDisposable Subscribe(IObserver<LobbyPresentationState> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            _observers.Add(observer);
            observer.OnNext(Current);
            return new Subscription(_observers, observer);
        }

        private void Publish(LobbyPresentationState state)
        {
            Current = state;
            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(state);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly List<IObserver<LobbyPresentationState>> _observers;
            private IObserver<LobbyPresentationState> _observer;

            public Subscription(
                List<IObserver<LobbyPresentationState>> observers,
                IObserver<LobbyPresentationState> observer)
            {
                _observers = observers;
                _observer = observer;
            }

            public void Dispose()
            {
                if (_observer == null)
                {
                    return;
                }

                _observers.Remove(_observer);
                _observer = null;
            }
        }
    }
}
