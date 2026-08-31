using Naraka.Server.Application.Networking;

namespace Naraka.Server.LegacyNetworkV1;

/// <summary>
/// Integration boundary for the frozen AES/handshake/Protobuf/framing/heartbeat/Socket.Select transport.
/// No legacy source is changed until compatibility golden tests exist.
/// </summary>
public sealed class LegacyNetworkTransport : ILegacyNetworkTransport
{
    public bool IsIntegrated => false;

    public string CompatibilityContract =>
        "LegacyNetworkV1 protocol and runtime behavior frozen; adapter integration pending";
}
