using Naraka.Core.Application.MVC;

namespace Naraka.Features.Loading.Controller
{
    public readonly struct LoadingPresentationState : IPresentationState
    {
        public LoadingPresentationState(bool isVisible, float progress)
            : this(isVisible, progress, false, string.Empty)
        {
        }

        public LoadingPresentationState(bool isVisible, float progress, bool hasError, string message)
        {
            IsVisible = isVisible;
            Progress = progress < 0f ? 0f : progress > 1f ? 1f : progress;
            HasError = hasError;
            Message = message ?? string.Empty;
        }

        public bool IsVisible { get; }

        /// <summary>0 到 1 的加载进度。</summary>
        public float Progress { get; }

        /// <summary>
        /// 本次加载是否失败。失败时界面必须恢复可操作状态，
        /// 不允许把进度条停在 99% 让玩家以为还在加载。
        /// </summary>
        public bool HasError { get; }

        /// <summary>失败原因。成功路径为空字符串。</summary>
        public string Message { get; }

        public static LoadingPresentationState Hidden => new LoadingPresentationState(false, 0f);

        public static LoadingPresentationState Failed(string message) =>
            new LoadingPresentationState(false, 0f, true, message);
    }
}
