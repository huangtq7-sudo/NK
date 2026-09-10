using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Features.Forge.Controller;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    internal sealed class FakeForgeGateway : IForgeGateway
    {
        public List<(string WeaponId, int ExpectedLevel, string RequestId)> Upgrades { get; } =
            new List<(string, int, string)>();

        public int LoadCount { get; private set; }

        public ForgeResult Result { get; set; }

        public Exception ThrowWith { get; set; }

        public FakeForgeGateway()
        {
            Result = Snapshot(1, 999, 999, 5000);
        }

        public static ForgeResult Snapshot(int level, long oreQuantity, long stoneQuantity, long copper) =>
            ForgeResult.Success(
                new[] { new AccountWeaponSnapshot("weapon_a", level, 0, 0) },
                new[]
                {
                    new ForgeMaterialSnapshot("mat_ore", oreQuantity),
                    new ForgeMaterialSnapshot("mat_stone", stoneQuantity)
                },
                copper, 0, 0);

        public UniTask<ForgeResult> RequestForgeAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return Respond();
        }

        public UniTask<ForgeResult> UpgradeAsync(
            string weaponId,
            int expectedLevel,
            string requestId,
            CancellationToken cancellationToken)
        {
            Upgrades.Add((weaponId, expectedLevel, requestId));
            return Respond();
        }

        private UniTask<ForgeResult> Respond() =>
            ThrowWith != null
                ? UniTask.FromException<ForgeResult>(ThrowWith)
                : UniTask.FromResult(Result);
    }

    /// <summary>
    /// 锻造客户端：下一级预览、材料齐备判定、最高等级与能力门禁。
    /// </summary>
    public sealed class ForgeControllerTests
    {
        private const int MaxLevel = 3;

        private static FakeGameConfigProvider ForgeConfig()
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
                        ItemId = "mat_ore", DisplayName = "矿石", Category = ConfigItemCategory.Material,
                        Quality = ConfigQuality.White, StackLimit = 999, SortOrder = 1, IconKey = "mat_ore"
                    },
                    new ItemConfig
                    {
                        ItemId = "mat_stone", DisplayName = "锻造石", Category = ConfigItemCategory.Material,
                        Quality = ConfigQuality.Blue, StackLimit = 999, SortOrder = 2, IconKey = "mat_stone"
                    }
                },
                Weapons = new[]
                {
                    new WeaponConfig
                    {
                        WeaponId = "weapon_a", DisplayName = "甲", SortOrder = 1,
                        MaxLevel = MaxLevel, IconKey = "weapon_a"
                    },
                    new WeaponConfig
                    {
                        WeaponId = "weapon_b", DisplayName = "乙", SortOrder = 2,
                        MaxLevel = MaxLevel, IconKey = "weapon_b"
                    }
                },
                WeaponLevels = new[]
                {
                    new WeaponLevelConfig { WeaponId = "weapon_a", Level = 1, Attack = 68, AttackSpeed = 1.00f },
                    new WeaponLevelConfig { WeaponId = "weapon_a", Level = 2, Attack = 75, AttackSpeed = 1.00f },
                    new WeaponLevelConfig { WeaponId = "weapon_a", Level = 3, Attack = 82, AttackSpeed = 1.00f },
                    new WeaponLevelConfig { WeaponId = "weapon_b", Level = 1, Attack = 60, AttackSpeed = 1.15f },
                    new WeaponLevelConfig { WeaponId = "weapon_b", Level = 2, Attack = 66, AttackSpeed = 1.15f },
                    new WeaponLevelConfig { WeaponId = "weapon_b", Level = 3, Attack = 72, AttackSpeed = 1.15f }
                },
                ForgeRecipes = new[]
                {
                    Recipe("forge_a_1", "weapon_a", 1, 200, 4, 2),
                    Recipe("forge_a_2", "weapon_a", 2, 400, 6, 3),
                    Recipe("forge_b_1", "weapon_b", 1, 200, 4, 2),
                    Recipe("forge_b_2", "weapon_b", 2, 400, 6, 3)
                }
            };

            return new FakeGameConfigProvider(catalog);
        }

        private static ForgeRecipeConfig Recipe(
            string recipeId, string weaponId, int fromLevel, long currency, int ore, int stone) =>
            new ForgeRecipeConfig
            {
                RecipeId = recipeId,
                WeaponId = weaponId,
                FromLevel = fromLevel,
                ToLevel = fromLevel + 1,
                CurrencyId = "Copper",
                CurrencyAmount = currency,
                Material1ItemId = "mat_ore",
                Material1Amount = ore,
                Material2ItemId = "mat_stone",
                Material2Amount = stone
            };

        private static (ForgeController Forge, LobbyController Lobby, FakeForgeGateway Gateway) Create(
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

            var gateway = new FakeForgeGateway();
            var forge = new ForgeController(ForgeConfig(), gateway, lobby, caps, new TestEventBus());

            lobby.Enter("player-one", 42);
            lobby.RequestFeature(LobbyFeature.Forge);
            return (forge, lobby, gateway);
        }

        [Test]
        public void OpeningTheForgeEntryLoadsWeaponState()
        {
            var (forge, _, gateway) = Create();

            Assert.That(forge.Current.IsOpen, Is.True);
            Assert.That(gateway.LoadCount, Is.EqualTo(1));
            Assert.That(forge.Current.HasServerState, Is.True);
            Assert.That(forge.Current.PreviewWeaponId, Is.EqualTo("weapon_a"));
            Assert.That(forge.Current.LevelOf("weapon_a"), Is.EqualTo(1));
        }

        [Test]
        public void CompatibilityModeNeverSendsAForgeRequest()
        {
            var (forge, _, gateway) = Create(LobbyTestCapabilities.LegacyCloud());

            Assert.That(gateway.LoadCount, Is.Zero);
            Assert.That(forge.Current.StatusMessage, Does.Contain("服务器功能尚未升级"));
        }

        [Test]
        public void NextLevelPreviewComesFromConfiguration()
        {
            var (forge, _, _) = Create();

            Assert.That(forge.CurrentLevelStats.Attack, Is.EqualTo(68));
            Assert.That(forge.NextLevelStats.Attack, Is.EqualTo(75), "界面上的 68 → 75 必须来自配置。");
            Assert.That(forge.CurrentRecipe.ToLevel, Is.EqualTo(2));
        }

        [Test]
        public void UpgradeIsAllowedWhenMaterialsAndCurrencyAreEnough()
        {
            var (forge, _, _) = Create();

            // 材料足够时必定成功：这里不存在任何成功率。
            Assert.That(forge.CanAffordUpgrade, Is.True);
        }

        [Test]
        public void UpgradeIsBlockedWhenAMaterialIsMissing()
        {
            var (forge, _, gateway) = Create();
            gateway.Result = FakeForgeGateway.Snapshot(1, oreQuantity: 1, stoneQuantity: 999, copper: 5000);
            forge.ReloadAsync(CancellationToken.None).Forget();

            Assert.That(forge.CanAffordUpgrade, Is.False);
        }

        [Test]
        public void UpgradeIsBlockedWhenCurrencyIsMissing()
        {
            var (forge, _, gateway) = Create();
            gateway.Result = FakeForgeGateway.Snapshot(1, 999, 999, copper: 10);
            forge.ReloadAsync(CancellationToken.None).Forget();

            Assert.That(forge.CanAffordUpgrade, Is.False);
        }

        [Test]
        public void UpgradeSendsTheWeaponAndTheLevelTheUiIsShowing()
        {
            var (forge, _, gateway) = Create();

            forge.Upgrade();

            var call = gateway.Upgrades[0];
            Assert.That(call.WeaponId, Is.EqualTo("weapon_a"));
            Assert.That(call.ExpectedLevel, Is.EqualTo(1));
            Assert.That(call.RequestId, Is.Not.Empty);
        }

        [Test]
        public void EachUpgradeUsesAFreshRequestId()
        {
            var (forge, _, gateway) = Create();

            forge.Upgrade();
            forge.Upgrade();

            Assert.That(gateway.Upgrades.Count, Is.EqualTo(2));
            Assert.That(gateway.Upgrades[0].RequestId, Is.Not.EqualTo(gateway.Upgrades[1].RequestId));
        }

        [Test]
        public void MaximumLevelStopsTheUpgradeBeforeAnythingIsSent()
        {
            var (forge, _, gateway) = Create();
            gateway.Result = FakeForgeGateway.Snapshot(MaxLevel, 999, 999, 5000);
            forge.ReloadAsync(CancellationToken.None).Forget();
            gateway.Upgrades.Clear();

            forge.Upgrade();

            Assert.That(forge.CurrentRecipe, Is.Null, "满级后没有配方。");
            Assert.That(forge.NextLevelStats, Is.Null);
            Assert.That(forge.CanAffordUpgrade, Is.False);
            Assert.That(gateway.Upgrades, Is.Empty);
        }

        [Test]
        public void WeaponBrowsingWrapsAround()
        {
            var (forge, _, _) = Create();

            forge.PreviewNextWeapon();
            Assert.That(forge.Current.PreviewWeaponId, Is.EqualTo("weapon_b"));

            forge.PreviewNextWeapon();
            Assert.That(forge.Current.PreviewWeaponId, Is.EqualTo("weapon_a"));

            forge.PreviewPreviousWeapon();
            Assert.That(forge.Current.PreviewWeaponId, Is.EqualTo("weapon_b"));
        }

        [Test]
        public void ConflictKeepsThePreviousLevel()
        {
            var (forge, _, gateway) = Create();
            gateway.Result = ForgeResult.Failed(LobbyOperationStatus.Conflict);

            forge.Upgrade();

            Assert.That(forge.Current.LevelOf("weapon_a"), Is.EqualTo(1));
            Assert.That(forge.Current.StatusMessage, Does.Contain("冲突"));
            Assert.That(forge.Current.IsBusy, Is.False);
        }

        [Test]
        public void TransportFailureKeepsThePreviousState()
        {
            var (forge, _, gateway) = Create();
            gateway.ThrowWith = new InvalidOperationException("socket down");

            forge.Upgrade();

            Assert.That(forge.Current.HasServerState, Is.True);
            Assert.That(forge.Current.LevelOf("weapon_a"), Is.EqualTo(1));
            Assert.That(forge.Current.StatusMessage, Does.Contain("无法连接服务器"));
        }

        [Test]
        public void LevelIsZeroBeforeAnyServerDataArrives()
        {
            var state = ForgePresentationState.Initial;

            // 尚未拿到权威数据时不能显示成 1 级，那会让玩家以为强化被回退了。
            Assert.That(state.LevelOf("weapon_a"), Is.Zero);
            Assert.That(state.HasServerState, Is.False);
        }

        [Test]
        public void DisposingReleasesTheLobbySubscription()
        {
            var (forge, lobby, gateway) = Create();

            forge.Dispose();
            lobby.CloseFeature();
            lobby.RequestFeature(LobbyFeature.Forge);

            Assert.That(gateway.LoadCount, Is.EqualTo(1));
        }
    }
}
