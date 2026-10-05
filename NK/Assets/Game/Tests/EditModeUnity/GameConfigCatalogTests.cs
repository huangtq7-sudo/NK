using System.Linq;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Infrastructure.Config;
using NUnit.Framework;

namespace Naraka.Unity.EditMode.Tests
{
    /// <summary>
    /// 客户端读取的是与服务端完全相同的生成配置文件。
    /// 这些测试同时证明三件事：StreamingAssets 里确实有生成物、JsonUtility 能完整解析它、
    /// 以及客户端目录的查表结果与配置内容一致。
    /// </summary>
    public sealed class GameConfigCatalogTests
    {
        private static GameConfigCatalog LoadCatalog()
        {
            var provider = new StreamingAssetsGameConfigProvider();
            Assert.That(provider.IsLoaded, Is.True, provider.LoadError);
            return provider.Catalog;
        }

        [Test]
        public void StreamingAssetsCatalogLoads()
        {
            var catalog = LoadCatalog();

            Assert.That(catalog.SchemaVersion, Is.EqualTo(StreamingAssetsGameConfigProvider.RequiredSchemaVersion));
            Assert.That(catalog.ConfigVersion, Does.StartWith("p1-config-"));
        }

        [Test]
        public void JsonUtilityKeepsEveryTable()
        {
            var catalog = LoadCatalog().Catalog;

            // JsonUtility 在字段名不匹配时会静默给出空数组，因此逐表断言非空
            // 才能真正证明模型与生成物没有漂移。
            Assert.That(catalog.Currencies, Is.Not.Empty);
            Assert.That(catalog.Items, Is.Not.Empty);
            Assert.That(catalog.Heroes, Is.Not.Empty);
            Assert.That(catalog.HeroSkills, Is.Not.Empty);
            Assert.That(catalog.Weapons, Is.Not.Empty);
            Assert.That(catalog.WeaponLevels, Is.Not.Empty);
            Assert.That(catalog.ForgeRecipes, Is.Not.Empty);
            Assert.That(catalog.ShopProducts, Is.Not.Empty);
            Assert.That(catalog.GachaPools, Is.Not.Empty);
            Assert.That(catalog.GachaEntries, Is.Not.Empty);
            Assert.That(catalog.SignInRewards, Is.Not.Empty);
            Assert.That(catalog.SignInMilestones, Is.Not.Empty);
            Assert.That(catalog.AccountLevelRewards, Is.Not.Empty);
            Assert.That(catalog.Achievements, Is.Not.Empty);
            Assert.That(catalog.InventoryCapacities, Is.Not.Empty);
            Assert.That(catalog.Avatars, Is.Not.Empty);
            Assert.That(catalog.AvatarFrames, Is.Not.Empty);
            Assert.That(catalog.Pets, Is.Not.Empty);
            Assert.That(catalog.Monsters, Is.Not.Empty);
            Assert.That(catalog.MonsterSkills, Is.Not.Empty);
        }

        [Test]
        public void StringFieldsSurviveDeserialization()
        {
            var catalog = LoadCatalog();

            Assert.That(catalog.TryGetCurrency("Copper", out var copper), Is.True);
            Assert.That(copper.DisplayName, Is.EqualTo("铜币"));
            Assert.That(copper.StarterGrant, Is.EqualTo(1000L));
        }

        [Test]
        public void ThreeCurrenciesAreOrderedForDisplay()
        {
            var catalog = LoadCatalog();

            var ids = catalog.CurrenciesInDisplayOrder.Select(currency => currency.CurrencyId).ToArray();

            Assert.That(ids, Is.EqualTo(new[] { "Copper", "Silk", "Gold" }));
        }

        [Test]
        public void HeroSkillsAreGroupedAndOrdered()
        {
            var catalog = LoadCatalog();
            var hero = catalog.HeroesInDisplayOrder[0];

            var skills = catalog.GetHeroSkills(hero.HeroId);

            Assert.That(skills, Is.Not.Empty);
            Assert.That(skills.Select(skill => skill.SlotKey), Is.EqualTo(new[] { "F", "V" }));
            Assert.That(skills.All(skill => skill.HeroId == hero.HeroId), Is.True);
        }

        [Test]
        public void ForgePreviewExposesNextLevelAttack()
        {
            var catalog = LoadCatalog();
            var weapon = catalog.WeaponsInDisplayOrder[0];

            Assert.That(catalog.TryGetWeaponLevel(weapon.WeaponId, 1, out var current), Is.True);
            Assert.That(catalog.TryGetWeaponLevel(weapon.WeaponId, 2, out var next), Is.True);
            Assert.That(catalog.TryGetForgeRecipe(weapon.WeaponId, 1, out var recipe), Is.True);

            // 锻造界面显示的"68 → 75"这类预览只来自配置，不代表服务端一定放行。
            Assert.That(next.Attack, Is.GreaterThan(current.Attack));
            Assert.That(recipe.ToLevel, Is.EqualTo(2));
            Assert.That(catalog.TryGetForgeRecipe(weapon.WeaponId, weapon.MaxLevel, out _), Is.False);
        }

        [Test]
        public void UnknownIdsReturnFalseInsteadOfThrowing()
        {
            var catalog = LoadCatalog();

            Assert.That(catalog.TryGetItem("does_not_exist", out _), Is.False);
            Assert.That(catalog.TryGetItem(null, out _), Is.False);
            Assert.That(catalog.GetHeroSkills("does_not_exist"), Is.Empty);
            Assert.That(catalog.GetItemDisplayName("does_not_exist"), Is.EqualTo("does_not_exist"));
        }

        [Test]
        public void MissingConfigFileReportsAnErrorInsteadOfThrowing()
        {
            var provider = new StreamingAssetsGameConfigProvider("Z:/naraka/does-not-exist/naraka-config.json");

            Assert.That(provider.IsLoaded, Is.False);
            Assert.That(provider.LoadError, Is.Not.Empty);
            // 提示中不得出现完整部署路径。
            Assert.That(provider.LoadError, Does.Not.Contain("Z:/naraka"));
        }

        [Test]
        public void FiveQualityTiersAreOrderedFromLowToHigh()
        {
            Assert.That(
                ConfigQuality.Ordered,
                Is.EqualTo(new[] { "White", "Blue", "Purple", "Gold", "Red" }));
            Assert.That(ConfigQuality.IndexOf(ConfigQuality.Red), Is.GreaterThan(ConfigQuality.IndexOf(ConfigQuality.Gold)));
            Assert.That(ConfigQuality.IndexOf("Rainbow"), Is.EqualTo(-1));
        }
    
        [Test]
        public void HeroStaminaMatchesTheConfirmedBaseline()
        {
            var catalog = LoadCatalog();

            foreach (var hero in catalog.HeroesInDisplayOrder)
            {
                Assert.That(
                    hero.Stamina,
                    Is.EqualTo(20),
                    $"英雄 {hero.HeroId} 的体力上限必须是 20（D-013 / ADR-0013）。");
            }
        }

        [Test]
        public void TheWolfIsConfiguredAsAGrayboxMonster()
        {
            var catalog = LoadCatalog();

            Assert.That(catalog.TryGetMonster("monster_wolf_duskshadow", out var wolf), Is.True);
            Assert.That(wolf.DisplayName, Is.EqualTo("暮影妖狼"));
            Assert.That(
                wolf.BalanceStatus,
                Is.EqualTo(ConfigBalanceStatus.P2Graybox),
                "怪物数值还没有平衡确认，必须标成灰盒调试值。");
            Assert.That(wolf.PhaseHealthRatio, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(wolf.DecisionsPerSecond, Is.InRange(5, 10));
            Assert.That(wolf.ChaseRadius, Is.GreaterThanOrEqualTo(wolf.PerceptionRadius));
            Assert.That(wolf.DormantDistance, Is.GreaterThan(wolf.ChaseRadius));
        }

        [Test]
        public void MonsterSkillsReferenceAnExistingMonsterAndAgreeWithTheirColor()
        {
            var catalog = LoadCatalog();

            foreach (var skill in catalog.Catalog.MonsterSkills)
            {
                Assert.That(
                    catalog.TryGetMonster(skill.MonsterId, out _),
                    Is.True,
                    $"技能 {skill.SkillId} 引用了不存在的怪物 {skill.MonsterId}。");
                Assert.That(
                    ConfigMonsterColorTag.All,
                    Does.Contain(skill.ColorTag),
                    $"技能 {skill.SkillId} 的颜色标签非法。");

                if (skill.ColorTag == ConfigMonsterColorTag.Red)
                {
                    Assert.That(skill.Counterable, Is.False, "红色技能不可反击。");
                }

                if (skill.ColorTag == ConfigMonsterColorTag.Gold)
                {
                    Assert.That(skill.Counterable, Is.True, "金色技能必须可反击。");
                }

                Assert.That(skill.WarningSeconds, Is.GreaterThan(0f), "预警必须先于伤害窗口。");
            }
        }

        [Test]
        public void TheWolfBreathIsAPhaseGatedRedSkill()
        {
            var catalog = LoadCatalog();

            var skills = catalog.GetMonsterSkills("monster_wolf_duskshadow");

            Assert.That(skills, Is.Not.Empty);
            var breath = skills[0];
            Assert.That(breath.SkillId, Is.EqualTo("monster_skill_wolf_breath"));
            Assert.That(breath.ColorTag, Is.EqualTo(ConfigMonsterColorTag.Red));
            Assert.That(breath.Counterable, Is.False);
            Assert.That(breath.RequiresPhaseAtOrBelow, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(breath.ConeAngleDegrees, Is.GreaterThan(0f), "锥形吐息必须有锥角。");
        }
    }
}
