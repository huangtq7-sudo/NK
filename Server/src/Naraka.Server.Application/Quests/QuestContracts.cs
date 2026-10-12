using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;
using Naraka.Server.Domain.Quests;

namespace Naraka.Server.Application.Quests;

/// <summary>客户端可以请求的任务操作。线级数值由协议适配器单独映射。</summary>
public enum QuestOperation
{
    GetJournal = 0,
    Accept = 1,
    CompleteDialogue = 2,
    Submit = 3,
    Claim = 4
}

public sealed record QuestObjectiveView(string ObjectiveId, int Progress, int TargetCount, bool IsCompleted);

public sealed record QuestEntryView(
    string QuestId,
    QuestStatus Status,
    IReadOnlyList<QuestObjectiveView> Objectives);

/// <summary>
/// 服务端视角的任务日志。可接取列表由服务端按配置、前置任务、等级与上限推导，
/// 客户端不自行判断任务能否接取。
/// </summary>
public sealed record QuestJournalView(
    IReadOnlyList<QuestEntryView> Quests,
    IReadOnlyList<string> AvailableQuestIds,
    long AccountXp,
    int AccountLevel);

/// <summary>一项已发放的奖励。Kind 取 <c>ConfigQuestRewardKind</c> 的值。</summary>
public sealed record QuestRewardLine(string Kind, string RewardId, long Amount);

/// <summary>一次成功领取的不可变摘要。同一 RequestId 重放时原样返回。</summary>
public sealed record QuestClaimSummary(
    string QuestId,
    IReadOnlyList<QuestRewardLine> Rewards,
    long AccountXpAfter);

/// <summary>领取时写入账号的全部内容。由应用层按服务端配置构造，客户端无法影响。</summary>
public sealed record QuestRewardGrant(
    string QuestId,
    string InstanceKey,
    long AccountXp,
    IReadOnlyList<InventoryDelta> Items,
    IReadOnlyList<CurrencyGrant> Currencies);

/// <summary>在账号锁内对重建的任务日志做出的决定。存储层只负责原子地落地它。</summary>
public sealed record QuestDecision(
    QuestRuleOutcome Outcome,
    QuestInstance? Changed,
    QuestRewardGrant? Grant)
{
    public static QuestDecision Reject(QuestRuleOutcome outcome) => new(outcome, null, null);
}

public sealed record QuestWriteCommand(
    long AccountId,
    string RequestId,
    QuestOperation Operation,
    string QuestId,
    DateTimeOffset Now,
    Func<QuestLog, QuestDecision> Decide);

public enum QuestWriteOutcome
{
    Applied = 0,

    /// <summary>同一 RequestId 已经成功过，返回首次结果，不再写入。</summary>
    Replayed = 1,

    /// <summary>领域规则拒绝，没有任何写入。</summary>
    Rejected = 2,

    InventoryFull = 3,

    /// <summary>领取记录主键已存在：换一个 RequestId 也不可能再领一次。</summary>
    AlreadyClaimed = 4,

    AccountMissing = 5,

    /// <summary>同一 RequestId 曾被用于另一个操作或另一条任务。</summary>
    RequestMismatch = 6
}

public sealed record QuestWriteResult(
    QuestWriteOutcome Outcome,
    QuestRuleOutcome Rule,
    QuestLog? Log,
    QuestClaimSummary? Claim);

public sealed class QuestStorageException : Exception
{
    public QuestStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 任务的原子存储端口。实现必须在一个事务内：校验 RequestId 重放、锁定账号、
/// 重建 <see cref="QuestLog"/>、调用 <see cref="QuestWriteCommand.Decide"/>，
/// 再把状态变更、奖励发放、经验流水、领取记录与幂等记录一起提交。
/// 实现内不得另写任何任务规则。
/// </summary>
public interface IQuestRepository
{
    /// <summary>读取账号的任务日志。账号不存在时返回 null。</summary>
    Task<QuestLog?> LoadAsync(long accountId, CancellationToken cancellationToken);

    Task<QuestWriteResult> ExecuteAsync(QuestWriteCommand command, CancellationToken cancellationToken);
}

/// <summary>只读的活动远征探测。任务只用它判断玩家是否身处开放世界，不读写远征资产。</summary>
public interface IActiveExpeditionProbe
{
    Task<bool> HasActiveExpeditionAsync(long accountId, CancellationToken cancellationToken);
}

public readonly struct QuestResult
{
    private QuestResult(
        LobbyOperationStatus status,
        QuestJournalView? journal,
        QuestClaimSummary? claim,
        bool isReplay)
    {
        Status = status;
        Journal = journal;
        Claim = claim;
        IsReplay = isReplay;
    }

    public LobbyOperationStatus Status { get; }

    /// <summary>当前任务日志。规则拒绝时同样附带，客户端据此重新同步，不在本地猜测状态。</summary>
    public QuestJournalView? Journal { get; }

    public QuestClaimSummary? Claim { get; }

    public bool IsReplay { get; }

    public static QuestResult Success(QuestJournalView journal, QuestClaimSummary? claim = null, bool isReplay = false) =>
        new(LobbyOperationStatus.Success, journal, claim, isReplay);

    public static QuestResult Failed(LobbyOperationStatus status, QuestJournalView? journal = null) =>
        new(status, journal, null, false);
}
