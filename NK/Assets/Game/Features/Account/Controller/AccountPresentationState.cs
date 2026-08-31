using Naraka.Core.Application.MVC;

namespace Naraka.Features.Account.Controller
{
    public enum AccountFlowPhase
    {
        Ready,
        Registering,
        LoggingIn,
        Authenticated,
        Failed
    }

    public readonly struct AccountPresentationState : IPresentationState
    {
        public AccountPresentationState(
            AccountFlowPhase phase,
            string username,
            long accountId,
            string message)
        {
            Phase = phase;
            Username = username ?? string.Empty;
            AccountId = accountId;
            Message = message ?? string.Empty;
        }

        public AccountFlowPhase Phase { get; }

        public string Username { get; }

        public long AccountId { get; }

        public string Message { get; }

        public bool IsBusy => Phase == AccountFlowPhase.Registering || Phase == AccountFlowPhase.LoggingIn;
    }
}
