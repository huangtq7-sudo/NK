namespace Naraka.Server.Domain.Quests;

/// <summary>
/// 任务的标准状态链。Locked 与 Available 由配置、前置任务和账号等级推导，从不落库；
/// 落库实例只会沿 Accepted → InProgress → ReadyToSubmit → RewardPending → Claimed 单向前进。
/// </summary>
public enum QuestStatus
{
    Locked = 0,
    Available = 1,
    Accepted = 2,
    InProgress = 3,
    ReadyToSubmit = 4,
    RewardPending = 5,
    Claimed = 6
}

public enum QuestCategory
{
    Main = 0,
    Side = 1,
    Guide = 2,
    Bounty = 3
}

/// <summary>领域规则判定结果。规则拒绝是正常业务分支，不用异常表达。</summary>
public enum QuestRuleOutcome
{
    Applied = 0,

    /// <summary>任务未启用、状态不允许该操作或请求的对象不是当前步骤。</summary>
    NotAvailable = 1,

    /// <summary>前置任务尚未领取奖励。</summary>
    PrerequisiteMissing = 2,

    /// <summary>账号等级不足。</summary>
    LevelTooLow = 3,

    /// <summary>主线或支线同时进行的数量已达上限。</summary>
    LimitReached = 4,

    /// <summary>距离当前步骤开始的服务端时间不足配置的最短秒数。</summary>
    TooEarly = 5,

    /// <summary>奖励已经领取。</summary>
    AlreadyClaimed = 6
}

/// <summary>一个任务目标的规则定义。由应用层从生成配置映射而来，领域层不依赖配置模型。</summary>
public sealed record QuestObjectiveDefinition(
    string ObjectiveId,
    int Order,
    int TargetCount,
    TimeSpan MinCompletionTime);

/// <summary>一个任务的规则定义。</summary>
public sealed record QuestDefinition(
    string QuestId,
    QuestCategory Category,
    string? PrerequisiteQuestId,
    int MinAccountLevel,
    TimeSpan SubmitMinTime,
    IReadOnlyList<QuestObjectiveDefinition> Objectives)
{
    /// <summary>
    /// 一次性任务的实例键就是 QuestId。重复悬赏落地时改为"QuestId@周期"，
    /// 让每个周期都有可区分的领取记录。
    /// </summary>
    public string InstanceKey => QuestId;
}
