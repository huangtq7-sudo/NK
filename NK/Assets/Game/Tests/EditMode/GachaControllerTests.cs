using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Features.Gacha.Controller;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    internal sealed class FakeGachaGateway : IGachaGateway
    {
        public List<(string PoolId, int PullCount, string OrderId)> Pulls { get; } =
            new List<(string, int, string)>();

        public List<string> Acknowledged { get; } = new List<string>();

        public int LoadCount { get; private set; }

        public GachaResult LoadResult { get; set; }

        public GachaResult PullResult { get; set; }

        public Exception ThrowOnPull { get; set; }

        public FakeGachaGateway()
        {
            LoadResult = GachaResult.Success(
                "pool_test", 0, 0, Array.Empty<GachaOrderSnapshot>(), null, 0, 0, 1000);
            PullResult = GachaResult.Success(
                "pool_test", 1, 1, Array.Empty<GachaOrderSnapshot>(), Order("order-1"), 0, 0, 900);
        }

        public static GachaOrderSnapshot Order(string orderId, int pullCount = 1, bool isShown = false) =>
            new GachaOrderSnapshot(
                orderId, "pool_test", pullCount, isShown,
                new[] { new GachaRewardSnapshot("r1", "mat_ore", 1, ConfigQuality.White) });

        public UniTask<GachaResult> RequestGachaAsync(string poolId, CancellationToken cancellationToken)
        {
            LoadCount++;
            return UniTask.FromResult(LoadResult);
        }

        public UniTask<GachaResult> PullAsync(
            string poolId, int pullCount, string orderId, CancellationToken cancellationToken)
        {
            Pulls.Add((poolId, pullCount, orderId));
            return ThrowOnPull != null
                ? UniTask.FromException<GachaResult>(ThrowOnPull)
                : UniTask.FromResult(PullResult);
        }

        public UniTask<GachaResult> AcknowledgeAsync(
            string poolId, string orderId, CancellationToken cancellationToken)
        {
            Acknowledged.Add(orderId);
            return UniTask.FromResult(GachaResult.Success(
                poolId, 1, 1, Array.Empty<GachaOrderSnapshot>(), null, 0, 0, 900));
        }
    }

    /// <summary>
    /// 抽奖客户端：动画只是表现，结果永远来自服务端；断线遗留的未展示结果必须被恢复。
    /// </summary>
    public sealed class GachaControllerTests
    {
        private static FakeGameConfigProvider GachaConfig()
        {
            var catalog = new NarakaConfigCatalog
            {
                SchemaVersion = "1.0.0",
                ConfigVersion = "test",
                Currencies = new[]
                {
                    new CurrencyConfig { CurrencyId = "Gold", DisplayName = "金币", SortOrder = 1 }
                },
                Items = new[]
                {
                    new ItemConfig
                    {
                        ItemId = "mat_ore", DisplayName = "矿石", Category = ConfigItemCategory.Material,
                        Quality = ConfigQuality.White, StackLimit = 999, SortOrder = 1, IconKey = "mat_ore"
                    }
                },
                GachaPools = new[]
                {
                    new GachaPoolConfig
                    {
                        PoolId = "pool_test", DisplayName = "常驻宝匣", CurrencyId = "Gold",
                        SinglePrice = 100, TenPullPrice = 900,
                        TenPullMinimumQuality = ConfigQuality.Blue,
                        PityCount = 20, PityQuality = ConfigQuality.Red, SortOrder = 1
                    }
                },
                GachaEntries = new[]
                {
                    new GachaEntryConfig
                    {
                        PoolId = "pool_test", RewardId = "r1", ItemId = "mat_ore",
                        Amount = 1, Quality = ConfigQuality.White, Weight = 100
                    }
                }
            };

            return new FakeGameConfigProvider(catalog);
        }

        private static (GachaController Gacha, LobbyController Lobby, FakeGachaGateway Gateway) Create(
            IServerCapabilities capabilities = null)
        {
            LobbyAccountSnapshot.TryCreate(1, 1000, 1000, 1000, out var summary);
            var caps = capabilities ?? LobbyTestCapabilities.Full();
            var lobby = new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                new FakeLobbyAccountGateway { Result = LobbyAccountSummaryResult.Success(summary) },
                new FakeLobbyProfileGateway(),
                caps);

            var gateway = new FakeGachaGateway();
            var gacha = new GachaController(GachaConfig(), gateway, lobby, caps, new TestEventBus());

            lobby.Enter("player-one", 42);
            lobby.RequestFeature(LobbyFeature.Draw);
            return (gacha, lobby, gateway);
        }

        [Test]
        public void OpeningTheGachaEntryLoadsPityAndBalances()
        {
            var (gacha, _, gateway) = Create();

            Assert.That(gacha.Current.IsOpen, Is.True);
            Assert.That(gateway.LoadCount, Is.EqualTo(1));
            Assert.That(gacha.Current.HasServerState, Is.True);
            Assert.That(gacha.PullsUntilPity, Is.EqualTo(20));
        }

        [Test]
        public void CompatibilityModeNeverSendsAGachaRequest()
        {
            var (gacha, _, gateway) = Create(LobbyTestCapabilities.LegacyCloud());

            Assert.That(gateway.LoadCount, Is.Zero);
            Assert.That(gacha.Current.StatusMessage, Does.Contain("服务器功能尚未升级"));
        }

        [Test]
        public void SinglePullSendsOneAndTenPullSendsTen()
        {
            var (gacha, _, gateway) = Create();

            gacha.PullOnce();
            Assert.That(gateway.Pulls[0].PullCount, Is.EqualTo(1));

            gacha.ConfirmResult();
            gacha.PullTen();
            Assert.That(gateway.Pulls[1].PullCount, Is.EqualTo(10));
        }

        [Test]
        public void EachPullUsesAFreshOrderId()
        {
            var (gacha, _, gateway) = Create();

            gacha.PullOnce();
            gacha.ConfirmResult();
            gacha.PullOnce();

            Assert.That(gateway.Pulls.Count, Is.EqualTo(2));
            Assert.That(gateway.Pulls[0].OrderId, Is.Not.EqualTo(gateway.Pulls[1].OrderId));
        }

        [Test]
        public void ResultComesFromTheServerAndAnimationStartsAutomatically()
        {
            var (gacha, _, _) = Create();

            gacha.PullOnce();

            Assert.That(gacha.Current.HasPendingResult, Is.True);
            Assert.That(gacha.Current.IsAnimating, Is.True);
            Assert.That(gacha.Current.PendingOrder.OrderId, Is.EqualTo("order-1"));
            Assert.That(gacha.Current.PendingOrder.Rewards.Count, Is.EqualTo(1));
        }

        [Test]
        public void SkippingTheAnimationDoesNotChangeTheResult()
        {
            var (gacha, _, _) = Create();
            gacha.PullOnce();
            var before = gacha.Current.PendingOrder;

            gacha.SkipAnimation();

            Assert.That(gacha.Current.IsAnimating, Is.False);
            Assert.That(gacha.Current.PendingOrder, Is.SameAs(before), "跳过动画不得改变结果。");
        }

        [Test]
        public void PullingAgainIsBlockedUntilTheResultIsConfirmed()
        {
            var (gacha, _, gateway) = Create();
            gacha.PullOnce();

            gacha.PullOnce();

            Assert.That(gateway.Pulls.Count, Is.EqualTo(1), "有未确认结果时不得再次抽奖。");
            Assert.That(gacha.Current.StatusMessage, Does.Contain("确认"));
        }

        [Test]
        public void ConfirmingMarksTheOrderAsShown()
        {
            var (gacha, _, gateway) = Create();
            gacha.PullOnce();

            gacha.ConfirmResult();

            Assert.That(gateway.Acknowledged, Is.EqualTo(new[] { "order-1" }));
            Assert.That(gacha.Current.HasPendingResult, Is.False);
        }

        [Test]
        public void UnshownOrdersFromAPreviousSessionAreRecovered()
        {
            var (gacha, lobby, gateway) = Create();
            gateway.LoadResult = GachaResult.Success(
                "pool_test", 3, 3,
                new[] { FakeGachaGateway.Order("order-old", 10) },
                null, 0, 0, 500);

            lobby.CloseFeature();
            lobby.RequestFeature(LobbyFeature.Draw);

            // 断线遗留的结果必须被取回并展示，但不再重播动画——玩家已经等过一次了。
            Assert.That(gacha.Current.HasPendingResult, Is.True);
            Assert.That(gacha.Current.PendingOrder.OrderId, Is.EqualTo("order-old"));
            Assert.That(gacha.Current.IsAnimating, Is.False);
        }

        [Test]
        public void AlreadyShownOrdersAreNotResurfaced()
        {
            var (gacha, lobby, gateway) = Create();
            gateway.LoadResult = GachaResult.Success(
                "pool_test", 3, 3,
                new[] { FakeGachaGateway.Order("order-old", 1, isShown: true) },
                null, 0, 0, 500);

            lobby.CloseFeature();
            lobby.RequestFeature(LobbyFeature.Draw);

            Assert.That(gacha.Current.HasPendingResult, Is.False);
        }

        [Test]
        public void InsufficientCurrencyShowsAReadableMessageAndNoResult()
        {
            var (gacha, _, gateway) = Create();
            gateway.PullResult = GachaResult.Failed(LobbyOperationStatus.InsufficientCurrency);

            gacha.PullOnce();

            Assert.That(gacha.Current.HasPendingResult, Is.False);
            Assert.That(gacha.Current.StatusMessage, Does.Contain("货币不足"));
            Assert.That(gacha.Current.IsBusy, Is.False);
        }

        [Test]
        public void TransportFailureNeverFabricatesAResult()
        {
            var (gacha, _, gateway) = Create();
            gateway.ThrowOnPull = new InvalidOperationException("socket down");

            gacha.PullOnce();

            Assert.That(gacha.Current.HasPendingResult, Is.False, "客户端绝不能自己造一个结果。");
            Assert.That(gacha.Current.StatusMessage, Does.Contain("重新打开"));
        }

        [Test]
        public void PityCountdownFollowsTheServerCounter()
        {
            var (gacha, lobby, gateway) = Create();
            gateway.LoadResult = GachaResult.Success(
                "pool_test", 19, 19, Array.Empty<GachaOrderSnapshot>(), null, 0, 0, 500);

            lobby.CloseFeature();
            lobby.RequestFeature(LobbyFeature.Draw);

            Assert.That(gacha.PullsUntilPity, Is.EqualTo(1));
        }

        [Test]
        public void OrderWithoutRewardsIsTreatedAsInvalid()
        {
            var empty = new GachaOrderSnapshot("order-x", "pool_test", 1, false, Array.Empty<GachaRewardSnapshot>());

            Assert.That(empty.IsValid, Is.False);
        }

        [Test]
        public void TenPullCountMatchesTheServerContract() =>
            Assert.That(GachaPresentationState.TenPullCount, Is.EqualTo(10));

        [Test]
        public void DisposingReleasesTheLobbySubscription()
        {
            var (gacha, lobby, gateway) = Create();

            gacha.Dispose();
            lobby.CloseFeature();
            lobby.RequestFeature(LobbyFeature.Draw);

            Assert.That(gateway.LoadCount, Is.EqualTo(1));
        }
    }
}
