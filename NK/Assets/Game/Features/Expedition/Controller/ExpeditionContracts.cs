using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Features.Expedition.Model;

namespace Naraka.Features.Expedition.Controller
{
    /// <summary>远征业务状态。0至14与服务端稳定状态码对齐，100以后仅存在于客户端。</summary>
    public enum ExpeditionOperationStatus
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
        TransportFailure = 100,
        ServerCapabilityMissing = 101
    }

    public readonly struct ExpeditionGatewayResult
    {
        private ExpeditionGatewayResult(
            ExpeditionOperationStatus status,
            ExpeditionSnapshot snapshot,
            ExpeditionDeathSummary death,
            ExpeditionSettlementSummary settlement,
            bool isReplay)
        {
            Status = status;
            Snapshot = snapshot;
            Death = death;
            Settlement = settlement;
            IsReplay = isReplay;
        }

        public ExpeditionOperationStatus Status { get; }

        public ExpeditionSnapshot Snapshot { get; }

        public ExpeditionDeathSummary Death { get; }

        public ExpeditionSettlementSummary Settlement { get; }

        public bool IsReplay { get; }

        public bool IsSuccess => Status == ExpeditionOperationStatus.Success;

        public static ExpeditionGatewayResult Success(
            ExpeditionSnapshot snapshot = null,
            ExpeditionDeathSummary death = null,
            ExpeditionSettlementSummary settlement = null,
            bool isReplay = false) =>
            new ExpeditionGatewayResult(
                ExpeditionOperationStatus.Success, snapshot, death, settlement, isReplay);

        public static ExpeditionGatewayResult Failed(ExpeditionOperationStatus status) =>
            new ExpeditionGatewayResult(status, null, null, null, false);
    }

    /// <summary>
    /// 客户端远征网关。没有AccountId和掉落写入口：身份来自认证连接，掉落只由服务端战斗模块产生。
    /// RequestId由控制器持有，传输失败重试时复用同一个值。
    /// </summary>
    public interface IExpeditionGateway
    {
        UniTask<ExpeditionGatewayResult> GetActiveAsync(
            string requestId,
            CancellationToken cancellationToken);

        UniTask<ExpeditionGatewayResult> StartAsync(
            string requestId,
            CancellationToken cancellationToken);

        UniTask<ExpeditionGatewayResult> RecordDeathAsync(
            string expeditionId,
            string requestId,
            CancellationToken cancellationToken);

        UniTask<ExpeditionGatewayResult> ReturnToLobbyAsync(
            string expeditionId,
            string requestId,
            CancellationToken cancellationToken);
    }

    public interface IExpeditionRequestIdSource
    {
        string NewId();
    }
}
