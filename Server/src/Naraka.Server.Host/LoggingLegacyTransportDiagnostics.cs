using Naraka.Server.Application.Sessions;
using Naraka.Server.LegacyNetworkV1;

namespace Naraka.Server.Host;

/// <summary>
/// Writes recovered transport faults to the host log. A dropped connection is a warning, never a
/// silent event: it is the only trace an operator gets when a client speaks an unsupported protocol.
/// </summary>
public sealed class LoggingLegacyTransportDiagnostics(ILogger<LegacyNetworkTransport> logger)
    : ILegacyTransportDiagnostics
{
    public void ConnectionDropped(ConnectionId connectionId, string reason, Exception? exception) =>
        logger.LogWarning(
            exception,
            "LegacyNetworkV1 closed connection {ConnectionId}: {Reason}. The listener stays open.",
            connectionId.ToString(),
            reason);

    public void AcceptFailed(Exception exception) =>
        logger.LogWarning(exception, "LegacyNetworkV1 failed to accept a connection. The listener stays open.");

    // Logged at error level: the player noticed nothing, so this line is the only signal that an
    // account is missing state it should already have.
    public void BusinessFaulted(ConnectionId connectionId, string operation, Exception? exception) =>
        logger.LogError(
            exception,
            "LegacyNetworkV1 business step '{Operation}' failed on connection {ConnectionId}. " +
            "The connection stays open and the step will be retried on the next attempt.",
            operation,
            connectionId.ToString());
}
