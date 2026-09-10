using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Config;
using Naraka.Features.Loadout.Controller;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    /// <summary>
    /// 内存配置目录。刻意不读 StreamingAssets：这些测试验证的是控制器的翻页与提交逻辑，
    /// 用固定的小目录才能断言"翻到最后一个再翻会回到第一个"这类边界。
    /// </summary>
    internal sealed class FakeGameConfigProvider : IGameConfigProvider
    {
        public FakeGameConfigProvider(NarakaConfigCatalog catalog = null)
        {
            Catalog = catalog == null ? null : new GameConfigCatalog(catalog);
            LoadError = catalog == null ? "测试未提供配置。" : string.Empty;
        }

        public bool IsLoaded => Catalog != null;

        public string LoadError { get; }

        public GameConfigCatalog Catalog { get; }

        public static FakeGameConfigProvider WithThreeHeroesAndTwoWeapons()
        {
            var catalog = new NarakaConfigCatalog
            {
                SchemaVersion = "1.0.0",
                ConfigVersion = "test",
                Heroes = new[]
                {
                    Hero("hero_a", 1),
                    Hero("hero_b", 2),
                    Hero("hero_c", 3)
                },
                HeroSkills = new[]
                {
                    Skill("skill_a_f", "hero_a", "F", 1),
                    Skill("skill_a_v", "hero_a", "V", 2),
                    Skill("skill_b_f", "hero_b", "F", 1)
                },
                Weapons = new[]
                {
                    new WeaponConfig { WeaponId = "weapon_a", DisplayName = "甲", SortOrder = 1, MaxLevel = 20 },
                    new WeaponConfig { WeaponId = "weapon_b", DisplayName = "乙", SortOrder = 2, MaxLevel = 20 }
                },
                Pets = new[]
                {
                    new PetConfig { PetId = "pet_lingyu", DisplayName = "灵愈", SortOrder = 1, DefaultOwned = true }
                }
            };

            return new FakeGameConfigProvider(catalog);
        }

        /// <summary>仓库测试用的最小物品目录：一个材料、一个魂玉、一件护甲。</summary>
        public static FakeGameConfigProvider WithInventoryItems()
        {
            var catalog = new NarakaConfigCatalog
            {
                SchemaVersion = "1.0.0",
                ConfigVersion = "test",
                Items = new[]
                {
                    Item("mat_ore_basic", ConfigItemCategory.Material, ConfigQuality.White, 999, 5),
                    Item("soul_feng_rui", ConfigItemCategory.Soulstone, ConfigQuality.White, 20, 50),
                    Item("armor_cloth", ConfigItemCategory.Armor, ConfigQuality.White, 20, 30)
                },
                Currencies = new[]
                {
                    new CurrencyConfig { CurrencyId = "Copper", DisplayName = "铜币", SortOrder = 1 }
                }
            };

            return new FakeGameConfigProvider(catalog);
        }

        private static ItemConfig Item(
            string id, string category, string quality, int stackLimit, long sellPrice) =>
            new ItemConfig
            {
                ItemId = id,
                DisplayName = id,
                Category = category,
                Quality = quality,
                StackLimit = stackLimit,
                SortOrder = 1,
                IconKey = id,
                SellCurrencyId = "Copper",
                SellPrice = sellPrice,
                Description = id
            };

        private static HeroConfig Hero(string id, int order) => new HeroConfig
        {
            HeroId = id,
            DisplayName = id,
            SortOrder = order,
            Health = 1000,
            Attack = 100,
            Defense = 80,
            Stamina = 60,
            MoveSpeed = 5f
        };

        private static HeroSkillConfig Skill(string id, string heroId, string slot, int order) =>
            new HeroSkillConfig
            {
                SkillId = id,
                HeroId = heroId,
                SlotKey = slot,
                DisplayName = id,
                SortOrder = order,
                CooldownSeconds = 15f,
                DamageMultiplier = 1.2f,
                RangeMeters = 4f
            };
    }

    internal sealed class FakeLoadoutGateway : ILoadoutGateway
    {
        public List<(string HeroId, string WeaponId, string PetId)> Calls { get; } =
            new List<(string HeroId, string WeaponId, string PetId)>();

        public LobbyProfileResult Result { get; set; } =
            LobbyProfileResult.Failed(LobbyOperationStatus.Success);

        public Exception ThrowWith { get; set; }

        public UniTask<LobbyProfileResult> SetLoadoutAsync(
            string heroId,
            string weaponId,
            string petId,
            CancellationToken cancellationToken)
        {
            Calls.Add((heroId, weaponId, petId));
            if (ThrowWith != null)
            {
                return UniTask.FromException<LobbyProfileResult>(ThrowWith);
            }

            return UniTask.FromResult(Result);
        }
    }

    public sealed class LoadoutControllerTests
    {
        private static (LoadoutController Loadout, LobbyController Lobby, FakeLoadoutGateway Gateway) Create(
            IServerCapabilities capabilities = null)
        {
            LobbyAccountSnapshot.TryCreate(1, 1000, 1000, 1000, out var summary);
            var profiles = new FakeLobbyProfileGateway
            {
                Result = LobbyProfileResult.Success(
                    Profile("hero_b", "weapon_a"))
            };

            var caps = capabilities ?? LobbyTestCapabilities.Full();
            var lobby = new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                new FakeLobbyAccountGateway { Result = LobbyAccountSummaryResult.Success(summary) },
                profiles,
                caps);

            var gateway = new FakeLoadoutGateway
            {
                Result = LobbyProfileResult.Success(Profile("hero_b", "weapon_a"))
            };

            var loadout = new LoadoutController(
                FakeGameConfigProvider.WithThreeHeroesAndTwoWeapons(), gateway, lobby, caps);

            lobby.Enter("player-one", 42);
            return (loadout, lobby, gateway);
        }

        private static LobbyProfileSnapshot Profile(string heroId, string weaponId)
        {
            LobbyProfileSnapshot.TryCreate(
                "avatar_01", "frame_white", heroId, weaponId, "pet_lingyu",
                0, 1, 0, 1000, 1000, 1000, out var snapshot);
            return snapshot;
        }

        [Test]
        public void PreviewStartsOnTheEquippedHeroAndWeapon()
        {
            var (loadout, _, _) = Create();

            Assert.That(loadout.Current.EquippedHeroId, Is.EqualTo("hero_b"));
            Assert.That(loadout.Current.PreviewHeroId, Is.EqualTo("hero_b"));
            Assert.That(loadout.Current.PreviewWeaponId, Is.EqualTo("weapon_a"));
            Assert.That(loadout.Current.IsPreviewHeroEquipped, Is.True);
            Assert.That(loadout.Current.IsReady, Is.True);
        }

        [Test]
        public void OpeningTheHeroEntryShowsTheHeroPanel()
        {
            var (loadout, lobby, _) = Create();

            lobby.RequestFeature(LobbyFeature.Hero);

            Assert.That(loadout.Current.Panel, Is.EqualTo(LoadoutPanel.Hero));

            lobby.CloseFeature();

            Assert.That(loadout.Current.Panel, Is.EqualTo(LoadoutPanel.None));
        }

        [Test]
        public void OtherLobbyEntriesDoNotOpenTheLoadoutPanels()
        {
            var (loadout, lobby, _) = Create();

            lobby.RequestFeature(LobbyFeature.Shop);

            Assert.That(loadout.Current.Panel, Is.EqualTo(LoadoutPanel.None));
        }

        [Test]
        public void HeroBrowsingWrapsAroundInBothDirections()
        {
            var (loadout, _, _) = Create();

            loadout.PreviewNextHero();
            Assert.That(loadout.Current.PreviewHeroId, Is.EqualTo("hero_c"));

            loadout.PreviewNextHero();
            Assert.That(loadout.Current.PreviewHeroId, Is.EqualTo("hero_a"), "最后一个之后应回到第一个。");

            loadout.PreviewPreviousHero();
            Assert.That(loadout.Current.PreviewHeroId, Is.EqualTo("hero_c"), "第一个之前应回到最后一个。");
        }

        [Test]
        public void SwitchingHeroSelectsThatHeroFirstSkill()
        {
            var (loadout, _, _) = Create();

            loadout.PreviewPreviousHero();

            Assert.That(loadout.Current.PreviewHeroId, Is.EqualTo("hero_a"));
            Assert.That(loadout.Current.SelectedSkillId, Is.EqualTo("skill_a_f"));

            loadout.SelectSkill("skill_a_v");
            Assert.That(loadout.Current.SelectedSkillId, Is.EqualTo("skill_a_v"));
        }

        [Test]
        public void EquippingTheAlreadyEquippedHeroSendsNothing()
        {
            var (loadout, _, gateway) = Create();

            loadout.EquipPreviewHero();

            Assert.That(gateway.Calls, Is.Empty);
        }

        [Test]
        public void EquippingAnotherHeroKeepsTheCurrentWeaponAndPet()
        {
            var (loadout, _, gateway) = Create();
            loadout.PreviewNextHero();

            loadout.EquipPreviewHero();

            Assert.That(gateway.Calls.Count, Is.EqualTo(1));
            Assert.That(gateway.Calls[0].HeroId, Is.EqualTo("hero_c"));
            Assert.That(gateway.Calls[0].WeaponId, Is.EqualTo("weapon_a"));
            Assert.That(gateway.Calls[0].PetId, Is.EqualTo("pet_lingyu"));
        }

        [Test]
        public void EquippingAnotherWeaponKeepsTheCurrentHero()
        {
            var (loadout, _, gateway) = Create();
            loadout.PreviewNextWeapon();

            loadout.EquipPreviewWeapon();

            Assert.That(gateway.Calls.Count, Is.EqualTo(1));
            Assert.That(gateway.Calls[0].HeroId, Is.EqualTo("hero_b"));
            Assert.That(gateway.Calls[0].WeaponId, Is.EqualTo("weapon_b"));
        }

        [Test]
        public void CompatibilityModeNeverSendsALoadoutRequest()
        {
            var (loadout, _, gateway) = Create(LobbyTestCapabilities.LegacyCloud());
            loadout.PreviewNextHero();

            loadout.EquipPreviewHero();

            Assert.That(gateway.Calls, Is.Empty, "旧云端不认识协议 25，绝不能发出去。");
            Assert.That(loadout.Current.StatusMessage, Does.Contain("服务器功能尚未升级"));
        }

        [Test]
        public void RejectedLoadoutShowsTheServerMessageAndKeepsTheEquippedHero()
        {
            var (loadout, _, gateway) = Create();
            gateway.Result = LobbyProfileResult.Failed(LobbyOperationStatus.NotAvailable);
            loadout.PreviewNextHero();

            loadout.EquipPreviewHero();

            Assert.That(loadout.Current.EquippedHeroId, Is.EqualTo("hero_b"), "失败不得改变已出战英雄。");
            Assert.That(loadout.Current.IsSaving, Is.False);
            Assert.That(loadout.Current.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void TransportFailureShowsAReadableMessage()
        {
            var (loadout, _, gateway) = Create();
            gateway.ThrowWith = new InvalidOperationException("socket down");
            loadout.PreviewNextHero();

            loadout.EquipPreviewHero();

            Assert.That(loadout.Current.StatusMessage, Does.Contain("无法连接服务器"));
            Assert.That(loadout.Current.IsSaving, Is.False);
        }

        [Test]
        public void MissingConfigurationLeavesThePanelNotReady()
        {
            LobbyAccountSnapshot.TryCreate(1, 0, 0, 0, out var summary);
            var lobby = new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                new FakeLobbyAccountGateway { Result = LobbyAccountSummaryResult.Success(summary) },
                new FakeLobbyProfileGateway(),
                LobbyTestCapabilities.Full());
            var loadout = new LoadoutController(
                new FakeGameConfigProvider(), new FakeLoadoutGateway(), lobby, LobbyTestCapabilities.Full());

            lobby.Enter("player-one", 42);

            Assert.That(loadout.Current.IsReady, Is.False);
            Assert.That(loadout.Heroes, Is.Empty);

            // 没有配置时翻页只能什么都不做：既不能崩，也不能凭空造出一个英雄 ID。
            var before = loadout.Current.PreviewHeroId;
            loadout.PreviewNextHero();
            loadout.PreviewPreviousWeapon();
            Assert.That(loadout.Current.PreviewHeroId, Is.EqualTo(before));
        }

        [Test]
        public void DisposingReleasesTheLobbySubscription()
        {
            var (loadout, lobby, _) = Create();

            loadout.Dispose();
            lobby.RequestFeature(LobbyFeature.Hero);

            // 已释放的控制器不再跟随大厅状态变化。
            Assert.That(loadout.Current.Panel, Is.EqualTo(LoadoutPanel.None));
        }
    }
}
