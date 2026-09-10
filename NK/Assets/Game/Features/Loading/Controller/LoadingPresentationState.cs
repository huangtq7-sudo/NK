using Naraka.Core.Application.MVC;

namespace Naraka.Features.Loading.Controller
{
    public readonly struct LoadingPresentationState : IPresentationState
    {
        public LoadingPresentationState(bool isVisible, float progress)
        {
            IsVisible = isVisible;
            Progress = progress < 0f ? 0f : progress > 1f ? 1f : progress;
        }

        public bool IsVisible { get; }

        /// <summary>0 到 1 的加载进度。</summary>
        public float Progress { get; }
    }
}
