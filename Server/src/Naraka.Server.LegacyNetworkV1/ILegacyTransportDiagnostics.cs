using Naraka.Server.Application.Sessions;

namespace Naraka.Server.LegacyNetworkV1;

/// <summary>
/// Reports transport faults the listener recovered from. Recovery is silent for the client, but it
/// must never be silent for the operator: a single rejected frame used to end the accept loop and
/// stop the whole host without leaving any trace behind.
/// </summary>
public interface ILegacyTransportDiagnostics
{
    /// <summary>One connection was closed. Every other connection and the listener keep running.</summary>
    void ConnectionDropped(ConnectionId connectionId, string reason, Exception? exception);

    /// <summary>A single accept failed. The listener stays open and keeps waiting for clients.</summary>
    void AcceptFailed(Exception exception);

    /// <summary>
    /// A business step failed without dropping the connection - account provisioning, for example.
    /// The player keeps playing, so this must reach the operator: nothing else records that a
    /// starter grant or a reward write silently did not happen.
    /// </summary>
    void BusinessFaulted(ConnectionId connectionId, string operation, Exception? exception);
}

/// <summary>Default sink for tests and for hosts that do not observe transport faults.</summary>
public sealed class NullLegacyTransportDiagnostics : ILegacyTransportDiagnostics
{
    public static readonly NullLegacyTransportDiagnostics Instance = new();

    private NullLegacyTransportDiagnostics()
    {
    }

    public void ConnectionDropped(ConnectionId connectionId, string reason, Exception? exception)
    {
    }

    public void AcceptFailed(Exception exception)
    {
    }

    public void BusinessFaulted(ConnectionId connectionId, string operation, Exception? exception)
    {
    }
}
