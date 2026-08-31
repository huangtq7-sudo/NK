using System;
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

    public sealed class LobbyController : IController, IReadOnlyState<LobbyPresentationState>, IDisposable
    {
        private readonly LobbyModel _model;
        private readonly ReactiveState<LobbyPresentationState> _state;

        public LobbyController(LobbyModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _state = new ReactiveState<LobbyPresentationState>(
                new LobbyPresentationState(false, string.Empty, 0));
        }

        public LobbyPresentationState Current => _state.Current;

        public void Enter(string username, long accountId)
        {
            if (_model.IsEntered && _model.AccountId == accountId)
            {
                return;
            }

            _model.Enter(username, accountId);
            Publish(new LobbyPresentationState(true, _model.Username, _model.AccountId));
        }

        public IDisposable Subscribe(IObserver<LobbyPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose() => _state.Dispose();

        private void Publish(LobbyPresentationState state) => _state.Set(state);
    }
}
