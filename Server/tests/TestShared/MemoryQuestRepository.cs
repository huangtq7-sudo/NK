using Naraka.Server.Application.Quests;
using Naraka.Server.Domain.Quests;

namespace Naraka.Server.Tests.Shared;

/// <summary>
/// 测试用的任务原子存储。与 MySQL 实现遵守同一契约：先查 RequestId 重放，再重建日志并调用决定，
/// 规则拒绝不写入，领取记录按实例键唯一。每次读取都返回副本，模拟"事务外看不到未提交的修改"。
/// </summary>
internal sealed class MemoryQuestRepository : IQuestRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<long, long> _accountXp = new();
    private readonly Dictionary<(long Account, string Key), QuestInstance> _instances = new();
    private readonly Dictionary<(long Account, string RequestId), (QuestOperation Operation, string QuestId, QuestClaimSummary? Claim)> _requests = new();
    private readonly HashSet<(long Account, string Key)> _claims = new();

    public long MaximumAccountXp { get; set; } = 23_200;

    /// <summary>仓库还能放下的新物品种类数。用于模拟仓库已满。</summary>
    public int FreeInventorySlots { get; set; } = int.MaxValue;

    public bool FailWrites { get; set; }

    public int Writes { get; private set; }

    public List<QuestRewardGrant> Grants { get; } = new();

    public void AddAccount(long accountId, long accountXp = 0)
    {
        lock (_gate)
        {
            _accountXp[accountId] = accountXp;
        }
    }

    public long AccountXp(long accountId)
    {
        lock (_gate)
        {
            return _accountXp[accountId];
        }
    }

    public QuestStatus? StatusOf(long accountId, string questId)
    {
        lock (_gate)
        {
            return _instances.TryGetValue((accountId, questId), out var instance) ? instance.Status : null;
        }
    }

    public Task<QuestLog?> LoadAsync(long accountId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(Snapshot(accountId));
        }
    }

    public Task<QuestWriteResult> ExecuteAsync(QuestWriteCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (FailWrites)
            {
                throw new QuestStorageException("Simulated storage failure.");
            }

            if (_requests.TryGetValue((command.AccountId, command.RequestId), out var previous))
            {
                return Task.FromResult(
                    previous.Operation == command.Operation &&
                    string.Equals(previous.QuestId, command.QuestId, StringComparison.Ordinal)
                        ? new QuestWriteResult(
                            QuestWriteOutcome.Replayed, QuestRuleOutcome.Applied, Snapshot(command.AccountId), previous.Claim)
                        : new QuestWriteResult(QuestWriteOutcome.RequestMismatch, QuestRuleOutcome.NotAvailable, null, null));
            }

            var log = Snapshot(command.AccountId);
            if (log is null)
            {
                return Task.FromResult(new QuestWriteResult(
                    QuestWriteOutcome.AccountMissing, QuestRuleOutcome.NotAvailable, null, null));
            }

            var decision = command.Decide(log);
            if (decision.Outcome != QuestRuleOutcome.Applied || decision.Changed is null)
            {
                return Task.FromResult(new QuestWriteResult(
                    decision.Outcome == QuestRuleOutcome.AlreadyClaimed
                        ? QuestWriteOutcome.AlreadyClaimed
                        : QuestWriteOutcome.Rejected,
                    decision.Outcome,
                    Snapshot(command.AccountId),
                    null));
            }

            QuestClaimSummary? claim = null;
            if (decision.Grant is { } grant)
            {
                if (_claims.Contains((command.AccountId, grant.InstanceKey)))
                {
                    return Task.FromResult(new QuestWriteResult(
                        QuestWriteOutcome.AlreadyClaimed, QuestRuleOutcome.AlreadyClaimed, Snapshot(command.AccountId), null));
                }

                if (grant.Items.Count > FreeInventorySlots)
                {
                    return Task.FromResult(new QuestWriteResult(
                        QuestWriteOutcome.InventoryFull, QuestRuleOutcome.Applied, Snapshot(command.AccountId), null));
                }

                _claims.Add((command.AccountId, grant.InstanceKey));
                Grants.Add(grant);
                var before = _accountXp[command.AccountId];
                var after = Math.Min(MaximumAccountXp, before + grant.AccountXp);
                _accountXp[command.AccountId] = after;
                var lines = new List<QuestRewardLine>();
                if (grant.AccountXp > 0)
                {
                    lines.Add(new QuestRewardLine("AccountXp", string.Empty, after - before));
                }

                lines.AddRange(grant.Items.Select(item => new QuestRewardLine("Item", item.ItemId, item.Amount)));
                lines.AddRange(grant.Currencies.Select(currency =>
                    new QuestRewardLine("Currency", currency.CurrencyId, currency.Amount)));
                claim = new QuestClaimSummary(grant.QuestId, lines, after);
            }

            _instances[(command.AccountId, decision.Changed.InstanceKey)] = Copy(decision.Changed);
            _requests[(command.AccountId, command.RequestId)] = (command.Operation, command.QuestId, claim);
            Writes++;
            return Task.FromResult(new QuestWriteResult(
                QuestWriteOutcome.Applied, QuestRuleOutcome.Applied, Snapshot(command.AccountId), claim));
        }
    }

    private QuestLog? Snapshot(long accountId)
    {
        if (!_accountXp.TryGetValue(accountId, out var xp))
        {
            return null;
        }

        return new QuestLog(
            accountId,
            xp,
            _instances.Where(pair => pair.Key.Account == accountId).Select(pair => Copy(pair.Value)).ToArray());
    }

    private static QuestInstance Copy(QuestInstance instance) => QuestInstance.Restore(
        instance.InstanceKey,
        instance.QuestId,
        instance.Status,
        instance.AcceptedAt,
        instance.StepStartedAt,
        instance.SubmittedAt,
        instance.ClaimedAt,
        instance.Objectives.Select(objective =>
            new QuestObjectiveProgress(objective.ObjectiveId, objective.Progress, objective.CompletedAt)));
}

/// <summary>可控的活动远征探测。</summary>
internal sealed class FixedExpeditionProbe : IActiveExpeditionProbe
{
    public bool Active { get; set; } = true;

    public Task<bool> HasActiveExpeditionAsync(long accountId, CancellationToken cancellationToken) =>
        Task.FromResult(Active);
}

/// <summary>可以手动推进的服务端时钟。</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan delta) => Now += delta;
}
