using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Scenes;
using Naraka.Core.Application.Presentation;
using Naraka.Core.Application.Timing;
using Naraka.Features.Loading.Controller;
using Naraka.Features.World.Controller;
using Naraka.P0.Tests;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// 可控的假场景加载：进度、就绪与激活都由测试推进，不依赖真实 AsyncOperation。
    /// </summary>
    internal sealed class FakeSceneLoader : ISceneLoader
    {
        public FakeOperation Last { get; private set; }

        public int BeginCount { get; private set; }

        public string LastSceneName { get; private set; }

        public Func<string, FakeOperation> Factory { get; set; }

        public ISceneLoadOperation BeginLoad(string sceneName)
        {
            BeginCount++;
            LastSceneName = sceneName;
            Last = Factory != null ? Factory(sceneName) : new FakeOperation();
            return Last;
        }

        internal sealed class FakeOperation : ISceneLoadOperation
        {
            private readonly UniTaskCompletionSource _completion = new UniTaskCompletionSource();

            public float Progress { get; set; }

            public bool IsReadyToActivate { get; set; }

            public bool Activated { get; private set; }

            public Exception CompletionError { get; set; }

            public void Activate()
            {
                Activated = true;
                if (CompletionError != null)
                {
                    _completion.TrySetException(CompletionError);
                }
                else
                {
                    _completion.TrySetResult();
                }
            }

            public UniTask WaitForCompletionAsync(CancellationToken cancellationToken) =>
                _completion.Task;
        }
    }

    /// <summary>
    /// 会真正挂起的假时钟。<see cref="FakeGameClock"/> 的 Delay 同步完成，
    /// 因此整条异步链会一口气跑完，测不出"切换在途时再次请求"这种并发情况。
    /// </summary>
    internal sealed class PumpableGameClock : IGameClock
    {
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly List<Pending> _batch = new List<Pending>();

        public double NowSeconds { get; private set; }

        /// <summary>每次推进时调用，用来让假加载操作前进。</summary>
        public Action OnAdvance { get; set; }

        public UniTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromCanceled(cancellationToken);
            }

            var source = new UniTaskCompletionSource();
            _pending.Add(new Pending(source, duration.TotalSeconds));
            return source.Task;
        }

        /// <summary>完成当前所有挂起的等待。继续排队的新等待留给下一次推进。</summary>
        public void Advance()
        {
            _batch.Clear();
            _batch.AddRange(_pending);
            _pending.Clear();
            for (var i = 0; i < _batch.Count; i++)
            {
                NowSeconds += _batch[i].Seconds;
            }

            OnAdvance?.Invoke();
            for (var i = 0; i < _batch.Count; i++)
            {
                _batch[i].Source.TrySetResult();
            }
        }

        private readonly struct Pending
        {
            public Pending(UniTaskCompletionSource source, double seconds)
            {
                Source = source;
                Seconds = seconds;
            }

            public UniTaskCompletionSource Source { get; }

            public double Seconds { get; }
        }
    }

    /// <summary>
    /// 会真正挂起的帧边界，直接复用 <see cref="PumpableGameClock"/> 的泵。
    ///
    /// 用于"切换在途时再次请求"这类并发断言：同步完成的帧等待会让整条异步链
    /// 一口气跑完，根本观察不到"在途"这个状态。
    /// </summary>
    internal sealed class PumpableFrameScheduler : IPresentationFrameScheduler
    {
        private readonly PumpableGameClock _clock;
        private readonly double _tickSeconds;

        public PumpableFrameScheduler(PumpableGameClock clock, double tickSeconds = 1.0 / 60.0)
        {
            _clock = clock;
            _tickSeconds = tickSeconds;
        }

        public UniTask NextFrameAsync(CancellationToken cancellationToken) =>
            _clock.DelayAsync(TimeSpan.FromSeconds(_tickSeconds), cancellationToken);
    }

    public sealed class SceneLoadingTests
    {
        private const double Minimum = 2.0;

        [UnityTest]
        public IEnumerator ProgressFollowsTheRealOperation() => UniTask.ToCoroutine(async () =>
        {
            var clock = new FakeGameClock();
            var loader = new FakeSceneLoader();
            var controller = new LoadingController(clock, loader, new FakeFrameScheduler(clock), Minimum);
            var seen = new System.Collections.Generic.List<LoadingPresentationState>();

            clock.OnDelay = () =>
            {
                var op = loader.Last;
                if (op == null)
                {
                    return;
                }

                // 真实进度慢慢爬到就绪。
                op.Progress = (float)Math.Min(1.0, clock.NowSeconds / 3.0);
                if (op.Progress >= 1f)
                {
                    op.IsReadyToActivate = true;
                }
            };

            using (controller.Subscribe(new StateRecorder(seen.Add)))
            {
                await controller.LoadSceneAsync(WorldSceneNames.Map01Task, CancellationToken.None);
            }

            Assert.That(loader.BeginCount, Is.EqualTo(1));
            Assert.That(loader.LastSceneName, Is.EqualTo(WorldSceneNames.Map01Task));
            Assert.That(loader.Last.Activated, Is.True);

            // 未就绪时绝不显示 100%。
            var prematureFull = seen.FindAll(s => s.IsVisible && s.Progress >= 1f && !s.HasError);
            Assert.That(
                prematureFull.Count, Is.LessThanOrEqualTo(1),
                "真实加载完成前不得显示 100%。");
            Assert.That(controller.Current.IsVisible, Is.False);
            Assert.That(controller.Current.HasError, Is.False);
        });

        [UnityTest]
        public IEnumerator ProgressGrowsGraduallyEvenWhenTheSceneLoadsInstantly() =>
            UniTask.ToCoroutine(async () =>
            {
                // 小场景的 AsyncOperation 几乎瞬间就就绪。进度条不能因此一开机就顶到满，
                // 必须在最短显示时长内平滑爬升 —— 否则它不是加载条，是个占位图。
                var clock = new FakeGameClock();
                var loader = new FakeSceneLoader
                {
                    Factory = _ => new FakeSceneLoader.FakeOperation
                    {
                        Progress = 1f,
                        IsReadyToActivate = true
                    }
                };
                var controller = new LoadingController(clock, loader, new FakeFrameScheduler(clock), Minimum);
                var samples = new System.Collections.Generic.List<(double At, float Progress)>();

                using (controller.Subscribe(new StateRecorder(state =>
                {
                    if (state.IsVisible)
                    {
                        samples.Add((clock.NowSeconds, state.Progress));
                    }
                })))
                {
                    await controller.LoadSceneAsync(WorldSceneNames.Map01Task, CancellationToken.None);
                }

                Assert.That(samples.Count, Is.GreaterThan(10), "应该有足够多的中间进度采样。");

                // 第一个采样必须接近 0，而不是接近 1。
                Assert.That(
                    samples[0].Progress, Is.LessThan(0.1f),
                    "资源秒就绪也不能让进度条一开始就接近满。");

                // 走到最短时长一半时，进度应该在中间附近而不是已经满了。
                var half = samples.Find(x => x.At >= Minimum * 0.5);
                Assert.That(
                    half.Progress, Is.InRange(0.3f, 0.7f),
                    $"最短时长过半时进度应该在中段，实际 {half.Progress:0.00}。");

                // 单调不回退。
                for (var i = 1; i < samples.Count; i++)
                {
                    Assert.That(
                        samples[i].Progress, Is.GreaterThanOrEqualTo(samples[i - 1].Progress),
                        "进度条不得往回跳。");
                }
            });

        [UnityTest]
        public IEnumerator ProgressStartsFromZeroEvenWhenBeginLoadItselfIsSlow() =>
            UniTask.ToCoroutine(async () =>
            {
                // 这条测试对应一个真实缺陷：正式战斗场景有 38.9 MB，
                // `SceneManager.LoadSceneAsync` 在 BeginLoad 里会同步烧掉一段真实时间。
                // 若最短显示时长从方法入口就开始计时，这段时间会让"按时间推进"
                // 的那一半在第一次绘制时就已经跑到十几个百分点，
                // 玩家看到的就是"进度条不从零开始"。
                //
                // 灰盒场景只有 31 KB，所以这个缺陷之前一直没露头。
                var clock = new FakeGameClock();
                var loader = new FakeSceneLoader
                {
                    // Factory 在 BeginLoad 内部被调用，因此这里推进时间就是
                    // 在模拟"BeginLoad 自己耗了 1 秒真实时间"。
                    Factory = _ =>
                    {
                        clock.Advance(1.0);
                        return new FakeSceneLoader.FakeOperation
                        {
                            Progress = 1f,
                            IsReadyToActivate = true
                        };
                    }
                };
                var controller = new LoadingController(clock, loader, new FakeFrameScheduler(clock), Minimum);
                var visible = new System.Collections.Generic.List<float>();

                using (controller.Subscribe(new StateRecorder(state =>
                {
                    if (state.IsVisible)
                    {
                        visible.Add(state.Progress);
                    }
                })))
                {
                    await controller.LoadSceneAsync(
                        WorldSceneNames.Map02Combat, CancellationToken.None);
                }

                Assert.That(visible, Is.Not.Empty);
                Assert.That(
                    visible[0], Is.LessThan(0.02f),
                    $"BeginLoad 耗了 1 秒之后，第一个可见进度是 {visible[0]:0.00}，" +
                    "进度条必须从零开始。");

                // 修了起点之后，其余保证不能跌：仍然单调，
                // 且仍然保留完整的最短显示时长（从 BeginLoad 返回后算起）。
                for (var i = 1; i < visible.Count; i++)
                {
                    Assert.That(
                        visible[i], Is.GreaterThanOrEqualTo(visible[i - 1]),
                        "进度条不得往回跳。");
                }

                Assert.That(
                    clock.NowSeconds, Is.GreaterThanOrEqualTo(1.0 + Minimum),
                    "最短显示时长必须在 BeginLoad 之后完整计满。");
            });

        [UnityTest]
        public IEnumerator ProgressNeverRunsAheadOfTheRealOperation() => UniTask.ToCoroutine(async () =>
        {
            // 真实加载很慢时，进度条不能靠时间跑到前面去。
            var clock = new FakeGameClock();
            var loader = new FakeSceneLoader();
            var controller = new LoadingController(clock, loader, new FakeFrameScheduler(clock), Minimum);
            var worst = 0f;

            clock.OnDelay = () =>
            {
                var op = loader.Last;
                if (op == null)
                {
                    return;
                }

                // 真实进度非常慢：10 秒才走完。
                op.Progress = (float)Math.Min(1.0, clock.NowSeconds / 10.0);
                if (op.Progress >= 1f)
                {
                    op.IsReadyToActivate = true;
                }
            };

            using (controller.Subscribe(new StateRecorder(state =>
            {
                var op = loader.Last;
                if (state.IsVisible && op != null)
                {
                    worst = Math.Max(worst, state.Progress - op.Progress);
                }
            })))
            {
                await controller.LoadSceneAsync(WorldSceneNames.Map01Task, CancellationToken.None);
            }

            Assert.That(
                worst, Is.LessThan(0.05f),
                "真实进度落后时，进度条不得靠时间超过它。");
        });

        [UnityTest]
        public IEnumerator ActivationWaitsForTheMinimumDisplayTime() => UniTask.ToCoroutine(async () =>
        {
            var clock = new FakeGameClock();
            var loader = new FakeSceneLoader
            {
                Factory = _ => new FakeSceneLoader.FakeOperation { Progress = 1f, IsReadyToActivate = true }
            };
            var controller = new LoadingController(clock, loader, new FakeFrameScheduler(clock), Minimum);

            await controller.LoadSceneAsync(WorldSceneNames.Map01Task, CancellationToken.None);

            Assert.That(clock.NowSeconds, Is.GreaterThanOrEqualTo(Minimum),
                "资源秒加载完也必须保留最短显示时间，避免闪屏。");
        });

        [UnityTest]
        public IEnumerator SlowLoadsHoldAtNinetyNinePercentInsteadOfCompleting() =>
            UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeGameClock();
                var loader = new FakeSceneLoader();
                var controller = new LoadingController(clock, loader, new FakeFrameScheduler(clock), Minimum);
                var maxWhileLoading = 0f;

                clock.OnDelay = () =>
                {
                    var op = loader.Last;
                    if (op == null)
                    {
                        return;
                    }

                    op.Progress = 0.95f;
                    if (clock.NowSeconds >= 6.0)
                    {
                        op.IsReadyToActivate = true;
                        op.Progress = 1f;
                    }
                };

                using (controller.Subscribe(new StateRecorder(state =>
                {
                    if (state.IsVisible && loader.Last != null && !loader.Last.IsReadyToActivate)
                    {
                        maxWhileLoading = Math.Max(maxWhileLoading, state.Progress);
                    }
                })))
                {
                    await controller.LoadSceneAsync(WorldSceneNames.Map02Combat, CancellationToken.None);
                }

                Assert.That(clock.NowSeconds, Is.GreaterThanOrEqualTo(6.0));
                Assert.That(maxWhileLoading, Is.LessThan(1f));
                Assert.That(maxWhileLoading, Is.GreaterThan(0.9f), "超过最短时长后进度应停在上限。");
            });

        [UnityTest]
        public IEnumerator AFailedLoadRecoversInsteadOfStickingAtNinetyNine() =>
            UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeGameClock();
                var loader = new FakeSceneLoader
                {
                    Factory = _ => new FakeSceneLoader.FakeOperation
                    {
                        Progress = 1f,
                        IsReadyToActivate = true,
                        CompletionError = new InvalidOperationException("scene missing")
                    }
                };
                var controller = new LoadingController(clock, loader, new FakeFrameScheduler(clock), Minimum);

                var thrown = false;
                try
                {
                    await controller.LoadSceneAsync("Missing", CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    thrown = true;
                }

                Assert.That(thrown, Is.True);
                Assert.That(controller.Current.IsVisible, Is.False, "失败时必须收回加载界面。");
                Assert.That(controller.Current.HasError, Is.True);
                Assert.That(controller.Current.Message, Is.Not.Empty, "必须显示明确失败信息。");
                Assert.That(controller.Current.Progress, Is.EqualTo(0f), "不得卡死在 99%。");
                Assert.That(controller.IsRunning, Is.False, "失败后必须恢复可发起新加载的状态。");

                controller.ClearError();
                Assert.That(controller.Current.HasError, Is.False);
            });

        [UnityTest]
        public IEnumerator MissingSceneLoaderIsReportedInsteadOfCrashing() =>
            UniTask.ToCoroutine(async () =>
            {
                var controller = new LoadingController(new FakeGameClock(), null, new FakeFrameScheduler(), Minimum);

                var thrown = false;
                try
                {
                    await controller.LoadSceneAsync(WorldSceneNames.Map01Task, CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    thrown = true;
                }

                Assert.That(thrown, Is.True);
                Assert.That(controller.Current.IsVisible, Is.False);
            });

        [UnityTest]
        public IEnumerator WorldFlowRefusesToStartASecondTransition() => UniTask.ToCoroutine(async () =>
        {
            var clock = new PumpableGameClock();
            var loader = new FakeSceneLoader();
            var loading = new LoadingController(
                clock, loader, new PumpableFrameScheduler(clock), Minimum);
            var world = new WorldFlowController(loading, WorldSceneCatalog.Default);

            clock.OnAdvance = () =>
            {
                var op = loader.Last;
                if (op == null)
                {
                    return;
                }

                op.Progress = 1f;
                op.IsReadyToActivate = true;
            };

            // 第一次切换挂在帧边界上还没跑完。
            var first = world.EnterMap02Async(CancellationToken.None).Preserve();
            Assert.That(first.Status.IsCompleted(), Is.False, "测试前提：第一次切换必须仍在途中。");

            // 加载流程现在先停在"让 0% 过一帧"上，此时 BeginLoad 还没发生。
            // 先推一帧让它真的开始加载，再发第二次请求 ——
            // 这样"在途"是真的在途，断言比以前更严。
            clock.Advance();
            Assert.That(loader.BeginCount, Is.EqualTo(1), "推过一帧后应该已经开始加载。");

            // 途中再发一次：绝不能创建第二个场景切换任务。
            await world.EnterMap02Async(CancellationToken.None);
            Assert.That(loader.BeginCount, Is.EqualTo(1));

            for (var i = 0; i < 1000 && !first.Status.IsCompleted(); i++)
            {
                clock.Advance();
            }

            await first;

            Assert.That(loader.BeginCount, Is.EqualTo(1));
            Assert.That(world.Current.CurrentMapId, Is.EqualTo(WorldMapIds.Map02));
            Assert.That(world.Current.IsTransitioning, Is.False);
        });

        [UnityTest]
        public IEnumerator WorldFlowReportsArrivalExactlyOnce() => UniTask.ToCoroutine(async () =>
        {
            var clock = new FakeGameClock();
            var loader = new FakeSceneLoader();
            var loading = new LoadingController(clock, loader, new FakeFrameScheduler(clock), Minimum);
            var world = new WorldFlowController(loading, WorldSceneCatalog.Default);
            clock.OnDelay = () =>
            {
                if (loader.Last == null)
                {
                    return;
                }

                loader.Last.Progress = 1f;
                loader.Last.IsReadyToActivate = true;
            };

            await world.LoadMapAsync(WorldMapIds.Map01, CancellationToken.None);

            Assert.That(world.ConsumeArrival(), Is.EqualTo(WorldArrival.LobbyToMap01));
            Assert.That(world.ConsumeArrival(), Is.EqualTo(WorldArrival.None),
                "到达方式只能被消费一次，重载场景不能重复播放出场动画。");
        });

        [UnityTest]
        public IEnumerator WorldFlowRecoversAfterAFailedTransition() => UniTask.ToCoroutine(async () =>
        {
            var clock = new FakeGameClock();
            var failing = true;
            var loader = new FakeSceneLoader
            {
                Factory = _ => new FakeSceneLoader.FakeOperation
                {
                    Progress = 1f,
                    IsReadyToActivate = true,
                    CompletionError = failing ? new InvalidOperationException("boom") : null
                }
            };
            var loading = new LoadingController(clock, loader, new FakeFrameScheduler(clock), Minimum);
            var world = new WorldFlowController(loading, WorldSceneCatalog.Default);

            try
            {
                await world.EnterMap02Async(CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
            }

            Assert.That(world.Current.IsTransitioning, Is.False);

            failing = false;
            await world.EnterMap02Async(CancellationToken.None);

            Assert.That(loader.BeginCount, Is.EqualTo(2), "失败之后必须能重新发起加载。");
        });

        private sealed class StateRecorder : IObserver<LoadingPresentationState>
        {
            private readonly Action<LoadingPresentationState> _onNext;

            public StateRecorder(Action<LoadingPresentationState> onNext) => _onNext = onNext;

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
