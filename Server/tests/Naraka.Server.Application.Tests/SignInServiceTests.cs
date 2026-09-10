using Naraka.Server.Application.Config;
using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;

namespace Naraka.Server.Application.Tests;

/// <summary>服务器游戏日的换算。05:00 之前仍算前一天，这条规则决定了整个签到系统的日界。</summary>
public sealed class ServerDayTests
{
    [Fact]
    public void ResetHappensAtFiveInTheMorningServerTime()
    {
        // 服务器时区为 UTC+8，因此 05:00 服务器时间等于 21:00 UTC（前一天）。
        var justBefore = new DateTime(2026, 3, 10, 20, 59, 59, DateTimeKind.Utc);
        var justAfter = new DateTime(2026, 3, 10, 21, 0, 1, DateTimeKind.Utc);

        Assert.Equal(ServerDay.FromUtc(justBefore) + 1, ServerDay.FromUtc(justAfter));
    }

    [Fact]
    public void TheSameServerDayCoversTwentyFourHours()
    {
        var start = new DateTime(2026, 3, 10, 21, 0, 1, DateTimeKind.Utc);

        Assert.Equal(ServerDay.FromUtc(start), ServerDay.FromUtc(start.AddHours(23)));
        Assert.Equal(ServerDay.FromUtc(start) + 1, ServerDay.FromUtc(start.AddHours(24)));
    }

    [Fact]
    public void DayNumbersAreMonotonicAcrossMonthAndYearBoundaries()
    {
        var december = ServerDay.FromUtc(new DateTime(2026, 12, 31, 23, 0, 0, DateTimeKind.Utc));
        var january = ServerDay.FromUtc(new DateTime(2027, 1, 1, 23, 0, 0, DateTimeKind.Utc));

        Assert.Equal(december + 1, january);
    }

    [Fact]
    public void RoundTripReturnsTheStartOfTheSameDay()
    {
        var now = new DateTime(2026, 5, 20, 10, 30, 0, DateTimeKind.Utc);
        var day = ServerDay.FromUtc(now);

        Assert.Equal(day, ServerDay.FromUtc(ServerDay.ToUtcStart(day)));
        Assert.Equal(day - 1, ServerDay.FromUtc(ServerDay.ToUtcStart(day).AddSeconds(-1)));
    }
}

/// <summary>
/// 签到业务规则：日界、补签限制、连续次数与主进度分离，以及重复领取幂等。
/// </summary>
public sealed class SignInServiceTests
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

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    }

    private sealed record Fixture(
        SignInService Service,
        RecordingSignInRepository Repository,
        InventoryServiceTests.MemoryInventoryRepository Inventory,
        FixedTime Time);

    private static Fixture Create(long makeupCards = 0)
    {
        var repository = new RecordingSignInRepository();
        var inventory = new InventoryServiceTests.MemoryInventoryRepository();
        var profiles = new InventoryServiceTests.MemoryProfiles();
        profiles.CreateAsync(AccountId).GetAwaiter().GetResult();
        var time = new FixedTime(new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc));

        if (makeupCards > 0)
        {
            inventory.TryApplyAsync(
                    AccountId,
                    new[] { new InventoryDelta(SignInService.MakeupCardItemId, makeupCards) },
                    Config.Catalog.Items.ToDictionary(
                        item => item.ItemId, item => item.StackLimit, StringComparer.Ordinal),
                    Config.InitialInventoryCapacity,
                    CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        return new Fixture(
            new SignInService(repository, inventory, profiles, Config, time), repository, inventory, time);
    }

    [Fact]
    public async Task ClaimingTodayUsesTheServerDayForTheCycleIndex()
    {
        var fixture = Create();

        var result = await fixture.Service.ClaimTodayAsync(AccountId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        var command = Assert.Single(fixture.Repository.SignInCommands);
        Assert.Equal(1, command.DayIndex);
        Assert.False(command.IsMakeup);
        Assert.Equal(1, command.ConsecutiveDaysAfter);
        Assert.Empty(command.MakeupCardCost);
    }

    [Fact]
    public async Task ClaimingTwiceOnTheSameDayIsRejected()
    {
        var fixture = Create();
        await fixture.Service.ClaimTodayAsync(AccountId, CancellationToken.None);

        var second = await fixture.Service.ClaimTodayAsync(AccountId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.AlreadyClaimed, second.Status);
        Assert.Single(fixture.Repository.SignInCommands);
    }

    [Fact]
    public async Task ConsecutiveDaysGrowOnlyWhenTheDaysAreAdjacent()
    {
        var fixture = Create();
        await fixture.Service.ClaimTodayAsync(AccountId, CancellationToken.None);

        fixture.Time.UtcNow = fixture.Time.UtcNow.AddDays(1);
        await fixture.Service.ClaimTodayAsync(AccountId, CancellationToken.None);
        Assert.Equal(2, fixture.Repository.SignInCommands[^1].ConsecutiveDaysAfter);

        // 跳过一天：连续次数从头开始，但主七日进度按周期天数继续。
        fixture.Time.UtcNow = fixture.Time.UtcNow.AddDays(2);
        await fixture.Service.ClaimTodayAsync(AccountId, CancellationToken.None);
        Assert.Equal(1, fixture.Repository.SignInCommands[^1].ConsecutiveDaysAfter);
        Assert.Equal(4, fixture.Repository.SignInCommands[^1].DayIndex);
    }

    /// <summary>
    /// 建立一个"第 1 天已签、第 2 天漏签、今天是第 4 天"的周期。
    /// 周期从首次签到那天开始，因此必须先真的签一次，而不是假设周期已经存在。
    /// </summary>
    private static async Task<Fixture> WithMissedSecondDayAsync(long makeupCards)
    {
        var fixture = Create(makeupCards);
        await fixture.Service.ClaimTodayAsync(AccountId, CancellationToken.None);
        fixture.Time.UtcNow = fixture.Time.UtcNow.AddDays(3);
        await fixture.Service.ClaimTodayAsync(AccountId, CancellationToken.None);
        return fixture;
    }

    [Fact]
    public async Task MakeUpConsumesOneCardAndDoesNotAdvanceTheStreak()
    {
        var fixture = await WithMissedSecondDayAsync(makeupCards: 2);
        var streakBefore = fixture.Repository.SignInCommands[^1].ConsecutiveDaysAfter;

        var result = await fixture.Service.MakeUpAsync(AccountId, 2, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        var command = fixture.Repository.SignInCommands[^1];
        Assert.True(command.IsMakeup);
        Assert.Equal(2, command.DayIndex);
        Assert.Equal(streakBefore, command.ConsecutiveDaysAfter);
        // 必须消耗一张补签卡。
        var cost = Assert.Single(command.MakeupCardCost);
        Assert.Equal(SignInService.MakeupCardItemId, cost.ItemId);
        Assert.Equal(-1, cost.Amount);
    }

    [Fact]
    public async Task MakeUpWithoutACardIsRejected()
    {
        var fixture = await WithMissedSecondDayAsync(makeupCards: 0);
        fixture.Repository.SignInCommands.Clear();

        var result = await fixture.Service.MakeUpAsync(AccountId, 2, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InsufficientItems, result.Status);
        Assert.Empty(fixture.Repository.SignInCommands);
    }

    [Fact]
    public async Task OnlyOneMakeUpIsAllowedPerCycle()
    {
        var fixture = await WithMissedSecondDayAsync(makeupCards: 5);
        await fixture.Service.MakeUpAsync(AccountId, 2, CancellationToken.None);
        fixture.Repository.SignInCommands.Clear();

        var second = await fixture.Service.MakeUpAsync(AccountId, 3, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.LimitReached, second.Status);
        Assert.Empty(fixture.Repository.SignInCommands);
        Assert.Equal(1, SignInService.MakeupPerCycle);
    }

    [Fact]
    public async Task MakeUpCannotClaimTodayOrTheFuture()
    {
        var fixture = await WithMissedSecondDayAsync(makeupCards: 3);
        fixture.Repository.SignInCommands.Clear();

        var today = await fixture.Service.MakeUpAsync(AccountId, 4, CancellationToken.None);
        var future = await fixture.Service.MakeUpAsync(AccountId, 6, CancellationToken.None);

        // 今天已经签过，因此是 AlreadyClaimed；未来的日子则是 NotAvailable。
        Assert.Equal(LobbyOperationStatus.AlreadyClaimed, today.Status);
        Assert.Equal(LobbyOperationStatus.NotAvailable, future.Status);
        Assert.Empty(fixture.Repository.SignInCommands);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(-1)]
    public async Task DayIndexOutsideTheCycleIsRejected(int dayIndex)
    {
        var fixture = Create(makeupCards: 1);

        var result = await fixture.Service.MakeUpAsync(AccountId, dayIndex, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Equal(7, SignInService.CycleLength);
    }

    [Fact]
    public async Task ANewCycleStartsAfterSevenDays()
    {
        var fixture = Create();
        await fixture.Service.ClaimTodayAsync(AccountId, CancellationToken.None);
        var firstCycle = fixture.Repository.SignInCommands[^1].CycleStartDay;

        fixture.Time.UtcNow = fixture.Time.UtcNow.AddDays(7);
        await fixture.Service.ClaimTodayAsync(AccountId, CancellationToken.None);

        var command = fixture.Repository.SignInCommands[^1];
        Assert.NotEqual(firstCycle, command.CycleStartDay);
        Assert.Equal(1, command.DayIndex);
    }

    [Fact]
    public async Task MilestoneRequiresTheStreakToBeReached()
    {
        var fixture = Create();
        fixture.Repository.State = new SignInState(
            ServerDay.FromUtc(fixture.Time.UtcNow), 2, ServerDay.FromUtc(fixture.Time.UtcNow),
            Array.Empty<SignInClaim>());

        var tooEarly = await fixture.Service.ClaimMilestoneAsync(AccountId, 3, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotAvailable, tooEarly.Status);

        fixture.Repository.State = fixture.Repository.State with { ConsecutiveDays = 3 };
        var reached = await fixture.Service.ClaimMilestoneAsync(AccountId, 3, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, reached.Status);
        Assert.Equal(RewardClaimKind.SignInMilestone, fixture.Repository.RewardCommands[^1].RewardKind);
    }

    [Fact]
    public async Task UnknownMilestoneIsRejected()
    {
        var fixture = Create();

        var result = await fixture.Service.ClaimMilestoneAsync(AccountId, 4, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(fixture.Repository.RewardCommands);
    }

    [Fact]
    public async Task RepeatedMilestoneClaimIsRejectedByStorage()
    {
        var fixture = Create();
        fixture.Repository.State = new SignInState(
            ServerDay.FromUtc(fixture.Time.UtcNow), 7, ServerDay.FromUtc(fixture.Time.UtcNow),
            Array.Empty<SignInClaim>());
        fixture.Repository.RewardOutcome = ClaimOutcome.AlreadyClaimed;

        var result = await fixture.Service.ClaimMilestoneAsync(AccountId, 7, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.AlreadyClaimed, result.Status);
    }

    [Fact]
    public async Task StorageFaultBecomesDatabaseUnavailable()
    {
        var fixture = Create();
        fixture.Repository.FailWithStorageError = true;

        var result = await fixture.Service.GetAsync(AccountId, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.DatabaseUnavailable, result.Status);
    }

    private sealed class RecordingSignInRepository : ISignInRepository
    {
        private readonly List<SignInClaim> _claims = new();

        public List<SignInClaimCommand> SignInCommands { get; } = new();

        public List<RewardClaimCommand> RewardCommands { get; } = new();

        public SignInState? State { get; set; }

        public ClaimOutcome RewardOutcome { get; set; } = ClaimOutcome.Applied;

        public bool FailWithStorageError { get; set; }

        public Task<SignInState?> FindStateAsync(long accountId, CancellationToken token)
        {
            Guard();
            return Task.FromResult(State);
        }

        public Task<IReadOnlyList<string>> ListClaimedRewardsAsync(
            long accountId, string rewardKind, CancellationToken token)
        {
            Guard();
            return Task.FromResult<IReadOnlyList<string>>(
                RewardCommands
                    .Where(command => command.RewardKind == rewardKind)
                    .Select(command => command.RewardKey)
                    .ToArray());
        }

        public Task<ClaimOutcome> TryClaimSignInAsync(SignInClaimCommand command, CancellationToken token)
        {
            Guard();
            SignInCommands.Add(command);
            _claims.Add(new SignInClaim(command.DayIndex, command.IsMakeup));
            State = new SignInState(
                command.CycleStartDay,
                command.ConsecutiveDaysAfter,
                command.LastClaimDayAfter,
                _claims.Where(claim => true).ToArray());
            return Task.FromResult(ClaimOutcome.Applied);
        }

        public Task<ClaimOutcome> TryClaimRewardAsync(RewardClaimCommand command, CancellationToken token)
        {
            Guard();
            if (RewardOutcome == ClaimOutcome.Applied)
            {
                RewardCommands.Add(command);
            }

            return Task.FromResult(RewardOutcome);
        }

        private void Guard()
        {
            if (FailWithStorageError)
            {
                throw new ProgressionStorageException("injected storage fault");
            }
        }
    }
}
