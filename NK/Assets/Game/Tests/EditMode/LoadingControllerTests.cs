using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Presentation;
using Naraka.Core.Application.Timing;
using Naraka.Features.Loading.Controller;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P0.Tests
{
    /// <summary>确定性假时钟：每次Delay直接推进虚拟时间并同步完成，测试不消耗真实时间。</summary>
    internal sealed class FakeGameClock : IGameClock
    {
        public double NowSeconds { get; private set; }

        public int DelayCount { get; private set; }

        public Action OnDelay { get; set; }

        /// <summary>
        /// 手动推进虚拟时间，用来模拟**不在 DelayAsync 里**消耗的真实时间，
        /// 比如 `SceneManager.LoadSceneAsync` 在 BeginLoad 里同步烧掉的那一段。
        /// 没有这个入口的话，假时钟永远认为 BeginLoad 是零耗时的，
        /// 于是一整类"进度条不从零开始"的缺陷单测根本抓不到。
        /// </summary>
        public void Advance(double seconds) => NowSeconds += seconds;

        /// <summary>
        /// 推进一帧的虚拟时间，并触发 <see cref="OnDelay"/>。
        ///
        /// 加载流程已经改成按帧等待而不是按时钟等待，于是 <c>DelayAsync</c> 不再被调用。
        /// 既有测试是靠 <see cref="OnDelay"/> 推进假加载操作的，所以"过了一帧"
        /// 必须同样触发它 —— 一帧确实也是会消耗时间的。
        /// </summary>
        public void Tick(double seconds)
        {
            NowSeconds += seconds;
            OnDelay?.Invoke();
        }

        public UniTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromCanceled(cancellationToken);
            }

            DelayCount++;
            NowSeconds += duration.TotalSeconds;
            OnDelay?.Invoke();
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// 可精确控制的帧边界。同步完成，因此整条异步链在测试里一口气跑完，结果确定。
    ///
    /// <see cref="OnWait"/> 让测试在"这一帧等待期间"做事，例如模拟主线程卡住 1 秒，
    /// 从而断言显示进度**不会**按墙钟跳跃。
    /// </summary>
    internal sealed class FakeFrameScheduler : IPresentationFrameScheduler
    {
        private readonly FakeGameClock _clock;
        private readonly double _tickSeconds;

        public FakeFrameScheduler(FakeGameClock clock = null, double tickSeconds = 1.0 / 60.0)
        {
            _clock = clock;
            _tickSeconds = tickSeconds;
        }

        public int WaitCount { get; private set; }

        /// <summary>每次帧等待时回调，参数是这是第几次等待（从 1 开始）。</summary>
        public Action<int> OnWait { get; set; }

        public UniTask NextFrameAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromCanceled(cancellationToken);
            }

            WaitCount++;

            // 先回调，再推进时钟：顺序要和旧实现一致（旧实现是 SetState 之后 DelayAsync），
            // 否则依赖"这一帧看到的进度"的断言会错位一格。
            OnWait?.Invoke(WaitCount);
            _clock?.Tick(_tickSeconds);
            return UniTask.CompletedTask;
        }
    }

    public sealed class LoadingControllerTests
    {
        private const double Minimum = 2.0;

        [Test]
        public void LoadingScreenStaysHiddenBeforeRun()
        {
            var controller = new LoadingController(new FakeGameClock(), null, new FakeFrameScheduler(), Minimum);

            Assert.That(controller.Current.IsVisible, Is.False);
            Assert.That(controller.Current.Progress, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator InstantWorkStillHoldsTheMinimumDuration() => UniTask.ToCoroutine(async () =>
        {
            var clock = new FakeGameClock();
            var controller = new LoadingController(clock, null, new FakeFrameScheduler(), Minimum);

            await controller.RunAsync(CancellationToken.None);

            Assert.That(clock.NowSeconds, Is.GreaterThanOrEqualTo(Minimum));
            Assert.That(controller.Current.IsVisible, Is.False);
            Assert.That(controller.Current.Progress, Is.EqualTo(1f));
        });

        [UnityTest]
        public IEnumerator SlowWorkExtendsBeyondTheMinimumDuration() => UniTask.ToCoroutine(async () =>
        {
            var clock = new FakeGameClock();
            var controller = new LoadingController(clock, null, new FakeFrameScheduler(), Minimum);
            var completion = new UniTaskCompletionSource();
            clock.OnDelay = () =>
            {
                if (clock.NowSeconds >= 5.0)
                {
                    completion.TrySetResult();
                }
            };

            await controller.RunAsync(_ => completion.Task, CancellationToken.None);

            Assert.That(clock.NowSeconds, Is.GreaterThanOrEqualTo(5.0));
            Assert.That(controller.Current.IsVisible, Is.False);
        });

        [UnityTest]
        public IEnumerator ProgressNeverReachesFullWhileWorkIsPending() => UniTask.ToCoroutine(async () =>
        {
            var clock = new FakeGameClock();
            var controller = new LoadingController(clock, null, new FakeFrameScheduler(), Minimum);
            var completion = new UniTaskCompletionSource();
            clock.OnDelay = () =>
            {
                if (clock.NowSeconds >= 6.0)
                {
                    completion.TrySetResult();
                }
            };

            // 记录每条状态的同时记下当时真实加载是否已完成，否则无法区分
            // "工作未完成却显示满进度"和"工作已完成理应显示满进度"。
            var seen = new List<KeyValuePair<LoadingPresentationState, bool>>();
            using (controller.Subscribe(new Recorder(state =>
                       seen.Add(new KeyValuePair<LoadingPresentationState, bool>(
                           state, completion.Task.Status.IsCompleted())))))
            {
                await controller.RunAsync(_ => completion.Task, CancellationToken.None);
            }

            var fullWhilePending = seen.FindAll(entry =>
                entry.Key.IsVisible && !entry.Value && entry.Key.Progress >= 1f);
            Assert.That(fullWhilePending, Is.Empty, "真实加载未完成时进度条不应显示为100%。");

            var cappedWhilePending = seen.FindAll(entry =>
                entry.Key.IsVisible && !entry.Value && entry.Key.Progress > 0.98f);
            Assert.That(cappedWhilePending, Is.Not.Empty, "超过最短时长后进度应停在上限而不是继续增长。");
            Assert.That(seen[seen.Count - 1].Key.IsVisible, Is.False);
        });

        [UnityTest]
        public IEnumerator WorkFailurePropagatesToTheCaller() => UniTask.ToCoroutine(async () =>
        {
            var clock = new FakeGameClock();
            var controller = new LoadingController(clock, null, new FakeFrameScheduler(), Minimum);

            var thrown = false;
            try
            {
                await controller.RunAsync(
                    _ => UniTask.FromException(new InvalidOperationException("load failed")),
                    CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                thrown = true;
            }

            Assert.That(thrown, Is.True);
        });

        private sealed class Recorder : IObserver<LoadingPresentationState>
        {
            private readonly Action<LoadingPresentationState> _onNext;

            public Recorder(Action<LoadingPresentationState> onNext) => _onNext = onNext;

            public void OnNext(LoadingPresentationState value) => _onNext(value);

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }
    }
}
