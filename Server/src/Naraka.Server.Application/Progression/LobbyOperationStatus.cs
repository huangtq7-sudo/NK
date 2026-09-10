namespace Naraka.Server.Application.Progression;

/// <summary>
/// P1 大厅业务的统一错误码。
///
/// 数值是线级契约：只能在末尾追加，不能改名、重排或复用。
/// 0-5 与 P1.1-A 的 <see cref="Lobby.LobbyAccountSummaryStatus"/> 保持同值，
/// 让两套响应在客户端可以用同一套提示文案。
/// </summary>
public enum LobbyOperationStatus
{
    Success = 0,
    Unauthenticated = 1,
    InvalidRequest = 2,
    NotFound = 3,
    DatabaseUnavailable = 4,
    InternalError = 5,

    /// <summary>请求本身合法，但当前账号无权执行（例如对已屏蔽的玩家发起操作）。</summary>
    Forbidden = 6,

    /// <summary>货币余额不足。</summary>
    InsufficientCurrency = 7,

    /// <summary>材料或物品数量不足。</summary>
    InsufficientItems = 8,

    /// <summary>仓库容量不足。</summary>
    InventoryFull = 9,

    /// <summary>触达限购、补签或其他上限。</summary>
    LimitReached = 10,

    /// <summary>奖励已经领取过。重复领取返回该码而不是再发一次。</summary>
    AlreadyClaimed = 11,

    /// <summary>功能或目标当前不可用（未上架、已达最高等级、无可领取内容）。</summary>
    NotAvailable = 12,

    /// <summary>触发频率限制。</summary>
    RateLimited = 13,

    /// <summary>并发写入冲突，调用方可以原样重试同一个 RequestId。</summary>
    Conflict = 14
}
