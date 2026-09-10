using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Naraka.P0.Tests
{
    internal sealed class FakeLobbyAccountGateway : ILobbyAccountGateway
    {
        private readonly UniTaskCompletionSource<LobbyAccountSummaryResult> _pending =
            new UniTaskCompletionSource<LobbyAccountSummaryResult>();

        public int CallCount { get; private set; }

        public bool CompleteImmediately { get; set; } = true;

        public LobbyAccountSummaryResult Result { get; set; }

        public Exception ThrowWith { get; set; }

        public UniTask<LobbyAccountSummaryResult> RequestAccountSummaryAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromCanceled<LobbyAccountSummaryResult>(cancellationToken);
            }

            if (ThrowWith != null)
            {
                return UniTask.FromException<LobbyAccountSummaryResult>(ThrowWith);
            }

            return CompleteImmediately ? UniTask.FromResult(Result) : _pending.Task;
        }

        public void Complete(LobbyAccountSummaryResult result) => _pending.TrySetResult(result);
    }

    public sealed class LobbyAccountSummaryTests
    {
        [Test]
        public void ValidSnapshotIsAccepted()
        {
            Assert.That(LobbyAccountSnapshot.TryCreate(12, 900, 80, 7, out var snapshot), Is.True);
            Assert.That(snapshot.AccountLevel, Is.EqualTo(12));
            Assert.That(snapshot.Copper, Is.EqualTo(900));
            Assert.That(snapshot.Silk, Is.EqualTo(80));
            Assert.That(snapshot.Gold, Is.EqualTo(7));
        }

        [Test]
        public void MinimumLevelOneWithZeroCurrenciesIsValid()
        {
            Assert.That(LobbyAccountSnapshot.TryCreate(1, 0, 0, 0, out _), Is.True);
        }

        [TestCase(0)]
        [TestCase(-3)]
        public void InvalidAccountLevelIsRejected(int level)
        {
            Assert.That(LobbyAccountSnapshot.TryCreate(level, 0, 0, 0, out _), Is.False);
        }

        [TestCase(-1L, 0L, 0L)]
        [TestCase(0L, -1L, 0L)]
        [TestCase(0L, 0L, -1L)]
        public void NegativeCurrencyIsRejected(long copper, long silk, long gold)
        {
            Assert.That(LobbyAccountSnapshot.TryCreate(1, copper, silk, gold, out _), Is.False);
        }

        [Test]
        public void NoRequestIsSentBeforeEnteringTheLobby()
        {
            var gateway = CreateGateway(5, 10, 20, 30);
            var controller = CreateController(gateway);

            Assert.That(controller.Current.HasAccountSummary, Is.False);
            Assert.That(gateway.CallCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator LoadIsIgnoredWhenTheLobbyWasNeverEntered() => UniTask.ToCoroutine(async () =>
        {
            var gateway = CreateGateway(5, 10, 20, 30);
            var controller = CreateController(gateway);

            await controller.LoadAccountSummaryAsync(CancellationToken.None);

            Assert.That(gateway.CallCount, Is.EqualTo(0));
            Assert.That(controller.Current.HasAccountSummary, Is.False);
        });

        [Test]
        public void EnteringTheLobbyLoadsAndMapsTheSummaryIntoPresentationState()
        {
            var gateway = CreateGateway(9, 1234, 56, 7);
            var controller = CreateController(gateway);

            controller.Enter("player-one", 42);

            Assert.That(gateway.CallCount, Is.EqualTo(1));
            var state = controller.Current;
            Assert.That(state.HasAccountSummary, Is.True);
            Assert.That(state.IsAccountSummaryLoading, Is.False);
            Assert.That(state.AccountSummary.AccountLevel, Is.EqualTo(9));
            Assert.That(state.AccountSummary.Copper, Is.EqualTo(1234));
            Assert.That(state.AccountSummary.Silk, Is.EqualTo(56));
            Assert.That(state.AccountSummary.Gold, Is.EqualTo(7));
            Assert.That(state.StatusMessage, Is.Empty);
        }

        [Test]
        public void FailureReportsAnErrorWithoutFabricatingBalances()
        {
            var gateway = new FakeLobbyAccountGateway
            {
                Result = LobbyAccountSummaryResult.Failed(LobbyAccountSummaryStatus.DatabaseUnavailable)
            };
            var controller = CreateController(gateway);

            controller.Enter("player-one", 42);

            var state = controller.Current;
            Assert.That(state.HasAccountSummary, Is.False);
            Assert.That(state.IsAccountSummaryLoading, Is.False);
            Assert.That(state.StatusMessage, Is.Not.Empty);
        }

        [UnityTest]
        public IEnumerator FailureNeverOverwritesTheLastSuccessfulSnapshot() => UniTask.ToCoroutine(async () =>
        {
            var gateway = CreateGateway(9, 1234, 56, 7);
            var controller = CreateController(gateway);
            controller.Enter("player-one", 42);
            Assert.That(controller.Current.AccountSummary.Copper, Is.EqualTo(1234));

            gateway.Result = LobbyAccountSummaryResult.Failed(LobbyAccountSummaryStatus.TransportFailure);
            await controller.LoadAccountSummaryAsync(CancellationToken.None);

            var state = controller.Current;
            Assert.That(state.HasAccountSummary, Is.True, "失败不得清掉已有快照。");
            Assert.That(state.AccountSummary.AccountLevel, Is.EqualTo(9));
            Assert.That(state.AccountSummary.Copper, Is.EqualTo(1234));
            Assert.That(state.StatusMessage, Is.Not.Empty);
        });

        [UnityTest]
        public IEnumerator ConcurrentRequestsAreCollapsedIntoOne() => UniTask.ToCoroutine(async () =>
        {
            var gateway = CreateGateway(3, 1, 2, 3);
            gateway.CompleteImmediately = false;
            var controller = CreateController(gateway);

            controller.Enter("player-one", 42);
            await controller.LoadAccountSummaryAsync(CancellationToken.None);

            Assert.That(gateway.CallCount, Is.EqualTo(1), "同一账号概要请求不得并发重复发送。");
            Assert.That(controller.Current.IsAccountSummaryLoading, Is.True);

            LobbyAccountSnapshot.TryCreate(3, 1, 2, 3, out var snapshot);
            gateway.Complete(LobbyAccountSummaryResult.Success(snapshot));
            await UniTask.Yield();
            Assert.That(controller.Current.HasAccountSummary, Is.True);
        });

        [UnityTest]
        public IEnumerator CancellationLeavesNoErrorMessage() => UniTask.ToCoroutine(async () =>
        {
            var gateway = CreateGateway(3, 1, 2, 3);
            var controller = CreateController(gateway);
            controller.Enter("player-one", 42);

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await controller.LoadAccountSummaryAsync(cancellation.Token);
            }

            var state = controller.Current;
            Assert.That(state.IsAccountSummaryLoading, Is.False);
            Assert.That(state.HasAccountSummary, Is.True);
            Assert.That(state.StatusMessage, Is.Empty);
        });

        [UnityTest]
        public IEnumerator ReEnteringTheSameLobbyReloadsTheSummary() => UniTask.ToCoroutine(async () =>
        {
            var gateway = CreateGateway(3, 1, 2, 3);
            var controller = CreateController(gateway);
            controller.Enter("player-one", 42);
            Assert.That(gateway.CallCount, Is.EqualTo(1));

            controller.Enter("player-one", 42);
            await UniTask.Yield();

            Assert.That(gateway.CallCount, Is.EqualTo(2), "重新进入大厅必须重新加载。");
        });

        [Test]
        public void SwitchingAccountDropsThePreviousSummaryBeforeReloading()
        {
            var gateway = CreateGateway(9, 1234, 56, 7);
            var controller = CreateController(gateway);
            controller.Enter("player-one", 42);

            gateway.Result = LobbyAccountSummaryResult.Failed(LobbyAccountSummaryStatus.NotFound);
            controller.Enter("player-two", 43);

            Assert.That(controller.Current.HasAccountSummary, Is.False, "换账号不得沿用上一个账号的余额。");
            Assert.That(controller.Current.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void TransportExceptionsBecomeAReadableMessage()
        {
            var gateway = new FakeLobbyAccountGateway
            {
                ThrowWith = new InvalidOperationException("socket down")
            };
            var controller = CreateController(gateway);

            controller.Enter("player-one", 42);

            Assert.That(controller.Current.HasAccountSummary, Is.False);
            Assert.That(controller.Current.IsAccountSummaryLoading, Is.False);
            Assert.That(controller.Current.StatusMessage, Is.Not.Empty);
        }

        private static FakeLobbyAccountGateway CreateGateway(int level, long copper, long silk, long gold)
        {
            LobbyAccountSnapshot.TryCreate(level, copper, silk, gold, out var snapshot);
            return new FakeLobbyAccountGateway
            {
                Result = LobbyAccountSummaryResult.Success(snapshot)
            };
        }

        private static LobbyController CreateController(ILobbyAccountGateway accountGateway) =>
            new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                accountGateway,
                new FakeLobbyProfileGateway(),
                LobbyTestCapabilities.Full());
    }
}
