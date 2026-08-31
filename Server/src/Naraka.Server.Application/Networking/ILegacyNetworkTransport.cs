namespace Naraka.Server.Application.Networking;

public interface ILegacyNetworkTransport
{
    bool IsIntegrated { get; }

    string CompatibilityContract { get; }
}
