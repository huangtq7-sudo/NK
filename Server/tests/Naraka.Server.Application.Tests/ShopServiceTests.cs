using Naraka.Server.Application.Config;
using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Shop;

namespace Naraka.Server.Application.Tests;

/// <summary>
/// 商店业务规则：配置价格、限购、余额不足、库存溢出与 RequestId 幂等。
/// 使用仓库中真实生成的配置，因此这里断言的价格与限购就是线上会用的那一套。
/// </summary>
public sealed class ShopServiceTests
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

    private static (ShopService Service, RecordingShopRepository Repository) Create()
    {
        var repository = new RecordingShopRepository();
        var profiles = new InventoryServiceTests.MemoryProfiles();
        profiles.CreateAsync(AccountId).GetAwaiter().GetResult();
        return (new ShopService(repository, profiles, Config), repository);
    }

    [Fact]
    public async Task PurchaseSendsTheConfiguredPriceAndLimitToTheRepository()
    {
        var (service, repository) = Create();
        Config.TryGetShopProduct("shop_blood_pack", out var product);
        Config.TryGetItem(product!.ItemId, out var item);

        var result = await service.PurchaseAsync(AccountId, "shop_blood_pack", 3, "order-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        var command = Assert.Single(repository.Commands);
        // 总价由服务端按配置计算，请求里根本没有价格字段。
        Assert.Equal(product.UnitPrice * 3, command.TotalPrice);
        Assert.Equal(product.CurrencyId, command.CurrencyId);
        Assert.Equal(product.ItemId, command.ItemId);
        Assert.Equal(product.PurchaseLimit, command.PurchaseLimit);
        Assert.Equal(item!.StackLimit, command.StackLimit);
        Assert.Equal(Config.InitialInventoryCapacity, command.Capacity);
        Assert.Equal("order-1", command.RequestId);
    }

    [Fact]
    public async Task RepeatedOrderIdReplaysInsteadOfChargingAgain()
    {
        var (service, repository) = Create();
        repository.Outcome = ShopPurchaseOutcome.AlreadyApplied;

        var result = await service.PurchaseAsync(AccountId, "shop_blood_pack", 1, "order-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
    }

    [Theory]
    [InlineData(ShopPurchaseOutcome.InsufficientCurrency, LobbyOperationStatus.InsufficientCurrency)]
    [InlineData(ShopPurchaseOutcome.LimitReached, LobbyOperationStatus.LimitReached)]
    [InlineData(ShopPurchaseOutcome.InventoryFull, LobbyOperationStatus.InventoryFull)]
    [InlineData(ShopPurchaseOutcome.AccountMissing, LobbyOperationStatus.NotFound)]
    public async Task EveryFailureMapsToItsOwnStableStatus(
        ShopPurchaseOutcome outcome,
        LobbyOperationStatus expected)
    {
        var (service, repository) = Create();
        repository.Outcome = outcome;

        var result = await service.PurchaseAsync(AccountId, "shop_blood_pack", 1, "order-1", CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData("shop_does_not_exist", 1, "order-1")]
    [InlineData("shop_blood_pack", 0, "order-1")]
    [InlineData("shop_blood_pack", -1, "order-1")]
    [InlineData("shop_blood_pack", 1000, "order-1")]
    [InlineData("shop_blood_pack", 1, "")]
    [InlineData("shop_blood_pack", 1, null)]
    [InlineData(null, 1, "order-1")]
    public async Task InvalidRequestsNeverReachTheRepository(string? productId, int quantity, string? requestId)
    {
        var (service, repository) = Create();

        var result = await service.PurchaseAsync(AccountId, productId, quantity, requestId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(repository.Commands);
    }

    [Fact]
    public async Task QuantityIsCappedSoTheTotalPriceCannotOverflow()
    {
        var (service, _) = Create();

        var result = await service.PurchaseAsync(
            AccountId, "shop_blood_pack", int.MaxValue, "order-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Equal(999, ShopService.MaximumQuantityPerPurchase);
    }

    [Fact]
    public async Task WeaponsAreNeverSoldInTheShop()
    {
        var (service, _) = Create();

        // 每种兵器每账号唯一一把，因此商店里不能出现任何武器商品。
        foreach (var product in Config.ShopProductsInDisplayOrder)
        {
            Assert.True(Config.TryGetItem(product.ItemId, out _));
            Assert.False(Config.TryGetWeapon(product.ItemId, out _));
        }

        var result = await service.PurchaseAsync(
            AccountId, Config.DefaultWeaponId, 1, "order-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task StorageFaultBecomesDatabaseUnavailable()
    {
        var (service, repository) = Create();
        repository.FailWithStorageError = true;

        var result = await service.PurchaseAsync(AccountId, "shop_blood_pack", 1, "order-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.DatabaseUnavailable, result.Status);
    }

    [Fact]
    public async Task GetReturnsPurchaseCountsAndBalances()
    {
        var (service, repository) = Create();
        repository.Purchases.Add(new ShopPurchaseCount("shop_blood_pack", 7));

        var result = await service.GetAsync(AccountId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Equal(7, result.View!.Purchases.Single().PurchasedTotal);
        Assert.NotNull(result.View.Balances);
    }

    private sealed class RecordingShopRepository : IShopRepository
    {
        public List<ShopPurchaseCommand> Commands { get; } = new();

        public List<ShopPurchaseCount> Purchases { get; } = new();

        public ShopPurchaseOutcome Outcome { get; set; } = ShopPurchaseOutcome.Applied;

        public bool FailWithStorageError { get; set; }

        public Task<IReadOnlyList<ShopPurchaseCount>> ListPurchasesAsync(
            long accountId,
            CancellationToken cancellationToken)
        {
            Guard();
            return Task.FromResult<IReadOnlyList<ShopPurchaseCount>>(Purchases);
        }

        public Task<ShopPurchaseResult> TryPurchaseAsync(
            ShopPurchaseCommand command,
            CancellationToken cancellationToken)
        {
            Guard();
            Commands.Add(command);
            return Task.FromResult(new ShopPurchaseResult(Outcome, null, command.Quantity));
        }

        private void Guard()
        {
            if (FailWithStorageError)
            {
                throw new ShopStorageException("injected storage fault");
            }
        }
    }
}
