using Naraka.Server.Application.Expeditions;
using Naraka.Server.Application.Progression;
using Naraka.Server.Domain;
using Naraka.Server.Domain.Expeditions;

namespace Naraka.Server.Application.Tests;

public sealed class ExpeditionServiceTests
{
    private const long AccountId = 42;
    private static readonly ExpeditionId Id = new("exp-fixed");
    private static readonly DateTimeOffset Now =
        new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartUsesServerGeneratedIdStableMapIdAndServerTime()
    {
        var fixture = Create();
        fixture.Repository.NextWrite = Applied(Snapshot());

        var result = await fixture.Service.StartAsync(AccountId, "start-001", default);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        var command = Assert.IsType<StartExpeditionCommand>(fixture.Repository.LastCommand);
        Assert.Equal(Id, command.ExpeditionId);
        Assert.Equal(ExpeditionMapIds.Map01, command.EntryMapId);
        Assert.Equal(Now, command.StartedAt);
    }

    [Theory]
    [InlineData(0, "request")]
    [InlineData(42, null)]
    [InlineData(42, "")]
    [InlineData(42, " ")]
    public async Task InvalidStartNeverReachesStorage(long accountId, string? requestId)
    {
        var fixture = Create();

        var result = await fixture.Service.StartAsync(accountId, requestId, default);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Null(fixture.Repository.LastCommand);
    }

    [Fact]
    public async Task RepeatedStartReplaysTheFirstResult()
    {
        var fixture = Create();
        fixture.Repository.NextWrite = new ExpeditionWriteResult(
            ExpeditionWriteOutcome.AlreadyApplied,
            Snapshot());

        var result = await fixture.Service.StartAsync(AccountId, "start-001", default);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.True(result.IsReplay);
    }

    [Fact]
    public async Task AnotherStartWhileActiveBecomesConflict()
    {
        var fixture = Create();
        fixture.Repository.NextWrite = new ExpeditionWriteResult(ExpeditionWriteOutcome.Conflict);

        var result = await fixture.Service.StartAsync(AccountId, "start-002", default);

        Assert.Equal(LobbyOperationStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task MonsterDropsCarryAServerEventIdAndCannotBeEmpty()
    {
        var fixture = Create();
        fixture.Repository.NextWrite = Applied(Snapshot());
        var assets = new[]
        {
            new ExpeditionAsset(
                ExpeditionAssetKind.Item,
                "mat_wolf_fang",
                2,
                ExpeditionAssetSource.MonsterDrop)
        };

        var applied = await fixture.Service.GrantMonsterDropsAsync(
            AccountId, Id, "drop-wolf-001", assets, default);
        var rejected = await fixture.Service.GrantMonsterDropsAsync(
            AccountId, Id, "drop-wolf-002", Array.Empty<ExpeditionAsset>(), default);

        Assert.Equal(LobbyOperationStatus.Success, applied.Status);
        Assert.Equal(LobbyOperationStatus.InvalidRequest, rejected.Status);
        var command = Assert.IsType<AppendExpeditionDropsCommand>(fixture.Repository.LastCommand);
        Assert.Equal("drop-wolf-001", command.DropEventId);
        Assert.Equal(assets, command.Assets);
        Assert.NotSame(assets, command.Assets);
    }

    [Fact]
    public async Task DeathUsesServerTimeAndReturnsTheClearedAssets()
    {
        var fixture = Create();
        var death = new ExpeditionDeathResult(
            Id,
            Now,
            new[]
            {
                new ExpeditionAsset(
                    ExpeditionAssetKind.Currency,
                    "Copper",
                    5,
                    ExpeditionAssetSource.MonsterDrop)
            },
            1);
        fixture.Repository.NextWrite = new ExpeditionWriteResult(
            ExpeditionWriteOutcome.Applied,
            Snapshot(deathCount: 1),
            death);

        var result = await fixture.Service.RecordDeathAsync(
            AccountId, Id, "death-001", default);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Same(death, result.Death);
        var command = Assert.IsType<RecordExpeditionDeathCommand>(fixture.Repository.LastCommand);
        Assert.Equal(Now, command.OccurredAt);
    }

    [Fact]
    public async Task ReturnToLobbyCreatesANormalSettlementCommand()
    {
        var fixture = Create();
        var summary = Summary(ExpeditionSettlementReason.ReturnedToLobby, "settle-001");
        fixture.Repository.NextWrite = new ExpeditionWriteResult(
            ExpeditionWriteOutcome.Applied,
            Settlement: summary);

        var result = await fixture.Service.ReturnToLobbyAsync(
            AccountId, Id, "settle-001", default);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Same(summary, result.Settlement);
        var command = Assert.IsType<SettleExpeditionCommand>(fixture.Repository.LastCommand);
        Assert.Equal(ExpeditionSettlementReason.ReturnedToLobby, command.Reason);
        Assert.Equal("settle-001", command.RequestId.Value);
    }

    [Fact]
    public async Task ConnectionLossFindsTheActiveExpeditionAndUsesServerIdempotencyKey()
    {
        var fixture = Create();
        fixture.Repository.Active = Snapshot();
        fixture.Repository.NextWrite = new ExpeditionWriteResult(
            ExpeditionWriteOutcome.AlreadyApplied,
            Settlement: Summary(ExpeditionSettlementReason.ConnectionLost, "server-connection-lost"));

        var result = await fixture.Service.SettleConnectionLossAsync(AccountId, default);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.True(result.IsReplay);
        var command = Assert.IsType<SettleExpeditionCommand>(fixture.Repository.LastCommand);
        Assert.Equal(ExpeditionSettlementReason.ConnectionLost, command.Reason);
        Assert.Equal("server-connection-lost", command.RequestId.Value);
    }

    [Fact]
    public async Task ConnectionLossWithoutAnActiveExpeditionIsASuccessfulNoOp()
    {
        var fixture = Create();

        var result = await fixture.Service.SettleConnectionLossAsync(AccountId, default);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Null(fixture.Repository.LastCommand);
    }

    [Fact]
    public async Task StorageFaultIsMappedWithoutLeakingDriverDetails()
    {
        var fixture = Create();
        fixture.Repository.Failure = new ExpeditionStorageException("secret connection detail");

        var result = await fixture.Service.StartAsync(AccountId, "start-001", default);

        Assert.Equal(LobbyOperationStatus.DatabaseUnavailable, result.Status);
    }

    [Fact]
    public async Task UnknownFailureBecomesInternalError()
    {
        var fixture = Create();
        fixture.Repository.Failure = new InvalidOperationException("injected");

        var result = await fixture.Service.StartAsync(AccountId, "start-001", default);

        Assert.Equal(LobbyOperationStatus.InternalError, result.Status);
    }

    [Fact]
    public async Task CancellationIsNeverConvertedIntoABusinessError()
    {
        var fixture = Create();
        fixture.Repository.Failure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Service.StartAsync(AccountId, "start-001", default));
    }

    private static Fixture Create()
    {
        var repository = new RecordingRepository();
        var time = new FixedTimeProvider(Now);
        var service = new ExpeditionService(repository, new FixedIdGenerator(), time);
        return new Fixture(service, repository);
    }

    private static ExpeditionWriteResult Applied(ExpeditionSnapshot snapshot) =>
        new(ExpeditionWriteOutcome.Applied, snapshot);

    private static ExpeditionSnapshot Snapshot(int deathCount = 0) =>
        new(
            Id,
            AccountId,
            ExpeditionMapIds.Map01,
            Now,
            ExpeditionStatus.Active,
            Array.Empty<ExpeditionAsset>(),
            deathCount);

    private static ExpeditionSettlementSummary Summary(
        ExpeditionSettlementReason reason,
        string requestId) =>
        new(
            Id,
            AccountId,
            new RequestId(requestId),
            reason,
            Now,
            Array.Empty<ExpeditionAsset>(),
            0);

    private sealed record Fixture(ExpeditionService Service, RecordingRepository Repository);

    private sealed class FixedIdGenerator : IExpeditionIdGenerator
    {
        public ExpeditionId NewId() => Id;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingRepository : IExpeditionRepository
    {
        public object? LastCommand { get; private set; }

        public ExpeditionSnapshot? Active { get; set; }

        public ExpeditionWriteResult NextWrite { get; set; } =
            new(ExpeditionWriteOutcome.Applied, Snapshot());

        public Exception? Failure { get; set; }

        public Task<ExpeditionSnapshot?> FindActiveAsync(long accountId, CancellationToken cancellationToken)
        {
            ThrowIfNeeded();
            return Task.FromResult(Active);
        }

        public Task<ExpeditionWriteResult> TryStartAsync(
            StartExpeditionCommand command,
            CancellationToken cancellationToken) => Record(command);

        public Task<ExpeditionWriteResult> TryAppendDropsAsync(
            AppendExpeditionDropsCommand command,
            CancellationToken cancellationToken) => Record(command);

        public Task<ExpeditionWriteResult> TryRecordDeathAsync(
            RecordExpeditionDeathCommand command,
            CancellationToken cancellationToken) => Record(command);

        public Task<ExpeditionWriteResult> TrySettleAsync(
            SettleExpeditionCommand command,
            CancellationToken cancellationToken) => Record(command);

        private Task<ExpeditionWriteResult> Record(object command)
        {
            ThrowIfNeeded();
            LastCommand = command;
            return Task.FromResult(NextWrite);
        }

        private void ThrowIfNeeded()
        {
            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }
}
