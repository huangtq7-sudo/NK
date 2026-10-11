using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Bootstrap;
using Naraka.Features.Expedition.Controller;
using Naraka.Features.Expedition.Model;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P0.Tests
{
    internal sealed class FakeLobbySceneGateway : ILobbySceneGateway
    {
        private readonly UniTaskCompletionSource _completion = new UniTaskCompletionSource();

        public int LoadCount { get; private set; }

        public string LastSceneName { get; private set; }

        public bool CompleteImmediately { get; set; } = true;

        public Exception FailWith { get; set; }

        /// <summary>最近一次切换收到的取消令牌。</summary>
        public CancellationToken LastToken { get; private set; }

        public UniTask LoadMapAsync(string sceneName, CancellationToken cancellationToken)
        {
            LoadCount++;
            LastSceneName = sceneName;
            LastToken = cancellationToken;
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromCanceled(cancellationToken);
            }

            if (FailWith != null)
            {
                return UniTask.FromException(FailWith);
            }

            return CompleteImmediately ? UniTask.CompletedTask : _completion.Task;
        }

        public void Complete() => _completion.TrySetResult();
    }

    public sealed class LobbyControllerTests
    {
        [Test]
        public void LobbyStaysHiddenUntilEntry()
        {
            var controller = CreateController(new FakeLobbySceneGateway());

            Assert.That(controller.Current.IsVisible, Is.False);
            controller.Enter("player-one", 42);
            Assert.That(controller.Current.IsVisible, Is.True);
            Assert.That(controller.Current.Username, Is.EqualTo("player-one"));
            Assert.That(controller.Current.AccountId, Is.EqualTo(42));
        }

        [Test]
        public void FeatureRequestOpensTheEntryWithoutAStatusMessage()
        {
            var controller = CreateController(new FakeLobbySceneGateway());
            controller.Enter("player-one", 42);

            controller.RequestFeature(LobbyFeature.Hero);

            Assert.That(controller.Current.IsFeatureOpen, Is.True);
            Assert.That(controller.Current.OpenFeature, Is.EqualTo(LobbyFeature.Hero));
            Assert.That(controller.Current.StatusMessage, Is.Empty);
            Assert.That(controller.Current.IsVisible, Is.True);
        }

        [Test]
        public void FeatureRequestIsIgnoredBeforeEntry()
        {
            var controller = CreateController(new FakeLobbySceneGateway());

            controller.RequestFeature(LobbyFeature.Shop);

            Assert.That(controller.Current.StatusMessage, Is.Empty);
        }

        [UnityTest]
        public IEnumerator StartGameLoadsTheConfiguredMap() => UniTask.ToCoroutine(async () =>
        {
            var gateway = new FakeLobbySceneGateway();
            var controller = CreateController(gateway);
            controller.Enter("player-one", 42);

            await controller.StartGameAsync(CancellationToken.None);

            Assert.That(gateway.LoadCount, Is.EqualTo(1));
            Assert.That(gateway.LastSceneName, Is.EqualTo("Map1"));
        });

        [UnityTest]
        public IEnumerator StartGameCreatesTheExpeditionBeforeLoadingTheMap() =>
            UniTask.ToCoroutine(async () =>
            {
                var scene = new FakeLobbySceneGateway();
                var expeditionGateway = new FakeExpeditionGateway();
                expeditionGateway.QueueActive(
                    ExpeditionGatewayResult.Failed(ExpeditionOperationStatus.NotFound));
                Assert.That(ExpeditionSnapshot.TryCreate(
                    "expedition-1", "Map01", 1700000000000,
                    ExpeditionState.Active, Array.Empty<ExpeditionAsset>(), 0,
                    out var snapshot), Is.True);
                expeditionGateway.QueueStart(ExpeditionGatewayResult.Success(snapshot));
                using (var expedition = new ExpeditionController(
                           new ExpeditionModel(), expeditionGateway,
                           LobbyTestCapabilities.Full(), new SequenceExpeditionRequestIds()))
                {
                    var controller = CreateController(scene, SuccessfulAccountGateway(), expedition);
                    controller.Enter("player-one", 42);

                    await controller.StartGameAsync(CancellationToken.None);

                    Assert.That(expedition.Current.HasActive, Is.True);
                    Assert.That(scene.LoadCount, Is.EqualTo(1));
                }
            });

        [UnityTest]
        public IEnumerator MissingExpeditionCapabilityBlocksMapLoading() =>
            UniTask.ToCoroutine(async () =>
            {
                var scene = new FakeLobbySceneGateway();
                var expeditionGateway = new FakeExpeditionGateway();
                using (var expedition = new ExpeditionController(
                           new ExpeditionModel(), expeditionGateway,
                           LobbyTestCapabilities.LegacyCloud(), new SequenceExpeditionRequestIds()))
                {
                    var controller = CreateController(scene, SuccessfulAccountGateway(), expedition);
                    controller.Enter("player-one", 42);

                    await controller.StartGameAsync(CancellationToken.None);

                    Assert.That(scene.LoadCount, Is.Zero);
                    Assert.That(expeditionGateway.CallCount, Is.Zero);
                    Assert.That(controller.Current.StatusMessage, Does.Contain("服务器尚未部署"));
                }
            });

        [UnityTest]
        public IEnumerator UnresolvedCapabilitiesBlockStartWithoutSendingOrLoading() =>
            UniTask.ToCoroutine(async () =>
            {
                var scene = new FakeLobbySceneGateway();
                var expeditionGateway = new FakeExpeditionGateway();
                var unresolved = new ServerCapabilityRegistry();
                using (var expedition = new ExpeditionController(
                           new ExpeditionModel(), expeditionGateway,
                           unresolved, new SequenceExpeditionRequestIds()))
                {
                    var controller = CreateController(scene, SuccessfulAccountGateway(), expedition);
                    controller.Enter("player-one", 42);

                    await controller.StartGameAsync(CancellationToken.None);

                    Assert.That(unresolved.IsResolved, Is.False);
                    Assert.That(scene.LoadCount, Is.Zero,
                        "能力预检尚未完成时不能提前加载地图。");
                    Assert.That(expeditionGateway.CallCount, Is.Zero,
                        "能力预检尚未完成时不能发送远征协议。");
                    Assert.That(controller.Current.IsStartingGame, Is.False,
                        "拒绝后必须恢复开始游戏按钮。");
                    Assert.That(controller.Current.StatusMessage, Does.Contain("服务器尚未部署"));
                }
            });

        [UnityTest]
        public IEnumerator StartGameIgnoresRepeatRequestWhileLoading() => UniTask.ToCoroutine(async () =>
        {
            var gateway = new FakeLobbySceneGateway { CompleteImmediately = false };
            var controller = CreateController(gateway);
            controller.Enter("player-one", 42);

            var pending = controller.StartGameAsync(CancellationToken.None);
            await controller.StartGameAsync(CancellationToken.None);

            Assert.That(gateway.LoadCount, Is.EqualTo(1));
            Assert.That(controller.Current.IsStartingGame, Is.True);

            gateway.Complete();
            await pending;
        });

        [UnityTest]
        public IEnumerator SceneTransitionCancellationAfterDisposalDoesNotWriteReleasedState() =>
            UniTask.ToCoroutine(async () =>
            {
                var gateway = new FakeLobbySceneGateway { CompleteImmediately = false };
                var controller = CreateController(gateway);
                controller.Enter("player-one", 42);

                var pending = controller.StartGameAsync(CancellationToken.None);
                Assert.That(gateway.LoadCount, Is.EqualTo(1));

                // Bootstrap卸载时容器先Dispose大厅控制器；场景异步操作随后才观察到取消。
                controller.Dispose();
                gateway.Complete();

                await pending;
            });

        [UnityTest]
        public IEnumerator DisposingTheLobbyMidTransitionDoesNotCancelTheTransition() =>
            UniTask.ToCoroutine(async () =>
            {
                // 真实流程：场景激活时 Bootstrap 被卸载，大厅控制器随 Scope 一起 Dispose。
                // 如果切换挂在大厅的生命周期上，它会在激活途中把自己取消，
                // 任务界面就收不到"从大厅进入"的到达状态，出场动作、镜头与色幕全部不播。
                var gateway = new FakeLobbySceneGateway { CompleteImmediately = false };
                var controller = CreateController(gateway);
                controller.Enter("player-one", 42);

                var pending = controller.StartGameAsync(CancellationToken.None);
                Assert.That(gateway.LoadCount, Is.EqualTo(1));

                controller.Dispose();

                Assert.That(gateway.LastToken.IsCancellationRequested, Is.False,
                    "大厅被卸载不能取消正在进行的场景切换。");
                Assert.That(gateway.LastToken.CanBeCanceled, Is.False,
                    "切换的生命周期归持久化的世界流转管，不挂在大厅上。");

                gateway.Complete();
                await pending;
            });

        [UnityTest]
        public IEnumerator StartGameFailureRestoresTheButton() => UniTask.ToCoroutine(async () =>
        {
            var gateway = new FakeLobbySceneGateway
            {
                FailWith = new InvalidOperationException("scene is not in build settings")
            };
            var controller = CreateController(gateway);
            controller.Enter("player-one", 42);

            await controller.StartGameAsync(CancellationToken.None);

            Assert.That(controller.Current.IsStartingGame, Is.False);
            Assert.That(controller.Current.StatusMessage, Does.Contain("失败"));
        });

        [UnityTest]
        public IEnumerator StartGameCancellationWritesNoFailure() => UniTask.ToCoroutine(async () =>
        {
            var gateway = new FakeLobbySceneGateway();
            var controller = CreateController(gateway);
            controller.Enter("player-one", 42);

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await controller.StartGameAsync(cancellation.Token);
            }

            Assert.That(controller.Current.IsStartingGame, Is.False);
            Assert.That(controller.Current.StatusMessage, Does.Not.Contain("失败"));
        });

        [Test]
        public void AppearancePanelCannotOpenBeforeEntry()
        {
            var controller = CreateController(new FakeLobbySceneGateway());

            controller.OpenAppearance();

            Assert.That(controller.Current.IsAppearanceOpen, Is.False);
        }

        [Test]
        public void AppearancePanelOpensAndClosesAfterEntry()
        {
            var controller = CreateController(new FakeLobbySceneGateway());
            controller.Enter("player-one", 42);

            controller.OpenAppearance();
            Assert.That(controller.Current.IsAppearanceOpen, Is.True);
            Assert.That(controller.Current.AppearanceTab, Is.EqualTo(LobbyAppearanceTab.Avatar));

            controller.CloseAppearance();
            Assert.That(controller.Current.IsAppearanceOpen, Is.False);
        }

        [Test]
        public void AppearanceTabOnlySwitchesWhilePanelIsOpen()
        {
            var controller = CreateController(new FakeLobbySceneGateway());
            controller.Enter("player-one", 42);

            controller.SelectAppearanceTab(LobbyAppearanceTab.Frame);
            Assert.That(controller.Current.AppearanceTab, Is.EqualTo(LobbyAppearanceTab.Avatar));

            controller.OpenAppearance();
            controller.SelectAppearanceTab(LobbyAppearanceTab.Frame);
            Assert.That(controller.Current.AppearanceTab, Is.EqualTo(LobbyAppearanceTab.Frame));
        }

        [Test]
        public void ReEnteringLobbyWithAnotherAccountClosesThePanel()
        {
            var controller = CreateController(new FakeLobbySceneGateway());
            controller.Enter("player-one", 42);
            controller.OpenAppearance();

            controller.Enter("player-two", 43);

            Assert.That(controller.Current.IsAppearanceOpen, Is.False);
            Assert.That(controller.Current.Username, Is.EqualTo("player-two"));
            Assert.That(controller.Current.AccountId, Is.EqualTo(43));
        }

        [Test]
        public void FeatureRequestOpensTheMatchingPanel()
        {
            var controller = CreateController(new FakeLobbySceneGateway());
            controller.Enter("player-one", 42);

            controller.RequestFeature(LobbyFeature.Shop);

            Assert.That(controller.Current.IsFeatureOpen, Is.True);
            Assert.That(controller.Current.OpenFeature, Is.EqualTo(LobbyFeature.Shop));
            // P1.9 之后每个入口都有专属面板，大厅不再叠一句占位说明；
            // 加载与失败提示由面板自己负责。
            Assert.That(controller.Current.StatusMessage, Is.Empty);
        }

        [Test]
        public void ClosingTheFeaturePanelClearsTheMessage()
        {
            var controller = CreateController(new FakeLobbySceneGateway());
            controller.Enter("player-one", 42);
            controller.RequestFeature(LobbyFeature.Forge);

            controller.CloseFeature();

            Assert.That(controller.Current.IsFeatureOpen, Is.False);
            Assert.That(controller.Current.OpenModal, Is.EqualTo(LobbyModal.None));
            Assert.That(controller.Current.StatusMessage, Is.Empty);
        }

        [Test]
        public void OnlyOneModalCanBeOpenAtATime()
        {
            var controller = CreateController(new FakeLobbySceneGateway());
            controller.Enter("player-one", 42);

            controller.OpenAppearance();
            Assert.That(controller.Current.IsAppearanceOpen, Is.True);

            controller.RequestFeature(LobbyFeature.Chat);
            Assert.That(controller.Current.IsFeatureOpen, Is.True);
            Assert.That(controller.Current.IsAppearanceOpen, Is.False);

            controller.OpenAppearance();
            Assert.That(controller.Current.IsAppearanceOpen, Is.True);
            Assert.That(controller.Current.IsFeatureOpen, Is.False);
        }

        [Test]
        public void EnteringTheLobbyClosesAnyOpenModal()
        {
            var controller = CreateController(new FakeLobbySceneGateway());
            controller.Enter("player-one", 42);
            controller.RequestFeature(LobbyFeature.Friends);

            controller.Enter("player-two", 43);

            Assert.That(controller.Current.OpenModal, Is.EqualTo(LobbyModal.None));
        }

        private static LobbyController CreateController(ILobbySceneGateway gateway) =>
            CreateController(gateway, SuccessfulAccountGateway());

        private static LobbyController CreateController(
            ILobbySceneGateway gateway,
            ILobbyAccountGateway accountGateway,
            IExpeditionController expedition = null) =>
            new LobbyController(
                new LobbyModel(),
                gateway,
                "Map1",
                accountGateway,
                new FakeLobbyProfileGateway(),
                LobbyTestCapabilities.Full(),
                expedition);

        private static FakeLobbyAccountGateway SuccessfulAccountGateway()
        {
            LobbyAccountSnapshot.TryCreate(1, 0, 0, 0, out var snapshot);
            return new FakeLobbyAccountGateway
            {
                Result = LobbyAccountSummaryResult.Success(snapshot)
            };
        }
    }
}
