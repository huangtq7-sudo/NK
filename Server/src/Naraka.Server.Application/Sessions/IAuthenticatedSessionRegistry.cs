namespace Naraka.Server.Application.Sessions;

public interface IAuthenticatedSessionRegistry
{
    AuthenticatedSession Bind(ConnectionId connectionId, long accountId);

    bool TryGet(ConnectionId connectionId, out AuthenticatedSession? session);

    bool Remove(ConnectionId connectionId);
}
