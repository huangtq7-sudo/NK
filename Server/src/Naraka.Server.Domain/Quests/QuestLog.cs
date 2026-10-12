namespace Naraka.Server.Domain.Quests;

/// <summary>一个目标的进度。</summary>
public sealed class QuestObjectiveProgress
{
    public QuestObjectiveProgress(string objectiveId, int progress, DateTimeOffset? completedAt)
    {
        if (string.IsNullOrWhiteSpace(objectiveId))
        {
            throw new ArgumentException("ObjectiveId cannot be empty.", nameof(objectiveId));
        }

        if (progress < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(progress));
        }

        ObjectiveId = objectiveId;
        Progress = progress;
        CompletedAt = completedAt;
    }

    public string ObjectiveId { get; }

    public int Progress { get; internal set; }

    public DateTimeOffset? CompletedAt { get; internal set; }

    public bool IsCompleted => CompletedAt is not null;
}

/// <summary>
/// 一个已接取的任务实例。状态只能前进：领取后不可能回到待领取，提交后不可能回到进行中。
/// </summary>
public sealed class QuestInstance
{
    private readonly List<QuestObjectiveProgress> _objectives;

    private QuestInstance(
        string instanceKey,
        string questId,
        QuestStatus status,
        DateTimeOffset acceptedAt,
        DateTimeOffset stepStartedAt,
        DateTimeOffset? submittedAt,
        DateTimeOffset? claimedAt,
        IEnumerable<QuestObjectiveProgress> objectives)
    {
        InstanceKey = instanceKey;
        QuestId = questId;
        Status = status;
        AcceptedAt = acceptedAt;
        StepStartedAt = stepStartedAt;
        SubmittedAt = submittedAt;
        ClaimedAt = claimedAt;
        _objectives = objectives.ToList();
    }

    public string InstanceKey { get; }

    public string QuestId { get; }

    public QuestStatus Status { get; private set; }

    public DateTimeOffset AcceptedAt { get; }

    /// <summary>当前步骤开始的服务端时间：当前目标成为当前目标的时刻，或全部目标完成的时刻。</summary>
    public DateTimeOffset StepStartedAt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? ClaimedAt { get; private set; }

    public IReadOnlyList<QuestObjectiveProgress> Objectives => _objectives;

    /// <summary>仍占用主线/支线进行名额的状态。待领取也算：奖励没拿走，这条线还没结束。</summary>
    public bool IsOpen => Status is QuestStatus.Accepted or QuestStatus.InProgress or
        QuestStatus.ReadyToSubmit or QuestStatus.RewardPending;

    public static QuestInstance Restore(
        string instanceKey,
        string questId,
        QuestStatus status,
        DateTimeOffset acceptedAt,
        DateTimeOffset stepStartedAt,
        DateTimeOffset? submittedAt,
        DateTimeOffset? claimedAt,
        IEnumerable<QuestObjectiveProgress> objectives)
    {
        if (string.IsNullOrWhiteSpace(instanceKey) || instanceKey.Length > 96)
        {
            throw new ArgumentException("Quest instance key must contain 1-96 characters.", nameof(instanceKey));
        }

        if (string.IsNullOrWhiteSpace(questId) || questId.Length > 64)
        {
            throw new ArgumentException("QuestId must contain 1-64 characters.", nameof(questId));
        }

        if (status is QuestStatus.Locked or QuestStatus.Available || !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Only accepted quests are persisted.");
        }

        ArgumentNullException.ThrowIfNull(objectives);
        return new QuestInstance(
            instanceKey, questId, status, acceptedAt, stepStartedAt, submittedAt, claimedAt, objectives);
    }

    internal static QuestInstance Accept(QuestDefinition definition, DateTimeOffset now) =>
        new(
            definition.InstanceKey,
            definition.QuestId,
            QuestStatus.Accepted,
            now,
            now,
            null,
            null,
            definition.Objectives
                .OrderBy(objective => objective.Order)
                .Select(objective => new QuestObjectiveProgress(objective.ObjectiveId, 0, null)));

    internal QuestRuleOutcome RecordProgress(
        QuestDefinition definition,
        string objectiveId,
        int amount,
        DateTimeOffset now)
    {
        if (Status is not (QuestStatus.Accepted or QuestStatus.InProgress))
        {
            return QuestRuleOutcome.NotAvailable;
        }

        var current = CurrentObjective(definition);
        if (current is null || !string.Equals(current.ObjectiveId, objectiveId, StringComparison.Ordinal))
        {
            return QuestRuleOutcome.NotAvailable;
        }

        if (now - StepStartedAt < current.MinCompletionTime)
        {
            return QuestRuleOutcome.TooEarly;
        }

        var progress = _objectives.First(value => value.ObjectiveId == current.ObjectiveId);
        progress.Progress = Math.Min(current.TargetCount, checked(progress.Progress + amount));
        if (progress.Progress >= current.TargetCount)
        {
            progress.CompletedAt = now;
            StepStartedAt = now;
        }

        Status = _objectives.All(value => value.IsCompleted)
            ? QuestStatus.ReadyToSubmit
            : QuestStatus.InProgress;
        return QuestRuleOutcome.Applied;
    }

    internal QuestRuleOutcome Submit(QuestDefinition definition, DateTimeOffset now)
    {
        if (Status != QuestStatus.ReadyToSubmit)
        {
            return QuestRuleOutcome.NotAvailable;
        }

        if (now - StepStartedAt < definition.SubmitMinTime)
        {
            return QuestRuleOutcome.TooEarly;
        }

        Status = QuestStatus.RewardPending;
        SubmittedAt = now;
        return QuestRuleOutcome.Applied;
    }

    internal QuestRuleOutcome Claim(DateTimeOffset now)
    {
        if (Status == QuestStatus.Claimed)
        {
            return QuestRuleOutcome.AlreadyClaimed;
        }

        if (Status != QuestStatus.RewardPending)
        {
            return QuestRuleOutcome.NotAvailable;
        }

        Status = QuestStatus.Claimed;
        ClaimedAt = now;
        return QuestRuleOutcome.Applied;
    }

    /// <summary>按顺序第一个未完成的目标。全部完成时为 null。</summary>
    public QuestObjectiveDefinition? CurrentObjective(QuestDefinition definition)
    {
        foreach (var objective in definition.Objectives.OrderBy(value => value.Order))
        {
            var progress = _objectives.FirstOrDefault(value => value.ObjectiveId == objective.ObjectiveId);
            if (progress is null || !progress.IsCompleted)
            {
                return objective;
            }
        }

        return null;
    }
}

/// <summary>
/// 一个账号的全部任务实例。接取上限、前置任务和顺序推进都依赖账号内多条任务的状态，
/// 因此规则落在这个账号级聚合上，由存储层在同一事务、同一账号锁内重建并调用。
/// </summary>
public sealed class QuestLog
{
    /// <summary>主线同时只能有 1 条进行中。</summary>
    public const int MaximumOpenMainQuests = 1;

    /// <summary>支线最多同时接取 5 条。</summary>
    public const int MaximumOpenSideQuests = 5;

    private readonly Dictionary<string, QuestInstance> _instances;

    public QuestLog(long accountId, long accountXp, IEnumerable<QuestInstance> instances)
    {
        if (accountId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }

        if (accountXp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountXp));
        }

        ArgumentNullException.ThrowIfNull(instances);
        AccountId = accountId;
        AccountXp = accountXp;
        _instances = instances.ToDictionary(value => value.InstanceKey, StringComparer.Ordinal);
    }

    public long AccountId { get; }

    public long AccountXp { get; }

    public IReadOnlyCollection<QuestInstance> Instances => _instances.Values;

    public QuestInstance? Find(string instanceKey) =>
        _instances.TryGetValue(instanceKey ?? string.Empty, out var instance) ? instance : null;

    public bool IsClaimed(string questId) =>
        _instances.Values.Any(value => value.QuestId == questId && value.Status == QuestStatus.Claimed);

    /// <summary>任务对当前账号是否可接取。纯判定，不修改任何状态。</summary>
    public QuestRuleOutcome CanAccept(
        QuestDefinition definition,
        int accountLevel,
        Func<string, QuestCategory?> categoryOf)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(categoryOf);

        if (_instances.ContainsKey(definition.InstanceKey))
        {
            return QuestRuleOutcome.NotAvailable;
        }

        if (!string.IsNullOrEmpty(definition.PrerequisiteQuestId) && !IsClaimed(definition.PrerequisiteQuestId))
        {
            return QuestRuleOutcome.PrerequisiteMissing;
        }

        if (accountLevel < definition.MinAccountLevel)
        {
            return QuestRuleOutcome.LevelTooLow;
        }

        var limit = definition.Category switch
        {
            QuestCategory.Main => MaximumOpenMainQuests,
            QuestCategory.Side => MaximumOpenSideQuests,
            _ => int.MaxValue
        };
        var open = _instances.Values.Count(value => value.IsOpen && categoryOf(value.QuestId) == definition.Category);
        return open >= limit ? QuestRuleOutcome.LimitReached : QuestRuleOutcome.Applied;
    }

    public QuestRuleOutcome Accept(
        QuestDefinition definition,
        int accountLevel,
        Func<string, QuestCategory?> categoryOf,
        DateTimeOffset now,
        out QuestInstance? accepted)
    {
        accepted = null;
        var outcome = CanAccept(definition, accountLevel, categoryOf);
        if (outcome != QuestRuleOutcome.Applied)
        {
            return outcome;
        }

        accepted = QuestInstance.Accept(definition, now);
        _instances.Add(accepted.InstanceKey, accepted);
        return QuestRuleOutcome.Applied;
    }

    public QuestRuleOutcome RecordObjectiveProgress(
        QuestDefinition definition,
        string objectiveId,
        int amount,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (amount <= 0 || string.IsNullOrWhiteSpace(objectiveId))
        {
            return QuestRuleOutcome.NotAvailable;
        }

        var instance = Find(definition.InstanceKey);
        return instance is null
            ? QuestRuleOutcome.NotAvailable
            : instance.RecordProgress(definition, objectiveId, amount, now);
    }

    public QuestRuleOutcome Submit(QuestDefinition definition, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var instance = Find(definition.InstanceKey);
        return instance is null ? QuestRuleOutcome.NotAvailable : instance.Submit(definition, now);
    }

    public QuestRuleOutcome Claim(QuestDefinition definition, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var instance = Find(definition.InstanceKey);
        return instance is null ? QuestRuleOutcome.NotAvailable : instance.Claim(now);
    }
}
