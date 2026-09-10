namespace Naraka.Server.LegacyNetworkV1.Messages;

public enum LegacyProtocolValue
{
    None = 0,
    MsgSecret = 1,
    MsgPing = 2,
    MsgTest = 3,
    MsgRegister = 4,
    MsgLogin = 5,
    MsgLoadPlayerData = 6,
    MsgSavePlayerData = 7,
    MsgLoadInventory = 8,
    MsgSaveInventory = 9,
    MsgLoadTask = 10,
    MsgSaveTask = 11,
    MsgPlayerDataResponse = 12,
    MsgInventoryResponse = 13,
    MsgTaskResponse = 14,
    MsgLoadConfig = 15,
    MsgConfigResponse = 16,
    MsgShopPurchase = 17,
    MsgTaskReward = 18,

    // ADR-0007: 0-18 above are the frozen P0 audit and must never be renamed, reordered or reused.
    // P1 application messages are appended from 19 and registered in ApplicationProtocolCatalog.
    MsgLobbyAccountSummaryRequest = 19,
    MsgLobbyAccountSummaryResponse = 20,
    MsgLobbyProfileRequest = 21,
    MsgLobbyProfileResponse = 22,
    MsgLobbySetAppearanceRequest = 23,
    MsgLobbySetAppearanceResponse = 24,
    MsgLobbySetLoadoutRequest = 25,
    MsgLobbySetLoadoutResponse = 26,
    MsgLobbyInventoryRequest = 27,
    MsgLobbyInventoryResponse = 28,
    MsgLobbyInventoryMutateRequest = 29,
    MsgLobbyInventoryMutateResponse = 30,
    MsgLobbyShopRequest = 31,
    MsgLobbyShopResponse = 32,
    MsgLobbyShopPurchaseRequest = 33,
    MsgLobbyShopPurchaseResponse = 34,
    MsgLobbyForgeRequest = 35,
    MsgLobbyForgeResponse = 36,
    MsgLobbyForgeUpgradeRequest = 37,
    MsgLobbyForgeUpgradeResponse = 38,
    MsgLobbyGachaRequest = 39,
    MsgLobbyGachaResponse = 40,
    MsgLobbyGachaPullRequest = 41,
    MsgLobbyGachaPullResponse = 42,
    MsgLobbyGachaAcknowledgeRequest = 43,
    MsgLobbyGachaAcknowledgeResponse = 44,
    MsgLobbySignInRequest = 45,
    MsgLobbySignInResponse = 46,
    MsgLobbySignInClaimRequest = 47,
    MsgLobbySignInClaimResponse = 48,
    MsgLobbyAchievementRequest = 49,
    MsgLobbyAchievementResponse = 50,
    MsgLobbyAchievementClaimRequest = 51,
    MsgLobbyAchievementClaimResponse = 52,
    MsgLobbyRedDotRequest = 53,
    MsgLobbyRedDotResponse = 54,
    MsgLobbyRedDotSeenRequest = 55,
    MsgLobbyRedDotSeenResponse = 56,
    MsgLobbySocialRequest = 57,
    MsgLobbySocialResponse = 58,
    MsgLobbySocialSearchRequest = 59,
    MsgLobbySocialSearchResponse = 60,
    MsgLobbySocialActionRequest = 61,
    MsgLobbySocialActionResponse = 62,
    MsgLobbyChatRequest = 63,
    MsgLobbyChatResponse = 64
}

/// <summary>Friend actions. Values are a wire contract: append only.</summary>
public enum LegacySocialAction
{
    None = 0,
    SendRequest = 1,
    AcceptRequest = 2,
    RejectRequest = 3,
    RemoveFriend = 4,
    Block = 5,
    Unblock = 6
}

/// <summary>Chat actions. Values are a wire contract: append only.</summary>
public enum LegacyChatAction
{
    None = 0,
    OpenConversation = 1,
    SendMessage = 2,
    MarkRead = 3
}

/// <summary>
/// Sign-in claim kinds. Values are a wire contract: append only.
/// </summary>
public enum LegacySignInClaimKind
{
    None = 0,
    Today = 1,
    MakeUp = 2,
    Milestone = 3
}

/// <summary>Reward claim kinds for the achievement screen.</summary>
public enum LegacyAchievementClaimKind
{
    None = 0,
    Achievement = 1,
    AccountLevel = 2
}

public enum LegacyRegisterResult
{
    Success,
    Failed,
    AlreadyExist,
    WrongCode,
    Forbidden
}

public enum LegacyLoginResult
{
    Success,
    Failed,
    WrongPwd,
    UserNotExist,
    TimeoutToken
}

/// <summary>
/// Stable P1 error codes carried on the wire. Values are fixed; new codes append at the end.
/// </summary>
public enum LegacyLobbyAccountSummaryStatus
{
    Success = 0,
    Unauthenticated = 1,
    InvalidRequest = 2,
    NotFound = 3,
    DatabaseUnavailable = 4,
    InternalError = 5
}

/// <summary>
/// Wire mirror of <c>Naraka.Server.Application.Progression.LobbyOperationStatus</c>.
/// Values are a wire contract: append only, never rename, reorder or reuse.
/// </summary>
public enum LegacyLobbyOperationStatus
{
    Success = 0,
    Unauthenticated = 1,
    InvalidRequest = 2,
    NotFound = 3,
    DatabaseUnavailable = 4,
    InternalError = 5,
    Forbidden = 6,
    InsufficientCurrency = 7,
    InsufficientItems = 8,
    InventoryFull = 9,
    LimitReached = 10,
    AlreadyClaimed = 11,
    NotAvailable = 12,
    RateLimited = 13,
    Conflict = 14
}

/// <summary>
/// 仓库写操作。数值是线级契约：只能在末尾追加。
/// 把七种写操作收敛到一个协议对，是为了让"每个写请求都携带 RequestId、都经过同一套认证与校验"
/// 成为结构性事实，而不是七处各自记得实现一遍。
/// </summary>
public enum LegacyInventoryOperation
{
    None = 0,
    Discard = 1,
    Sell = 2,
    Reorder = 3,
    AutoSort = 4,
    Equip = 5,
    Unequip = 6,
    Expand = 7
}
