namespace Naraka.Server.Application.Lobby;

/// <summary>
/// Stable P1 error codes. The wire enum in the adapter mirrors these values one to one.
/// </summary>
public enum LobbyAccountSummaryStatus
{
    Success = 0,
    Unauthenticated = 1,
    InvalidRequest = 2,
    NotFound = 3,
    DatabaseUnavailable = 4,
    InternalError = 5
}

/// <summary>
/// Validated lobby account snapshot. Construction is the only way to obtain one, so an out-of-range
/// level or a negative balance can never reach the transport or the client.
/// </summary>
public sealed class LobbyAccountSummary
{
    public const int MinimumAccountLevel = 1;

    private LobbyAccountSummary(int accountLevel, long copper, long silk, long gold)
    {
        AccountLevel = accountLevel;
        Copper = copper;
        Silk = silk;
        Gold = gold;
    }

    public int AccountLevel { get; }

    public long Copper { get; }

    public long Silk { get; }

    public long Gold { get; }

    public static bool TryCreate(
        int accountLevel,
        long copper,
        long silk,
        long gold,
        out LobbyAccountSummary? summary)
    {
        if (accountLevel < MinimumAccountLevel || copper < 0 || silk < 0 || gold < 0)
        {
            summary = null;
            return false;
        }

        summary = new LobbyAccountSummary(accountLevel, copper, silk, gold);
        return true;
    }
}

public readonly struct LobbyAccountSummaryResult
{
    private LobbyAccountSummaryResult(LobbyAccountSummaryStatus status, LobbyAccountSummary? summary)
    {
        Status = status;
        Summary = summary;
    }

    public LobbyAccountSummaryStatus Status { get; }

    public LobbyAccountSummary? Summary { get; }

    public static LobbyAccountSummaryResult Success(LobbyAccountSummary summary) =>
        new(LobbyAccountSummaryStatus.Success, summary);

    public static LobbyAccountSummaryResult Failed(LobbyAccountSummaryStatus status) =>
        new(status, null);
}

/// <summary>Raised by storage adapters when MySQL is unreachable or rejects the read.</summary>
public sealed class LobbyAccountStorageException : Exception
{
    public LobbyAccountStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed record LobbyAccountProgressionRecord(int AccountLevel, long Copper, long Silk, long Gold);

public interface ILobbyAccountRepository
{
    /// <summary>
    /// Reads the progression row for an account. Returns null when the account has no row yet.
    /// Implementations must translate storage faults into <see cref="LobbyAccountStorageException"/>.
    /// </summary>
    Task<LobbyAccountProgressionRecord?> FindProgressionAsync(long accountId, CancellationToken cancellationToken);
}
