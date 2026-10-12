using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Domain;
using Naraka.Features.Quest.Model;

namespace Naraka.Features.Quest.Controller
{
    /// <summary>任务业务状态。0至14与服务端稳定状态码对齐，100以后仅存在于客户端。</summary>
    public enum QuestOperationStatus
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
        ServerCapabilityMissing = 101,
        ConfigUnavailable = 102
    }

    public readonly struct QuestGatewayResult
    {
        private QuestGatewayResult(
            QuestOperationStatus status,
            QuestJournalSnapshot journal,
            QuestClaimSummary claim,
            bool isReplay)
        {
            Status = status;
            Journal = journal;
            Claim = claim;
            IsReplay = isReplay;
        }

        public QuestOperationStatus Status { get; }

        /// <summary>服务端日志。规则拒绝时同样可能附带，用来重新同步。</summary>
        public QuestJournalSnapshot Journal { get; }

        public QuestClaimSummary Claim { get; }

        public bool IsReplay { get; }

        public bool IsSuccess => Status == QuestOperationStatus.Success;

        public static QuestGatewayResult Success(
            QuestJournalSnapshot journal,
            QuestClaimSummary claim = null,
            bool isReplay = false) =>
            new QuestGatewayResult(QuestOperationStatus.Success, journal, claim, isReplay);

        public static QuestGatewayResult Failed(QuestOperationStatus status, QuestJournalSnapshot journal = null) =>
            new QuestGatewayResult(status, journal, null, false);
    }

    /// <summary>
    /// 客户端任务网关。请求只有意图：哪条任务、哪个目标、读完了哪段对话。
    /// 没有账号、奖励、数量或经验字段；身份来自认证连接，奖励只来自服务端配置。
    /// </summary>
    public interface IQuestGateway
    {
        UniTask<QuestGatewayResult> GetJournalAsync(string requestId, CancellationToken cancellationToken);

        UniTask<QuestGatewayResult> AcceptAsync(
            string requestId,
            string questId,
            string dialogueId,
            CancellationToken cancellationToken);

        UniTask<QuestGatewayResult> CompleteDialogueAsync(
            string requestId,
            string questId,
            string objectiveId,
            string dialogueId,
            CancellationToken cancellationToken);

        UniTask<QuestGatewayResult> SubmitAsync(
            string requestId,
            string questId,
            string dialogueId,
            CancellationToken cancellationToken);

        UniTask<QuestGatewayResult> ClaimAsync(
            string requestId,
            string questId,
            CancellationToken cancellationToken);
    }

    public interface IQuestRequestIdSource
    {
        string NewId();
    }

    public sealed class GuidQuestRequestIdSource : IQuestRequestIdSource
    {
        public string NewId() => new RequestId(Guid.NewGuid().ToString("N")).Value;
    }

    /// <summary>
    /// 交互键的输入来源。由 Input System 适配器实现：Player 动作表里存在 Interact 动作时才会报告按下；
    /// 动作缺失时恒为 false，功能静默不可用，而不是改用旧输入接口。
    /// </summary>
    public interface IInteractInputSource
    {
        bool InteractPressed { get; }
    }

    /// <summary>组合根缺少输入适配器时的空实现：交互静默不可用，其余功能照常。</summary>
    public sealed class NoInteractInputSource : IInteractInputSource
    {
        public bool InteractPressed => false;
    }
}
