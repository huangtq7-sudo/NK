using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Core.Application.Scenes;
using Naraka.Core.Application.Timing;

namespace Naraka.Features.Loading.Controller
{
    public interface ILoadingController : IReadOnlyState<LoadingPresentationState>
    {
        UniTask RunAsync(CancellationToken cancellationToken);

        UniTask RunAsync(Func<CancellationToken, UniTask> work, CancellationToken cancellationToken);

        /// <summary>
        /// 显示加载界面并异步加载场景。进度来自真实 AsyncOperation，
        /// 加载完成且满足最短显示时长之后才允许激活。
        /// </summary>
        UniTask LoadSceneAsync(string sceneName, CancellationToken cancellationToken);

        /// <summary>清除上一次的失败提示。</summary>
        void ClearError();
    }

    /// <summary>
    /// 加载界面的可见性与 PresentationState。真实的 Unity AsyncOperation 由
    /// <see cref="ISceneLoader"/> 持有，这里只读它暴露的进度与就绪标记，
    /// View 因此永远不会直接接触 AsyncOperation。
    ///
    /// 规则：真实加载不足最短时长时补足到最短时长；超过最短时长时以真实完成时间为准，
    /// 进度条在此期间停在 99% 而不是提前显示完成。
    /// </summary>
    public sealed class LoadingController : IController, ILoadingController, IDisposable
    {
        public const double DefaultMinimumSeconds = 2.0;

        private const double TickSeconds = 1.0 / 60.0;
        private const float IncompleteCeiling = 0.99f;

        private readonly IGameClock _clock;
        private readonly ISceneLoader _sceneLoader;
        private readonly IPresentationFrameScheduler _frames;
        private readonly double _minimumSeconds;
        private readonly ReactiveState<LoadingPresentationState> _state;
        private bool _isRunning;
        private bool _disposed;
        private float _reportedProgress;

        public LoadingController(
            IGameClock clock,
            ISceneLoader sceneLoader,
            IPresentationFrameScheduler frames,
            double minimumSeconds)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _sceneLoader = sceneLoader;
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
            _minimumSeconds = minimumSeconds < 0d ? 0d : minimumSeconds;
            _state = new ReactiveState<LoadingPresentationState>(LoadingPresentationState.Hidden);
        }

        public LoadingPresentationState Current => _state.Current;

        /// <summary>是否已有加载在途。调用方用它阻止重复创建场景切换任务。</summary>
        public bool IsRunning => _isRunning;

        public void ClearError()
        {
            if (Current.HasError)
            {
                SetState(LoadingPresentationState.Hidden);
            }
        }

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
            _isRunning = true;
            SetState(new LoadingPresentationState(true, 0f));

            try
            {
                // 真实加载立刻开始，进度条与它并行推进，两者都完成才算结束。
                var workTask = work(cancellationToken);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var elapsed = _clock.NowSeconds - startedAt;
                    var workCompleted = workTask.Status.IsCompleted();
                    SetState(new LoadingPresentationState(
                        true, ElapsedProgress(elapsed, workCompleted)));
                    if (workCompleted && elapsed >= _minimumSeconds)
                    {
                        break;
                    }

                    await _clock.DelayAsync(TimeSpan.FromSeconds(TickSeconds), cancellationToken);
                }

                // 重新await以传播真实加载过程中的异常。
                await workTask;
                SetState(new LoadingPresentationState(false, 1f));
            }
            catch (OperationCanceledException)
            {
                SetState(LoadingPresentationState.Hidden);
                throw;
            }
            catch
            {
                SetState(LoadingPresentationState.Failed(LoadFailedMessage));
                throw;
            }
            finally
            {
                _isRunning = false;
            }
        }

        public async UniTask LoadSceneAsync(string sceneName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("场景名不能为空。", nameof(sceneName));
            }

            if (_sceneLoader == null)
            {
                throw new InvalidOperationException("未注入 ISceneLoader，无法加载场景。");
            }

            _isRunning = true;
            _reportedProgress = 0f;
            SetState(new LoadingPresentationState(true, 0f));

            try
            {
                // 先让 0% 真的过一帧，**再**开始加载。
                //
                // 写 PresentationState 只是改内存值，不等于屏幕已经显示。
                // 原来的实现写完 0% 立刻调 BeginLoad，而 BeginLoad 里
                // `SceneManager.LoadSceneAsync` 会同步烧掉真实时间（开文件、读头、分配），
                // 正式战斗场景 38.9 MB，这一下就把 0% 那一帧吃掉了 ——
                // 玩家第一次看到进度条时它已经不在零上。
                await _frames.NextFrameAsync(cancellationToken);

                var operation = _sceneLoader.BeginLoad(sceneName);

                // 显示进度按**已经真正走过的帧数**推进，不按墙钟。
                //
                // 主线程卡在场景反序列化里的时候，墙钟照走而屏幕一帧没刷；
                // 按墙钟算，恢复后第一次能画的时候进度会一口气跳到一半。
                // 按帧算，卡顿期间进度条跟着停住，恢复后从下一小步继续 ——
                // 停住可以接受，跳一半不可以。
                var visualElapsed = 0d;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var ready = operation.IsReadyToActivate;
                    SetState(new LoadingPresentationState(
                        true, SceneProgress(operation.Progress, visualElapsed)));
                    if (ready && visualElapsed >= _minimumSeconds)
                    {
                        break;
                    }

                    await _frames.NextFrameAsync(cancellationToken);

                    // 一次成功的帧等待只推进一个固定步长，无论它实际花了多久。
                    visualElapsed += TickSeconds;
                }

                operation.Activate();
                await operation.WaitForCompletionAsync(cancellationToken);

                // 100% 必须自己占住一帧，否则它和"隐藏界面"落在同一帧里，
                // 玩家永远看不到满进度。
                SetState(new LoadingPresentationState(true, 1f));
                await _frames.NextFrameAsync(cancellationToken);
                SetState(new LoadingPresentationState(false, 1f));
            }
            catch (OperationCanceledException)
            {
                SetState(LoadingPresentationState.Hidden);
                throw;
            }
            catch
            {
                // 失败必须把界面收回并给出原因：卡在 99% 的加载条等于告诉玩家"再等等"，
                // 而实际上不会再有任何进展。
                SetState(LoadingPresentationState.Failed(LoadFailedMessage));
                throw;
            }
            finally
            {
                _isRunning = false;
            }
        }

        public IDisposable Subscribe(IObserver<LoadingPresentationState> observer) =>
            _state.Subscribe(observer);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // 先标记再释放：取消会让在途的加载走进 catch，
            // 那里不能再往已经释放的 ReactiveState 写值。
            _disposed = true;
            _state.Dispose();
        }

        /// <summary>
        /// 释放之后不再写状态。场景切换途中容器被销毁属于正常情况，
        /// 继续回调一个已经释放的状态源只会抛 ObjectDisposedException。
        /// </summary>
        private void SetState(LoadingPresentationState state)
        {
            if (_disposed)
            {
                return;
            }

            _state.Set(state);
        }

        internal const string LoadFailedMessage = "加载失败，请重试。";

        private static UniTask NoWork(CancellationToken cancellationToken) => UniTask.CompletedTask;

        /// <summary>没有真实进度来源时的时间进度（登录到大厅这类纯等待）。</summary>
        private float ElapsedProgress(double elapsed, bool workCompleted)
        {
            var value = _minimumSeconds <= 0d ? 1d : elapsed / _minimumSeconds;
            var progress = value <= 0d ? 0f : value >= 1d ? 1f : (float)value;
            return workCompleted || progress < IncompleteCeiling ? progress : IncompleteCeiling;
        }

        /// <summary>
        /// 真实场景进度。
        ///
        /// 取"真实进度"与"时间进度"两者的**较小值**，而不是较大值。
        /// 原因：小场景的 <c>AsyncOperation.progress</c> 几乎瞬间就到 0.9（归一化后即 1），
        /// 取较大值会让进度条一开机就顶到 99% 然后干等两秒 —— 那不是加载条，是个占位图。
        /// 取较小值之后：
        ///
        /// - 秒加载完的场景由时间驱动，进度条在最短显示时长内平滑地从 0 爬到 100%；
        /// - 真的加载很久的场景由真实进度驱动，且在资源就绪之前封顶 99%；
        /// - 任何情况下都不会在真实加载完成之前显示 100%。
        ///
        /// 结果还会做单调处理：真实进度的抖动不该让进度条往回跳。
        ///
        /// <paramref name="visualElapsed"/> 是**视觉时间**：每成功等过一帧加一个
        /// 固定步长，与墙钟无关。因此主线程卡顿不会让进度条跳跃。
        /// </summary>
        private float SceneProgress(float rawProgress, double visualElapsed)
        {
            var real = rawProgress < 0f ? 0f : rawProgress > 1f ? 1f : rawProgress;

            var timeRatio = _minimumSeconds <= 0d ? 1d : visualElapsed / _minimumSeconds;
            var byTime = timeRatio <= 0d ? 0f : timeRatio >= 1d ? 1f : (float)timeRatio;

            // 循环里**永远**封顶 99%。100% 只在 Activate 之后、场景真正切换完成之后写一次，
            // 并且自己占住一帧。否则"资源就绪"那一刻就会先闪一个 100%，
            // 而场景其实还没切过去。
            var value = real < byTime ? real : byTime;
            value = value < IncompleteCeiling ? value : IncompleteCeiling;

            if (value < _reportedProgress)
            {
                value = _reportedProgress;
            }

            _reportedProgress = value;
            return value;
        }
    }
}
