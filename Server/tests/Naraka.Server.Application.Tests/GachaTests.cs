using Naraka.Config;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Gacha;
using Naraka.Server.Application.Progression;

namespace Naraka.Server.Application.Tests;

/// <summary>
/// 抽奖的三条保底规则。这些测试用确定序列驱动随机源，
/// 因此断言的是规则本身，而不是"这一次恰好抽到了什么"。
/// </summary>
public sealed class GachaRollerTests
{
    private static readonly GachaPoolConfig Pool = new()
    {
        PoolId = "pool_test",
        CurrencyId = "Gold",
        SinglePrice = 100,
        TenPullPrice = 900,
        TenPullMinimumQuality = ConfigQuality.Blue,
        PityCount = 20,
        PityQuality = ConfigQuality.Red
    };

    private static readonly GachaEntryConfig[] Entries =
    {
        Entry("white", ConfigQuality.White, 900),
        Entry("blue", ConfigQuality.Blue, 90),
        Entry("red", ConfigQuality.Red, 10)
    };

    private static GachaEntryConfig Entry(string id, string quality, int weight) => new()
    {
        PoolId = "pool_test",
        RewardId = id,
        ItemId = "item_" + id,
        Amount = 1,
        Quality = quality,
        Weight = weight
    };

    /// <summary>总是抽中第一个（白色）条目的随机源。</summary>
    private sealed class AlwaysFirst : IGachaRandom
    {
        public int Next(int exclusiveUpperBound) => 0;
    }

    /// <summary>按给定序列返回，用尽后回到 0。</summary>
    private sealed class Scripted(params int[] values) : IGachaRandom
    {
        private int _index;

        public int Next(int exclusiveUpperBound)
        {
            var value = _index < values.Length ? values[_index] : 0;
            _index++;
            return value % Math.Max(exclusiveUpperBound, 1);
        }
    }

    [Fact]
    public void SinglePullAdvancesThePityCounter()
    {
        var outcome = GachaRoller.Roll(Pool, Entries, 1, 0, new AlwaysFirst());

        Assert.Single(outcome.Rewards);
        Assert.Equal(ConfigQuality.White, outcome.Rewards[0].Quality);
        Assert.Equal(1, outcome.PityCounterAfter);
    }

    [Fact]
    public void TenPullAlwaysContainsAtLeastOneBlueOrBetter()
    {
        // 随机源永远给出白色，因此十连保底必须替换掉最后一个。
        var outcome = GachaRoller.Roll(Pool, Entries, 10, 0, new AlwaysFirst());

        Assert.Equal(10, outcome.Rewards.Count);
        Assert.Contains(
            outcome.Rewards,
            reward => ConfigQuality.IndexOf(reward.Quality) >= ConfigQuality.IndexOf(ConfigQuality.Blue));
    }

    [Fact]
    public void TwentiethPullIsForcedToTheRedPity()
    {
        // 已经连续 19 抽未出红：第 20 抽必须是红色。
        var outcome = GachaRoller.Roll(Pool, Entries, 1, 19, new AlwaysFirst());

        Assert.Equal(ConfigQuality.Red, outcome.Rewards[0].Quality);
        Assert.Equal(0, outcome.PityCounterAfter);
    }

    [Fact]
    public void PityResetsWhenRedArrivesEarly()
    {
        // 随机源直接命中红色条目（权重区间的最后一段）。
        var outcome = GachaRoller.Roll(Pool, Entries, 1, 5, new Scripted(995));

        Assert.Equal(ConfigQuality.Red, outcome.Rewards[0].Quality);
        Assert.Equal(0, outcome.PityCounterAfter);
    }

    [Fact]
    public void PityNeverExceedsTheConfiguredCountAcrossAFullCycle()
    {
        var pity = 0;
        var sawRed = false;

        for (var pull = 0; pull < Pool.PityCount; pull++)
        {
            var outcome = GachaRoller.Roll(Pool, Entries, 1, pity, new AlwaysFirst());
            pity = outcome.PityCounterAfter;
            sawRed |= outcome.Rewards[0].Quality == ConfigQuality.Red;
            Assert.True(pity < Pool.PityCount, "保底计数不能达到或超过 PityCount。");
        }

        Assert.True(sawRed, "20 抽之内必须出现一次红色。");
    }

    [Fact]
    public void TenPullDoesNotReplaceWhenTheThresholdIsAlreadyMet()
    {
        // 第一抽就命中红色，之后全白：十连保底不需要替换，最后一个仍是白色。
        var outcome = GachaRoller.Roll(Pool, Entries, 10, 0, new Scripted(995));

        Assert.Equal(ConfigQuality.Red, outcome.Rewards[0].Quality);
        Assert.Equal(ConfigQuality.White, outcome.Rewards[^1].Quality);
    }

    [Fact]
    public void EmptyPoolIsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            GachaRoller.Roll(Pool, Array.Empty<GachaEntryConfig>(), 1, 0, new AlwaysFirst()));

    [Fact]
    public void NonPositivePullCountIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GachaRoller.Roll(Pool, Entries, 0, 0, new AlwaysFirst()));

    [Fact]
    public void PoolWithoutTheRequiredPityQualityFailsLoudly()
    {
        var withoutRed = new[] { Entry("white", ConfigQuality.White, 100) };

        // 配置编译器会拦住这种奖池；真的走到这里必须显式失败，而不是悄悄发一个白色当保底。
        Assert.Throws<InvalidOperationException>(() =>
            GachaRoller.Roll(Pool, withoutRed, 1, 19, new AlwaysFirst()));
    }
}

/// <summary>抽奖服务：价格、订单幂等、未展示结果恢复与请求校验。</summary>
public sealed class GachaServiceTests
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

    private static (GachaService Service, RecordingGachaRepository Repository) Create()
    {
        var repository = new RecordingGachaRepository();
        var profiles = new InventoryServiceTests.MemoryProfiles();
        profiles.CreateAsync(AccountId).GetAwaiter().GetResult();
        return (new GachaService(repository, profiles, Config, new CryptoGachaRandom()), repository);
    }

    [Fact]
    public async Task SinglePullChargesTheConfiguredSinglePrice()
    {
        var (service, repository) = Create();
        Config.TryGetGachaPool(service.DefaultPoolId, out var pool);

        var result = await service.PullAsync(AccountId, null, 1, "order-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        var command = Assert.Single(repository.Commands);
        Assert.Equal(pool!.SinglePrice, command.Price);
        Assert.Equal("Gold", command.CurrencyId);
        Assert.Single(command.Rewards);
    }

    [Fact]
    public async Task TenPullChargesTheConfiguredTenPullPrice()
    {
        var (service, repository) = Create();
        Config.TryGetGachaPool(service.DefaultPoolId, out var pool);

        await service.PullAsync(AccountId, null, 10, "order-1", CancellationToken.None);

        var command = Assert.Single(repository.Commands);
        Assert.Equal(pool!.TenPullPrice, command.Price);
        Assert.Equal(10, command.Rewards.Count);
    }

    [Fact]
    public async Task GachaSpendsGoldNotCopper()
    {
        var (service, _) = Create();

        Assert.True(Config.TryGetGachaPool(service.DefaultPoolId, out var pool));
        Assert.Equal("Gold", pool!.CurrencyId);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(11)]
    public async Task OnlySingleAndTenPullAreAccepted(int pullCount)
    {
        var (service, repository) = Create();

        var result = await service.PullAsync(AccountId, null, pullCount, "order-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(repository.Commands);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task OrderIdIsRequired(string? orderId)
    {
        var (service, repository) = Create();

        var result = await service.PullAsync(AccountId, null, 1, orderId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(repository.Commands);
    }

    [Fact]
    public async Task UnknownPoolIsRejected()
    {
        var (service, repository) = Create();

        var result = await service.PullAsync(AccountId, "pool_does_not_exist", 1, "order-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(repository.Commands);
    }

    [Theory]
    [InlineData(GachaPullOutcome.InsufficientCurrency, LobbyOperationStatus.InsufficientCurrency)]
    [InlineData(GachaPullOutcome.InventoryFull, LobbyOperationStatus.InventoryFull)]
    [InlineData(GachaPullOutcome.AccountMissing, LobbyOperationStatus.NotFound)]
    public async Task EveryFailureMapsToItsOwnStableStatus(
        GachaPullOutcome outcome,
        LobbyOperationStatus expected)
    {
        var (service, repository) = Create();
        repository.Outcome = outcome;

        var result = await service.PullAsync(AccountId, null, 1, "order-1", CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task RepeatedOrderIdReplaysTheFirstResult()
    {
        var (service, repository) = Create();
        repository.Outcome = GachaPullOutcome.AlreadyApplied;

        var result = await service.PullAsync(AccountId, null, 1, "order-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
    }

    [Fact]
    public async Task UnshownOrdersAreReturnedSoAnInterruptedAnimationCanBeRecovered()
    {
        var (service, repository) = Create();
        repository.Unshown.Add(new GachaOrder(
            "order-1", service.DefaultPoolId, 1, false,
            new[] { new GachaRolledReward("r", "mat_ore_basic", 1, ConfigQuality.White) }));

        var result = await service.GetAsync(AccountId, null, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        var order = Assert.Single(result.View!.UnshownOrders);
        Assert.Equal("order-1", order.OrderId);
        Assert.False(order.IsShown);
    }

    [Fact]
    public async Task AcknowledgingAnOrderRemovesItFromTheUnshownList()
    {
        var (service, repository) = Create();
        repository.Unshown.Add(new GachaOrder("order-1", service.DefaultPoolId, 1, false,
            Array.Empty<GachaRolledReward>()));

        var result = await service.AcknowledgeAsync(AccountId, null, "order-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Empty(result.View!.UnshownOrders);
        Assert.Contains("order-1", repository.Acknowledged);
    }

    [Fact]
    public async Task AcknowledgingTwiceIsSafe()
    {
        var (service, _) = Create();

        var first = await service.AcknowledgeAsync(AccountId, null, "order-1", CancellationToken.None);
        var second = await service.AcknowledgeAsync(AccountId, null, "order-1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, first.Status);
        Assert.Equal(LobbyOperationStatus.Success, second.Status);
    }

    [Fact]
    public async Task StorageFaultBecomesDatabaseUnavailable()
    {
        var (service, repository) = Create();
        repository.FailWithStorageError = true;

        var result = await service.GetAsync(AccountId, null, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.DatabaseUnavailable, result.Status);
    }

    [Fact]
    public void MaximumPullCountMatchesTheTenPullContract() =>
        Assert.Equal(10, GachaService.MaximumPullCount);

    private sealed class RecordingGachaRepository : IGachaRepository
    {
        public List<GachaPullCommand> Commands { get; } = new();

        public List<GachaOrder> Unshown { get; } = new();

        public List<string> Acknowledged { get; } = new();

        public GachaPullOutcome Outcome { get; set; } = GachaPullOutcome.Applied;

        public bool FailWithStorageError { get; set; }

        public Task<GachaAccountState?> FindStateAsync(long accountId, string poolId, CancellationToken token)
        {
            Guard();
            return Task.FromResult<GachaAccountState?>(new GachaAccountState(poolId, 0, 0));
        }

        public Task<IReadOnlyList<GachaOrder>> ListUnshownOrdersAsync(long accountId, CancellationToken token)
        {
            Guard();
            return Task.FromResult<IReadOnlyList<GachaOrder>>(Unshown.ToArray());
        }

        public Task<GachaPullResult> TryPullAsync(GachaPullCommand command, CancellationToken token)
        {
            Guard();
            Commands.Add(command);
            return Task.FromResult(new GachaPullResult(
                Outcome,
                Outcome is GachaPullOutcome.Applied or GachaPullOutcome.AlreadyApplied
                    ? new GachaOrder(command.OrderId, command.PoolId, command.PullCount, false, command.Rewards)
                    : null));
        }

        public Task<bool> MarkShownAsync(long accountId, string orderId, CancellationToken token)
        {
            Guard();
            Acknowledged.Add(orderId);
            Unshown.RemoveAll(order => order.OrderId == orderId);
            return Task.FromResult(true);
        }

        private void Guard()
        {
            if (FailWithStorageError)
            {
                throw new GachaStorageException("injected storage fault");
            }
        }
    }
}
