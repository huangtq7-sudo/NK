using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Core.Application.Timing;

namespace Naraka.Features.Loading.Controller
{
    public interface ILoadingController : IReadOnlyState<LoadingPresentationState>
    {
        UniTask RunAsync(CancellationToken cancellationToken);

        UniTask RunAsync(Func<CancellationToken, UniTask> work, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 登录完成到进入大厅之间的异步加载。规则：真实加载不足最短时长时补足到最短时长；
    /// 超过最短时长时以真实完成时间为准，进度条在此期间停在99%而不是提前显示完成。
    /// </summary>
    public sealed class LoadingController : IController, ILoadingController, IDisposable
    {
        public const double DefaultMinimumSeconds = 2.0;

        private const double TickSeconds = 1.0 / 60.0;
        private const float IncompleteCeiling = 0.99f;

        private readonly IGameClock _clock;
        private readonly double _minimumSeconds;
        private readonly ReactiveState<LoadingPresentationState> _state;

        public LoadingController(IGameClock clock, double minimumSeconds)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _minimumSeconds = minimumSeconds < 0d ? 0d : minimumSeconds;
            _state = new ReactiveState<LoadingPresentationState>(
                new LoadingPresentationState(false, 0f));
        }

        public LoadingPresentationState Current => _state.Current;

        public UniTask RunAsync(CancellationToken cancellationToken) =>
            RunAsync(NoWork, cancellationToken);

        public async UniTask RunAsync(
            Func<CancellationToken, UniTask> work,
            CancellationToken cancellationToken)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            var startedAt = _clock.NowSeconds;
            _state.Set(new LoadingPresentationState(true, 0f));

            // 真实加载立刻开始，进度条与它并行推进，两者都完成才算结束。
            var workTask = work(cancellationToken);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var elapsed = _clock.NowSeconds - startedAt;
                var workCompleted = workTask.Status.IsCompleted();
                _state.Set(new LoadingPresentationState(true, ToProgress(elapsed, workCompleted)));
                if (workCompleted && elapsed >= _minimumSeconds)
                {
                    break;
                }

                await _clock.DelayAsync(TimeSpan.FromSeconds(TickSeconds), cancellationToken);
            }

            // 重新await以传播真实加载过程中的异常。
            await workTask;
            _state.Set(new LoadingPresentationState(false, 1f));
        }

        public IDisposable Subscribe(IObserver<LoadingPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose() => _state.Dispose();

        private static UniTask NoWork(CancellationToken cancellationToken) => UniTask.CompletedTask;

        private float ToProgress(double elapsed, bool workCompleted)
        {
            var value = _minimumSeconds <= 0d ? 1d : elapsed / _minimumSeconds;
            var progress = value <= 0d ? 0f : value >= 1d ? 1f : (float)value;
            return workCompleted || progress < IncompleteCeiling ? progress : IncompleteCeiling;
        }
    }
}
