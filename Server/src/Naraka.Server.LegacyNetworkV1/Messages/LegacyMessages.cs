using ProtoBuf;

namespace Naraka.Server.LegacyNetworkV1.Messages;

public abstract class LegacyMessage
{
    public abstract LegacyProtocolValue ProtocolType { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgSecret : LegacyMessage
{
    public LegacyMsgSecret() => ProtocolType = LegacyProtocolValue.MsgSecret;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)]
    public string? Secret { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgPing : LegacyMessage
{
    public LegacyMsgPing() => ProtocolType = LegacyProtocolValue.MsgPing;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgRegister : LegacyMessage
{
    public LegacyMsgRegister() => ProtocolType = LegacyProtocolValue.MsgRegister;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)]
    public string? Account { get; set; }

    [ProtoMember(3)]
    public string? Password { get; set; }

    [ProtoMember(4)]
    public LegacyRegisterResult Result { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLogin : LegacyMessage
{
    public LegacyMsgLogin() => ProtocolType = LegacyProtocolValue.MsgLogin;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)]
    public string? Account { get; set; }

    [ProtoMember(3)]
    public string? Password { get; set; }

    [ProtoMember(4)]
    public LegacyLoginResult Result { get; set; }

    [ProtoMember(5)]
    public int AccountId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgPlayerDataResponse : LegacyMessage
{
    public LegacyMsgPlayerDataResponse() => ProtocolType = LegacyProtocolValue.MsgLoadPlayerData;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public int Result { get; set; }
    [ProtoMember(3)] public int Gold { get; set; }
    [ProtoMember(4)] public int MaxHealth { get; set; }
    [ProtoMember(5)] public int CurrentHealth { get; set; }
    [ProtoMember(6)] public int Attack { get; set; }
    [ProtoMember(7)] public int Defense { get; set; }
    [ProtoMember(8)] public int CurrentWeaponId { get; set; }
    [ProtoMember(9)] public int SelectedHeroId { get; set; }
}

/// <summary>
/// P1 request for the authenticated account's lobby summary. The account is resolved from the
/// connection session; ADR-0007 forbids letting the client choose the account it reads.
/// </summary>
[ProtoContract]
public sealed class LegacyMsgLobbyAccountSummaryRequest : LegacyMessage
{
    public LegacyMsgLobbyAccountSummaryRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyAccountSummaryRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    /// <summary>Client generated idempotency token echoed back on the response.</summary>
    [ProtoMember(2)]
    public string? RequestId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyAccountSummaryResponse : LegacyMessage
{
    public LegacyMsgLobbyAccountSummaryResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyAccountSummaryResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)]
    public string? RequestId { get; set; }

    [ProtoMember(3)]
    public LegacyLobbyAccountSummaryStatus Status { get; set; }

    [ProtoMember(4)]
    public int AccountLevel { get; set; }

    [ProtoMember(5)]
    public long Copper { get; set; }

    [ProtoMember(6)]
    public long Silk { get; set; }

    [ProtoMember(7)]
    public long Gold { get; set; }
}

/// <summary>
/// P1.1-B request for the authenticated account's profile. Like every P1 request it carries no
/// account id: the server resolves the account from the connection session (ADR-0007).
/// </summary>
[ProtoContract]
public sealed class LegacyMsgLobbyProfileRequest : LegacyMessage
{
    public LegacyMsgLobbyProfileRequest() => ProtocolType = LegacyProtocolValue.MsgLobbyProfileRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)]
    public string? RequestId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyProfileResponse : LegacyMessage
{
    public LegacyMsgLobbyProfileResponse() => ProtocolType = LegacyProtocolValue.MsgLobbyProfileResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }

    /// <summary>Stable configuration ids. Never list indexes: inserting a hero would shift them all.</summary>
    [ProtoMember(4)] public string? AvatarId { get; set; }
    [ProtoMember(5)] public string? AvatarFrameId { get; set; }
    [ProtoMember(6)] public string? SelectedHeroId { get; set; }
    [ProtoMember(7)] public string? SelectedWeaponId { get; set; }
    [ProtoMember(8)] public string? SelectedPetId { get; set; }
    [ProtoMember(9)] public long AccountXp { get; set; }
    [ProtoMember(10)] public int AccountLevel { get; set; }
    [ProtoMember(11)] public int InventoryTier { get; set; }
    [ProtoMember(12)] public long Copper { get; set; }
    [ProtoMember(13)] public long Silk { get; set; }
    [ProtoMember(14)] public long Gold { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbySetAppearanceRequest : LegacyMessage
{
    public LegacyMsgLobbySetAppearanceRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbySetAppearanceRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public string? AvatarId { get; set; }
    [ProtoMember(4)] public string? AvatarFrameId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbySetAppearanceResponse : LegacyMessage
{
    public LegacyMsgLobbySetAppearanceResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbySetAppearanceResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public string? AvatarId { get; set; }
    [ProtoMember(5)] public string? AvatarFrameId { get; set; }
}

/// <summary>
/// P1.2 request: change the selected hero, weapon and pet. All three are stable configuration ids;
/// a list index would silently point at a different hero as soon as the catalog grows.
/// </summary>
[ProtoContract]
public sealed class LegacyMsgLobbySetLoadoutRequest : LegacyMessage
{
    public LegacyMsgLobbySetLoadoutRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbySetLoadoutRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public string? HeroId { get; set; }
    [ProtoMember(4)] public string? WeaponId { get; set; }
    [ProtoMember(5)] public string? PetId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbySetLoadoutResponse : LegacyMessage
{
    public LegacyMsgLobbySetLoadoutResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbySetLoadoutResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public string? HeroId { get; set; }
    [ProtoMember(5)] public string? WeaponId { get; set; }
    [ProtoMember(6)] public string? PetId { get; set; }
}

/// <summary>One warehouse slot on the wire. One item type occupies exactly one slot.</summary>
[ProtoContract]
public sealed class LegacyInventorySlot
{
    [ProtoMember(1)] public string? ItemId { get; set; }
    [ProtoMember(2)] public long Quantity { get; set; }
    [ProtoMember(3)] public int SlotIndex { get; set; }
}

[ProtoContract]
public sealed class LegacyEquipmentSlot
{
    [ProtoMember(1)] public string? SlotKind { get; set; }
    [ProtoMember(2)] public int SlotIndex { get; set; }
    [ProtoMember(3)] public string? ItemId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyInventoryRequest : LegacyMessage
{
    public LegacyMsgLobbyInventoryRequest() => ProtocolType = LegacyProtocolValue.MsgLobbyInventoryRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyInventoryResponse : LegacyMessage
{
    public LegacyMsgLobbyInventoryResponse() => ProtocolType = LegacyProtocolValue.MsgLobbyInventoryResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public List<LegacyInventorySlot> Slots { get; } = new();
    [ProtoMember(5)] public List<LegacyEquipmentSlot> Equipment { get; } = new();
    [ProtoMember(6)] public int Capacity { get; set; }
    [ProtoMember(7)] public int Tier { get; set; }
}

/// <summary>
/// One warehouse write. Every operation carries a RequestId and is validated server side against
/// the generated configuration; the client can never choose a price, a stack limit or a capacity.
/// </summary>
[ProtoContract]
public sealed class LegacyMsgLobbyInventoryMutateRequest : LegacyMessage
{
    public LegacyMsgLobbyInventoryMutateRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyInventoryMutateRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyInventoryOperation Operation { get; set; }
    [ProtoMember(4)] public string? ItemId { get; set; }
    [ProtoMember(5)] public long Quantity { get; set; }
    [ProtoMember(6)] public string? SlotKind { get; set; }
    [ProtoMember(7)] public int SlotIndex { get; set; }

    /// <summary>Reorder only: the complete new slot order.</summary>
    [ProtoMember(8)] public List<string> ItemOrder { get; } = new();
}

[ProtoContract]
public sealed class LegacyMsgLobbyInventoryMutateResponse : LegacyMessage
{
    public LegacyMsgLobbyInventoryMutateResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyInventoryMutateResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public List<LegacyInventorySlot> Slots { get; } = new();
    [ProtoMember(5)] public List<LegacyEquipmentSlot> Equipment { get; } = new();
    [ProtoMember(6)] public int Capacity { get; set; }
    [ProtoMember(7)] public int Tier { get; set; }

    /// <summary>Only set when the operation moved currency (sell, expand).</summary>
    [ProtoMember(8)] public bool HasBalances { get; set; }
    [ProtoMember(9)] public long Copper { get; set; }
    [ProtoMember(10)] public long Silk { get; set; }
    [ProtoMember(11)] public long Gold { get; set; }
}

[ProtoContract]
public sealed class LegacyShopPurchaseCount
{
    [ProtoMember(1)] public string? ProductId { get; set; }
    [ProtoMember(2)] public long PurchasedTotal { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyShopRequest : LegacyMessage
{
    public LegacyMsgLobbyShopRequest() => ProtocolType = LegacyProtocolValue.MsgLobbyShopRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
}

/// <summary>
/// The shop catalog itself is not on the wire: prices, categories and limits come from the
/// generated configuration both sides already share. Only the per-account facts travel here.
/// </summary>
[ProtoContract]
public sealed class LegacyMsgLobbyShopResponse : LegacyMessage
{
    public LegacyMsgLobbyShopResponse() => ProtocolType = LegacyProtocolValue.MsgLobbyShopResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public List<LegacyShopPurchaseCount> Purchases { get; } = new();
    [ProtoMember(5)] public long Copper { get; set; }
    [ProtoMember(6)] public long Silk { get; set; }
    [ProtoMember(7)] public long Gold { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyShopPurchaseRequest : LegacyMessage
{
    public LegacyMsgLobbyShopPurchaseRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyShopPurchaseRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    /// <summary>Order id. The same value replays the first result instead of charging again.</summary>
    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public string? ProductId { get; set; }
    [ProtoMember(4)] public int Quantity { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyShopPurchaseResponse : LegacyMessage
{
    public LegacyMsgLobbyShopPurchaseResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyShopPurchaseResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public List<LegacyShopPurchaseCount> Purchases { get; } = new();
    [ProtoMember(5)] public long Copper { get; set; }
    [ProtoMember(6)] public long Silk { get; set; }
    [ProtoMember(7)] public long Gold { get; set; }
}

[ProtoContract]
public sealed class LegacyAccountWeapon
{
    [ProtoMember(1)] public string? WeaponId { get; set; }
    [ProtoMember(2)] public int Level { get; set; }
    [ProtoMember(3)] public long Proficiency { get; set; }
    [ProtoMember(4)] public long KillCount { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyForgeRequest : LegacyMessage
{
    public LegacyMsgLobbyForgeRequest() => ProtocolType = LegacyProtocolValue.MsgLobbyForgeRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
}

/// <summary>
/// Forge state. Recipes and the next-level preview come from the shared configuration, so only the
/// per-account facts - weapon levels, material stock and balances - travel here.
/// </summary>
[ProtoContract]
public sealed class LegacyMsgLobbyForgeResponse : LegacyMessage
{
    public LegacyMsgLobbyForgeResponse() => ProtocolType = LegacyProtocolValue.MsgLobbyForgeResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public List<LegacyAccountWeapon> Weapons { get; } = new();
    [ProtoMember(5)] public List<LegacyInventorySlot> Materials { get; } = new();
    [ProtoMember(6)] public long Copper { get; set; }
    [ProtoMember(7)] public long Silk { get; set; }
    [ProtoMember(8)] public long Gold { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyForgeUpgradeRequest : LegacyMessage
{
    public LegacyMsgLobbyForgeUpgradeRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyForgeUpgradeRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public string? WeaponId { get; set; }

    /// <summary>The level the client is showing. A mismatch means the UI is stale, not that materials ran out.</summary>
    [ProtoMember(4)] public int ExpectedLevel { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyForgeUpgradeResponse : LegacyMessage
{
    public LegacyMsgLobbyForgeUpgradeResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyForgeUpgradeResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public List<LegacyAccountWeapon> Weapons { get; } = new();
    [ProtoMember(5)] public List<LegacyInventorySlot> Materials { get; } = new();
    [ProtoMember(6)] public long Copper { get; set; }
    [ProtoMember(7)] public long Silk { get; set; }
    [ProtoMember(8)] public long Gold { get; set; }
}

[ProtoContract]
public sealed class LegacyGachaReward
{
    [ProtoMember(1)] public string? RewardId { get; set; }
    [ProtoMember(2)] public string? ItemId { get; set; }
    [ProtoMember(3)] public int Amount { get; set; }
    [ProtoMember(4)] public string? Quality { get; set; }
}

/// <summary>
/// One finalized order. It exists in the database before the client ever animates it, which is why
/// a skipped animation, a crash or a dropped connection cannot lose or duplicate a pull.
/// </summary>
[ProtoContract]
public sealed class LegacyGachaOrder
{
    [ProtoMember(1)] public string? OrderId { get; set; }
    [ProtoMember(2)] public string? PoolId { get; set; }
    [ProtoMember(3)] public int PullCount { get; set; }
    [ProtoMember(4)] public bool IsShown { get; set; }
    [ProtoMember(5)] public List<LegacyGachaReward> Rewards { get; } = new();
}

[ProtoContract]
public sealed class LegacyMsgLobbyGachaRequest : LegacyMessage
{
    public LegacyMsgLobbyGachaRequest() => ProtocolType = LegacyProtocolValue.MsgLobbyGachaRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public string? PoolId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyGachaResponse : LegacyMessage
{
    public LegacyMsgLobbyGachaResponse() => ProtocolType = LegacyProtocolValue.MsgLobbyGachaResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public string? PoolId { get; set; }
    [ProtoMember(5)] public int PityCounter { get; set; }
    [ProtoMember(6)] public long TotalPulls { get; set; }

    /// <summary>Orders the client has not confirmed as displayed. Replayed on the next login.</summary>
    [ProtoMember(7)] public List<LegacyGachaOrder> UnshownOrders { get; } = new();
    [ProtoMember(8)] public long Copper { get; set; }
    [ProtoMember(9)] public long Silk { get; set; }
    [ProtoMember(10)] public long Gold { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyGachaPullRequest : LegacyMessage
{
    public LegacyMsgLobbyGachaPullRequest() => ProtocolType = LegacyProtocolValue.MsgLobbyGachaPullRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    /// <summary>Order id. The same value replays the first result instead of rolling again.</summary>
    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public string? PoolId { get; set; }
    [ProtoMember(4)] public int PullCount { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyGachaPullResponse : LegacyMessage
{
    public LegacyMsgLobbyGachaPullResponse() => ProtocolType = LegacyProtocolValue.MsgLobbyGachaPullResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public LegacyGachaOrder? Order { get; set; }
    [ProtoMember(5)] public int PityCounter { get; set; }
    [ProtoMember(6)] public long TotalPulls { get; set; }
    [ProtoMember(7)] public long Copper { get; set; }
    [ProtoMember(8)] public long Silk { get; set; }
    [ProtoMember(9)] public long Gold { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyGachaAcknowledgeRequest : LegacyMessage
{
    public LegacyMsgLobbyGachaAcknowledgeRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyGachaAcknowledgeRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public string? PoolId { get; set; }
    [ProtoMember(4)] public string? OrderId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyGachaAcknowledgeResponse : LegacyMessage
{
    public LegacyMsgLobbyGachaAcknowledgeResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyGachaAcknowledgeResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public int PityCounter { get; set; }
    [ProtoMember(5)] public List<LegacyGachaOrder> UnshownOrders { get; } = new();
}

[ProtoContract]
public sealed class LegacySignInClaim
{
    [ProtoMember(1)] public int DayIndex { get; set; }
    [ProtoMember(2)] public bool IsMakeup { get; set; }
}

/// <summary>
/// Sign-in state. The seven day grid, the streak and the make-up card count are separate facts:
/// the main cycle progress and the streak are stored apart, so a make-up never advances the streak.
/// </summary>
[ProtoContract]
public sealed class LegacyMsgLobbySignInRequest : LegacyMessage
{
    public LegacyMsgLobbySignInRequest() => ProtocolType = LegacyProtocolValue.MsgLobbySignInRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbySignInResponse : LegacyMessage
{
    public LegacyMsgLobbySignInResponse() => ProtocolType = LegacyProtocolValue.MsgLobbySignInResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public long CycleStartDay { get; set; }
    [ProtoMember(5)] public long ServerDay { get; set; }
    [ProtoMember(6)] public int ConsecutiveDays { get; set; }
    [ProtoMember(7)] public List<LegacySignInClaim> Claims { get; } = new();
    [ProtoMember(8)] public List<string> ClaimedMilestones { get; } = new();
    [ProtoMember(9)] public long MakeupCardCount { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbySignInClaimRequest : LegacyMessage
{
    public LegacyMsgLobbySignInClaimRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbySignInClaimRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacySignInClaimKind Kind { get; set; }

    /// <summary>Make-up: the missed day index. Milestone: the streak day count.</summary>
    [ProtoMember(4)] public int Target { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbySignInClaimResponse : LegacyMessage
{
    public LegacyMsgLobbySignInClaimResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbySignInClaimResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public long CycleStartDay { get; set; }
    [ProtoMember(5)] public long ServerDay { get; set; }
    [ProtoMember(6)] public int ConsecutiveDays { get; set; }
    [ProtoMember(7)] public List<LegacySignInClaim> Claims { get; } = new();
    [ProtoMember(8)] public List<string> ClaimedMilestones { get; } = new();
    [ProtoMember(9)] public long MakeupCardCount { get; set; }
}

[ProtoContract]
public sealed class LegacyAchievementProgress
{
    [ProtoMember(1)] public string? AchievementId { get; set; }
    [ProtoMember(2)] public long Progress { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyAchievementRequest : LegacyMessage
{
    public LegacyMsgLobbyAchievementRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyAchievementRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
}

/// <summary>
/// Achievement and account level state. AchievementXp and AccountXp are separate fields because
/// they are separate systems: achievements never contribute to the account level.
/// </summary>
[ProtoContract]
public sealed class LegacyMsgLobbyAchievementResponse : LegacyMessage
{
    public LegacyMsgLobbyAchievementResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyAchievementResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public long AchievementXp { get; set; }
    [ProtoMember(5)] public int AchievementLevel { get; set; }
    [ProtoMember(6)] public List<LegacyAchievementProgress> Progress { get; } = new();
    [ProtoMember(7)] public List<string> ClaimedAchievements { get; } = new();
    [ProtoMember(8)] public List<string> ClaimedAccountLevels { get; } = new();
    [ProtoMember(9)] public long AccountXp { get; set; }
    [ProtoMember(10)] public int AccountLevel { get; set; }
}

[ProtoContract]
public sealed class LegacyRedDotNode
{
    [ProtoMember(1)] public string? Path { get; set; }
    [ProtoMember(2)] public long Version { get; set; }
    [ProtoMember(3)] public long SeenVersion { get; set; }
}

/// <summary>
/// Persisted red dot versions. The server stores version pairs, never a boolean: whether a dot
/// lights up is Version > SeenVersion, and a stored boolean would go stale the moment a business
/// module produces new content.
/// </summary>
[ProtoContract]
public sealed class LegacyMsgLobbyRedDotRequest : LegacyMessage
{
    public LegacyMsgLobbyRedDotRequest() => ProtocolType = LegacyProtocolValue.MsgLobbyRedDotRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyRedDotResponse : LegacyMessage
{
    public LegacyMsgLobbyRedDotResponse() => ProtocolType = LegacyProtocolValue.MsgLobbyRedDotResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public List<LegacyRedDotNode> Nodes { get; } = new();
}

[ProtoContract]
public sealed class LegacyMsgLobbyRedDotSeenRequest : LegacyMessage
{
    public LegacyMsgLobbyRedDotSeenRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyRedDotSeenRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public string? Path { get; set; }
    [ProtoMember(4)] public long SeenVersion { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyRedDotSeenResponse : LegacyMessage
{
    public LegacyMsgLobbyRedDotSeenResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyRedDotSeenResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public List<LegacyRedDotNode> Nodes { get; } = new();
}

[ProtoContract]
public sealed class LegacySocialPlayer
{
    [ProtoMember(1)] public long AccountId { get; set; }
    [ProtoMember(2)] public string? DisplayName { get; set; }
    [ProtoMember(3)] public string? AvatarId { get; set; }

    /// <summary>Derived from live connections, never stored. Green and grey dots are not red dots.</summary>
    [ProtoMember(4)] public bool IsOnline { get; set; }
}

[ProtoContract]
public sealed class LegacyChatConversation
{
    [ProtoMember(1)] public long ConversationId { get; set; }
    [ProtoMember(2)] public LegacySocialPlayer? Peer { get; set; }
    [ProtoMember(3)] public long LastMessageId { get; set; }
    [ProtoMember(4)] public long LastReadMessageId { get; set; }
}

[ProtoContract]
public sealed class LegacyChatMessage
{
    [ProtoMember(1)] public long MessageId { get; set; }
    [ProtoMember(2)] public long ConversationId { get; set; }
    [ProtoMember(3)] public long SenderAccountId { get; set; }
    [ProtoMember(4)] public string? Body { get; set; }
    [ProtoMember(5)] public long SentUnixSeconds { get; set; }
}

/// <summary>
/// The whole social view. Every social response carries it, so the client never has to merge a
/// partial update into a list it already holds - a merge that goes wrong leaves a friend the
/// player can neither message nor remove.
/// </summary>
[ProtoContract]
public sealed class LegacyMsgLobbySocialRequest : LegacyMessage
{
    public LegacyMsgLobbySocialRequest() => ProtocolType = LegacyProtocolValue.MsgLobbySocialRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbySocialResponse : LegacyMessage
{
    public LegacyMsgLobbySocialResponse() => ProtocolType = LegacyProtocolValue.MsgLobbySocialResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public List<LegacySocialPlayer> Friends { get; } = new();
    [ProtoMember(5)] public List<LegacySocialPlayer> IncomingRequests { get; } = new();
    [ProtoMember(6)] public List<LegacySocialPlayer> Blocked { get; } = new();
    [ProtoMember(7)] public List<LegacyChatConversation> Conversations { get; } = new();
}

[ProtoContract]
public sealed class LegacyMsgLobbySocialSearchRequest : LegacyMessage
{
    public LegacyMsgLobbySocialSearchRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbySocialSearchRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public string? DisplayName { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbySocialSearchResponse : LegacyMessage
{
    public LegacyMsgLobbySocialSearchResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbySocialSearchResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }

    /// <summary>Null when nothing matched. Blocked players and the searcher never match.</summary>
    [ProtoMember(4)] public LegacySocialPlayer? Found { get; set; }

    [ProtoMember(5)] public List<LegacySocialPlayer> Friends { get; } = new();
    [ProtoMember(6)] public List<LegacySocialPlayer> IncomingRequests { get; } = new();
    [ProtoMember(7)] public List<LegacySocialPlayer> Blocked { get; } = new();
    [ProtoMember(8)] public List<LegacyChatConversation> Conversations { get; } = new();
}

[ProtoContract]
public sealed class LegacyMsgLobbySocialActionRequest : LegacyMessage
{
    public LegacyMsgLobbySocialActionRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbySocialActionRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacySocialAction Action { get; set; }

    /// <summary>The other account. The acting account always comes from the authenticated session.</summary>
    [ProtoMember(4)] public long TargetAccountId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbySocialActionResponse : LegacyMessage
{
    public LegacyMsgLobbySocialActionResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbySocialActionResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public List<LegacySocialPlayer> Friends { get; } = new();
    [ProtoMember(5)] public List<LegacySocialPlayer> IncomingRequests { get; } = new();
    [ProtoMember(6)] public List<LegacySocialPlayer> Blocked { get; } = new();
    [ProtoMember(7)] public List<LegacyChatConversation> Conversations { get; } = new();
}

[ProtoContract]
public sealed class LegacyMsgLobbyChatRequest : LegacyMessage
{
    public LegacyMsgLobbyChatRequest() => ProtocolType = LegacyProtocolValue.MsgLobbyChatRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyChatAction Action { get; set; }
    [ProtoMember(4)] public long PeerAccountId { get; set; }
    [ProtoMember(5)] public string? Body { get; set; }
    [ProtoMember(6)] public long ConversationId { get; set; }
    [ProtoMember(7)] public long LastReadMessageId { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyChatResponse : LegacyMessage
{
    public LegacyMsgLobbyChatResponse() => ProtocolType = LegacyProtocolValue.MsgLobbyChatResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public long ConversationId { get; set; }
    [ProtoMember(5)] public List<LegacyChatMessage> Messages { get; } = new();
    [ProtoMember(6)] public List<LegacySocialPlayer> Friends { get; } = new();
    [ProtoMember(7)] public List<LegacySocialPlayer> IncomingRequests { get; } = new();
    [ProtoMember(8)] public List<LegacySocialPlayer> Blocked { get; } = new();
    [ProtoMember(9)] public List<LegacyChatConversation> Conversations { get; } = new();
}

[ProtoContract]
public sealed class LegacyMsgLobbyAchievementClaimRequest : LegacyMessage
{
    public LegacyMsgLobbyAchievementClaimRequest() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyAchievementClaimRequest;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyAchievementClaimKind Kind { get; set; }
    [ProtoMember(4)] public string? AchievementId { get; set; }
    [ProtoMember(5)] public int AccountLevel { get; set; }
}

[ProtoContract]
public sealed class LegacyMsgLobbyAchievementClaimResponse : LegacyMessage
{
    public LegacyMsgLobbyAchievementClaimResponse() =>
        ProtocolType = LegacyProtocolValue.MsgLobbyAchievementClaimResponse;

    [ProtoMember(1)]
    public override LegacyProtocolValue ProtocolType { get; set; }

    [ProtoMember(2)] public string? RequestId { get; set; }
    [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
    [ProtoMember(4)] public long AchievementXp { get; set; }
    [ProtoMember(5)] public int AchievementLevel { get; set; }
    [ProtoMember(6)] public List<LegacyAchievementProgress> Progress { get; } = new();
    [ProtoMember(7)] public List<string> ClaimedAchievements { get; } = new();
    [ProtoMember(8)] public List<string> ClaimedAccountLevels { get; } = new();
    [ProtoMember(9)] public long AccountXp { get; set; }
    [ProtoMember(10)] public int AccountLevel { get; set; }
}
