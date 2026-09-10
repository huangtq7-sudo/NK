using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Lobby.Model;

namespace Naraka.Features.Lobby.Controller
{
    /// <summary>
    /// 账号概要加载结果。与服务端稳定错误码一一对应，客户端不自行发明错误语义。
    /// </summary>
    public enum LobbyAccountSummaryStatus
    {
        Success = 0,
        Unauthenticated = 1,
        InvalidRequest = 2,
        NotFound = 3,
        DatabaseUnavailable = 4,
        InternalError = 5,

        /// <summary>网络不可达、超时或响应无法解析，属于客户端侧的传输失败。</summary>
        TransportFailure = 6
    }

    public readonly struct LobbyAccountSummaryResult
    {
        private LobbyAccountSummaryResult(LobbyAccountSummaryStatus status, LobbyAccountSnapshot snapshot)
        {
            Status = status;
            Snapshot = snapshot;
        }

        public LobbyAccountSummaryStatus Status { get; }

        public LobbyAccountSnapshot Snapshot { get; }

        public bool IsSuccess => Status == LobbyAccountSummaryStatus.Success;

        public static LobbyAccountSummaryResult Success(LobbyAccountSnapshot snapshot) =>
            new LobbyAccountSummaryResult(LobbyAccountSummaryStatus.Success, snapshot);

        public static LobbyAccountSummaryResult Failed(LobbyAccountSummaryStatus status) =>
            new LobbyAccountSummaryResult(status, default);
    }

    /// <summary>
    /// 大厅账号概要网关。刻意不接受 accountId 参数：查询对象完全由服务端的已认证连接会话决定，
    /// 客户端无法选择读取谁的数据（ADR-0007）。
    /// </summary>
    public interface ILobbyAccountGateway
    {
        UniTask<LobbyAccountSummaryResult> RequestAccountSummaryAsync(CancellationToken cancellationToken);
    }
}
