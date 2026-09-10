using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Lobby.Model;

namespace Naraka.Features.Lobby.Controller
{
    /// <summary>
    /// P1 大厅业务的统一状态码。数值与服务端 <c>LobbyOperationStatus</c> 一一对应；
    /// <see cref="TransportFailure"/> 是客户端侧独有的传输失败，不占用服务端的编号空间。
    /// </summary>
    public enum LobbyOperationStatus
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
        Conflict = 14,

        /// <summary>网络不可达、超时或响应无法解析。永远由客户端产生，服务端不会发送。</summary>
        TransportFailure = 100,

        /// <summary>当前服务器没有部署该功能，请求根本没有发出去。</summary>
        ServerCapabilityMissing = 101
    }

    public readonly struct LobbyProfileResult
    {
        private LobbyProfileResult(LobbyOperationStatus status, LobbyProfileSnapshot snapshot)
        {
            Status = status;
            Snapshot = snapshot;
        }

        public LobbyOperationStatus Status { get; }

        public LobbyProfileSnapshot Snapshot { get; }

        public bool IsSuccess => Status == LobbyOperationStatus.Success;

        public static LobbyProfileResult Success(LobbyProfileSnapshot snapshot) =>
            new LobbyProfileResult(LobbyOperationStatus.Success, snapshot);

        public static LobbyProfileResult Failed(LobbyOperationStatus status) =>
            new LobbyProfileResult(status, default);
    }

    /// <summary>
    /// 账号资料网关。与账号概要一样刻意不接受 accountId：
    /// 操作对象完全由服务端的已认证连接会话决定（ADR-0007）。
    /// </summary>
    public interface ILobbyProfileGateway
    {
        UniTask<LobbyProfileResult> RequestProfileAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 提交头像与头像框。服务端校验 ID 是否存在于配置中，
        /// 校验通过后返回完整资料，客户端据此刷新界面而不是自行推断结果。
        /// </summary>
        UniTask<LobbyProfileResult> SetAppearanceAsync(
            string avatarId,
            string avatarFrameId,
            CancellationToken cancellationToken);
    }

    /// <summary>把状态码翻译成玩家可读的中文提示。集中一处，避免各界面各写一套文案。</summary>
    public static class LobbyOperationMessages
    {
        public static string Describe(LobbyOperationStatus status)
        {
            switch (status)
            {
                case LobbyOperationStatus.Success:
                    return string.Empty;
                case LobbyOperationStatus.Unauthenticated:
                    return "登录状态已失效，请重新登录。";
                case LobbyOperationStatus.InvalidRequest:
                    return "请求无效，请重试。";
                case LobbyOperationStatus.NotFound:
                    return "未找到对应的数据。";
                case LobbyOperationStatus.DatabaseUnavailable:
                    return "服务器数据暂时不可用，请稍后重试。";
                case LobbyOperationStatus.Forbidden:
                    return "当前没有执行该操作的权限。";
                case LobbyOperationStatus.InsufficientCurrency:
                    return "货币不足。";
                case LobbyOperationStatus.InsufficientItems:
                    return "材料不足。";
                case LobbyOperationStatus.InventoryFull:
                    return "仓库容量不足。";
                case LobbyOperationStatus.LimitReached:
                    return "已达到上限。";
                case LobbyOperationStatus.AlreadyClaimed:
                    return "该奖励已经领取过。";
                case LobbyOperationStatus.NotAvailable:
                    return "该内容当前不可用。";
                case LobbyOperationStatus.RateLimited:
                    return "操作过于频繁，请稍后再试。";
                case LobbyOperationStatus.Conflict:
                    return "操作冲突，请重试。";
                case LobbyOperationStatus.TransportFailure:
                    return "无法连接服务器，请检查网络后重试。";
                case LobbyOperationStatus.ServerCapabilityMissing:
                    return "服务器功能尚未升级，该功能暂不可用。";
                default:
                    return "操作失败，请稍后重试。";
            }
        }
    }
}
