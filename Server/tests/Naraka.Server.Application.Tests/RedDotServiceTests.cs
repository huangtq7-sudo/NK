using Naraka.Server.Application.Progression;

namespace Naraka.Server.Application.Tests;

/// <summary>
/// 红点持久化。
///
/// 服务端只保存版本对，不判断红点该不该亮：这里的测试因此全部围绕"能不能正确保存与读回
/// SeenVersion"，以及"迟到的旧请求不会把已经看过的节点退回去"。
/// </summary>
public sealed class RedDotServiceTests
{
    private const long Account = 42;
    private const string Path = "Lobby/SignIn/DailyClaim";

    [Fact]
    public async Task UnauthenticatedAccountIsRejected()
    {
        var service = new RedDotService(new FakeRedDotRepository());

        var result = await service.GetAsync(0, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task SeenVersionIsPersistedAndReadBack()
    {
        var repository = new FakeRedDotRepository();
        var service = new RedDotService(repository);

        var saved = await service.MarkSeenAsync(Account, Path, 7, CancellationToken.None);
        var loaded = await service.GetAsync(Account, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, saved.Status);
        var row = Assert.Single(loaded.Rows);
        Assert.Equal(Path, row.Path);
        Assert.Equal(7, row.SeenVersion);
    }

    [Fact]
    public async Task ALateArrivingOlderRequestNeverRelightsASeenNode()
    {
        var repository = new FakeRedDotRepository();
        var service = new RedDotService(repository);

        await service.MarkSeenAsync(Account, Path, 9, CancellationToken.None);
        await service.MarkSeenAsync(Account, Path, 4, CancellationToken.None);

        var loaded = await service.GetAsync(Account, CancellationToken.None);
        Assert.Equal(9, Assert.Single(loaded.Rows).SeenVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task EmptyPathsAreRejected(string? path)
    {
        var service = new RedDotService(new FakeRedDotRepository());

        var result = await service.MarkSeenAsync(Account, path, 1, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task PathsLongerThanTheColumnAreRejectedRatherThanTruncated()
    {
        var service = new RedDotService(new FakeRedDotRepository());
        var tooLong = new string('a', RedDotService.MaxPathLength + 1);

        var result = await service.MarkSeenAsync(Account, tooLong, 1, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task NegativeVersionsAreRejected()
    {
        var service = new RedDotService(new FakeRedDotRepository());

        var result = await service.MarkSeenAsync(Account, Path, -1, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task StorageFaultsSurfaceAsDatabaseUnavailableRatherThanCrashing()
    {
        var service = new RedDotService(new FaultingRedDotRepository());

        var read = await service.GetAsync(Account, CancellationToken.None);
        var write = await service.MarkSeenAsync(Account, Path, 1, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.DatabaseUnavailable, read.Status);
        Assert.Equal(LobbyOperationStatus.DatabaseUnavailable, write.Status);
    }

    [Fact]
    public async Task EachAccountKeepsItsOwnSeenVersions()
    {
        var repository = new FakeRedDotRepository();
        var service = new RedDotService(repository);

        await service.MarkSeenAsync(Account, Path, 5, CancellationToken.None);
        await service.MarkSeenAsync(Account + 1, Path, 2, CancellationToken.None);

        var first = await service.GetAsync(Account, CancellationToken.None);
        var second = await service.GetAsync(Account + 1, CancellationToken.None);

        Assert.Equal(5, Assert.Single(first.Rows).SeenVersion);
        Assert.Equal(2, Assert.Single(second.Rows).SeenVersion);
    }

    private sealed class FakeRedDotRepository : IRedDotRepository
    {
        private readonly Dictionary<(long, string), RedDotStateRow> _rows = new();

        public Task<IReadOnlyList<RedDotStateRow>> ListAsync(
            long accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RedDotStateRow>>(_rows
                .Where(pair => pair.Key.Item1 == accountId)
                .Select(pair => pair.Value)
                .OrderBy(row => row.Path, StringComparer.Ordinal)
                .ToArray());

        public Task SaveSeenAsync(
            long accountId,
            string path,
            long seenVersion,
            CancellationToken cancellationToken)
        {
            var key = (accountId, path);
            if (_rows.TryGetValue(key, out var existing) && existing.SeenVersion >= seenVersion)
            {
                return Task.CompletedTask;
            }

            _rows[key] = new RedDotStateRow(path, seenVersion, seenVersion);
            return Task.CompletedTask;
        }
    }

    private sealed class FaultingRedDotRepository : IRedDotRepository
    {
        public Task<IReadOnlyList<RedDotStateRow>> ListAsync(
            long accountId,
            CancellationToken cancellationToken) =>
            throw new ProgressionStorageException("boom");

        public Task SaveSeenAsync(
            long accountId,
            string path,
            long seenVersion,
            CancellationToken cancellationToken) =>
            throw new ProgressionStorageException("boom");
    }
}
