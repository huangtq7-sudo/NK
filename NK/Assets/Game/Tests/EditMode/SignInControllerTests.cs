using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.RedDot;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using Naraka.Features.SignIn.Controller;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    internal sealed class FakeSignInGateway : ISignInGateway
    {
        public int LoadCount { get; private set; }

        public List<string> Calls { get; } = new List<string>();

        public SignInResult LoadResult { get; set; }

        public SignInResult ClaimResult { get; set; }

        public Exception ThrowOnClaim { get; set; }

        public FakeSignInGateway()
        {
            // 周期第 1 天，什么都还没领。
            LoadResult = Snapshot(serverDay: 100, cycleStart: 100, consecutive: 0, cards: 1);
            ClaimResult = Snapshot(
                serverDay: 100, cycleStart: 100, consecutive: 1, cards: 1,
                claims: new[] { new SignInClaimRecord(1, false) });
        }

        public static SignInResult Snapshot(
            long serverDay,
            long cycleStart,
            int consecutive,
            long cards,
            IReadOnlyList<SignInClaimRecord> claims = null,
            IReadOnlyList<string> milestones = null) =>
            SignInResult.Success(
                cycleStart, serverDay, consecutive, cards,
                claims ?? Array.Empty<SignInClaimRecord>(),
                milestones ?? Array.Empty<string>());

        public UniTask<SignInResult> RequestSignInAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return UniTask.FromResult(LoadResult);
        }

        public UniTask<SignInResult> ClaimTodayAsync(CancellationToken cancellationToken)
        {
            Calls.Add("today");
            return ThrowOnClaim != null
                ? UniTask.FromException<SignInResult>(ThrowOnClaim)
                : UniTask.FromResult(ClaimResult);
        }

        public UniTask<SignInResult> MakeUpAsync(int dayIndex, CancellationToken cancellationToken)
        {
            Calls.Add("makeup:" + dayIndex);
            return UniTask.FromResult(ClaimResult);
        }

        public UniTask<SignInResult> ClaimMilestoneAsync(
            int milestoneDays,
            CancellationToken cancellationToken)
        {
            Calls.Add("milestone:" + milestoneDays);
            return UniTask.FromResult(ClaimResult);
        }
    }

    /// <summary>
    /// 签到客户端。
    ///
    /// 核心不变量：七个格子的状态只能来自服务端返回的领取记录；主进度与连续次数分开；
    /// 补签能力同时受"确实漏签""周期次数""补签卡数量"三个条件约束。
    /// </summary>
    public sealed class SignInControllerTests
    {
        private static FakeGameConfigProvider SignInConfig()
        {
            var catalog = new NarakaConfigCatalog
            {
                SchemaVersion = "1.0.0",
                ConfigVersion = "test",
                Currencies = new[]
                {
                    new CurrencyConfig { CurrencyId = "Copper", DisplayName = "铜币", SortOrder = 1 }
                },
                Items = new[]
                {
                    new ItemConfig
                    {
                        ItemId = "special_makeup_card", DisplayName = "补签卡",
                        Category = ConfigItemCategory.Special, Quality = ConfigQuality.Blue,
                        StackLimit = 99, SortOrder = 1, IconKey = "item_makeup"
                    }
                },
                SignInRewards = new[]
                {
                    Reward(1), Reward(2), Reward(3), Reward(4), Reward(5), Reward(6), Reward(7)
                },
                SignInMilestones = new[]
                {
                    Milestone(3), Milestone(5), Milestone(7)
                }
            };

            return new FakeGameConfigProvider(catalog);
        }

        private static SignInRewardConfig Reward(int day) => new SignInRewardConfig
        {
            Day = day,
            RewardId = "signin_day_" + day,
            RewardKind = ConfigRewardKind.Currency,
            CurrencyId = "Copper",
            CurrencyAmount = 100 * day
        };

        private static SignInMilestoneConfig Milestone(int days) => new SignInMilestoneConfig
        {
            MilestoneDays = days,
            RewardId = "signin_streak_" + days,
            RewardKind = ConfigRewardKind.Currency,
            CurrencyId = "Copper",
            CurrencyAmount = 500 * days
        };

        private static (SignInController SignIn, FakeSignInGateway Gateway, TestEventBus Bus) Create(
            IServerCapabilities capabilities = null,
            SignInResult? load = null)
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

            var gateway = new FakeSignInGateway();
            if (load.HasValue)
            {
                gateway.LoadResult = load.Value;
            }

            var bus = new TestEventBus();
            var signIn = new SignInController(SignInConfig(), gateway, lobby, caps, bus);

            lobby.Enter("player-one", 42);
            lobby.RequestFeature(LobbyFeature.CheckIn);
            return (signIn, gateway, bus);
        }

        [Test]
        public void OpeningTheEntryLoadsTheCycle()
        {
            var (signIn, gateway, _) = Create();

            Assert.That(signIn.Current.IsOpen, Is.True);
            Assert.That(gateway.LoadCount, Is.EqualTo(1));
            Assert.That(signIn.Current.HasServerState, Is.True);
            Assert.That(signIn.Current.Days.Count, Is.EqualTo(7));
        }

        [Test]
        public void CompatibilityModeNeverSendsASignInRequest()
        {
            var (signIn, gateway, _) = Create(LobbyTestCapabilities.LegacyCloud());

            Assert.That(gateway.LoadCount, Is.Zero);
            Assert.That(signIn.Current.StatusMessage, Does.Contain("服务器功能尚未升级"));
        }

        [Test]
        public void TodayIsClaimableAndFutureDaysAreNot()
        {
            var (signIn, _, _) = Create();

            Assert.That(signIn.Current.TodayIndex, Is.EqualTo(1));
            Assert.That(signIn.Current.Days[0].Status, Is.EqualTo(SignInDayStatus.Claimable));
            Assert.That(signIn.Current.Days[1].Status, Is.EqualTo(SignInDayStatus.Upcoming));
            Assert.That(signIn.Current.CanClaimToday, Is.True);
        }

        [Test]
        public void AClaimedDayShowsAsClaimedAndCannotBeClaimedAgain()
        {
            var (signIn, _, _) = Create(load: FakeSignInGateway.Snapshot(
                serverDay: 100, cycleStart: 100, consecutive: 1, cards: 1,
                claims: new[] { new SignInClaimRecord(1, false) }));

            Assert.That(signIn.Current.Days[0].Status, Is.EqualTo(SignInDayStatus.Claimed));
            Assert.That(signIn.Current.CanClaimToday, Is.False);
        }

        [Test]
        public void AMissedDayIsOfferedForMakeUpWhenACardIsAvailable()
        {
            // 今天是第 3 天，第 1 天领过、第 2 天漏了。
            var (signIn, _, _) = Create(load: FakeSignInGateway.Snapshot(
                serverDay: 102, cycleStart: 100, consecutive: 1, cards: 1,
                claims: new[] { new SignInClaimRecord(1, false) }));

            Assert.That(signIn.Current.TodayIndex, Is.EqualTo(3));
            Assert.That(signIn.Current.Days[1].Status, Is.EqualTo(SignInDayStatus.Missed));
            Assert.That(signIn.Current.CanMakeUpAny, Is.True);
            Assert.That(signIn.Current.FirstMakeUpDay, Is.EqualTo(2));
        }

        [Test]
        public void WithoutACardTheMissedDayIsLockedRatherThanOffered()
        {
            var (signIn, _, _) = Create(load: FakeSignInGateway.Snapshot(
                serverDay: 102, cycleStart: 100, consecutive: 1, cards: 0,
                claims: new[] { new SignInClaimRecord(1, false) }));

            Assert.That(signIn.Current.Days[1].Status, Is.EqualTo(SignInDayStatus.MissedLocked));
            Assert.That(signIn.Current.CanMakeUpAny, Is.False);
        }

        [Test]
        public void AMakeUpAlreadyUsedThisCycleLocksTheRemainingMissedDays()
        {
            // 今天是第 5 天，第 2 天已经补过，第 3、4 天仍然空着，但本周期不能再补。
            var (signIn, _, _) = Create(load: FakeSignInGateway.Snapshot(
                serverDay: 104, cycleStart: 100, consecutive: 1, cards: 5,
                claims: new[]
                {
                    new SignInClaimRecord(1, false),
                    new SignInClaimRecord(2, true)
                }));

            Assert.That(signIn.Current.MakeupUsedThisCycle, Is.True);
            Assert.That(signIn.Current.Days[1].Status, Is.EqualTo(SignInDayStatus.MadeUp));
            Assert.That(signIn.Current.Days[2].Status, Is.EqualTo(SignInDayStatus.MissedLocked));
            Assert.That(signIn.Current.CanMakeUpAny, Is.False);
        }

        [Test]
        public void MakeUpNeverAdvancesTheStreak()
        {
            // 主进度点亮了两天，连续次数仍然只有 1：两者是分开存储的字段。
            var (signIn, _, _) = Create(load: FakeSignInGateway.Snapshot(
                serverDay: 102, cycleStart: 100, consecutive: 1, cards: 0,
                claims: new[]
                {
                    new SignInClaimRecord(1, false),
                    new SignInClaimRecord(2, true)
                }));

            Assert.That(signIn.Current.Days[1].IsClaimed, Is.True);
            Assert.That(signIn.Current.ConsecutiveDays, Is.EqualTo(1));
        }

        [Test]
        public void MilestonesBecomeClaimableOnlyWhenTheStreakReachesThem()
        {
            var (signIn, _, _) = Create(load: FakeSignInGateway.Snapshot(
                serverDay: 103, cycleStart: 100, consecutive: 3, cards: 0));

            Assert.That(signIn.Current.Milestones.Count, Is.EqualTo(3));
            Assert.That(signIn.Current.Milestones[0].IsClaimable, Is.True);
            Assert.That(signIn.Current.Milestones[1].IsClaimable, Is.False);
        }

        [Test]
        public void AClaimedMilestoneIsNotOfferedAgain()
        {
            var (signIn, _, _) = Create(load: FakeSignInGateway.Snapshot(
                serverDay: 103, cycleStart: 100, consecutive: 3, cards: 0,
                milestones: new[] { "signin_streak_3" }));

            Assert.That(signIn.Current.Milestones[0].IsClaimed, Is.True);
            Assert.That(signIn.Current.Milestones[0].IsClaimable, Is.False);
        }

        [Test]
        public void ClaimingTodaySendsExactlyOneRequest()
        {
            var (signIn, gateway, _) = Create();

            signIn.ClaimToday();

            Assert.That(gateway.Calls, Is.EqualTo(new[] { "today" }));
        }

        [Test]
        public void MakeUpSendsTheTargetDay()
        {
            var (signIn, gateway, _) = Create(load: FakeSignInGateway.Snapshot(
                serverDay: 102, cycleStart: 100, consecutive: 1, cards: 1,
                claims: new[] { new SignInClaimRecord(1, false) }));

            signIn.MakeUp(2);

            Assert.That(gateway.Calls, Is.EqualTo(new[] { "makeup:2" }));
        }

        [Test]
        public void MilestoneClaimSendsTheStreakLength()
        {
            var (signIn, gateway, _) = Create(load: FakeSignInGateway.Snapshot(
                serverDay: 103, cycleStart: 100, consecutive: 3, cards: 0));

            signIn.ClaimMilestone(3);

            Assert.That(gateway.Calls, Is.EqualTo(new[] { "milestone:3" }));
        }

        [Test]
        public void AFailedClaimShowsAMessageAndKeepsTheOldState()
        {
            var (signIn, gateway, _) = Create();
            gateway.ClaimResult = SignInResult.Failed(LobbyOperationStatus.AlreadyClaimed);

            signIn.ClaimToday();

            Assert.That(signIn.Current.StatusMessage, Is.Not.Empty);
            Assert.That(signIn.Current.Days.Count, Is.EqualTo(7));
        }

        [Test]
        public void ATransportFailureIsReportedRatherThanSilentlySwallowed()
        {
            var (signIn, gateway, _) = Create();
            gateway.ThrowOnClaim = new InvalidOperationException("offline");

            signIn.ClaimToday();

            Assert.That(signIn.Current.IsBusy, Is.False);
            Assert.That(signIn.Current.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void ClosedPanelIgnoresClaimRequests()
        {
            var (signIn, gateway, _) = Create();
            signIn.Close();
            gateway.Calls.Clear();

            signIn.ClaimToday();

            Assert.That(gateway.Calls, Is.Empty);
        }

        [Test]
        public void TheRedDotFollowsWhatIsActuallyClaimable()
        {
            var (_, _, bus) = Create();

            Assert.That(bus.LastStateOf(RedDotPath.SignInDailyClaim), Is.True);
            Assert.That(bus.LastStateOf(RedDotPath.SignInMilestoneClaim), Is.False);
        }

        [Test]
        public void NothingClaimableMeansNoRedDot()
        {
            var (_, _, bus) = Create(load: FakeSignInGateway.Snapshot(
                serverDay: 100, cycleStart: 100, consecutive: 1, cards: 0,
                claims: new[] { new SignInClaimRecord(1, false) }));

            Assert.That(bus.LastStateOf(RedDotPath.SignInDailyClaim), Is.False);
            Assert.That(bus.LastStateOf(RedDotPath.SignInMilestoneClaim), Is.False);
        }

        [Test]
        public void AReachedMilestoneLightsItsOwnRedDot()
        {
            var (_, _, bus) = Create(load: FakeSignInGateway.Snapshot(
                serverDay: 103, cycleStart: 100, consecutive: 3, cards: 0,
                claims: new[] { new SignInClaimRecord(3, false) }));

            Assert.That(bus.LastStateOf(RedDotPath.SignInMilestoneClaim), Is.True);
        }
    }
}
