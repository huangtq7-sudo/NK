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
    MsgTaskReward = 18
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
