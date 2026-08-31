namespace Naraka.Server.LegacyNetworkV1.Protocol;

public sealed record LegacyFrame(string ProtocolName, byte[] EncryptedBody);
