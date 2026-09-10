using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
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

    public sealed class LoadingControllerTests
    {
        private const double Minimum = 2.0;

        [Test]
        public void LoadingScreenStaysHiddenBeforeRun()
        {
            var controller = new LoadingController(new FakeGameClock(), Minimum);

            Assert.That(controller.Current.IsVisible, Is.False);
            Assert.That(controller.Current.Progress, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator InstantWorkStillHoldsTheMinimumDuration() => UniTask.ToCoroutine(async () =>
        {
            var clock = new FakeGameClock();
            var controller = new LoadingController(clock, Minimum);

            await controller.RunAsync(CancellationToken.None);

            Assert.That(clock.NowSeconds, Is.GreaterThanOrEqualTo(Minimum));
            Assert.That(controller.Current.IsVisible, Is.False);
            Assert.That(controller.Current.Progress, Is.EqualTo(1f));
        });

        [UnityTest]
        public IEnumerator SlowWorkExtendsBeyondTheMinimumDuration() => UniTask.ToCoroutine(async () =>
        {
            var clock = new FakeGameClock();
            var controller = new LoadingController(clock, Minimum);
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
            var controller = new LoadingController(clock, Minimum);
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
            var controller = new LoadingController(clock, Minimum);

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
