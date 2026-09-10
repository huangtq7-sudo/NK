using System.Collections.Generic;
using ProtoBuf;

namespace Naraka.Infrastructure.Network
{
    internal enum LegacyProtocolValue
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

        // ADR-0007：0-18 属于冻结的 P0 审计范围，不得改名、重排或复用。
        // P1 业务消息从 19 起追加，并与服务端 ApplicationProtocolCatalog 保持一致。
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

    /// <summary>与服务端 LegacySocialAction 数值一一对应。</summary>
    internal enum LegacySocialAction
    {
        None = 0,
        SendRequest = 1,
        AcceptRequest = 2,
        RejectRequest = 3,
        RemoveFriend = 4,
        Block = 5,
        Unblock = 6
    }

    /// <summary>与服务端 LegacyChatAction 数值一一对应。</summary>
    internal enum LegacyChatAction
    {
        None = 0,
        OpenConversation = 1,
        SendMessage = 2,
        MarkRead = 3
    }

    /// <summary>与服务端 LegacySignInClaimKind 数值一一对应。只能追加，不得重排。</summary>
    internal enum LegacySignInClaimKind
    {
        None = 0,
        Today = 1,
        MakeUp = 2,
        Milestone = 3
    }

    /// <summary>与服务端 LegacyAchievementClaimKind 数值一一对应。</summary>
    internal enum LegacyAchievementClaimKind
    {
        None = 0,
        Achievement = 1,
        AccountLevel = 2
    }

    /// <summary>与服务端 LegacyInventoryOperation 数值一一对应。</summary>
    internal enum LegacyInventoryOperation
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

    /// <summary>与服务端 LegacyLobbyOperationStatus 数值一一对应的稳定状态码。</summary>
    internal enum LegacyLobbyOperationStatus
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

    /// <summary>与服务端 LegacyLobbyAccountSummaryStatus 数值一一对应的稳定错误码。</summary>
    internal enum LegacyLobbyAccountSummaryStatus
    {
        Success = 0,
        Unauthenticated = 1,
        InvalidRequest = 2,
        NotFound = 3,
        DatabaseUnavailable = 4,
        InternalError = 5
    }

    internal enum LegacyRegisterResult
    {
        Success,
        Failed,
        AlreadyExist,
        WrongCode,
        Forbidden
    }

    internal enum LegacyLoginResult
    {
        Success,
        Failed,
        WrongPwd,
        UserNotExist,
        TimeoutToken
    }

    internal abstract class LegacyMessage
    {
        public abstract LegacyProtocolValue ProtocolType { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgSecret : LegacyMessage
    {
        public LegacyMsgSecret()
        {
            ProtocolType = LegacyProtocolValue.MsgSecret;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)]
        public string Secret { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgPing : LegacyMessage
    {
        public LegacyMsgPing()
        {
            ProtocolType = LegacyProtocolValue.MsgPing;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgRegister : LegacyMessage
    {
        public LegacyMsgRegister()
        {
            ProtocolType = LegacyProtocolValue.MsgRegister;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)]
        public string Account { get; set; }

        [ProtoMember(3)]
        public string Password { get; set; }

        [ProtoMember(4)]
        public LegacyRegisterResult Result { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLogin : LegacyMessage
    {
        public LegacyMsgLogin()
        {
            ProtocolType = LegacyProtocolValue.MsgLogin;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)]
        public string Account { get; set; }

        [ProtoMember(3)]
        public string Password { get; set; }

        [ProtoMember(4)]
        public LegacyLoginResult Result { get; set; }

        [ProtoMember(5)]
        public int AccountId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyAccountSummaryRequest : LegacyMessage
    {
        public LegacyMsgLobbyAccountSummaryRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyAccountSummaryRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)]
        public string RequestId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyAccountSummaryResponse : LegacyMessage
    {
        public LegacyMsgLobbyAccountSummaryResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyAccountSummaryResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)]
        public string RequestId { get; set; }

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

    [ProtoContract]
    internal sealed class LegacyMsgLobbyProfileRequest : LegacyMessage
    {
        public LegacyMsgLobbyProfileRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyProfileRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)]
        public string RequestId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyProfileResponse : LegacyMessage
    {
        public LegacyMsgLobbyProfileResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyProfileResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public string AvatarId { get; set; }
        [ProtoMember(5)] public string AvatarFrameId { get; set; }
        [ProtoMember(6)] public string SelectedHeroId { get; set; }
        [ProtoMember(7)] public string SelectedWeaponId { get; set; }
        [ProtoMember(8)] public string SelectedPetId { get; set; }
        [ProtoMember(9)] public long AccountXp { get; set; }
        [ProtoMember(10)] public int AccountLevel { get; set; }
        [ProtoMember(11)] public int InventoryTier { get; set; }
        [ProtoMember(12)] public long Copper { get; set; }
        [ProtoMember(13)] public long Silk { get; set; }
        [ProtoMember(14)] public long Gold { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySetAppearanceRequest : LegacyMessage
    {
        public LegacyMsgLobbySetAppearanceRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySetAppearanceRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public string AvatarId { get; set; }
        [ProtoMember(4)] public string AvatarFrameId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySetAppearanceResponse : LegacyMessage
    {
        public LegacyMsgLobbySetAppearanceResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySetAppearanceResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public string AvatarId { get; set; }
        [ProtoMember(5)] public string AvatarFrameId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySetLoadoutRequest : LegacyMessage
    {
        public LegacyMsgLobbySetLoadoutRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySetLoadoutRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public string HeroId { get; set; }
        [ProtoMember(4)] public string WeaponId { get; set; }
        [ProtoMember(5)] public string PetId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySetLoadoutResponse : LegacyMessage
    {
        public LegacyMsgLobbySetLoadoutResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySetLoadoutResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public string HeroId { get; set; }
        [ProtoMember(5)] public string WeaponId { get; set; }
        [ProtoMember(6)] public string PetId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyInventorySlot
    {
        [ProtoMember(1)] public string ItemId { get; set; }
        [ProtoMember(2)] public long Quantity { get; set; }
        [ProtoMember(3)] public int SlotIndex { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyEquipmentSlot
    {
        [ProtoMember(1)] public string SlotKind { get; set; }
        [ProtoMember(2)] public int SlotIndex { get; set; }
        [ProtoMember(3)] public string ItemId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyInventoryRequest : LegacyMessage
    {
        public LegacyMsgLobbyInventoryRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyInventoryRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyInventoryResponse : LegacyMessage
    {
        public LegacyMsgLobbyInventoryResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyInventoryResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public List<LegacyInventorySlot> Slots { get; } = new List<LegacyInventorySlot>();
        [ProtoMember(5)] public List<LegacyEquipmentSlot> Equipment { get; } = new List<LegacyEquipmentSlot>();
        [ProtoMember(6)] public int Capacity { get; set; }
        [ProtoMember(7)] public int Tier { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyInventoryMutateRequest : LegacyMessage
    {
        public LegacyMsgLobbyInventoryMutateRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyInventoryMutateRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyInventoryOperation Operation { get; set; }
        [ProtoMember(4)] public string ItemId { get; set; }
        [ProtoMember(5)] public long Quantity { get; set; }
        [ProtoMember(6)] public string SlotKind { get; set; }
        [ProtoMember(7)] public int SlotIndex { get; set; }
        [ProtoMember(8)] public List<string> ItemOrder { get; } = new List<string>();
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyInventoryMutateResponse : LegacyMessage
    {
        public LegacyMsgLobbyInventoryMutateResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyInventoryMutateResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public List<LegacyInventorySlot> Slots { get; } = new List<LegacyInventorySlot>();
        [ProtoMember(5)] public List<LegacyEquipmentSlot> Equipment { get; } = new List<LegacyEquipmentSlot>();
        [ProtoMember(6)] public int Capacity { get; set; }
        [ProtoMember(7)] public int Tier { get; set; }
        [ProtoMember(8)] public bool HasBalances { get; set; }
        [ProtoMember(9)] public long Copper { get; set; }
        [ProtoMember(10)] public long Silk { get; set; }
        [ProtoMember(11)] public long Gold { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyShopPurchaseCount
    {
        [ProtoMember(1)] public string ProductId { get; set; }
        [ProtoMember(2)] public long PurchasedTotal { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyShopRequest : LegacyMessage
    {
        public LegacyMsgLobbyShopRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyShopRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyShopResponse : LegacyMessage
    {
        public LegacyMsgLobbyShopResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyShopResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public List<LegacyShopPurchaseCount> Purchases { get; } =
            new List<LegacyShopPurchaseCount>();
        [ProtoMember(5)] public long Copper { get; set; }
        [ProtoMember(6)] public long Silk { get; set; }
        [ProtoMember(7)] public long Gold { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyShopPurchaseRequest : LegacyMessage
    {
        public LegacyMsgLobbyShopPurchaseRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyShopPurchaseRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public string ProductId { get; set; }
        [ProtoMember(4)] public int Quantity { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyShopPurchaseResponse : LegacyMessage
    {
        public LegacyMsgLobbyShopPurchaseResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyShopPurchaseResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public List<LegacyShopPurchaseCount> Purchases { get; } =
            new List<LegacyShopPurchaseCount>();
        [ProtoMember(5)] public long Copper { get; set; }
        [ProtoMember(6)] public long Silk { get; set; }
        [ProtoMember(7)] public long Gold { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyAccountWeapon
    {
        [ProtoMember(1)] public string WeaponId { get; set; }
        [ProtoMember(2)] public int Level { get; set; }
        [ProtoMember(3)] public long Proficiency { get; set; }
        [ProtoMember(4)] public long KillCount { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyForgeRequest : LegacyMessage
    {
        public LegacyMsgLobbyForgeRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyForgeRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyForgeResponse : LegacyMessage
    {
        public LegacyMsgLobbyForgeResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyForgeResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public List<LegacyAccountWeapon> Weapons { get; } = new List<LegacyAccountWeapon>();
        [ProtoMember(5)] public List<LegacyInventorySlot> Materials { get; } = new List<LegacyInventorySlot>();
        [ProtoMember(6)] public long Copper { get; set; }
        [ProtoMember(7)] public long Silk { get; set; }
        [ProtoMember(8)] public long Gold { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyForgeUpgradeRequest : LegacyMessage
    {
        public LegacyMsgLobbyForgeUpgradeRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyForgeUpgradeRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public string WeaponId { get; set; }
        [ProtoMember(4)] public int ExpectedLevel { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyForgeUpgradeResponse : LegacyMessage
    {
        public LegacyMsgLobbyForgeUpgradeResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyForgeUpgradeResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public List<LegacyAccountWeapon> Weapons { get; } = new List<LegacyAccountWeapon>();
        [ProtoMember(5)] public List<LegacyInventorySlot> Materials { get; } = new List<LegacyInventorySlot>();
        [ProtoMember(6)] public long Copper { get; set; }
        [ProtoMember(7)] public long Silk { get; set; }
        [ProtoMember(8)] public long Gold { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyGachaReward
    {
        [ProtoMember(1)] public string RewardId { get; set; }
        [ProtoMember(2)] public string ItemId { get; set; }
        [ProtoMember(3)] public int Amount { get; set; }
        [ProtoMember(4)] public string Quality { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyGachaOrder
    {
        [ProtoMember(1)] public string OrderId { get; set; }
        [ProtoMember(2)] public string PoolId { get; set; }
        [ProtoMember(3)] public int PullCount { get; set; }
        [ProtoMember(4)] public bool IsShown { get; set; }
        [ProtoMember(5)] public List<LegacyGachaReward> Rewards { get; } = new List<LegacyGachaReward>();
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyGachaRequest : LegacyMessage
    {
        public LegacyMsgLobbyGachaRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyGachaRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public string PoolId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyGachaResponse : LegacyMessage
    {
        public LegacyMsgLobbyGachaResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyGachaResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public string PoolId { get; set; }
        [ProtoMember(5)] public int PityCounter { get; set; }
        [ProtoMember(6)] public long TotalPulls { get; set; }
        [ProtoMember(7)] public List<LegacyGachaOrder> UnshownOrders { get; } = new List<LegacyGachaOrder>();
        [ProtoMember(8)] public long Copper { get; set; }
        [ProtoMember(9)] public long Silk { get; set; }
        [ProtoMember(10)] public long Gold { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyGachaPullRequest : LegacyMessage
    {
        public LegacyMsgLobbyGachaPullRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyGachaPullRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public string PoolId { get; set; }
        [ProtoMember(4)] public int PullCount { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyGachaPullResponse : LegacyMessage
    {
        public LegacyMsgLobbyGachaPullResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyGachaPullResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public LegacyGachaOrder Order { get; set; }
        [ProtoMember(5)] public int PityCounter { get; set; }
        [ProtoMember(6)] public long TotalPulls { get; set; }
        [ProtoMember(7)] public long Copper { get; set; }
        [ProtoMember(8)] public long Silk { get; set; }
        [ProtoMember(9)] public long Gold { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyGachaAcknowledgeRequest : LegacyMessage
    {
        public LegacyMsgLobbyGachaAcknowledgeRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyGachaAcknowledgeRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public string PoolId { get; set; }
        [ProtoMember(4)] public string OrderId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyGachaAcknowledgeResponse : LegacyMessage
    {
        public LegacyMsgLobbyGachaAcknowledgeResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyGachaAcknowledgeResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public int PityCounter { get; set; }
        [ProtoMember(5)] public List<LegacyGachaOrder> UnshownOrders { get; } = new List<LegacyGachaOrder>();
    }

    [ProtoContract]
    internal sealed class LegacySignInClaim
    {
        [ProtoMember(1)] public int DayIndex { get; set; }
        [ProtoMember(2)] public bool IsMakeup { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySignInRequest : LegacyMessage
    {
        public LegacyMsgLobbySignInRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySignInRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySignInResponse : LegacyMessage
    {
        public LegacyMsgLobbySignInResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySignInResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public long CycleStartDay { get; set; }
        [ProtoMember(5)] public long ServerDay { get; set; }
        [ProtoMember(6)] public int ConsecutiveDays { get; set; }
        [ProtoMember(7)] public List<LegacySignInClaim> Claims { get; } = new List<LegacySignInClaim>();
        [ProtoMember(8)] public List<string> ClaimedMilestones { get; } = new List<string>();
        [ProtoMember(9)] public long MakeupCardCount { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySignInClaimRequest : LegacyMessage
    {
        public LegacyMsgLobbySignInClaimRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySignInClaimRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacySignInClaimKind Kind { get; set; }
        [ProtoMember(4)] public int Target { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySignInClaimResponse : LegacyMessage
    {
        public LegacyMsgLobbySignInClaimResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySignInClaimResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public long CycleStartDay { get; set; }
        [ProtoMember(5)] public long ServerDay { get; set; }
        [ProtoMember(6)] public int ConsecutiveDays { get; set; }
        [ProtoMember(7)] public List<LegacySignInClaim> Claims { get; } = new List<LegacySignInClaim>();
        [ProtoMember(8)] public List<string> ClaimedMilestones { get; } = new List<string>();
        [ProtoMember(9)] public long MakeupCardCount { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyAchievementProgress
    {
        [ProtoMember(1)] public string AchievementId { get; set; }
        [ProtoMember(2)] public long Progress { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyAchievementRequest : LegacyMessage
    {
        public LegacyMsgLobbyAchievementRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyAchievementRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyAchievementResponse : LegacyMessage
    {
        public LegacyMsgLobbyAchievementResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyAchievementResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public long AchievementXp { get; set; }
        [ProtoMember(5)] public int AchievementLevel { get; set; }
        [ProtoMember(6)] public List<LegacyAchievementProgress> Progress { get; } =
            new List<LegacyAchievementProgress>();
        [ProtoMember(7)] public List<string> ClaimedAchievements { get; } = new List<string>();
        [ProtoMember(8)] public List<string> ClaimedAccountLevels { get; } = new List<string>();
        [ProtoMember(9)] public long AccountXp { get; set; }
        [ProtoMember(10)] public int AccountLevel { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyRedDotNode
    {
        [ProtoMember(1)] public string Path { get; set; }
        [ProtoMember(2)] public long Version { get; set; }
        [ProtoMember(3)] public long SeenVersion { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyRedDotRequest : LegacyMessage
    {
        public LegacyMsgLobbyRedDotRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyRedDotRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyRedDotResponse : LegacyMessage
    {
        public LegacyMsgLobbyRedDotResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyRedDotResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public List<LegacyRedDotNode> Nodes { get; } = new List<LegacyRedDotNode>();
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyRedDotSeenRequest : LegacyMessage
    {
        public LegacyMsgLobbyRedDotSeenRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyRedDotSeenRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public string Path { get; set; }
        [ProtoMember(4)] public long SeenVersion { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyRedDotSeenResponse : LegacyMessage
    {
        public LegacyMsgLobbyRedDotSeenResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyRedDotSeenResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public List<LegacyRedDotNode> Nodes { get; } = new List<LegacyRedDotNode>();
    }

    [ProtoContract]
    internal sealed class LegacySocialPlayer
    {
        [ProtoMember(1)] public long AccountId { get; set; }
        [ProtoMember(2)] public string DisplayName { get; set; }
        [ProtoMember(3)] public string AvatarId { get; set; }
        [ProtoMember(4)] public bool IsOnline { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyChatConversation
    {
        [ProtoMember(1)] public long ConversationId { get; set; }
        [ProtoMember(2)] public LegacySocialPlayer Peer { get; set; }
        [ProtoMember(3)] public long LastMessageId { get; set; }
        [ProtoMember(4)] public long LastReadMessageId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyChatMessage
    {
        [ProtoMember(1)] public long MessageId { get; set; }
        [ProtoMember(2)] public long ConversationId { get; set; }
        [ProtoMember(3)] public long SenderAccountId { get; set; }
        [ProtoMember(4)] public string Body { get; set; }
        [ProtoMember(5)] public long SentUnixSeconds { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySocialRequest : LegacyMessage
    {
        public LegacyMsgLobbySocialRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySocialRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySocialResponse : LegacyMessage
    {
        public LegacyMsgLobbySocialResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySocialResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public List<LegacySocialPlayer> Friends { get; } = new List<LegacySocialPlayer>();
        [ProtoMember(5)] public List<LegacySocialPlayer> IncomingRequests { get; } =
            new List<LegacySocialPlayer>();
        [ProtoMember(6)] public List<LegacySocialPlayer> Blocked { get; } = new List<LegacySocialPlayer>();
        [ProtoMember(7)] public List<LegacyChatConversation> Conversations { get; } =
            new List<LegacyChatConversation>();
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySocialSearchRequest : LegacyMessage
    {
        public LegacyMsgLobbySocialSearchRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySocialSearchRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public string DisplayName { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySocialSearchResponse : LegacyMessage
    {
        public LegacyMsgLobbySocialSearchResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySocialSearchResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public LegacySocialPlayer Found { get; set; }
        [ProtoMember(5)] public List<LegacySocialPlayer> Friends { get; } = new List<LegacySocialPlayer>();
        [ProtoMember(6)] public List<LegacySocialPlayer> IncomingRequests { get; } =
            new List<LegacySocialPlayer>();
        [ProtoMember(7)] public List<LegacySocialPlayer> Blocked { get; } = new List<LegacySocialPlayer>();
        [ProtoMember(8)] public List<LegacyChatConversation> Conversations { get; } =
            new List<LegacyChatConversation>();
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySocialActionRequest : LegacyMessage
    {
        public LegacyMsgLobbySocialActionRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySocialActionRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacySocialAction Action { get; set; }
        [ProtoMember(4)] public long TargetAccountId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbySocialActionResponse : LegacyMessage
    {
        public LegacyMsgLobbySocialActionResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbySocialActionResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public List<LegacySocialPlayer> Friends { get; } = new List<LegacySocialPlayer>();
        [ProtoMember(5)] public List<LegacySocialPlayer> IncomingRequests { get; } =
            new List<LegacySocialPlayer>();
        [ProtoMember(6)] public List<LegacySocialPlayer> Blocked { get; } = new List<LegacySocialPlayer>();
        [ProtoMember(7)] public List<LegacyChatConversation> Conversations { get; } =
            new List<LegacyChatConversation>();
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyChatRequest : LegacyMessage
    {
        public LegacyMsgLobbyChatRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyChatRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyChatAction Action { get; set; }
        [ProtoMember(4)] public long PeerAccountId { get; set; }
        [ProtoMember(5)] public string Body { get; set; }
        [ProtoMember(6)] public long ConversationId { get; set; }
        [ProtoMember(7)] public long LastReadMessageId { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyChatResponse : LegacyMessage
    {
        public LegacyMsgLobbyChatResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyChatResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public long ConversationId { get; set; }
        [ProtoMember(5)] public List<LegacyChatMessage> Messages { get; } = new List<LegacyChatMessage>();
        [ProtoMember(6)] public List<LegacySocialPlayer> Friends { get; } = new List<LegacySocialPlayer>();
        [ProtoMember(7)] public List<LegacySocialPlayer> IncomingRequests { get; } =
            new List<LegacySocialPlayer>();
        [ProtoMember(8)] public List<LegacySocialPlayer> Blocked { get; } = new List<LegacySocialPlayer>();
        [ProtoMember(9)] public List<LegacyChatConversation> Conversations { get; } =
            new List<LegacyChatConversation>();
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyAchievementClaimRequest : LegacyMessage
    {
        public LegacyMsgLobbyAchievementClaimRequest()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyAchievementClaimRequest;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyAchievementClaimKind Kind { get; set; }
        [ProtoMember(4)] public string AchievementId { get; set; }
        [ProtoMember(5)] public int AccountLevel { get; set; }
    }

    [ProtoContract]
    internal sealed class LegacyMsgLobbyAchievementClaimResponse : LegacyMessage
    {
        public LegacyMsgLobbyAchievementClaimResponse()
        {
            ProtocolType = LegacyProtocolValue.MsgLobbyAchievementClaimResponse;
        }

        [ProtoMember(1)]
        public override LegacyProtocolValue ProtocolType { get; set; }

        [ProtoMember(2)] public string RequestId { get; set; }
        [ProtoMember(3)] public LegacyLobbyOperationStatus Status { get; set; }
        [ProtoMember(4)] public long AchievementXp { get; set; }
        [ProtoMember(5)] public int AchievementLevel { get; set; }
        [ProtoMember(6)] public List<LegacyAchievementProgress> Progress { get; } =
            new List<LegacyAchievementProgress>();
        [ProtoMember(7)] public List<string> ClaimedAchievements { get; } = new List<string>();
        [ProtoMember(8)] public List<string> ClaimedAccountLevels { get; } = new List<string>();
        [ProtoMember(9)] public long AccountXp { get; set; }
        [ProtoMember(10)] public int AccountLevel { get; set; }
    }
}
