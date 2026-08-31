using Naraka.Server.LegacyNetworkV1.Messages;
using ProtoBuf;

namespace Naraka.Server.LegacyNetworkV1.Protocol;

public static class LegacyProtobufCodec
{
    private static readonly IReadOnlyDictionary<string, Type> IncomingTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["MsgSecret"] = typeof(LegacyMsgSecret),
            ["MsgPing"] = typeof(LegacyMsgPing),
            ["MsgRegister"] = typeof(LegacyMsgRegister),
            ["MsgLogin"] = typeof(LegacyMsgLogin)
        };

    public static byte[] Serialize(LegacyMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var stream = new MemoryStream();
        Serializer.NonGeneric.Serialize(stream, message);
        return stream.ToArray();
    }

    public static LegacyMessage DeserializeIncoming(string protocolName, ReadOnlySpan<byte> payload)
    {
        if (!IncomingTypes.TryGetValue(protocolName, out var messageType))
        {
            throw new InvalidDataException($"Unsupported legacy inbound protocol: {protocolName}.");
        }

        using var stream = new MemoryStream(payload.ToArray(), writable: false);
        return (LegacyMessage)(Serializer.NonGeneric.Deserialize(messageType, stream)
            ?? throw new InvalidDataException($"Empty legacy payload for {protocolName}."));
    }
}
