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
        private readonly double _minimumSeconds;
        private readonly ReactiveState<LoadingPresentationState> _state;
        private bool _isRunning;
        private bool _disposed;
        private float _reportedProgress;

        public LoadingController(IGameClock clock, ISceneLoader sceneLoader, double minimumSeconds)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _sceneLoader = sceneLoader;
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
                var operation = _sceneLoader.BeginLoad(sceneName);

                // 最短显示时长从**BeginLoad 返回之后**开始算，而不是从方法入口。
                //
                // 原因：`SceneManager.LoadSceneAsync` 在 BeginLoad 里会同步烧掉一段真实时间
                // （开文件、读头、分配）。正式战斗场景有 38.9 MB，这段时间足够让
                // "按时间推进"的那一半在**第一次绘制时就已经跑到十几个百分点**，
                // 于是进度条看起来不是从零开始的。
                //
                // 灰盒场景只有 31 KB，BeginLoad 几乎不耗时，所以这个缺陷在 P2.3
                // 接入正式场景之前一直没露头；单测也没抓到，因为假时钟只在
                // DelayAsync 里前进，BeginLoad 在测试里永远是零耗时的。
                var startedAt = _clock.NowSeconds;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var elapsed = _clock.NowSeconds - startedAt;
                    var ready = operation.IsReadyToActivate;
                    SetState(new LoadingPresentationState(
                        true, SceneProgress(operation.Progress, ready, elapsed)));
                    if (ready && elapsed >= _minimumSeconds)
                    {
                        break;
                    }

                    await _clock.DelayAsync(TimeSpan.FromSeconds(TickSeconds), cancellationToken);
                }

                operation.Activate();
                await operation.WaitForCompletionAsync(cancellationToken);
                SetState(new LoadingPresentationState(true, 1f));
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
        /// </summary>
        private float SceneProgress(float rawProgress, bool ready, double elapsed)
        {
            var real = rawProgress < 0f ? 0f : rawProgress > 1f ? 1f : rawProgress;

            var timeRatio = _minimumSeconds <= 0d ? 1d : elapsed / _minimumSeconds;
            var byTime = timeRatio <= 0d ? 0f : timeRatio >= 1d ? 1f : (float)timeRatio;

            var value = real < byTime ? real : byTime;
            if (!ready || byTime < 1f)
            {
                // 资源没就绪，或者最短显示时长还没走完，都不允许显示 100%。
                value = value < IncompleteCeiling ? value : IncompleteCeiling;
            }

            if (value < _reportedProgress)
            {
                value = _reportedProgress;
            }

            _reportedProgress = value;
            return value;
        }
    }
}
