using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Presentation;
using Naraka.Features.Loading.Controller;
using Naraka.P0.Tests;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// 加载进度必须跨**真实呈现帧**推进，而不是跨墙钟时间。
    ///
    /// 用户实测到两种现象：进度条有时一次性显示完成，有时第一次看到就已经在 50% 左右。
    /// 两者是同一个根因的两面：写 PresentationState 只是改内存值，不等于屏幕已经显示；
    /// 而显示进度曾经按"墙钟 − 起始时刻"算，于是主线程卡在场景反序列化里的那一秒
    /// （屏幕一帧没刷）照样被算成进度，恢复后第一次能画时就直接跳到一半。
    ///
    /// 这些测试盯的是**状态序列与帧边界**。屏幕上实际画成什么样仍然要人看，
    /// 自动化只能证明 Controller 发出的序列是对的。
    /// </summary>
    public sealed class LoadingProgressFrameTests
    {
        private const double Minimum = 2.0;

        /// <summary>只记录可见状态的进度序列。</summary>
        private sealed class ProgressLog : IObserver<LoadingPresentationState>
        {
            public readonly List<float> Visible = new List<float>();
            public readonly List<LoadingPresentationState> All =
                new List<LoadingPresentationState>();

            public void OnNext(LoadingPresentationState value)
            {
                All.Add(value);
                if (value.IsVisible)
                {
                    Visible.Add(value.Progress);
                }
            }

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }

        private static FakeSceneLoader ReadyLoader() => new FakeSceneLoader
        {
            Factory = _ => new FakeSceneLoader.FakeOperation
            {
                Progress = 1f,
                IsReadyToActivate = true
            }
        };

        // ------------------------------------------------------------ 0% 必须先过一帧

        [UnityTest]
        public IEnumerator LoadingScreenIsRenderedAtZeroBeforeBeginLoad() =>
            UniTask.ToCoroutine(async () =>
            {
                // 这是整条修复的核心：BeginLoad 会同步烧掉真实时间，
                // 所以 0% 必须在它之前就拿到一帧，否则玩家第一眼看到的就不是零。
                var clock = new FakeGameClock();
                var frames = new FakeFrameScheduler(clock);
                var loader = ReadyLoader();
                var controller = new LoadingController(clock, loader, frames, Minimum);

                var waitsWhenLoadBegan = -1;
                var progressWhenLoadBegan = float.NaN;
                loader.Factory = _ =>
                {
                    waitsWhenLoadBegan = frames.WaitCount;
                    progressWhenLoadBegan = controller.Current.Progress;
                    return new FakeSceneLoader.FakeOperation
                    {
                        Progress = 1f,
                        IsReadyToActivate = true
                    };
                };

                await controller.LoadSceneAsync("Map01_Task", CancellationToken.None);

                Assert.That(
                    progressWhenLoadBegan, Is.EqualTo(0f),
                    "调用 BeginLoad 之前必须已经发布 0%。");
                Assert.That(
                    waitsWhenLoadBegan, Is.GreaterThanOrEqualTo(1),
                    "调用 BeginLoad 之前必须已经等过至少一帧，否则 0% 没有机会被画出来。");
            });

        [UnityTest]
        public IEnumerator ALongBeginLoadDoesNotSkipTheFirstVisibleZero() =>
            UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeGameClock();
                var loader = new FakeSceneLoader
                {
                    // Factory 在 BeginLoad 内部被调用，所以这里推进时钟就是在模拟
                    // "BeginLoad 自己耗了 1 秒真实时间"。
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
                var controller = new LoadingController(
                    clock, loader, new FakeFrameScheduler(clock), Minimum);
                var log = new ProgressLog();

                using (controller.Subscribe(log))
                {
                    await controller.LoadSceneAsync("Map02_Combat", CancellationToken.None);
                }

                Assert.That(log.Visible, Is.Not.Empty);
                Assert.That(
                    log.Visible[0], Is.EqualTo(0f),
                    $"BeginLoad 耗了 1 秒之后第一个可见进度是 {log.Visible[0]:0.000}，必须是 0。");
            });

        // ------------------------------------------------------------ 卡顿不得按墙钟跳

        [UnityTest]
        public IEnumerator AStalledFrameDoesNotAdvanceByWallClockTime() =>
            UniTask.ToCoroutine(async () =>
            {
                // 模拟一次帧等待实际花了 1 秒（主线程卡住）。
                // 显示进度只能前进一个固定步长，绝不能按那 1 秒算成 50%。
                var clock = new FakeGameClock();
                var frames = new FakeFrameScheduler(clock);
                var loader = ReadyLoader();
                var controller = new LoadingController(clock, loader, frames, Minimum);
                var log = new ProgressLog();

                frames.OnWait = index =>
                {
                    if (index == 2)
                    {
                        // 第二次等待时主线程卡死一秒。
                        clock.Advance(1.0);
                    }
                };

                using (controller.Subscribe(log))
                {
                    await controller.LoadSceneAsync("Map01_Task", CancellationToken.None);
                }

                // 一个步长 = TickSeconds / Minimum = (1/60) / 2 ≈ 0.00833。
                var step = (1.0 / 60.0) / Minimum;

                Assert.That(log.Visible.Count, Is.GreaterThan(3));
                Assert.That(
                    log.Visible[1], Is.LessThan(step * 2),
                    $"卡顿之后的进度是 {log.Visible[1]:0.000}，只允许前进一个步长（约 {step:0.000}）。");
                Assert.That(
                    log.Visible[2], Is.LessThan(step * 3),
                    "墙钟跳了一秒，显示进度不得跟着跳。");
                Assert.That(
                    log.Visible[2], Is.LessThan(0.1f),
                    "绝不允许因为一次卡顿就跳到 50%。");
            });

        // ------------------------------------------------------------ 必须有中间值

        [UnityTest]
        public IEnumerator AnInstantlyReadySceneStillShowsIntermediateProgress() =>
            UniTask.ToCoroutine(async () =>
            {
                // 资源第一次查询就 Ready。进度条仍然要平滑爬过中间值，
                // 不能是"0% 之后直接 100%" —— 那不是加载条，是个开关。
                var clock = new FakeGameClock();
                var loader = ReadyLoader();
                var controller = new LoadingController(
                    clock, loader, new FakeFrameScheduler(clock), Minimum);
                var log = new ProgressLog();

                using (controller.Subscribe(log))
                {
                    await controller.LoadSceneAsync("Map01_Task", CancellationToken.None);
                }

                Assert.That(log.Visible, Does.Contain(0f), "序列必须以 0 开始。");
                Assert.That(log.Visible, Does.Contain(1f), "序列必须出现 100%。");

                var intermediate = new List<float>();
                foreach (var value in log.Visible)
                {
                    if (value > 0f && value < 1f)
                    {
                        intermediate.Add(value);
                    }
                }

                Assert.That(
                    intermediate.Count, Is.GreaterThan(10),
                    $"0 与 1 之间只出现了 {intermediate.Count} 个中间值，进度条没有平滑爬升。");
            });

        [UnityTest]
        public IEnumerator ProgressIsMonotonic() => UniTask.ToCoroutine(async () =>
        {
            var clock = new FakeGameClock();
            var loader = new FakeSceneLoader();
            var frames = new FakeFrameScheduler(clock);
            var controller = new LoadingController(clock, loader, frames, Minimum);
            var log = new ProgressLog();

            // 真实进度故意抖动：进度条不得跟着往回跳。
            var jitter = new[] { 0.4f, 0.2f, 0.6f, 0.3f, 0.9f, 0.5f, 1f };
            frames.OnWait = index =>
            {
                var op = loader.Last;
                if (op == null)
                {
                    return;
                }

                op.Progress = jitter[Math.Min(index - 1, jitter.Length - 1)];
                if (index > jitter.Length)
                {
                    op.IsReadyToActivate = true;
                }
            };

            using (controller.Subscribe(log))
            {
                await controller.LoadSceneAsync("Map01_Task", CancellationToken.None);
            }

            for (var i = 1; i < log.Visible.Count; i++)
            {
                Assert.That(
                    log.Visible[i], Is.GreaterThanOrEqualTo(log.Visible[i - 1]),
                    $"进度在第 {i} 个采样处回退了：{log.Visible[i - 1]:0.000} -> {log.Visible[i]:0.000}");
            }
        });

        [UnityTest]
        public IEnumerator ProgressNeverRunsAheadOfRealSceneProgress() =>
            UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeGameClock();
                var loader = new FakeSceneLoader();
                var frames = new FakeFrameScheduler(clock);
                var controller = new LoadingController(clock, loader, frames, Minimum);
                var worst = 0f;

                // 真实加载很慢：600 帧才走完。
                frames.OnWait = index =>
                {
                    var op = loader.Last;
                    if (op == null)
                    {
                        return;
                    }

                    op.Progress = Math.Min(1f, index / 600f);
                    if (op.Progress >= 1f)
                    {
                        op.IsReadyToActivate = true;
                    }
                };

                using (controller.Subscribe(new ProgressObserver(state =>
                {
                    var op = loader.Last;
                    if (state.IsVisible && op != null)
                    {
                        worst = Math.Max(worst, state.Progress - op.Progress);
                    }
                })))
                {
                    await controller.LoadSceneAsync("Map02_Combat", CancellationToken.None);
                }

                Assert.That(
                    worst, Is.LessThan(0.05f),
                    "真实进度落后时，显示进度不得靠视觉时间超过它。");
            });

        [UnityTest]
        public IEnumerator ProgressIsCappedBelowOneUntilActivationCompletes() =>
            UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeGameClock();
                var loader = ReadyLoader();
                var frames = new FakeFrameScheduler(clock);
                var controller = new LoadingController(clock, loader, frames, Minimum);
                var fullBeforeActivation = 0;

                using (controller.Subscribe(new ProgressObserver(state =>
                {
                    var op = loader.Last;
                    if (state.IsVisible && state.Progress >= 1f && (op == null || !op.Activated))
                    {
                        fullBeforeActivation++;
                    }
                })))
                {
                    await controller.LoadSceneAsync("Map01_Task", CancellationToken.None);
                }

                Assert.That(
                    fullBeforeActivation, Is.Zero,
                    "激活完成之前不得显示 100%，即使资源早就就绪。");
            });

        // ------------------------------------------------------------ 100% 必须占住一帧

        [UnityTest]
        public IEnumerator OneHundredPercentIsKeptForOneRenderableFrame() =>
            UniTask.ToCoroutine(async () =>
            {
                // 写 100% 之后立刻隐藏，两个状态会落在同一帧里，玩家看不到满进度。
                var clock = new FakeGameClock();
                var loader = ReadyLoader();
                var frames = new FakeFrameScheduler(clock);
                var controller = new LoadingController(clock, loader, frames, Minimum);

                var waitsAtFull = -1;
                var waitsAtHide = -1;

                using (controller.Subscribe(new ProgressObserver(state =>
                {
                    if (state.IsVisible && state.Progress >= 1f && waitsAtFull < 0)
                    {
                        waitsAtFull = frames.WaitCount;
                    }

                    if (!state.IsVisible && waitsAtFull >= 0 && waitsAtHide < 0)
                    {
                        waitsAtHide = frames.WaitCount;
                    }
                })))
                {
                    await controller.LoadSceneAsync("Map01_Task", CancellationToken.None);
                }

                Assert.That(waitsAtFull, Is.GreaterThanOrEqualTo(0), "从未发布过 100%。");
                Assert.That(waitsAtHide, Is.GreaterThanOrEqualTo(0), "从未隐藏加载界面。");
                Assert.That(
                    waitsAtHide, Is.GreaterThan(waitsAtFull),
                    "100% 与隐藏之间必须至少隔一次帧等待，否则满进度永远不会被画出来。");
            });

        // ------------------------------------------------------------ 取消与失败

        [UnityTest]
        public IEnumerator CancellationDuringInitialFrameYieldHidesTheScreen() =>
            UniTask.ToCoroutine(async () =>
            {
                // 在 0% 那一帧等待上就被取消：界面必须收回，且绝不能已经开始加载。
                var clock = new FakeGameClock();
                var loader = ReadyLoader();
                var frames = new FakeFrameScheduler(clock);
                var controller = new LoadingController(clock, loader, frames, Minimum);
                var source = new CancellationTokenSource();
                source.Cancel();

                var cancelled = false;
                try
                {
                    await controller.LoadSceneAsync("Map01_Task", source.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }

                Assert.That(cancelled, Is.True, "取消必须向上传播。");
                Assert.That(controller.Current.IsVisible, Is.False, "取消之后必须收回加载界面。");
                Assert.That(controller.Current.HasError, Is.False, "取消不是失败，不该写错误信息。");
                Assert.That(loader.BeginCount, Is.Zero, "在 0% 帧等待上取消时不应该已经开始加载。");
                Assert.That(controller.IsRunning, Is.False, "取消后必须恢复可再次发起加载的状态。");
            });

        [UnityTest]
        public IEnumerator FailureFromBeginLoadShowsTheExistingFailureState() =>
            UniTask.ToCoroutine(async () =>
            {
                // BeginLoad 直接抛（例如场景不在 Build Settings 里）。
                var clock = new FakeGameClock();
                var loader = new FakeSceneLoader
                {
                    Factory = _ => throw new InvalidOperationException("scene not in build settings")
                };
                var controller = new LoadingController(
                    clock, loader, new FakeFrameScheduler(clock), Minimum);

                var thrown = false;
                try
                {
                    await controller.LoadSceneAsync("Nope", CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    thrown = true;
                }

                Assert.That(thrown, Is.True);
                Assert.That(controller.Current.HasError, Is.True, "必须进入既有的失败状态。");
                Assert.That(controller.Current.Message, Is.Not.Empty);
                Assert.That(controller.Current.IsVisible, Is.False, "失败时必须收回界面。");
                Assert.That(controller.Current.Progress, Is.EqualTo(0f), "不得卡死在中途进度。");
                Assert.That(controller.IsRunning, Is.False);
            });

        [UnityTest]
        public IEnumerator LoadingAcrossSuspendingFramesBeginsExactlyOnce() =>
            UniTask.ToCoroutine(async () =>
            {
                // 帧等待改成挂起式，才观察得到"在途"这个状态。
                // 这条盯的是：一次调用只会 BeginLoad 一次（循环里不会重复开启），
                // 且完成后释放 IsRunning。
                // 跨请求的重复保护在 WorldFlowController，由
                // WorldFlowRefusesToStartASecondTransition 守（已一并改成挂起式帧等待）。
                var clock = new PumpableGameClock();
                var loader = new FakeSceneLoader();
                var controller = new LoadingController(
                    clock, loader, new PumpableFrameScheduler(clock), Minimum);

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

                var first = controller.LoadSceneAsync("Map01_Task", CancellationToken.None)
                    .Preserve();
                Assert.That(first.Status.IsCompleted(), Is.False, "测试前提：第一次加载仍在途中。");
                Assert.That(controller.IsRunning, Is.True);

                for (var i = 0; i < 2000 && !first.Status.IsCompleted(); i++)
                {
                    clock.Advance();
                }

                await first;

                Assert.That(loader.BeginCount, Is.EqualTo(1), "在途期间不得创建第二个加载。");
                Assert.That(controller.IsRunning, Is.False);
            });

        private sealed class ProgressObserver : IObserver<LoadingPresentationState>
        {
            private readonly Action<LoadingPresentationState> _onNext;

            public ProgressObserver(Action<LoadingPresentationState> onNext) => _onNext = onNext;

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
