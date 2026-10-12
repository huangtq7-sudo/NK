using Naraka.Config;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;
using Naraka.Server.Domain.Quests;

namespace Naraka.Server.Application.Quests;

/// <summary>
/// 任务用例边界。身份只来自认证连接；请求里只有意图（哪条任务、哪个目标、读完了哪段对话），
/// 没有账号、奖励、数量或经验。
///
/// 信任边界（ADR-0023）：对话目标是"客户端声称读完 + 服务端核对顺序、对话归属、活动远征与最短时长"。
/// 服务端不知道玩家站在哪，因此它只适用于低价值、一次性、不可刷的对话目标；
/// 击杀、采集等战斗目标在可信战斗事实落地前由配置编译器禁止启用。
/// </summary>
public sealed class QuestService(
    IQuestRepository repository,
    IActiveExpeditionProbe expeditions,
    GameConfig config,
    TimeProvider? timeProvider = null)
{
    private const int MaximumIdLength = 64;

    private readonly IQuestRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    private readonly IActiveExpeditionProbe _expeditions =
        expeditions ?? throw new ArgumentNullException(nameof(expeditions));

    private readonly GameConfig _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly QuestRequestThrottle _throttle = new(timeProvider ?? TimeProvider.System);

    public async Task<QuestResult> GetJournalAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return QuestResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (!_throttle.TryAcquire(accountId, isWrite: false))
        {
            return QuestResult.Failed(LobbyOperationStatus.RateLimited);
        }

        return await GuardAsync(async () =>
        {
            var log = await _repository.LoadAsync(accountId, cancellationToken);
            return log is null
                ? QuestResult.Failed(LobbyOperationStatus.NotFound)
                : QuestResult.Success(BuildJournal(log));
        });
    }

    /// <summary>读完接取对话最后一句即请求接取。对话必须是该任务配置的接取对话。</summary>
    public Task<QuestResult> AcceptAsync(
        long accountId,
        string? requestId,
        string? questId,
        string? dialogueId,
        CancellationToken cancellationToken)
    {
        if (!ValidCommon(accountId, requestId, questId) || !ValidId(dialogueId) ||
            !TryGetActiveQuest(questId!, out var quest) ||
            !string.Equals(quest.OfferDialogueId, dialogueId, StringComparison.Ordinal))
        {
            return Task.FromResult(QuestResult.Failed(LobbyOperationStatus.InvalidRequest));
        }

        var definition = ToDefinition(quest);
        return ExecuteInWorldAsync(
            accountId,
            requestId!,
            QuestOperation.Accept,
            quest.QuestId,
            (log, now) =>
            {
                var outcome = log.Accept(
                    definition, _config.ResolveAccountLevel(log.AccountXp), CategoryOf, now, out var accepted);
                return new QuestDecision(outcome, accepted, null);
            },
            cancellationToken);
    }

    /// <summary>读完对话目标的对话即请求完成该目标。目标必须是当前顺序目标且为已实现的对话类型。</summary>
    public Task<QuestResult> CompleteDialogueAsync(
        long accountId,
        string? requestId,
        string? questId,
        string? objectiveId,
        string? dialogueId,
        CancellationToken cancellationToken)
    {
        if (!ValidCommon(accountId, requestId, questId) || !ValidId(objectiveId) || !ValidId(dialogueId) ||
            !TryGetActiveQuest(questId!, out var quest))
        {
            return Task.FromResult(QuestResult.Failed(LobbyOperationStatus.InvalidRequest));
        }

        var objective = _config.GetQuestObjectives(quest.QuestId)
            .FirstOrDefault(value => string.Equals(value.ObjectiveId, objectiveId, StringComparison.Ordinal));
        if (objective is null ||
            !objective.IsActive ||
            objective.ObjectiveType != ConfigQuestObjectiveType.Talk ||
            !ConfigQuestObjectiveType.IsImplemented(objective.ObjectiveType) ||
            !string.Equals(objective.TargetDialogueId, dialogueId, StringComparison.Ordinal))
        {
            return Task.FromResult(QuestResult.Failed(LobbyOperationStatus.InvalidRequest));
        }

        var definition = ToDefinition(quest);
        return ExecuteInWorldAsync(
            accountId,
            requestId!,
            QuestOperation.CompleteDialogue,
            quest.QuestId,
            (log, now) =>
            {
                var outcome = log.RecordObjectiveProgress(definition, objective.ObjectiveId, 1, now);
                return new QuestDecision(outcome, log.Find(definition.InstanceKey), null);
            },
            cancellationToken);
    }

    /// <summary>读完提交对话即请求提交。提交后奖励进入待领取，必须由玩家手动领取。</summary>
    public Task<QuestResult> SubmitAsync(
        long accountId,
        string? requestId,
        string? questId,
        string? dialogueId,
        CancellationToken cancellationToken)
    {
        if (!ValidCommon(accountId, requestId, questId) || !ValidId(dialogueId) ||
            !TryGetActiveQuest(questId!, out var quest) ||
            !string.Equals(quest.SubmitDialogueId, dialogueId, StringComparison.Ordinal))
        {
            return Task.FromResult(QuestResult.Failed(LobbyOperationStatus.InvalidRequest));
        }

        var definition = ToDefinition(quest);
        return ExecuteInWorldAsync(
            accountId,
            requestId!,
            QuestOperation.Submit,
            quest.QuestId,
            (log, now) =>
            {
                var outcome = log.Submit(definition, now);
                return new QuestDecision(outcome, log.Find(definition.InstanceKey), null);
            },
            cancellationToken);
    }

    /// <summary>
    /// 手动领取。奖励、经验和数量全部来自服务端配置；同一实例的领取记录主键保证
    /// 换 RequestId 也只能领一次。领取写入账号，不进入远征临时背包，死亡不影响。
    /// </summary>
    public async Task<QuestResult> ClaimAsync(
        long accountId,
        string? requestId,
        string? questId,
        CancellationToken cancellationToken)
    {
        // 已领取奖励的任务即使后来停用，也不能再领；待领取的任务停用后仍允许领取，奖励已经赚到。
        if (!ValidCommon(accountId, requestId, questId) ||
            !_config.TryGetQuest(questId!, out var quest) || quest is null)
        {
            return QuestResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        if (!_throttle.TryAcquire(accountId, isWrite: true))
        {
            return QuestResult.Failed(LobbyOperationStatus.RateLimited);
        }

        var definition = ToDefinition(quest);
        var grant = BuildGrant(quest, definition.InstanceKey);
        return await ExecuteAsync(
            accountId,
            requestId!,
            QuestOperation.Claim,
            quest.QuestId,
            (log, now) =>
            {
                var outcome = log.Claim(definition, now);
                return outcome == QuestRuleOutcome.Applied
                    ? new QuestDecision(outcome, log.Find(definition.InstanceKey), grant)
                    : QuestDecision.Reject(outcome);
            },
            cancellationToken);
    }

    /// <summary>
    /// 接取、对话目标与提交只在开放世界里发生：账号必须有活动远征。
    /// 这只证明玩家进入了世界，不证明玩家站在 NPC 旁边，见类注释的信任边界。
    /// </summary>
    private async Task<QuestResult> ExecuteInWorldAsync(
        long accountId,
        string requestId,
        QuestOperation operation,
        string questId,
        Func<QuestLog, DateTimeOffset, QuestDecision> decide,
        CancellationToken cancellationToken)
    {
        if (!_throttle.TryAcquire(accountId, isWrite: true))
        {
            return QuestResult.Failed(LobbyOperationStatus.RateLimited);
        }

        return await GuardAsync(async () =>
        {
            if (!await _expeditions.HasActiveExpeditionAsync(accountId, cancellationToken))
            {
                var log = await _repository.LoadAsync(accountId, cancellationToken);
                return QuestResult.Failed(
                    log is null ? LobbyOperationStatus.NotFound : LobbyOperationStatus.NotAvailable,
                    log is null ? null : BuildJournal(log));
            }

            return await ExecuteAsync(accountId, requestId, operation, questId, decide, cancellationToken);
        });
    }

    private Task<QuestResult> ExecuteAsync(
        long accountId,
        string requestId,
        QuestOperation operation,
        string questId,
        Func<QuestLog, DateTimeOffset, QuestDecision> decide,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var command = new QuestWriteCommand(
            accountId, requestId, operation, questId, now, log => decide(log, now));

        return GuardAsync(async () =>
        {
            var result = await _repository.ExecuteAsync(command, cancellationToken);
            var journal = result.Log is null ? null : BuildJournal(result.Log);
            return result.Outcome switch
            {
                QuestWriteOutcome.Applied when journal is not null => QuestResult.Success(journal, result.Claim),
                QuestWriteOutcome.Replayed when journal is not null =>
                    QuestResult.Success(journal, result.Claim, isReplay: true),
                QuestWriteOutcome.Rejected => QuestResult.Failed(MapRule(result.Rule), journal),
                QuestWriteOutcome.InventoryFull => QuestResult.Failed(LobbyOperationStatus.InventoryFull, journal),
                QuestWriteOutcome.AlreadyClaimed => QuestResult.Failed(LobbyOperationStatus.AlreadyClaimed, journal),
                QuestWriteOutcome.AccountMissing => QuestResult.Failed(LobbyOperationStatus.NotFound),
                QuestWriteOutcome.RequestMismatch => QuestResult.Failed(LobbyOperationStatus.InvalidRequest),
                _ => QuestResult.Failed(LobbyOperationStatus.InternalError)
            };
        });
    }

    /// <summary>把服务端日志转换为只读视图，并由服务端推导可接取列表。</summary>
    public QuestJournalView BuildJournal(QuestLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var level = _config.ResolveAccountLevel(log.AccountXp);
        var entries = new List<QuestEntryView>();
        foreach (var instance in log.Instances.OrderBy(value => value.QuestId, StringComparer.Ordinal))
        {
            if (!_config.TryGetQuest(instance.QuestId, out var quest) || quest is null)
            {
                continue;
            }

            var objectives = _config.GetQuestObjectives(quest.QuestId)
                .Select(objective =>
                {
                    var progress = instance.Objectives.FirstOrDefault(value => value.ObjectiveId == objective.ObjectiveId);
                    return new QuestObjectiveView(
                        objective.ObjectiveId,
                        progress?.Progress ?? 0,
                        objective.TargetCount,
                        progress?.IsCompleted ?? false);
                })
                .ToArray();
            entries.Add(new QuestEntryView(instance.QuestId, instance.Status, objectives));
        }

        var available = _config.QuestsInDisplayOrder
            .Where(quest => quest.IsActive &&
                            Array.IndexOf(ConfigQuestRepeatRule.Implemented, quest.RepeatRule) >= 0 &&
                            log.CanAccept(ToDefinition(quest), level, CategoryOf) == QuestRuleOutcome.Applied)
            .Select(quest => quest.QuestId)
            .ToArray();

        return new QuestJournalView(entries, available, log.AccountXp, level);
    }

    private QuestRewardGrant BuildGrant(QuestConfig quest, string instanceKey)
    {
        long xp = 0;
        var items = new List<InventoryDelta>();
        var currencies = new List<CurrencyGrant>();
        foreach (var reward in _config.GetQuestRewards(quest.QuestId))
        {
            switch (reward.RewardKind)
            {
                case ConfigQuestRewardKind.AccountXp:
                    xp = checked(xp + reward.Amount);
                    break;
                case ConfigQuestRewardKind.Item:
                    items.Add(new InventoryDelta(reward.ItemId, reward.Amount));
                    break;
                case ConfigQuestRewardKind.Currency:
                    currencies.Add(new CurrencyGrant(reward.CurrencyId, reward.Amount));
                    break;
            }
        }

        return new QuestRewardGrant(quest.QuestId, instanceKey, xp, items, currencies);
    }

    private QuestDefinition ToDefinition(QuestConfig quest) => new(
        quest.QuestId,
        ParseCategory(quest.Category) ?? QuestCategory.Side,
        string.IsNullOrEmpty(quest.PrerequisiteQuestId) ? null : quest.PrerequisiteQuestId,
        Math.Max(1, quest.MinAccountLevel),
        TimeSpan.FromSeconds(Math.Max(0f, quest.SubmitMinSeconds)),
        _config.GetQuestObjectives(quest.QuestId)
            .Select(objective => new QuestObjectiveDefinition(
                objective.ObjectiveId,
                objective.Order,
                objective.TargetCount,
                TimeSpan.FromSeconds(Math.Max(0f, objective.MinCompletionSeconds))))
            .ToArray());

    private QuestCategory? CategoryOf(string questId) =>
        _config.TryGetQuest(questId, out var quest) && quest is not null ? ParseCategory(quest.Category) : null;

    private static QuestCategory? ParseCategory(string category) => category switch
    {
        ConfigQuestCategory.Main => QuestCategory.Main,
        ConfigQuestCategory.Side => QuestCategory.Side,
        ConfigQuestCategory.Guide => QuestCategory.Guide,
        ConfigQuestCategory.Bounty => QuestCategory.Bounty,
        _ => null
    };

    private bool TryGetActiveQuest(string questId, out QuestConfig quest)
    {
        if (_config.TryGetQuest(questId, out var found) && found is not null && found.IsActive &&
            Array.IndexOf(ConfigQuestRepeatRule.Implemented, found.RepeatRule) >= 0)
        {
            quest = found;
            return true;
        }

        quest = null!;
        return false;
    }

    private static LobbyOperationStatus MapRule(QuestRuleOutcome outcome) => outcome switch
    {
        QuestRuleOutcome.LimitReached => LobbyOperationStatus.LimitReached,
        QuestRuleOutcome.TooEarly => LobbyOperationStatus.RateLimited,
        QuestRuleOutcome.AlreadyClaimed => LobbyOperationStatus.AlreadyClaimed,
        _ => LobbyOperationStatus.NotAvailable
    };

    private static async Task<QuestResult> GuardAsync(Func<Task<QuestResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QuestStorageException)
        {
            return QuestResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return QuestResult.Failed(LobbyOperationStatus.InternalError);
        }
    }

    private static bool ValidCommon(long accountId, string? requestId, string? questId) =>
        accountId > 0 && ValidId(requestId) && ValidId(questId);

    private static bool ValidId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumIdLength;
}

/// <summary>
/// 每个账号的任务请求频率下限。服务端在 Select 循环里同步处理请求，每个写请求都是一次数据库事务，
/// 因此对单个账号限流，防止连点或脚本把数据库占满。条目数有上限，超出时清理过期条目。
/// </summary>
public sealed class QuestRequestThrottle(TimeProvider time)
{
    public static readonly TimeSpan MinimumWriteInterval = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan MinimumReadInterval = TimeSpan.FromMilliseconds(500);
    private const int MaximumTrackedAccounts = 1024;
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly Dictionary<long, (DateTimeOffset Read, DateTimeOffset Write)> _last = new();

    public bool TryAcquire(long accountId, bool isWrite)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_last.Count >= MaximumTrackedAccounts)
            {
                foreach (var stale in _last
                             .Where(pair => now - Max(pair.Value.Read, pair.Value.Write) > EntryLifetime)
                             .Select(pair => pair.Key)
                             .ToArray())
                {
                    _last.Remove(stale);
                }
            }

            _last.TryGetValue(accountId, out var entry);
            var previous = isWrite ? entry.Write : entry.Read;
            var interval = isWrite ? MinimumWriteInterval : MinimumReadInterval;
            if (previous != default && now - previous < interval)
            {
                return false;
            }

            _last[accountId] = isWrite ? (entry.Read, now) : (now, entry.Write);
            return true;
        }
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;
}
