using Naraka.Server.Application.Config;
using Naraka.Server.Application.Forge;
using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;

namespace Naraka.Server.Application.Tests;

/// <summary>
/// 锻造业务规则。
///
/// 玩法基线规定"材料足够时强化必定成功"，因此这里最重要的断言是：
/// 服务端没有任何随机判定，失败只可能来自材料不足、货币不足、已达最高等级或界面等级过期。
/// </summary>
public sealed class ForgeServiceTests
{
    private const long AccountId = 42;

    private static readonly GameConfig Config = LoadConfig();

    private static GameConfig LoadConfig()
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Config", "naraka-config.json"),
            System.Text.Encoding.UTF8);
        return GameConfig.Load(json, GameConfig.RequiredSchemaVersion).Config
               ?? throw new InvalidOperationException("测试无法加载生成配置。");
    }

    private static (ForgeService Service, RecordingWeaponRepository Weapons) Create()
    {
        var weapons = new RecordingWeaponRepository();
        var profiles = new InventoryServiceTests.MemoryProfiles();
        profiles.CreateAsync(AccountId).GetAwaiter().GetResult();
        return (
            new ForgeService(weapons, new InventoryServiceTests.MemoryInventoryRepository(), profiles, Config),
            weapons);
    }

    [Fact]
    public async Task ReadingTheForgeProvisionsEveryConfiguredWeaponAtLevelOne()
    {
        var (service, weapons) = Create();

        var result = await service.GetAsync(AccountId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Equal(Config.WeaponsInDisplayOrder.Count, result.View!.Weapons.Count);
        Assert.All(result.View.Weapons, weapon => Assert.Equal(1, weapon.Level));
        // 武器不进入仓库，因此这里没有任何数量概念。
        Assert.All(result.View.Weapons, weapon => Assert.True(Config.TryGetWeapon(weapon.WeaponId, out _)));
        Assert.Equal(1, weapons.EnsureCallCount);
    }

    [Fact]
    public async Task ProvisioningNeverResetsAnExistingLevel()
    {
        var (service, weapons) = Create();
        await service.GetAsync(AccountId, CancellationToken.None);
        weapons.SetLevel(AccountId, Config.DefaultWeaponId, 9);

        var result = await service.GetAsync(AccountId, CancellationToken.None);

        Assert.Equal(
            9,
            result.View!.Weapons.Single(weapon => weapon.WeaponId == Config.DefaultWeaponId).Level);
    }

    [Fact]
    public async Task UpgradeSendsTheConfiguredRecipeToTheRepository()
    {
        var (service, weapons) = Create();
        await service.GetAsync(AccountId, CancellationToken.None);
        Config.TryGetForgeRecipe(Config.DefaultWeaponId, 1, out var recipe);

        var result = await service.UpgradeAsync(
            AccountId, Config.DefaultWeaponId, 1, "forge-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        var command = Assert.Single(weapons.Commands);
        Assert.Equal(recipe!.CurrencyId, command.CurrencyId);
        Assert.Equal(recipe.CurrencyAmount, command.CurrencyAmount);
        Assert.Equal(2, command.ToLevel);
        Assert.Equal("forge-1", command.RequestId);
        // 材料消耗必须是负数：正数会变成一次发放。
        Assert.All(command.MaterialCosts, cost => Assert.True(cost.Amount < 0));
        Assert.Contains(command.MaterialCosts, cost => cost.ItemId == recipe.Material1ItemId);
    }

    [Fact]
    public async Task UpgradeIsRefusedWhenTheClientLevelIsStale()
    {
        var (service, weapons) = Create();
        await service.GetAsync(AccountId, CancellationToken.None);
        weapons.SetLevel(AccountId, Config.DefaultWeaponId, 4);

        var result = await service.UpgradeAsync(
            AccountId, Config.DefaultWeaponId, 1, "forge-1", CancellationToken.None);

        // 界面过期是 Conflict，不是"材料不足"，玩家因此不会误以为自己缺材料。
        Assert.Equal(LobbyOperationStatus.Conflict, result.Status);
        Assert.Empty(weapons.Commands);
    }

    [Fact]
    public async Task UpgradeStopsAtTheMaximumLevel()
    {
        var (service, weapons) = Create();
        await service.GetAsync(AccountId, CancellationToken.None);
        Config.TryGetWeapon(Config.DefaultWeaponId, out var weapon);
        weapons.SetLevel(AccountId, Config.DefaultWeaponId, weapon!.MaxLevel);

        var result = await service.UpgradeAsync(
            AccountId, Config.DefaultWeaponId, weapon.MaxLevel, "forge-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotAvailable, result.Status);
        Assert.Empty(weapons.Commands);
    }

    [Theory]
    [InlineData(ForgeUpgradeOutcome.InsufficientCurrency, LobbyOperationStatus.InsufficientCurrency)]
    [InlineData(ForgeUpgradeOutcome.InsufficientItems, LobbyOperationStatus.InsufficientItems)]
    [InlineData(ForgeUpgradeOutcome.LevelMismatch, LobbyOperationStatus.Conflict)]
    [InlineData(ForgeUpgradeOutcome.AccountMissing, LobbyOperationStatus.NotFound)]
    public async Task EveryFailureMapsToItsOwnStableStatus(
        ForgeUpgradeOutcome outcome,
        LobbyOperationStatus expected)
    {
        var (service, weapons) = Create();
        await service.GetAsync(AccountId, CancellationToken.None);
        weapons.Outcome = outcome;

        var result = await service.UpgradeAsync(
            AccountId, Config.DefaultWeaponId, 1, "forge-1", CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task RepeatedRequestIdReplaysInsteadOfConsumingAgain()
    {
        var (service, weapons) = Create();
        await service.GetAsync(AccountId, CancellationToken.None);
        weapons.Outcome = ForgeUpgradeOutcome.AlreadyApplied;

        var result = await service.UpgradeAsync(
            AccountId, Config.DefaultWeaponId, 1, "forge-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
    }

    [Theory]
    [InlineData("weapon_does_not_exist", "forge-1")]
    [InlineData("weapon_longsword", "")]
    [InlineData("weapon_longsword", null)]
    [InlineData(null, "forge-1")]
    public async Task InvalidRequestsNeverReachTheRepository(string? weaponId, string? requestId)
    {
        var (service, weapons) = Create();

        var result = await service.UpgradeAsync(AccountId, weaponId, 1, requestId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(weapons.Commands);
    }

    [Fact]
    public async Task EveryLevelBelowTheMaximumHasARecipeSoThereIsNoDeadEnd()
    {
        var (service, weapons) = Create();
        await service.GetAsync(AccountId, CancellationToken.None);

        foreach (var weapon in Config.WeaponsInDisplayOrder)
        {
            for (var level = 1; level < weapon.MaxLevel; level++)
            {
                weapons.SetLevel(AccountId, weapon.WeaponId, level);
                weapons.Commands.Clear();

                var result = await service.UpgradeAsync(
                    AccountId, weapon.WeaponId, level, "forge-" + level, CancellationToken.None);

                Assert.Equal(LobbyOperationStatus.Success, result.Status);
                Assert.Equal(level + 1, weapons.Commands.Single().ToLevel);
            }
        }
    }

    [Fact]
    public async Task StorageFaultBecomesDatabaseUnavailable()
    {
        var (service, weapons) = Create();
        weapons.FailWithStorageError = true;

        var result = await service.GetAsync(AccountId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.DatabaseUnavailable, result.Status);
    }

    private sealed class RecordingWeaponRepository : IWeaponRepository
    {
        private readonly Dictionary<long, Dictionary<string, AccountWeapon>> _weapons = new();

        public List<ForgeUpgradeCommand> Commands { get; } = new();

        public int EnsureCallCount { get; private set; }

        public ForgeUpgradeOutcome Outcome { get; set; } = ForgeUpgradeOutcome.Applied;

        public bool FailWithStorageError { get; set; }

        public void SetLevel(long accountId, string weaponId, int level) =>
            _weapons[accountId][weaponId] = _weapons[accountId][weaponId] with { Level = level };

        public Task<IReadOnlyList<AccountWeapon>> ListAsync(long accountId, CancellationToken token)
        {
            Guard();
            return Task.FromResult<IReadOnlyList<AccountWeapon>>(
                _weapons.TryGetValue(accountId, out var owned)
                    ? owned.Values.OrderBy(weapon => weapon.WeaponId, StringComparer.Ordinal).ToArray()
                    : Array.Empty<AccountWeapon>());
        }

        public Task EnsureWeaponsAsync(long accountId, IReadOnlyList<string> weaponIds, CancellationToken token)
        {
            Guard();
            EnsureCallCount++;
            if (!_weapons.TryGetValue(accountId, out var owned))
            {
                owned = new Dictionary<string, AccountWeapon>(StringComparer.Ordinal);
                _weapons[accountId] = owned;
            }

            foreach (var weaponId in weaponIds)
            {
                if (!owned.ContainsKey(weaponId))
                {
                    owned[weaponId] = new AccountWeapon(weaponId, 1, 0, 0);
                }
            }

            return Task.CompletedTask;
        }

        public Task<ForgeUpgradeResult> TryUpgradeAsync(ForgeUpgradeCommand command, CancellationToken token)
        {
            Guard();
            Commands.Add(command);
            if (Outcome == ForgeUpgradeOutcome.Applied)
            {
                _weapons[command.AccountId][command.WeaponId] =
                    _weapons[command.AccountId][command.WeaponId] with { Level = command.ToLevel };
            }

            return Task.FromResult(new ForgeUpgradeResult(Outcome, command.ToLevel));
        }

        private void Guard()
        {
            if (FailWithStorageError)
            {
                throw new ForgeStorageException("injected storage fault");
            }
        }
    }
}
