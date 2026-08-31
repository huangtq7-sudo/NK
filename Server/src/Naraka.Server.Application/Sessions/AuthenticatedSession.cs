namespace Naraka.Server.Application.Sessions;

public sealed record AuthenticatedSession(ConnectionId ConnectionId, long AccountId, DateTime CreatedUtc);
