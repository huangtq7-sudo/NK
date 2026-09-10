using System.Collections.ObjectModel;

namespace Naraka.Server.LegacyNetworkV1.Protocol;

/// <summary>
/// P1 application message registry. ADR-0007 freezes the audited LegacyNetworkV1 wire, so the
/// P0 numbers 0-18 stay in <see cref="LegacyProtocolCatalog"/> untouched and every new business
/// contract is registered here instead. Keeping the two catalogs apart is what lets the golden
/// tests keep asserting the frozen catalog sizes as proof that 0-18 were never edited or reused.
/// </summary>
public static class ApplicationProtocolCatalog
{
    /// <summary>First protocol number available to P1; everything below belongs to the frozen audit.</summary>
    public const int FirstApplicationProtocolValue = 19;

    /// <summary>Client to server request: read the authenticated account's lobby summary.</summary>
    public const string LobbyAccountSummaryRequest = "MsgLobbyAccountSummaryRequest";

    /// <summary>Server to client response carrying account level and the three currencies.</summary>
    public const string LobbyAccountSummaryResponse = "MsgLobbyAccountSummaryResponse";

    /// <summary>Client to server request: read avatar, avatar frame and the current loadout.</summary>
    public const string LobbyProfileRequest = "MsgLobbyProfileRequest";

    public const string LobbyProfileResponse = "MsgLobbyProfileResponse";

    /// <summary>Client to server request: change the avatar and avatar frame.</summary>
    public const string LobbySetAppearanceRequest = "MsgLobbySetAppearanceRequest";

    public const string LobbySetAppearanceResponse = "MsgLobbySetAppearanceResponse";

    /// <summary>Client to server request: change the selected hero, weapon and pet.</summary>
    public const string LobbySetLoadoutRequest = "MsgLobbySetLoadoutRequest";

    public const string LobbySetLoadoutResponse = "MsgLobbySetLoadoutResponse";

    /// <summary>Client to server request: read the whole warehouse and battle load.</summary>
    public const string LobbyInventoryRequest = "MsgLobbyInventoryRequest";

    public const string LobbyInventoryResponse = "MsgLobbyInventoryResponse";

    /// <summary>Client to server request: one warehouse write (discard, sell, sort, equip, expand).</summary>
    public const string LobbyInventoryMutateRequest = "MsgLobbyInventoryMutateRequest";

    public const string LobbyInventoryMutateResponse = "MsgLobbyInventoryMutateResponse";

    /// <summary>Client to server request: read per-product purchase counts and current balances.</summary>
    public const string LobbyShopRequest = "MsgLobbyShopRequest";

    public const string LobbyShopResponse = "MsgLobbyShopResponse";

    /// <summary>Client to server request: buy a quantity of one product.</summary>
    public const string LobbyShopPurchaseRequest = "MsgLobbyShopPurchaseRequest";

    public const string LobbyShopPurchaseResponse = "MsgLobbyShopPurchaseResponse";

    /// <summary>Client to server request: read weapon levels, materials and balances.</summary>
    public const string LobbyForgeRequest = "MsgLobbyForgeRequest";

    public const string LobbyForgeResponse = "MsgLobbyForgeResponse";

    /// <summary>Client to server request: upgrade one weapon by exactly one level.</summary>
    public const string LobbyForgeUpgradeRequest = "MsgLobbyForgeUpgradeRequest";

    public const string LobbyForgeUpgradeResponse = "MsgLobbyForgeUpgradeResponse";

    /// <summary>Client to server request: read pity, balances and any result not shown yet.</summary>
    public const string LobbyGachaRequest = "MsgLobbyGachaRequest";

    public const string LobbyGachaResponse = "MsgLobbyGachaResponse";

    /// <summary>Client to server request: one single pull or one ten pull.</summary>
    public const string LobbyGachaPullRequest = "MsgLobbyGachaPullRequest";

    public const string LobbyGachaPullResponse = "MsgLobbyGachaPullResponse";

    /// <summary>Client to server request: confirm one order was displayed to the player.</summary>
    public const string LobbyGachaAcknowledgeRequest = "MsgLobbyGachaAcknowledgeRequest";

    public const string LobbyGachaAcknowledgeResponse = "MsgLobbyGachaAcknowledgeResponse";

    /// <summary>Client to server request: read the sign-in cycle, streak and make-up cards.</summary>
    public const string LobbySignInRequest = "MsgLobbySignInRequest";

    public const string LobbySignInResponse = "MsgLobbySignInResponse";

    /// <summary>Client to server request: claim today, make up a missed day, or claim a streak reward.</summary>
    public const string LobbySignInClaimRequest = "MsgLobbySignInClaimRequest";

    public const string LobbySignInClaimResponse = "MsgLobbySignInClaimResponse";

    /// <summary>Client to server request: read achievements and account level rewards.</summary>
    public const string LobbyAchievementRequest = "MsgLobbyAchievementRequest";

    public const string LobbyAchievementResponse = "MsgLobbyAchievementResponse";

    /// <summary>Client to server request: claim one achievement or one account level reward.</summary>
    public const string LobbyAchievementClaimRequest = "MsgLobbyAchievementClaimRequest";

    public const string LobbyAchievementClaimResponse = "MsgLobbyAchievementClaimResponse";

    /// <summary>Client to server request: read persisted red dot versions.</summary>
    public const string LobbyRedDotRequest = "MsgLobbyRedDotRequest";

    public const string LobbyRedDotResponse = "MsgLobbyRedDotResponse";

    /// <summary>Client to server request: record that one red dot node was seen.</summary>
    public const string LobbyRedDotSeenRequest = "MsgLobbyRedDotSeenRequest";

    public const string LobbyRedDotSeenResponse = "MsgLobbyRedDotSeenResponse";

    /// <summary>Client to server request: read friends, requests, blocks and conversations.</summary>
    public const string LobbySocialRequest = "MsgLobbySocialRequest";

    public const string LobbySocialResponse = "MsgLobbySocialResponse";

    /// <summary>Client to server request: search one player by exact display name.</summary>
    public const string LobbySocialSearchRequest = "MsgLobbySocialSearchRequest";

    public const string LobbySocialSearchResponse = "MsgLobbySocialSearchResponse";

    /// <summary>
    /// Client to server request: one friend action - request, accept, reject, remove, block or
    /// unblock. They share a protocol because they share a response: the whole social view.
    /// </summary>
    public const string LobbySocialActionRequest = "MsgLobbySocialActionRequest";

    public const string LobbySocialActionResponse = "MsgLobbySocialActionResponse";

    /// <summary>Client to server request: open a conversation, send a message, or mark it read.</summary>
    public const string LobbyChatRequest = "MsgLobbyChatRequest";

    public const string LobbyChatResponse = "MsgLobbyChatResponse";

    /// <summary>Requests the client may send. Names, not numbers, travel on the wire.</summary>
    public static IReadOnlyDictionary<string, int> Inbound { get; } =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [LobbyAccountSummaryRequest] = 19,
            [LobbyProfileRequest] = 21,
            [LobbySetAppearanceRequest] = 23,
            [LobbySetLoadoutRequest] = 25,
            [LobbyInventoryRequest] = 27,
            [LobbyInventoryMutateRequest] = 29,
            [LobbyShopRequest] = 31,
            [LobbyShopPurchaseRequest] = 33,
            [LobbyForgeRequest] = 35,
            [LobbyForgeUpgradeRequest] = 37,
            [LobbyGachaRequest] = 39,
            [LobbyGachaPullRequest] = 41,
            [LobbyGachaAcknowledgeRequest] = 43,
            [LobbySignInRequest] = 45,
            [LobbySignInClaimRequest] = 47,
            [LobbyAchievementRequest] = 49,
            [LobbyAchievementClaimRequest] = 51,
            [LobbyRedDotRequest] = 53,
            [LobbyRedDotSeenRequest] = 55,
            [LobbySocialRequest] = 57,
            [LobbySocialSearchRequest] = 59,
            [LobbySocialActionRequest] = 61,
            [LobbyChatRequest] = 63
        });

    /// <summary>Responses the server may send.</summary>
    public static IReadOnlyDictionary<string, int> Outbound { get; } =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [LobbyAccountSummaryResponse] = 20,
            [LobbyProfileResponse] = 22,
            [LobbySetAppearanceResponse] = 24,
            [LobbySetLoadoutResponse] = 26,
            [LobbyInventoryResponse] = 28,
            [LobbyInventoryMutateResponse] = 30,
            [LobbyShopResponse] = 32,
            [LobbyShopPurchaseResponse] = 34,
            [LobbyForgeResponse] = 36,
            [LobbyForgeUpgradeResponse] = 38,
            [LobbyGachaResponse] = 40,
            [LobbyGachaPullResponse] = 42,
            [LobbyGachaAcknowledgeResponse] = 44,
            [LobbySignInResponse] = 46,
            [LobbySignInClaimResponse] = 48,
            [LobbyAchievementResponse] = 50,
            [LobbyAchievementClaimResponse] = 52,
            [LobbyRedDotResponse] = 54,
            [LobbyRedDotSeenResponse] = 56,
            [LobbySocialResponse] = 58,
            [LobbySocialSearchResponse] = 60,
            [LobbySocialActionResponse] = 62,
            [LobbyChatResponse] = 64
        });

    public static bool TryGetInbound(string protocolName, out int protocolValue) =>
        Inbound.TryGetValue(protocolName, out protocolValue);

    public static bool TryGetOutbound(string protocolName, out int protocolValue) =>
        Outbound.TryGetValue(protocolName, out protocolValue);

    /// <summary>
    /// Registration rule enforced by tests: application numbers start at 19, never collide with the
    /// frozen catalog, and never collide with each other across directions.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, int>> All() => Inbound.Concat(Outbound);
}
