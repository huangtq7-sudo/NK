using Naraka.Server.Application.Lobby;

namespace Naraka.Server.Application.Tests;

public sealed class LobbyAccountServiceTests
{
    [Fact]
    public async Task SuccessReturnsTheAuthenticatedAccountSummary()
    {
        var repository = new StubRepository { Record = new LobbyAccountProgressionRecord(12, 900, 80, 7) };
        var service = new LobbyAccountService(repository);

        var result = await service.GetSummaryAsync(42, CancellationToken.None);

        Assert.Equal(LobbyAccountSummaryStatus.Success, result.Status);
        Assert.NotNull(result.Summary);
        Assert.Equal(12, result.Summary!.AccountLevel);
        Assert.Equal(900, result.Summary.Copper);
        Assert.Equal(80, result.Summary.Silk);
        Assert.Equal(7, result.Summary.Gold);
        Assert.Equal(42, repository.LastAccountId);
    }

    [Fact]
    public async Task NewAccountDefaultsAreAcceptedAsAValidSummary()
    {
        var repository = new StubRepository { Record = new LobbyAccountProgressionRecord(1, 0, 0, 0) };
        var service = new LobbyAccountService(repository);

        var result = await service.GetSummaryAsync(1, CancellationToken.None);

        Assert.Equal(LobbyAccountSummaryStatus.Success, result.Status);
        Assert.Equal(1, result.Summary!.AccountLevel);
        Assert.Equal(0, result.Summary.Copper);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveAccountIdIsRejectedWithoutTouchingStorage(long accountId)
    {
        var repository = new StubRepository { Record = new LobbyAccountProgressionRecord(1, 0, 0, 0) };
        var service = new LobbyAccountService(repository);

        var result = await service.GetSummaryAsync(accountId, CancellationToken.None);

        Assert.Equal(LobbyAccountSummaryStatus.InvalidRequest, result.Status);
        Assert.Null(result.Summary);
        Assert.Null(repository.LastAccountId);
    }

    [Fact]
    public async Task MissingProgressionRowIsReportedAsNotFound()
    {
        var service = new LobbyAccountService(new StubRepository { Record = null });

        var result = await service.GetSummaryAsync(42, CancellationToken.None);

        Assert.Equal(LobbyAccountSummaryStatus.NotFound, result.Status);
        Assert.Null(result.Summary);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, -1, 0, 0)]
    [InlineData(1, 0, -5, 0)]
    [InlineData(1, 0, 0, -9)]
    public async Task InvalidStoredDataNeverReachesTheCaller(int level, long copper, long silk, long gold)
    {
        var repository = new StubRepository
        {
            Record = new LobbyAccountProgressionRecord(level, copper, silk, gold)
        };
        var service = new LobbyAccountService(repository);

        var result = await service.GetSummaryAsync(42, CancellationToken.None);

        Assert.Equal(LobbyAccountSummaryStatus.InternalError, result.Status);
        Assert.Null(result.Summary);
    }

    [Fact]
    public async Task StorageFaultsMapToDatabaseUnavailable()
    {
        var service = new LobbyAccountService(new ThrowingRepository(
            new LobbyAccountStorageException("offline")));

        var result = await service.GetSummaryAsync(42, CancellationToken.None);

        Assert.Equal(LobbyAccountSummaryStatus.DatabaseUnavailable, result.Status);
    }

    [Fact]
    public async Task UnexpectedFaultsMapToInternalError()
    {
        var service = new LobbyAccountService(new ThrowingRepository(new InvalidOperationException("boom")));

        var result = await service.GetSummaryAsync(42, CancellationToken.None);

        Assert.Equal(LobbyAccountSummaryStatus.InternalError, result.Status);
    }

    [Fact]
    public async Task CancellationPropagatesInsteadOfBecomingAnErrorCode()
    {
        var service = new LobbyAccountService(new ThrowingRepository(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetSummaryAsync(42, CancellationToken.None));
    }

    [Fact]
    public void SummaryRejectsInvalidValuesAtConstruction()
    {
        Assert.False(LobbyAccountSummary.TryCreate(0, 0, 0, 0, out _));
        Assert.False(LobbyAccountSummary.TryCreate(1, -1, 0, 0, out _));
        Assert.True(LobbyAccountSummary.TryCreate(1, 0, 0, 0, out var summary));
        Assert.NotNull(summary);
    }

    private sealed class StubRepository : ILobbyAccountRepository
    {
        public LobbyAccountProgressionRecord? Record { get; set; }

        public long? LastAccountId { get; private set; }

        public Task<LobbyAccountProgressionRecord?> FindProgressionAsync(
            long accountId,
            CancellationToken cancellationToken)
        {
            LastAccountId = accountId;
            return Task.FromResult(Record);
        }
    }

    private sealed class ThrowingRepository(Exception exception) : ILobbyAccountRepository
    {
        public Task<LobbyAccountProgressionRecord?> FindProgressionAsync(
            long accountId,
            CancellationToken cancellationToken) => throw exception;
    }
}
