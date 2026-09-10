using Naraka.Config;
using Naraka.Server.Application.Bootstrap;
using Naraka.Server.Application.Config;

namespace Naraka.Server.Application.Tests;

/// <summary>
/// 服务端配置加载与 Bootstrap 能力声明。这些测试读取仓库中真实的生成配置，
/// 因此"配置能在服务端加载"与"配置已经生成过"是同一条断言。
/// </summary>
public sealed class GameConfigTests
{
    private static readonly string CatalogJson = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Config", "naraka-config.json"),
        System.Text.Encoding.UTF8);

    private static GameConfig Load()
    {
        var result = GameConfig.Load(CatalogJson, GameConfig.RequiredSchemaVersion);
        Assert.Equal(GameConfigLoadStatus.Success, result.Status);
        return result.Config!;
    }

    [Fact]
    public void GeneratedCatalogLoads()
    {
        var config = Load();

        Assert.StartsWith("p1-config-", config.ConfigVersion, StringComparison.Ordinal);
        Assert.Equal(GameConfig.RequiredSchemaVersion, config.SchemaVersion);
        Assert.Equal(64, config.CatalogSha256.Length);
    }

    [Fact]
    public void ThreeCurrenciesEachGrantOneThousandOnAccountCreation()
    {
        var config = Load();

        Assert.Equal(3, config.Currencies.Count);
        Assert.All(config.Currencies, currency => Assert.Equal(1000L, currency.StarterGrant));
        Assert.True(config.TryGetCurrency("Copper", out _));
        Assert.True(config.TryGetCurrency("Silk", out _));
        Assert.True(config.TryGetCurrency("Gold", out _));
    }

    [Fact]
    public void GachaConsumesGoldAndKeepsTheRedPityReachable()
    {
        var config = Load();

        Assert.True(config.TryGetGachaPool("pool_standard", out var pool));
        Assert.Equal("Gold", pool!.CurrencyId);
        Assert.Equal(20, pool.PityCount);
        Assert.Equal(ConfigQuality.Red, pool.PityQuality);
        Assert.Equal(ConfigQuality.Blue, pool.TenPullMinimumQuality);

        var entries = config.GetGachaEntries(pool.PoolId);
        Assert.NotEmpty(entries);
        Assert.Contains(entries, entry => entry.Quality == ConfigQuality.Red && entry.Weight > 0);
        Assert.Contains(entries, entry => entry.Quality == ConfigQuality.Blue && entry.Weight > 0);
    }

    [Fact]
    public void DefaultsUseStableIdsRatherThanListIndexes()
    {
        var config = Load();

        Assert.True(config.TryGetHero(config.DefaultHeroId, out _));
        Assert.True(config.TryGetWeapon(config.DefaultWeaponId, out _));
        Assert.True(config.TryGetAvatar(config.DefaultAvatarId, out _));
        Assert.True(config.TryGetAvatarFrame(config.DefaultAvatarFrameId, out _));
        Assert.True(config.TryGetPet(config.DefaultPetId, out var pet));
        Assert.True(pet!.DefaultOwned);
    }

    [Fact]
    public void EveryWeaponHasAContiguousUpgradePath()
    {
        var config = Load();

        foreach (var weapon in config.WeaponsInDisplayOrder)
        {
            for (var level = 1; level <= weapon.MaxLevel; level++)
            {
                Assert.True(config.TryGetWeaponLevel(weapon.WeaponId, level, out _), $"{weapon.WeaponId} L{level}");
            }

            for (var level = 1; level < weapon.MaxLevel; level++)
            {
                Assert.True(config.TryGetForgeRecipe(weapon.WeaponId, level, out var recipe));
                Assert.Equal(level + 1, recipe!.ToLevel);
            }

            // 满级不能再有配方，否则界面会一直显示可继续强化。
            Assert.False(config.TryGetForgeRecipe(weapon.WeaponId, weapon.MaxLevel, out _));
        }
    }

    [Fact]
    public void AccountLevelIsResolvedFromExperienceTable()
    {
        var config = Load();

        Assert.Equal(1, config.ResolveAccountLevel(0));
        Assert.Equal(1, config.ResolveAccountLevel(99));
        Assert.Equal(2, config.ResolveAccountLevel(100));
        Assert.Equal(30, config.MaximumAccountLevel);
        Assert.Equal(23200L, config.AccountLevelRewards[^1].XpToReach);
        Assert.Equal(30, config.ResolveAccountLevel(23200));
        Assert.Equal(30, config.ResolveAccountLevel(long.MaxValue));
    }

    [Fact]
    public void InventoryCapacityGrowsWithEachTier()
    {
        var config = Load();

        Assert.Equal(config.InventoryCapacities[0].Capacity, config.GetInventoryCapacity(0));
        Assert.Equal(config.InventoryCapacities[0].Capacity, config.GetInventoryCapacity(-5));
        Assert.Equal(config.InventoryCapacities[^1].Capacity, config.GetInventoryCapacity(int.MaxValue));
        Assert.True(config.TryGetNextInventoryTier(0, out var next));
        Assert.True(next!.ExpandPrice > 0);
        Assert.False(config.TryGetNextInventoryTier(config.MaximumInventoryTier, out _));
    }

    [Fact]
    public void AchievementsNeedingCombatStayInactiveUntilLaterPhases()
    {
        var config = Load();

        var battle = config.Achievements
            .Where(achievement => achievement.Category == ConfigAchievementCategory.Battle)
            .ToArray();

        Assert.NotEmpty(battle);
        // 战斗成就依赖 P2 的击杀/反击/处决事件，P1 没有事件源，进度必须保持未激活。
        Assert.All(battle, achievement => Assert.False(achievement.IsActiveInP1));
        Assert.Contains(config.Achievements, achievement => achievement.IsActiveInP1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyJsonIsRejected(string json) =>
        Assert.Equal(GameConfigLoadStatus.Missing, GameConfig.Load(json, GameConfig.RequiredSchemaVersion).Status);

    [Fact]
    public void MalformedJsonIsRejected() =>
        Assert.Equal(
            GameConfigLoadStatus.Malformed,
            GameConfig.Load("{ not json", GameConfig.RequiredSchemaVersion).Status);

    [Fact]
    public void SchemaMismatchIsRejected() =>
        Assert.Equal(GameConfigLoadStatus.SchemaMismatch, GameConfig.Load(CatalogJson, "9.9.9").Status);

    [Fact]
    public void CatalogWithoutHeroesIsRejected()
    {
        var stripped = CatalogJson.Replace("\"Heroes\":", "\"HeroesRemoved\":", StringComparison.Ordinal);

        var result = GameConfig.Load(stripped, GameConfig.RequiredSchemaVersion);

        Assert.Equal(GameConfigLoadStatus.Invalid, result.Status);
        Assert.Contains("英雄", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BootstrapManifestOmitsCapabilitiesByDefault()
    {
        // 缺省即"没有能力字段"，与只部署到 P1.1-A 的旧云端 Host 行为一致。
        var manifest = ConfigVersionManifest.Create("p0-config-1", "0.1", "0.1", "LegacyNetworkV1");

        Assert.Empty(manifest.ServerCapabilities);
    }

    [Fact]
    public void BootstrapManifestAcceptsRegisteredCapabilities()
    {
        var manifest = ConfigVersionManifest.Create(
            "p0-config-1", "0.1", "0.1", "LegacyNetworkV1", NarakaServerCapabilities.Full);

        Assert.Equal(NarakaServerCapabilities.Full.Length, manifest.ServerCapabilities.Count);
        Assert.Contains(NarakaServerCapabilities.LobbyAccountSummary, manifest.ServerCapabilities);
    }

    [Fact]
    public void BootstrapManifestRejectsUnknownCapability() =>
        Assert.Throws<InvalidOperationException>(() => ConfigVersionManifest.Create(
            "p0-config-1", "0.1", "0.1", "LegacyNetworkV1", new[] { "lobby.typo" }));

    [Fact]
    public void CompatibilityModeOnlyContainsCapabilitiesTheOldCloudActuallyDeployed()
    {
        // 旧云端只部署到 P1.1-A。兼容集合一旦多出一个能力，客户端就会向旧云端发送
        // 它不认识的协议，直接触发断线。
        Assert.Equal(new[] { NarakaServerCapabilities.LobbyAccountSummary }, NarakaServerCapabilities.CompatibilityMode);
        Assert.All(
            NarakaServerCapabilities.CompatibilityMode,
            capability => Assert.Contains(capability, NarakaServerCapabilities.Full));
    }
}
