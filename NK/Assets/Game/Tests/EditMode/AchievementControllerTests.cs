using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.RedDot;
using Naraka.Features.Achievement.Controller;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    internal sealed class FakeAchievementGateway : IAchievementGateway
    {
        public int LoadCount { get; private set; }

        public List<string> Calls { get; } = new List<string>();

        public AchievementResult LoadResult { get; set; }

        public AchievementResult ClaimResult { get; set; }

        public Exception ThrowOnClaim { get; set; }

        public FakeAchievementGateway()
        {
            LoadResult = Snapshot(accountXp: 0, accountLevel: 1, achievementXp: 0, achievementLevel: 1);
            ClaimResult = LoadResult;
        }

        public static AchievementResult Snapshot(
            long accountXp,
            int accountLevel,
            long achievementXp,
            int achievementLevel,
            IReadOnlyList<AchievementProgressRecord> progress = null,
            IReadOnlyList<string> claimedAchievements = null,
            IReadOnlyList<string> claimedAccountLevels = null) =>
            AchievementResult.Success(
                accountXp, accountLevel, achievementXp, achievementLevel,
                progress ?? Array.Empty<AchievementProgressRecord>(),
                claimedAchievements ?? Array.Empty<string>(),
                claimedAccountLevels ?? Array.Empty<string>());

        public UniTask<AchievementResult> RequestAchievementsAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return UniTask.FromResult(LoadResult);
        }

        public UniTask<AchievementResult> ClaimAchievementAsync(
            string achievementId,
            CancellationToken cancellationToken)
        {
            Calls.Add("achievement:" + achievementId);
            return ThrowOnClaim != null
                ? UniTask.FromException<AchievementResult>(ThrowOnClaim)
                : UniTask.FromResult(ClaimResult);
        }

        public UniTask<AchievementResult> ClaimAccountLevelRewardAsync(
            int level,
            CancellationToken cancellationToken)
        {
            Calls.Add("level:" + level);
            return UniTask.FromResult(ClaimResult);
        }
    }

    /// <summary>
    /// 成就与账号等级奖励客户端。
    ///
    /// 最重要的一条：这是两套互不相干的系统。成就经验与账号经验分别显示、分别领取，
    /// 领成就奖励不会让账号等级前进，两者的红点也各自独立。
    /// </summary>
    public sealed class AchievementControllerTests
    {
        private const string ActiveId = "achv_wealth_copper";
        private const string InactiveId = "achv_battle_kills";

        private static FakeGameConfigProvider AchievementConfig()
        {
            var catalog = new NarakaConfigCatalog
            {
                SchemaVersion = "1.0.0",
                ConfigVersion = "test",
                Currencies = new[]
                {
                    new CurrencyConfig { CurrencyId = "Copper", DisplayName = "铜币", SortOrder = 1 }
                },
                Achievements = new[]
                {
                    new AchievementConfig
                    {
                        AchievementId = ActiveId, Category = ConfigAchievementCategory.Wealth,
                        DisplayName = "小有积蓄", Description = "累计持有铜币",
                        SortOrder = 1, TargetProgress = 100, AchievementXp = 100,
                        SourceEvent = "currency.copper.total", IsActiveInP1 = true,
                        RewardKind = ConfigRewardKind.Currency, CurrencyId = "Copper", CurrencyAmount = 500
                    },
                    new AchievementConfig
                    {
                        AchievementId = InactiveId, Category = ConfigAchievementCategory.Battle,
                        DisplayName = "百人斩", Description = "击败敌人",
                        SortOrder = 2, TargetProgress = 100, AchievementXp = 200,
                        SourceEvent = "battle.kill", IsActiveInP1 = false,
                        RewardKind = ConfigRewardKind.Currency, CurrencyId = "Copper", CurrencyAmount = 800
                    }
                },
                AccountLevelRewards = new[]
                {
                    new AccountLevelRewardConfig
                    {
                        Level = 1, XpToReach = 0, RewardId = "account_level_1",
                        RewardKind = ConfigRewardKind.Currency, CurrencyId = "Copper", CurrencyAmount = 100
                    },
                    new AccountLevelRewardConfig
                    {
                        Level = 2, XpToReach = 100, RewardId = "account_level_2",
                        RewardKind = ConfigRewardKind.Currency, CurrencyId = "Copper", CurrencyAmount = 200
                    },
                    new AccountLevelRewardConfig
                    {
                        Level = 3, XpToReach = 300, RewardId = "account_level_3",
                        RewardKind = ConfigRewardKind.None
                    }
                }
            };

            return new FakeGameConfigProvider(catalog);
        }

        private static (AchievementController Achievements, FakeAchievementGateway Gateway, TestEventBus Bus)
            Create(IServerCapabilities capabilities = null, AchievementResult? load = null)
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

            var gateway = new FakeAchievementGateway();
            if (load.HasValue)
            {
                gateway.LoadResult = load.Value;
            }

            var bus = new TestEventBus();
            var achievements = new AchievementController(AchievementConfig(), gateway, lobby, caps, bus);

            lobby.Enter("player-one", 42);
            lobby.RequestFeature(LobbyFeature.AccountLevelReward);
            return (achievements, gateway, bus);
        }

        [Test]
        public void OpeningTheEntryLoadsBothSystems()
        {
            var (achievements, gateway, _) = Create();

            Assert.That(achievements.Current.IsOpen, Is.True);
            Assert.That(gateway.LoadCount, Is.EqualTo(1));
            Assert.That(achievements.Current.Achievements.Count, Is.EqualTo(2));
            Assert.That(achievements.Current.AccountLevelRewards.Count, Is.EqualTo(3));
        }

        [Test]
        public void CompatibilityModeNeverSendsAnAchievementRequest()
        {
            var (achievements, gateway, _) = Create(LobbyTestCapabilities.LegacyCloud());

            Assert.That(gateway.LoadCount, Is.Zero);
            Assert.That(achievements.Current.StatusMessage, Does.Contain("服务器功能尚未升级"));
        }

        [Test]
        public void AchievementXpAndAccountXpAreKeptApart()
        {
            var (achievements, _, _) = Create(load: FakeAchievementGateway.Snapshot(
                accountXp: 40, accountLevel: 1, achievementXp: 900, achievementLevel: 2));

            Assert.That(achievements.Current.AccountXp, Is.EqualTo(40));
            Assert.That(achievements.Current.AccountLevel, Is.EqualTo(1));
            Assert.That(achievements.Current.AchievementXp, Is.EqualTo(900));
            Assert.That(achievements.Current.AchievementLevel, Is.EqualTo(2));
        }

        [Test]
        public void AnInactiveAchievementShowsZeroProgressEvenIfTheServerSendsSome()
        {
            // P2/P3 的事件源还没接入，界面绝不显示编造的进度。
            var (achievements, _, _) = Create(load: FakeAchievementGateway.Snapshot(
                accountXp: 0, accountLevel: 1, achievementXp: 0, achievementLevel: 1,
                progress: new[]
                {
                    new AchievementProgressRecord(ActiveId, 30),
                    new AchievementProgressRecord(InactiveId, 77)
                }));

            var active = Find(achievements, ActiveId);
            var inactive = Find(achievements, InactiveId);

            Assert.That(active.Progress, Is.EqualTo(30));
            Assert.That(active.IsActive, Is.True);
            Assert.That(inactive.Progress, Is.Zero);
            Assert.That(inactive.IsActive, Is.False);
        }

        [Test]
        public void ACompletedAchievementBecomesClaimable()
        {
            var (achievements, _, _) = Create(load: FakeAchievementGateway.Snapshot(
                accountXp: 0, accountLevel: 1, achievementXp: 0, achievementLevel: 1,
                progress: new[] { new AchievementProgressRecord(ActiveId, 100) }));

            Assert.That(Find(achievements, ActiveId).IsClaimable, Is.True);
        }

        [Test]
        public void AClaimedAchievementIsNotOfferedAgain()
        {
            var (achievements, _, _) = Create(load: FakeAchievementGateway.Snapshot(
                accountXp: 0, accountLevel: 1, achievementXp: 0, achievementLevel: 1,
                progress: new[] { new AchievementProgressRecord(ActiveId, 100) },
                claimedAchievements: new[] { ActiveId }));

            Assert.That(Find(achievements, ActiveId).IsClaimed, Is.True);
            Assert.That(Find(achievements, ActiveId).IsClaimable, Is.False);
        }

        [Test]
        public void AccountLevelRewardsUnlockByAccountLevelOnly()
        {
            var (achievements, _, _) = Create(load: FakeAchievementGateway.Snapshot(
                accountXp: 150, accountLevel: 2, achievementXp: 5000, achievementLevel: 11));

            var rewards = achievements.Current.AccountLevelRewards;
            Assert.That(rewards[0].IsClaimable, Is.True);
            Assert.That(rewards[1].IsClaimable, Is.True);
            Assert.That(rewards[2].IsUnlocked, Is.False);
        }

        [Test]
        public void ALevelWithoutARewardIsNeverClaimable()
        {
            var (achievements, _, _) = Create(load: FakeAchievementGateway.Snapshot(
                accountXp: 900, accountLevel: 9, achievementXp: 0, achievementLevel: 1));

            var third = achievements.Current.AccountLevelRewards[2];
            Assert.That(third.IsUnlocked, Is.True);
            Assert.That(third.HasReward, Is.False);
            Assert.That(third.IsClaimable, Is.False);
        }

        [Test]
        public void AClaimedLevelRewardIsMatchedByItsRewardId()
        {
            var (achievements, _, _) = Create(load: FakeAchievementGateway.Snapshot(
                accountXp: 150, accountLevel: 2, achievementXp: 0, achievementLevel: 1,
                claimedAccountLevels: new[] { "account_level_1" }));

            Assert.That(achievements.Current.AccountLevelRewards[0].IsClaimed, Is.True);
            Assert.That(achievements.Current.AccountLevelRewards[1].IsClaimed, Is.False);
        }

        [Test]
        public void ClaimingAnAchievementSendsItsId()
        {
            var (achievements, gateway, _) = Create(load: FakeAchievementGateway.Snapshot(
                accountXp: 0, accountLevel: 1, achievementXp: 0, achievementLevel: 1,
                progress: new[] { new AchievementProgressRecord(ActiveId, 100) }));

            achievements.ClaimAchievement(ActiveId);

            Assert.That(gateway.Calls, Is.EqualTo(new[] { "achievement:" + ActiveId }));
        }

        [Test]
        public void ClaimingALevelRewardSendsTheLevel()
        {
            var (achievements, gateway, _) = Create(load: FakeAchievementGateway.Snapshot(
                accountXp: 150, accountLevel: 2, achievementXp: 0, achievementLevel: 1));

            achievements.ClaimAccountLevelReward(2);

            Assert.That(gateway.Calls, Is.EqualTo(new[] { "level:2" }));
        }

        [Test]
        public void AFailedClaimReportsTheReason()
        {
            var (achievements, gateway, _) = Create();
            gateway.ClaimResult = AchievementResult.Failed(LobbyOperationStatus.AlreadyClaimed);

            achievements.ClaimAchievement(ActiveId);

            Assert.That(achievements.Current.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void ATransportFailureIsReportedRatherThanSilentlySwallowed()
        {
            var (achievements, gateway, _) = Create();
            gateway.ThrowOnClaim = new InvalidOperationException("offline");

            achievements.ClaimAchievement(ActiveId);

            Assert.That(achievements.Current.IsBusy, Is.False);
            Assert.That(achievements.Current.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void SwitchingTabsAndCategoriesOnlyChangesTheView()
        {
            var (achievements, gateway, _) = Create();
            var loadsBefore = gateway.LoadCount;

            achievements.SelectTab(AchievementTab.Achievement);
            achievements.SelectCategory(ConfigAchievementCategory.Wealth);

            Assert.That(achievements.Current.Tab, Is.EqualTo(AchievementTab.Achievement));
            Assert.That(achievements.Current.Category, Is.EqualTo(ConfigAchievementCategory.Wealth));
            Assert.That(gateway.LoadCount, Is.EqualTo(loadsBefore));
        }

        [Test]
        public void AnUnknownCategoryIsIgnored()
        {
            var (achievements, _, _) = Create();
            var before = achievements.Current.Category;

            achievements.SelectCategory("NotACategory");

            Assert.That(achievements.Current.Category, Is.EqualTo(before));
        }

        [Test]
        public void FilteringByCategoryReturnsOnlyThatCategory()
        {
            var (achievements, _, _) = Create();

            achievements.SelectCategory(ConfigAchievementCategory.Battle);

            var rows = achievements.Current.AchievementsInCategory;
            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(rows[0].AchievementId, Is.EqualTo(InactiveId));
        }

        [Test]
        public void ClosedPanelIgnoresClaimRequests()
        {
            var (achievements, gateway, _) = Create();
            achievements.Close();
            gateway.Calls.Clear();

            achievements.ClaimAchievement(ActiveId);
            achievements.ClaimAccountLevelReward(1);

            Assert.That(gateway.Calls, Is.Empty);
        }

        [Test]
        public void TheTwoSystemsCarryTwoIndependentRedDots()
        {
            var (_, _, bus) = Create(load: FakeAchievementGateway.Snapshot(
                accountXp: 0, accountLevel: 1, achievementXp: 0, achievementLevel: 1,
                progress: new[] { new AchievementProgressRecord(ActiveId, 100) },
                claimedAccountLevels: new[] { "account_level_1" }));

            Assert.That(bus.LastStateOf(RedDotPath.AchievementClaimableReward), Is.True);
            Assert.That(bus.LastStateOf(RedDotPath.AccountLevelClaimableReward), Is.False);
        }

        [Test]
        public void NothingClaimableMeansNoRedDotAtAll()
        {
            var (_, _, bus) = Create(load: FakeAchievementGateway.Snapshot(
                accountXp: 0, accountLevel: 1, achievementXp: 0, achievementLevel: 1,
                claimedAccountLevels: new[] { "account_level_1" }));

            Assert.That(bus.LastStateOf(RedDotPath.AchievementClaimableReward), Is.False);
            Assert.That(bus.LastStateOf(RedDotPath.AccountLevelClaimableReward), Is.False);
        }

        private static AchievementSnapshot Find(AchievementController controller, string id)
        {
            foreach (var achievement in controller.Current.Achievements)
            {
                if (string.Equals(achievement.AchievementId, id, StringComparison.Ordinal))
                {
                    return achievement;
                }
            }

            throw new InvalidOperationException("测试配置里没有成就 " + id + "。");
        }
    }
}
