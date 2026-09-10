using Naraka.Server.Application.Progression;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Progression;

[SugarTable("account_reddot")]
internal sealed class AccountRedDotRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "node_path", IsPrimaryKey = true, Length = 160)]
    public string NodePath { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "version")]
    public long Version { get; set; }

    [SugarColumn(ColumnName = "seen_version")]
    public long SeenVersion { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// 红点版本的持久化。
///
/// 这里只保存版本对，不保存"亮不亮"：亮不亮是 Version 与 SeenVersion 比较的结果，
/// 存布尔值会在业务产生新内容时失效。
/// </summary>
public sealed class SqlSugarRedDotRepository(SqlSugarClientFactory factory) : IRedDotRepository
{
    public async Task<IReadOnlyList<RedDotStateRow>> ListAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<RedDotStateRow>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var rows = await database.Queryable<AccountRedDotRow>()
                .Where(row => row.AccountId == accountId)
                .ToListAsync();

            return rows
                .OrderBy(row => row.NodePath, StringComparer.Ordinal)
                .Select(row => new RedDotStateRow(row.NodePath, row.Version, row.SeenVersion))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Failed to read red dot state.", exception);
        }
    }

    public async Task SaveSeenAsync(
        long accountId,
        string path,
        long seenVersion,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || string.IsNullOrEmpty(path))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var now = DateTime.UtcNow;
            var existing = await database.Queryable<AccountRedDotRow>()
                .Where(row => row.AccountId == accountId && row.NodePath == path)
                .SingleAsync();

            if (existing is null)
            {
                await database.Insertable(new AccountRedDotRow
                {
                    AccountId = accountId,
                    NodePath = path,
                    // 一个从未记录过的节点，其 Version 至少要跟上刚看过的版本，
                    // 否则下次登录会因为 Version 落后而永远熄灭。
                    Version = seenVersion,
                    SeenVersion = seenVersion,
                    UpdatedUtc = now
                }).ExecuteCommandAsync();
                return;
            }

            // 只前进不后退：迟到的旧请求不会把已经看过的节点重新点亮。
            if (existing.SeenVersion >= seenVersion)
            {
                return;
            }

            await database.Updateable<AccountRedDotRow>()
                .SetColumns(row => new AccountRedDotRow
                {
                    SeenVersion = seenVersion,
                    Version = existing.Version < seenVersion ? seenVersion : existing.Version,
                    UpdatedUtc = now
                })
                .Where(row => row.AccountId == accountId && row.NodePath == path)
                .ExecuteCommandAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Failed to save red dot state.", exception);
        }
    }
}
