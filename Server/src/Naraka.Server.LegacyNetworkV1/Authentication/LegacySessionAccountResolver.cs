using Naraka.Server.Application.Sessions;

namespace Naraka.Server.LegacyNetworkV1.Authentication;

/// <summary>
/// Resolves the authoritative account from the connection session. A legacy accountId is only a consistency check.
/// </summary>
public sealed class LegacySessionAccountResolver(IAuthenticatedSessionRegistry sessions)
{
    public long Resolve(ConnectionId connectionId, long? claimedAccountId = null)
    {
        if (!sessions.TryGet(connectionId, out var session) || session is null)
        {
            throw new UnauthorizedAccessException("Legacy connection is not authenticated.");
        }

        if (claimedAccountId is not null && claimedAccountId.Value != session.AccountId)
        {
            throw new UnauthorizedAccessException("Legacy account claim does not match the authenticated session.");
        }

        return session.AccountId;
    }
}
