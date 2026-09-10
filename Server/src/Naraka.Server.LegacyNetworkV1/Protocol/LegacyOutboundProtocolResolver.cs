namespace Naraka.Server.LegacyNetworkV1.Protocol;

public sealed record LegacyOutboundProtocol(string MessageTypeName, string WireProtocolName, int EmbeddedProtocolValue);

/// <summary>
/// Adapts server response DTO names to the frozen client's request-name aliases without changing the wire format.
/// </summary>
public static class LegacyOutboundProtocolResolver
{
    public static LegacyOutboundProtocol Resolve(string messageTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTypeName);

        var wireProtocolName = LegacyProtocolCatalog.ClientResponseAliases.TryGetValue(messageTypeName, out var alias)
            ? alias
            : messageTypeName;

        if (LegacyProtocolCatalog.Client.TryGetValue(wireProtocolName, out var embeddedProtocolValue))
        {
            return new LegacyOutboundProtocol(messageTypeName, wireProtocolName, embeddedProtocolValue);
        }

        // P1 responses live in the application catalog so the frozen P0 catalog stays byte-identical.
        if (ApplicationProtocolCatalog.TryGetOutbound(wireProtocolName, out var applicationProtocolValue))
        {
            return new LegacyOutboundProtocol(messageTypeName, wireProtocolName, applicationProtocolValue);
        }

        throw new InvalidOperationException(
            $"Legacy client does not recognize outbound protocol '{messageTypeName}'.");
    }
}
