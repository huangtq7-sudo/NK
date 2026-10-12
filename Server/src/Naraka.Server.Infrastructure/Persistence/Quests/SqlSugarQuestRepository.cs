using System.Text.Json;
using Naraka.Config;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Quests;
using Naraka.Server.Domain.Quests;
using Naraka.Server.Infrastructure.Persistence.Inventory;
using Naraka.Server.Infrastructure.Persistence.Lobby;
using Naraka.Server.Infrastructure.Persistence.Progression;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Quests;

/// <summary>
/// 任务的 MySQL 5.7 原子存储实现。
///
/// 每个写入口在一个事务内：先查 RequestId 重放，再锁账号资料行串行化同一账号的全部任务写入，
/// 重建 <see cref="QuestLog"/> 并调用应用层给出的决定，最后把实例状态、目标进度、奖励发放、
/// 货币流水、经验流水、领取记录与幂等记录一起提交。任一步失败全部回滚。
/// 这里不写任何任务规则：规则都在领域聚合里。
/// </summary>
public sealed class SqlSugarQuestRepository(
    SqlSugarClientFactory factory,
    GameConfig config) : IQuestRepository
{
    /// <summary>经验流水的来源。账号经验只能来自任务。</summary>
    private const string QuestXpSource = "Quest";

    private static readonly JsonSerializerOptions PayloadOptions = new() { WriteIndented = false };

    public async Task<QuestLog?> LoadAsync(long accountId, CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var database = factory.Create();
            var profile = await database.Queryable<AccountProfileRow>()
                .Where(row => row.AccountId == accountId)
                .SingleAsync();
            return profile is null ? null : await LoadLogAsync(database, accountId, profile.AccountXp);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new QuestStorageException("Quest journal read failed.", exception);
        }
    }

    public async Task<QuestWriteResult> ExecuteAsync(
        QuestWriteCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // InnoDB 可能以死锁或锁等待超时中止事务（例如与其他模块交错加锁）。
        // 事务已整体回滚，重试一次是安全的：账号锁之后的幂等与状态检查会给出正确结果。
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteOnceAsync(command, cancellationToken);
            }
            catch (QuestStorageException exception) when (attempt < 2 && IsTransientLockFailure(exception))
            {
            }
        }
    }

    private async Task<QuestWriteResult> ExecuteOnceAsync(
        QuestWriteCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            // 先取账号锁，再查幂等记录。加锁顺序与远征结算一致（货币行 → 资料行 → 仓库），
            // 两类事务并发时不会互相死锁；锁住这两行后，同一账号的任务写入完全串行，
            // 接取上限与领取不会被并发绕过。幂等记录必须在锁之后用普通读查询：
            // 对不存在的行做加锁读会在 InnoDB 里加间隙锁，两条并发领取随后插入同一间隙就会死锁。
            var progression = await database.Queryable<AccountProgressionRow>()
                .TranLock(DbLockType.Wait)
                .Where(row => row.AccountId == command.AccountId)
                .SingleAsync();
            var profile = await database.Queryable<AccountProfileRow>()
                .TranLock(DbLockType.Wait)
                .Where(row => row.AccountId == command.AccountId)
                .SingleAsync();
            if (profile is null || progression is null)
            {
                database.Ado.RollbackTran();
                return new QuestWriteResult(QuestWriteOutcome.AccountMissing, QuestRuleOutcome.NotAvailable, null, null);
            }

            var replay = await TryReplayAsync(database, command);
            if (replay is not null)
            {
                database.Ado.CommitTran();
                return replay;
            }

            var log = await LoadLogAsync(database, command.AccountId, profile.AccountXp);
            var decision = command.Decide(log);
            if (decision.Outcome != QuestRuleOutcome.Applied || decision.Changed is null)
            {
                database.Ado.RollbackTran();
                var outcome = decision.Outcome == QuestRuleOutcome.AlreadyClaimed
                    ? QuestWriteOutcome.AlreadyClaimed
                    : QuestWriteOutcome.Rejected;
                return new QuestWriteResult(
                    outcome, decision.Outcome, await ReloadAsync(database, command.AccountId), null);
            }

            var now = ToDatabaseTime(command.Now);
            await SaveInstanceAsync(database, command.AccountId, decision.Changed, command.Operation, now);

            QuestClaimSummary? claim = null;
            var accountXp = profile.AccountXp;
            if (decision.Grant is not null)
            {
                var grant = await ApplyGrantAsync(database, progression, profile, decision.Grant, now);
                if (grant.Outcome != QuestWriteOutcome.Applied)
                {
                    SafeRollback(database);
                    return new QuestWriteResult(
                        grant.Outcome, QuestRuleOutcome.Applied, await ReloadAsync(database, command.AccountId), null);
                }

                claim = grant.Summary;
                accountXp = grant.Summary!.AccountXpAfter;
            }

            await database.Insertable(new IdempotencyRow
            {
                AccountId = command.AccountId,
                RequestId = command.RequestId,
                Operation = OperationName(command.Operation),
                StatusCode = (int)LobbyOperationStatus.Success,
                ResponsePayload = JsonSerializer.Serialize(new ReplayPayload(command.QuestId, claim), PayloadOptions),
                CreatedUtc = now
            }).ExecuteCommandAsync();

            database.Ado.CommitTran();
            return new QuestWriteResult(
                QuestWriteOutcome.Applied,
                QuestRuleOutcome.Applied,
                new QuestLog(command.AccountId, accountXp, log.Instances),
                claim);
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception) when (IsDuplicateKey(exception))
        {
            // 主键冲突只可能来自并发：同一 RequestId 已被另一条连接提交，或同一实例已被领取。
            SafeRollback(database);
            return await ResolveConflictAsync(command, cancellationToken);
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new QuestStorageException("Quest write failed.", exception);
        }
    }

    private async Task<QuestWriteResult?> TryReplayAsync(ISqlSugarClient database, QuestWriteCommand command)
    {
        var record = await database.Queryable<IdempotencyRow>()
            .Where(row => row.AccountId == command.AccountId && row.RequestId == command.RequestId)
            .SingleAsync();
        if (record is null)
        {
            return null;
        }

        var payload = record.Operation == OperationName(command.Operation)
            ? JsonSerializer.Deserialize<ReplayPayload>(record.ResponsePayload, PayloadOptions)
            : null;
        if (payload is null || !string.Equals(payload.QuestId, command.QuestId, StringComparison.Ordinal))
        {
            return new QuestWriteResult(QuestWriteOutcome.RequestMismatch, QuestRuleOutcome.NotAvailable, null, null);
        }

        var profile = await database.Queryable<AccountProfileRow>()
            .Where(row => row.AccountId == command.AccountId)
            .SingleAsync();
        var log = profile is null ? null : await LoadLogAsync(database, command.AccountId, profile.AccountXp);
        return new QuestWriteResult(QuestWriteOutcome.Replayed, QuestRuleOutcome.Applied, log, payload.Claim);
    }

    private async Task<QuestWriteResult> ResolveConflictAsync(
        QuestWriteCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var database = factory.Create();
            var replay = await TryReplayAsync(database, command);
            if (replay is not null)
            {
                return replay;
            }

            var log = await ReloadAsync(database, command.AccountId);
            return command.Operation == QuestOperation.Claim
                ? new QuestWriteResult(QuestWriteOutcome.AlreadyClaimed, QuestRuleOutcome.AlreadyClaimed, log, null)
                : new QuestWriteResult(QuestWriteOutcome.Rejected, QuestRuleOutcome.NotAvailable, log, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new QuestStorageException("Quest conflict resolution failed.", exception);
        }
    }

    private static async Task SaveInstanceAsync(
        ISqlSugarClient database,
        long accountId,
        QuestInstance instance,
        QuestOperation operation,
        DateTime now)
    {
        var row = new AccountQuestRow
        {
            AccountId = accountId,
            QuestInstanceKey = instance.InstanceKey,
            QuestId = instance.QuestId,
            Status = instance.Status.ToString(),
            AcceptedUtc = ToDatabaseTime(instance.AcceptedAt),
            StepStartedUtc = ToDatabaseTime(instance.StepStartedAt),
            SubmittedUtc = instance.SubmittedAt is null ? null : ToDatabaseTime(instance.SubmittedAt.Value),
            ClaimedUtc = instance.ClaimedAt is null ? null : ToDatabaseTime(instance.ClaimedAt.Value),
            UpdatedUtc = now
        };
        var objectives = instance.Objectives
            .Select(objective => new AccountQuestObjectiveRow
            {
                AccountId = accountId,
                QuestInstanceKey = instance.InstanceKey,
                ObjectiveId = objective.ObjectiveId,
                Progress = objective.Progress,
                CompletedUtc = objective.CompletedAt is null ? null : ToDatabaseTime(objective.CompletedAt.Value),
                UpdatedUtc = now
            })
            .ToList();

        if (operation == QuestOperation.Accept)
        {
            await database.Insertable(row).ExecuteCommandAsync();
            if (objectives.Count > 0)
            {
                await database.Insertable(objectives).ExecuteCommandAsync();
            }

            return;
        }

        await database.Updateable(row)
            .UpdateColumns(value => new
            {
                value.Status,
                value.StepStartedUtc,
                value.SubmittedUtc,
                value.ClaimedUtc,
                value.UpdatedUtc
            })
            .ExecuteCommandAsync();

        if (operation == QuestOperation.CompleteDialogue)
        {
            foreach (var objective in objectives)
            {
                await database.Updateable(objective)
                    .UpdateColumns(value => new { value.Progress, value.CompletedUtc, value.UpdatedUtc })
                    .ExecuteCommandAsync();
            }
        }
    }

    private async Task<GrantResult> ApplyGrantAsync(
        ISqlSugarClient database,
        AccountProgressionRow progression,
        AccountProfileRow profile,
        QuestRewardGrant grant,
        DateTime now)
    {
        var accountId = profile.AccountId;

        // 领取记录主键是最终防线：即使状态机被绕过，同一实例也只能插入一次。
        var claimed = await database.Queryable<AccountRewardClaimRow>()
            .Where(row => row.AccountId == accountId &&
                          row.RewardKind == RewardClaimKind.Quest &&
                          row.RewardKey == grant.InstanceKey)
            .AnyAsync();
        if (claimed)
        {
            return new GrantResult(QuestWriteOutcome.AlreadyClaimed, null);
        }

        await database.Insertable(new AccountRewardClaimRow
        {
            AccountId = accountId,
            RewardKind = RewardClaimKind.Quest,
            RewardKey = grant.InstanceKey,
            ClaimedUtc = now
        }).ExecuteCommandAsync();

        var lines = new List<QuestRewardLine>();
        if (!await ApplyItemsAsync(database, profile, grant, now))
        {
            return new GrantResult(QuestWriteOutcome.InventoryFull, null);
        }

        lines.AddRange(grant.Items.Select(item => new QuestRewardLine(ConfigQuestRewardKind.Item, item.ItemId, item.Amount)));

        var ledger = new List<CurrencyLedgerRow>();
        foreach (var currency in grant.Currencies)
        {
            if (!config.TryGetCurrency(currency.CurrencyId, out _) ||
                !TryGrantCurrency(progression, currency.CurrencyId, currency.Amount, out var balanceAfter))
            {
                return new GrantResult(QuestWriteOutcome.AccountMissing, null);
            }

            ledger.Add(new CurrencyLedgerRow
            {
                AccountId = accountId,
                CurrencyId = currency.CurrencyId,
                Delta = currency.Amount,
                BalanceAfter = balanceAfter,
                Reason = CurrencyLedgerReason.QuestReward,
                ReferenceId = grant.InstanceKey,
                CreatedUtc = now
            });
            lines.Add(new QuestRewardLine(ConfigQuestRewardKind.Currency, currency.CurrencyId, currency.Amount));
        }

        if (ledger.Count > 0)
        {
            await database.Insertable(ledger).ExecuteCommandAsync();
        }

        // 账号经验只来自任务，并封顶在满级所需经验。超出部分不发放，流水只记实际增量。
        var xpBefore = profile.AccountXp;
        var xpAfter = Math.Min(config.MaximumAccountXp, checked(xpBefore + Math.Max(0L, grant.AccountXp)));
        var xpDelta = Math.Max(0L, xpAfter - xpBefore);
        if (xpDelta > 0)
        {
            profile.AccountXp = xpAfter;
            profile.UpdatedUtc = now;
            await database.Updateable(profile)
                .UpdateColumns(value => new { value.AccountXp, value.UpdatedUtc })
                .ExecuteCommandAsync();
            await database.Insertable(new AccountXpLedgerRow
            {
                AccountId = accountId,
                Source = QuestXpSource,
                ReferenceId = grant.InstanceKey,
                Delta = xpDelta,
                XpAfter = xpAfter,
                CreatedUtc = now
            }).ExecuteCommandAsync();
        }

        if (grant.AccountXp > 0)
        {
            lines.Insert(0, new QuestRewardLine(ConfigQuestRewardKind.AccountXp, string.Empty, xpDelta));
        }

        // 大厅头部读取的是 account_progression.account_level，而等级奖励由 account_xp 推导。
        // 两者必须在同一事务里保持一致，否则任务升级后大厅等级不会变化。
        progression.AccountLevel = config.ResolveAccountLevel(xpAfter);
        progression.UpdatedUtc = now;
        await database.Updateable(progression)
            .UpdateColumns(value => new
            {
                value.AccountLevel,
                value.Copper,
                value.Silk,
                value.Gold,
                value.UpdatedUtc
            })
            .ExecuteCommandAsync();

        return new GrantResult(
            QuestWriteOutcome.Applied,
            new QuestClaimSummary(grant.QuestId, lines, xpAfter));
    }

    private async Task<bool> ApplyItemsAsync(
        ISqlSugarClient database,
        AccountProfileRow profile,
        QuestRewardGrant grant,
        DateTime now)
    {
        if (grant.Items.Count == 0)
        {
            return true;
        }

        var inventory = await database.Queryable<AccountInventoryRow>()
            .TranLock(DbLockType.Wait)
            .Where(row => row.AccountId == profile.AccountId)
            .ToListAsync();
        var byItem = inventory.ToDictionary(row => row.ItemId, StringComparer.Ordinal);
        var capacity = config.GetInventoryCapacity(profile.InventoryTier);
        var occupied = inventory.Count;
        var nextSlot = occupied == 0 ? 0 : inventory.Max(row => row.SlotIndex) + 1;
        var inserts = new List<AccountInventoryRow>();
        var updates = new List<AccountInventoryRow>();

        foreach (var group in grant.Items.GroupBy(item => item.ItemId, StringComparer.Ordinal))
        {
            var amount = group.Sum(item => item.Amount);
            if (!config.TryGetItem(group.Key, out var item) || item is null || item.StackLimit <= 0 || amount <= 0)
            {
                return false;
            }

            if (byItem.TryGetValue(group.Key, out var slot))
            {
                // 任务奖励不溢出到邮件：放不下就保持待领取，玩家腾出空间后再领，奖励不会丢。
                if (slot.Quantity + amount > item.StackLimit)
                {
                    return false;
                }

                slot.Quantity += amount;
                slot.UpdatedUtc = now;
                updates.Add(slot);
                continue;
            }

            if (occupied >= capacity || amount > item.StackLimit)
            {
                return false;
            }

            occupied++;
            inserts.Add(new AccountInventoryRow
            {
                AccountId = profile.AccountId,
                ItemId = group.Key,
                Quantity = amount,
                SlotIndex = nextSlot++,
                CreatedUtc = now,
                UpdatedUtc = now
            });
        }

        if (inserts.Count > 0)
        {
            await database.Insertable(inserts).ExecuteCommandAsync();
        }

        foreach (var slot in updates)
        {
            await database.Updateable(slot)
                .UpdateColumns(row => new { row.Quantity, row.UpdatedUtc })
                .ExecuteCommandAsync();
        }

        return true;
    }

    private static async Task<QuestLog> LoadLogAsync(ISqlSugarClient database, long accountId, long accountXp)
    {
        var quests = await database.Queryable<AccountQuestRow>()
            .Where(row => row.AccountId == accountId)
            .ToListAsync();
        var objectives = await database.Queryable<AccountQuestObjectiveRow>()
            .Where(row => row.AccountId == accountId)
            .ToListAsync();
        var objectivesByInstance = objectives
            .GroupBy(row => row.QuestInstanceKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var instances = new List<QuestInstance>(quests.Count);
        foreach (var row in quests)
        {
            if (!Enum.TryParse<QuestStatus>(row.Status, ignoreCase: false, out var status))
            {
                throw new InvalidDataException($"Quest instance {row.QuestInstanceKey} has an unknown status.");
            }

            objectivesByInstance.TryGetValue(row.QuestInstanceKey, out var rows);
            instances.Add(QuestInstance.Restore(
                row.QuestInstanceKey,
                row.QuestId,
                status,
                FromDatabaseTime(row.AcceptedUtc),
                FromDatabaseTime(row.StepStartedUtc),
                row.SubmittedUtc is null ? null : FromDatabaseTime(row.SubmittedUtc.Value),
                row.ClaimedUtc is null ? null : FromDatabaseTime(row.ClaimedUtc.Value),
                (rows ?? Array.Empty<AccountQuestObjectiveRow>()).Select(objective => new QuestObjectiveProgress(
                    objective.ObjectiveId,
                    objective.Progress,
                    objective.CompletedUtc is null ? null : FromDatabaseTime(objective.CompletedUtc.Value)))));
        }

        return new QuestLog(accountId, accountXp, instances);
    }

    private static async Task<QuestLog?> ReloadAsync(ISqlSugarClient database, long accountId)
    {
        var profile = await database.Queryable<AccountProfileRow>()
            .Where(row => row.AccountId == accountId)
            .SingleAsync();
        return profile is null ? null : await LoadLogAsync(database, accountId, profile.AccountXp);
    }

    private static bool TryGrantCurrency(
        AccountProgressionRow row,
        string currencyId,
        long amount,
        out long balanceAfter)
    {
        balanceAfter = 0;
        if (amount <= 0)
        {
            return false;
        }

        switch (currencyId)
        {
            case "Copper":
                row.Copper = checked(row.Copper + amount);
                balanceAfter = row.Copper;
                return true;
            case "Silk":
                row.Silk = checked(row.Silk + amount);
                balanceAfter = row.Silk;
                return true;
            case "Gold":
                row.Gold = checked(row.Gold + amount);
                balanceAfter = row.Gold;
                return true;
            default:
                return false;
        }
    }

    private static string OperationName(QuestOperation operation) => operation switch
    {
        QuestOperation.Accept => "quest.accept",
        QuestOperation.CompleteDialogue => "quest.complete-dialogue",
        QuestOperation.Submit => "quest.submit",
        QuestOperation.Claim => "quest.claim",
        _ => "quest.unknown"
    };

    private static DateTime ToDatabaseTime(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new DateTime(utc.Ticks - (utc.Ticks % 10L), DateTimeKind.Utc);
    }

    private static DateTimeOffset FromDatabaseTime(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static bool IsTransientLockFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            // 1213 = 死锁被选为牺牲者，1205 = 锁等待超时。两者都意味着整个事务已回滚。
            if (current is MySqlConnector.MySqlException { Number: 1213 or 1205 })
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDuplicateKey(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlConnector.MySqlException { Number: 1062 })
            {
                return true;
            }
        }

        return false;
    }

    private static void SafeRollback(ISqlSugarClient database)
    {
        try
        {
            database.Ado.RollbackTran();
        }
        catch (Exception)
        {
            // 连接已经断开或事务已结束时，MySQL 会自行终止未提交事务。
        }
    }

    private sealed record GrantResult(QuestWriteOutcome Outcome, QuestClaimSummary? Summary);

    /// <summary>幂等记录里保存的首次结果。重放时用来核对同一 RequestId 是否换了任务。</summary>
    private sealed record ReplayPayload(string QuestId, QuestClaimSummary? Claim);
}
