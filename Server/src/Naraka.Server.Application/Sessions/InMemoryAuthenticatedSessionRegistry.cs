using System.Collections.Concurrent;

namespace Naraka.Server.Application.Sessions;

public sealed class InMemoryAuthenticatedSessionRegistry : IAuthenticatedSessionRegistry
{
    private readonly ConcurrentDictionary<ConnectionId, AuthenticatedSession> _sessions = new();

    public AuthenticatedSession Bind(ConnectionId connectionId, long accountId)
    {
        if (connectionId.Value == Guid.Empty)
        {
            throw new ArgumentException("Connection ID must not be empty.", nameof(connectionId));
        }

        if (accountId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }

        var session = new AuthenticatedSession(connectionId, accountId, DateTime.UtcNow);
        _sessions[connectionId] = session;
        return session;
    }

    public bool TryGet(ConnectionId connectionId, out AuthenticatedSession? session) =>
        _sessions.TryGetValue(connectionId, out session);

    public bool Remove(ConnectionId connectionId) =>
        _sessions.TryRemove(connectionId, out _);
}
