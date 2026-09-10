namespace Naraka.Server.Application.Sessions;

public interface IAuthenticatedSessionRegistry
{
    AuthenticatedSession Bind(ConnectionId connectionId, long accountId);

    bool TryGet(ConnectionId connectionId, out AuthenticatedSession? session);

    bool Remove(ConnectionId connectionId);

    /// <summary>
    /// Whether the account currently holds at least one authenticated connection.
    ///
    /// Online status is derived from live connections rather than stored on the account row: a
    /// stored flag survives a crash and leaves the player permanently "online".
    /// </summary>
    bool IsOnline(long accountId);
}
