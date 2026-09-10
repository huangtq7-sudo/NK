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
            ["MsgLogin"] = typeof(LegacyMsgLogin),
            [ApplicationProtocolCatalog.LobbyAccountSummaryRequest] =
                typeof(LegacyMsgLobbyAccountSummaryRequest),
            [ApplicationProtocolCatalog.LobbyProfileRequest] =
                typeof(LegacyMsgLobbyProfileRequest),
            [ApplicationProtocolCatalog.LobbySetAppearanceRequest] =
                typeof(LegacyMsgLobbySetAppearanceRequest),
            [ApplicationProtocolCatalog.LobbySetLoadoutRequest] =
                typeof(LegacyMsgLobbySetLoadoutRequest),
            [ApplicationProtocolCatalog.LobbyInventoryRequest] =
                typeof(LegacyMsgLobbyInventoryRequest),
            [ApplicationProtocolCatalog.LobbyInventoryMutateRequest] =
                typeof(LegacyMsgLobbyInventoryMutateRequest),
            [ApplicationProtocolCatalog.LobbyShopRequest] =
                typeof(LegacyMsgLobbyShopRequest),
            [ApplicationProtocolCatalog.LobbyShopPurchaseRequest] =
                typeof(LegacyMsgLobbyShopPurchaseRequest),
            [ApplicationProtocolCatalog.LobbyForgeRequest] =
                typeof(LegacyMsgLobbyForgeRequest),
            [ApplicationProtocolCatalog.LobbyForgeUpgradeRequest] =
                typeof(LegacyMsgLobbyForgeUpgradeRequest),
            [ApplicationProtocolCatalog.LobbyGachaRequest] =
                typeof(LegacyMsgLobbyGachaRequest),
            [ApplicationProtocolCatalog.LobbyGachaPullRequest] =
                typeof(LegacyMsgLobbyGachaPullRequest),
            [ApplicationProtocolCatalog.LobbyGachaAcknowledgeRequest] =
                typeof(LegacyMsgLobbyGachaAcknowledgeRequest),
            [ApplicationProtocolCatalog.LobbySignInRequest] =
                typeof(LegacyMsgLobbySignInRequest),
            [ApplicationProtocolCatalog.LobbySignInClaimRequest] =
                typeof(LegacyMsgLobbySignInClaimRequest),
            [ApplicationProtocolCatalog.LobbyAchievementRequest] =
                typeof(LegacyMsgLobbyAchievementRequest),
            [ApplicationProtocolCatalog.LobbyAchievementClaimRequest] =
                typeof(LegacyMsgLobbyAchievementClaimRequest),
            [ApplicationProtocolCatalog.LobbyRedDotRequest] =
                typeof(LegacyMsgLobbyRedDotRequest),
            [ApplicationProtocolCatalog.LobbyRedDotSeenRequest] =
                typeof(LegacyMsgLobbyRedDotSeenRequest),
            [ApplicationProtocolCatalog.LobbySocialRequest] =
                typeof(LegacyMsgLobbySocialRequest),
            [ApplicationProtocolCatalog.LobbySocialSearchRequest] =
                typeof(LegacyMsgLobbySocialSearchRequest),
            [ApplicationProtocolCatalog.LobbySocialActionRequest] =
                typeof(LegacyMsgLobbySocialActionRequest),
            [ApplicationProtocolCatalog.LobbyChatRequest] =
                typeof(LegacyMsgLobbyChatRequest)
        };

    /// <summary>
    /// Response types the server emits. Kept separate from <see cref="IncomingTypes"/> so a client can
    /// never get a response shape accepted as an inbound request.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Type> OutgoingTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [ApplicationProtocolCatalog.LobbyAccountSummaryResponse] =
                typeof(LegacyMsgLobbyAccountSummaryResponse),
            [ApplicationProtocolCatalog.LobbyProfileResponse] =
                typeof(LegacyMsgLobbyProfileResponse),
            [ApplicationProtocolCatalog.LobbySetAppearanceResponse] =
                typeof(LegacyMsgLobbySetAppearanceResponse),
            [ApplicationProtocolCatalog.LobbySetLoadoutResponse] =
                typeof(LegacyMsgLobbySetLoadoutResponse),
            [ApplicationProtocolCatalog.LobbyInventoryResponse] =
                typeof(LegacyMsgLobbyInventoryResponse),
            [ApplicationProtocolCatalog.LobbyInventoryMutateResponse] =
                typeof(LegacyMsgLobbyInventoryMutateResponse),
            [ApplicationProtocolCatalog.LobbyShopResponse] =
                typeof(LegacyMsgLobbyShopResponse),
            [ApplicationProtocolCatalog.LobbyShopPurchaseResponse] =
                typeof(LegacyMsgLobbyShopPurchaseResponse),
            [ApplicationProtocolCatalog.LobbyForgeResponse] =
                typeof(LegacyMsgLobbyForgeResponse),
            [ApplicationProtocolCatalog.LobbyForgeUpgradeResponse] =
                typeof(LegacyMsgLobbyForgeUpgradeResponse),
            [ApplicationProtocolCatalog.LobbyGachaResponse] =
                typeof(LegacyMsgLobbyGachaResponse),
            [ApplicationProtocolCatalog.LobbyGachaPullResponse] =
                typeof(LegacyMsgLobbyGachaPullResponse),
            [ApplicationProtocolCatalog.LobbyGachaAcknowledgeResponse] =
                typeof(LegacyMsgLobbyGachaAcknowledgeResponse),
            [ApplicationProtocolCatalog.LobbySignInResponse] =
                typeof(LegacyMsgLobbySignInResponse),
            [ApplicationProtocolCatalog.LobbySignInClaimResponse] =
                typeof(LegacyMsgLobbySignInClaimResponse),
            [ApplicationProtocolCatalog.LobbyAchievementResponse] =
                typeof(LegacyMsgLobbyAchievementResponse),
            [ApplicationProtocolCatalog.LobbyAchievementClaimResponse] =
                typeof(LegacyMsgLobbyAchievementClaimResponse),
            [ApplicationProtocolCatalog.LobbyRedDotResponse] =
                typeof(LegacyMsgLobbyRedDotResponse),
            [ApplicationProtocolCatalog.LobbyRedDotSeenResponse] =
                typeof(LegacyMsgLobbyRedDotSeenResponse),
            [ApplicationProtocolCatalog.LobbySocialResponse] =
                typeof(LegacyMsgLobbySocialResponse),
            [ApplicationProtocolCatalog.LobbySocialSearchResponse] =
                typeof(LegacyMsgLobbySocialSearchResponse),
            [ApplicationProtocolCatalog.LobbySocialActionResponse] =
                typeof(LegacyMsgLobbySocialActionResponse),
            [ApplicationProtocolCatalog.LobbyChatResponse] =
                typeof(LegacyMsgLobbyChatResponse)
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

        return Deserialize(messageType, protocolName, payload);
    }

    /// <summary>Decodes a server response. Used by tests and tooling, never by the inbound dispatcher.</summary>
    public static LegacyMessage DeserializeOutgoing(string protocolName, ReadOnlySpan<byte> payload)
    {
        if (OutgoingTypes.TryGetValue(protocolName, out var responseType))
        {
            return Deserialize(responseType, protocolName, payload);
        }

        // Frozen P0 responses reuse the request protocol name, so fall back to the inbound registry.
        return DeserializeIncoming(protocolName, payload);
    }

    private static LegacyMessage Deserialize(Type messageType, string protocolName, ReadOnlySpan<byte> payload)
    {
        using var stream = new MemoryStream(payload.ToArray(), writable: false);
        return (LegacyMessage)(Serializer.NonGeneric.Deserialize(messageType, stream)
            ?? throw new InvalidDataException($"Empty legacy payload for {protocolName}."));
    }
}
