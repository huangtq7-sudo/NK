using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Loading.Controller;
using Naraka.P0.Tests;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// 场景切过去之后，加载界面要等帧率稳定再撤。
    ///
    /// 用户报告"出场动画看不到，角色突然出现"。新场景头几帧最重（首次渲染、着色器与贴图上传），
    /// 这些帧如果落在加载界面撤掉之后，Animator 单帧最多推进 0.333 秒，而玩家状态机最多 0.1 秒，
    /// 出场动作在几帧之内就被播过去了。这些测试盯的是：重帧留在加载界面下面，
    /// 但再慢的设备也不会被永远关在加载界面里。
    /// </summary>
    public sealed class LoadingSettleTests
    {
        private const double Minimum = 0.5;
        private const double Heavy = 0.5;

        private static FakeSceneLoader ReadyLoader() => new FakeSceneLoader
        {
            Factory = _ => new FakeSceneLoader.FakeOperation
            {
                Progress = 1f,
                IsReadyToActivate = true
            }
        };

        [UnityTest]
        public IEnumerator HeavyFramesAfterTheSceneSwitchStayUnderTheLoadingScreen() =>
            UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeGameClock();
                var loader = ReadyLoader();
                var frames = new FakeFrameScheduler(clock);
                var controller = new LoadingController(clock, loader, frames, Minimum);

                // 激活之后的前 4 帧每帧卡 0.5 秒，之后恢复正常。
                var heavyLeft = 4;
                var lastHeavyWait = -1;
                frames.OnWait = index =>
                {
                    if (loader.Last != null && loader.Last.Activated && heavyLeft > 0)
                    {
                        heavyLeft--;
                        lastHeavyWait = index;
                        clock.Advance(Heavy);
                    }
                };

                var heavyLeftAtHide = -1;
                var waitsAtHide = -1;
                using (controller.Subscribe(new Observer(state =>
                {
                    if (!state.IsVisible && state.Progress >= 1f && waitsAtHide < 0)
                    {
                        heavyLeftAtHide = heavyLeft;
                        waitsAtHide = frames.WaitCount;
                    }
                })))
                {
                    await controller.LoadSceneAsync("Map01_Task", CancellationToken.None);
                }

                Assert.That(heavyLeftAtHide, Is.Zero, "还有重帧没过去，加载界面就撤了。");
                Assert.That(
                    waitsAtHide - lastHeavyWait, Is.GreaterThanOrEqualTo(LoadingController.StableFramesRequired),
                    "最后一个重帧之后必须再有连续的稳定帧，才能撤掉加载界面。");
            });

        [UnityTest]
        public IEnumerator ASmoothSceneOnlyWaitsTheStableFrames() =>
            UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeGameClock();
                var loader = ReadyLoader();
                var frames = new FakeFrameScheduler(clock);
                var controller = new LoadingController(clock, loader, frames, Minimum);

                var waitsAtFull = -1;
                var waitsAtHide = -1;
                using (controller.Subscribe(new Observer(state =>
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

                Assert.That(
                    waitsAtHide - waitsAtFull, Is.EqualTo(LoadingController.StableFramesRequired),
                    "本来就流畅的场景只多等那几帧稳定帧，不额外拖延。");
            });

        [UnityTest]
        public IEnumerator AMachineThatNeverSettlesIsStillLetIn() =>
            UniTask.ToCoroutine(async () =>
            {
                var clock = new FakeGameClock();
                var loader = ReadyLoader();
                var frames = new FakeFrameScheduler(clock);
                var controller = new LoadingController(clock, loader, frames, Minimum);

                // 激活之后每一帧都卡：永远等不到稳定。
                double activatedAt = -1;
                frames.OnWait = _ =>
                {
                    if (loader.Last != null && loader.Last.Activated)
                    {
                        if (activatedAt < 0)
                        {
                            activatedAt = clock.NowSeconds;
                        }

                        clock.Advance(Heavy);
                    }
                };

                double hiddenAt = -1;
                using (controller.Subscribe(new Observer(state =>
                {
                    if (!state.IsVisible && state.Progress >= 1f && hiddenAt < 0)
                    {
                        hiddenAt = clock.NowSeconds;
                    }
                })))
                {
                    await controller.LoadSceneAsync("Map01_Task", CancellationToken.None);
                }

                Assert.That(hiddenAt, Is.GreaterThan(0d), "加载界面必须最终撤掉。");
                Assert.That(
                    hiddenAt - activatedAt,
                    Is.LessThanOrEqualTo(LoadingController.SettleTimeoutSeconds + Heavy + 0.1),
                    "等帧率稳定有上限，再慢的设备也要放进去。");
            });

        private sealed class Observer : IObserver<LoadingPresentationState>
        {
            private readonly Action<LoadingPresentationState> _onNext;

            public Observer(Action<LoadingPresentationState> onNext) => _onNext = onNext;

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
