using Naraka.Server.Application.Networking;

namespace Naraka.Server.LegacyNetworkV1;

/// <summary>
/// Integration boundary for the frozen AES/handshake/Protobuf/framing/heartbeat/Socket.Select transport.
/// Golden tests now protect AES and framing compatibility; live socket integration remains disabled.
/// </summary>
public sealed class LegacyNetworkTransport : ILegacyNetworkTransport
{
    public bool IsIntegrated => false;

    public string CompatibilityContract =>
        "LegacyNetworkV1 wire, session guard and response aliases protected; live socket integration pending";
}
