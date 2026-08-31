using Naraka.Core.Application.MVC;

namespace Naraka.Features.Bootstrap.Controller
{
    public enum ConfigVersionPhase
    {
        Pending,
        Checking,
        Ready,
        Blocked
    }

    public readonly struct ConfigVersionPresentationState : IPresentationState
    {
        public ConfigVersionPresentationState(
            ConfigVersionPhase phase,
            string localConfigVersion,
            string requiredConfigVersion,
            string message)
        {
            Phase = phase;
            LocalConfigVersion = localConfigVersion ?? string.Empty;
            RequiredConfigVersion = requiredConfigVersion ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public ConfigVersionPhase Phase { get; }

        public string LocalConfigVersion { get; }

        public string RequiredConfigVersion { get; }

        public string Message { get; }
    }
}
