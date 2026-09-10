namespace Naraka.Server.Application.Progression;

/// <summary>一个红点节点的持久化版本对。</summary>
public sealed record RedDotStateRow(string Path, long Version, long SeenVersion);

public interface IRedDotRepository
{
    Task<IReadOnlyList<RedDotStateRow>> ListAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>
    /// 记下玩家已经看过某个节点。
    ///
    /// 只允许把 SeenVersion 往前推：迟到的旧请求不能把一个已经看过的节点重新点亮，
    /// 也不能把玩家刚看过的记录退回去。
    /// </summary>
    Task SaveSeenAsync(
        long accountId,
        string path,
        long seenVersion,
        CancellationToken cancellationToken);
}

public readonly struct RedDotResult
{
    private RedDotResult(LobbyOperationStatus status, IReadOnlyList<RedDotStateRow>? rows)
    {
        Status = status;
        Rows = rows ?? Array.Empty<RedDotStateRow>();
    }

    public LobbyOperationStatus Status { get; }

    public IReadOnlyList<RedDotStateRow> Rows { get; }

    public static RedDotResult Success(IReadOnlyList<RedDotStateRow> rows) =>
        new(LobbyOperationStatus.Success, rows);

    public static RedDotResult Failed(LobbyOperationStatus status) => new(status, null);
}

/// <summary>
/// 红点持久化。
///
/// 服务端<b>不判断</b>某个节点该不该亮：那是各业务模块的数据说了算。这里只保存
/// "玩家看到哪一版了"，让红点跨登录保持一致。因此这个服务不引用任何业务规则。
/// </summary>
public sealed class RedDotService(IRedDotRepository redDots)
{
    /// <summary>路径长度上限。与 account_reddot.node_path 的列宽一致。</summary>
    public const int MaxPathLength = 160;

    private readonly IRedDotRepository _redDots = redDots ?? throw new ArgumentNullException(nameof(redDots));

    public async Task<RedDotResult> GetAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return RedDotResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            return RedDotResult.Success(await _redDots.ListAsync(accountId, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProgressionStorageException)
        {
            return RedDotResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return RedDotResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    public async Task<RedDotResult> MarkSeenAsync(
        long accountId,
        string? path,
        long seenVersion,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 ||
            string.IsNullOrWhiteSpace(path) ||
            path.Length > MaxPathLength ||
            seenVersion < 0)
        {
            return RedDotResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            await _redDots.SaveSeenAsync(accountId, path, seenVersion, cancellationToken);
            return RedDotResult.Success(await _redDots.ListAsync(accountId, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProgressionStorageException)
        {
            return RedDotResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return RedDotResult.Failed(LobbyOperationStatus.InternalError);
        }
    }
}
