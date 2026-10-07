using System.Text.Json;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Expeditions;
using Naraka.Server.Application.Progression;
using Naraka.Server.Domain;
using Naraka.Server.Domain.Expeditions;
using Naraka.Server.Infrastructure.Persistence.Accounts;
using Naraka.Server.Infrastructure.Persistence.Inventory;
using Naraka.Server.Infrastructure.Persistence.Lobby;
using Naraka.Server.Infrastructure.Persistence.Progression;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Expeditions;

/// <summary>
/// 远征的MySQL 5.7原子存储实现。
///
/// 每个写入口都锁定远征行，并在同一事务内重建领域聚合、应用领域操作和保存结果。
/// 结算事务同时覆盖临时资产、仓库或邮件、货币余额与流水、不可变摘要和活动远征释放。
/// </summary>
public sealed class SqlSugarExpeditionRepository(
    SqlSugarClientFactory factory,
    GameConfig config) : IExpeditionRepository
{
    private const string ActiveStatus = "Active";
    private const string SettledStatus = "Settled";
    private const string DropEventKind = "MonsterDrop";
    private const string DeathEventKind = "Death";
    private const string SettlementMailSource = "ExpeditionSettlement";

    public async Task<ExpeditionSnapshot?> FindActiveAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var database = factory.Create();
            var row = await database.Queryable<ExpeditionRow>()
                .Where(value => value.ActiveAccountId == accountId && value.Status == ActiveStatus)
                .SingleAsync();
            if (row is null)
            {
                return null;
            }

            return await LoadSnapshotAsync(database, row, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExpeditionStorageException("Expedition read failed.", exception);
        }
    }

    public async Task<ExpeditionWriteResult> TryStartAsync(
        StartExpeditionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var replay = await database.Queryable<ExpeditionRow>()
                .TranLock(DbLockType.Wait)
                .Where(row => row.AccountId == command.AccountId &&
                              row.StartRequestId == command.RequestId)
                .SingleAsync();
            if (replay is not null)
            {
                var snapshot = await LoadSnapshotAsync(database, replay, cancellationToken);
                database.Ado.CommitTran();
                return new ExpeditionWriteResult(ExpeditionWriteOutcome.AlreadyApplied, snapshot);
            }

            var accountExists = await database.Queryable<AccountRow>()
                .Where(row => row.AccountId == command.AccountId)
                .AnyAsync();
            if (!accountExists)
            {
                database.Ado.RollbackTran();
                return new ExpeditionWriteResult(ExpeditionWriteOutcome.AccountMissing);
            }

            var active = await database.Queryable<ExpeditionRow>()
                .TranLock(DbLockType.Wait)
                .Where(row => row.ActiveAccountId == command.AccountId && row.Status == ActiveStatus)
                .SingleAsync();
            if (active is not null)
            {
                database.Ado.RollbackTran();
                return new ExpeditionWriteResult(ExpeditionWriteOutcome.Conflict);
            }

            var aggregate = ExpeditionAggregate.Start(
                command.ExpeditionId,
                command.AccountId,
                command.EntryMapId,
                command.StartedAt);
            var row = new ExpeditionRow
            {
                ExpeditionId = aggregate.Id.Value,
                AccountId = aggregate.AccountId,
                ActiveAccountId = aggregate.AccountId,
                StartRequestId = command.RequestId,
                EntryMapId = aggregate.EntryMapId,
                Status = ActiveStatus,
                DeathCount = aggregate.DeathCount,
                StartedUtc = ToDatabaseTime(aggregate.StartedAt),
                SettledUtc = null
            };
            await database.Insertable(row).ExecuteCommandAsync();

            database.Ado.CommitTran();
            return new ExpeditionWriteResult(
                ExpeditionWriteOutcome.Applied,
                ToSnapshot(aggregate));
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception) when (IsDuplicateKey(exception))
        {
            SafeRollback(database);
            return await ResolveConcurrentStartAsync(command, cancellationToken);
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ExpeditionStorageException("Expedition start failed.", exception);
        }
    }

    public async Task<ExpeditionWriteResult> TryAppendDropsAsync(
        AppendExpeditionDropsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();
            var row = await LockExpeditionAsync(database, command.ExpeditionId, cancellationToken);
            if (row is null || row.AccountId != command.AccountId)
            {
                database.Ado.RollbackTran();
                return new ExpeditionWriteResult(ExpeditionWriteOutcome.NotFound);
            }

            var repeated = await HasEventAsync(
                database, command.ExpeditionId, DropEventKind, command.DropEventId);
            if (repeated)
            {
                var replay = await LoadSnapshotAsync(database, row!, cancellationToken);
                database.Ado.CommitTran();
                return new ExpeditionWriteResult(ExpeditionWriteOutcome.AlreadyApplied, replay);
            }

            if (!MatchesActiveOwner(row, command.AccountId))
            {
                database.Ado.RollbackTran();
                return new ExpeditionWriteResult(ExpeditionWriteOutcome.Conflict);
            }

            var aggregate = await RestoreActiveAsync(database, row, cancellationToken);
            foreach (var asset in command.Assets)
            {
                aggregate.AddMonsterDrop(asset.Kind, asset.AssetId, asset.Quantity);
            }

            await ReplaceTemporaryAssetsAsync(database, aggregate, command.OccurredAt);
            await database.Insertable(new ExpeditionEventRow
            {
                ExpeditionId = command.ExpeditionId.Value,
                EventKind = DropEventKind,
                EventId = command.DropEventId,
                ResultPayload = "{}",
                OccurredUtc = ToDatabaseTime(command.OccurredAt)
            }).ExecuteCommandAsync();

            database.Ado.CommitTran();
            return new ExpeditionWriteResult(
                ExpeditionWriteOutcome.Applied,
                ToSnapshot(aggregate));
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ExpeditionStorageException("Expedition drop write failed.", exception);
        }
    }

    public async Task<ExpeditionWriteResult> TryRecordDeathAsync(
        RecordExpeditionDeathCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();
            var row = await LockExpeditionAsync(database, command.ExpeditionId, cancellationToken);
            if (row is null || row.AccountId != command.AccountId)
            {
                database.Ado.RollbackTran();
                return new ExpeditionWriteResult(ExpeditionWriteOutcome.NotFound);
            }

            var repeated = await FindEventAsync(
                database, command.ExpeditionId, DeathEventKind, command.DeathEventId);
            if (repeated is not null)
            {
                var death = DeserializeDeath(command.ExpeditionId, repeated.ResultPayload);
                var replay = await LoadSnapshotAsync(database, row!, cancellationToken);
                database.Ado.CommitTran();
                return new ExpeditionWriteResult(
                    ExpeditionWriteOutcome.AlreadyApplied,
                    replay,
                    death);
            }

            if (!MatchesActiveOwner(row, command.AccountId))
            {
                database.Ado.RollbackTran();
                return new ExpeditionWriteResult(ExpeditionWriteOutcome.Conflict);
            }

            var aggregate = await RestoreActiveAsync(database, row, cancellationToken);
            var result = aggregate.RecordDeath(command.OccurredAt);
            await ReplaceTemporaryAssetsAsync(database, aggregate, command.OccurredAt);

            row.DeathCount = aggregate.DeathCount;
            await database.Updateable(row)
                .UpdateColumns(value => new { value.DeathCount })
                .ExecuteCommandAsync();
            await database.Insertable(new ExpeditionEventRow
            {
                ExpeditionId = command.ExpeditionId.Value,
                EventKind = DeathEventKind,
                EventId = command.DeathEventId,
                ResultPayload = SerializeDeath(result),
                OccurredUtc = ToDatabaseTime(command.OccurredAt)
            }).ExecuteCommandAsync();

            database.Ado.CommitTran();
            return new ExpeditionWriteResult(
                ExpeditionWriteOutcome.Applied,
                ToSnapshot(aggregate),
                result);
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ExpeditionStorageException("Expedition death write failed.", exception);
        }
    }

    public async Task<ExpeditionWriteResult> TrySettleAsync(
        SettleExpeditionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();
            var row = await LockExpeditionAsync(database, command.ExpeditionId, cancellationToken);
            if (row is null || row.AccountId != command.AccountId)
            {
                database.Ado.RollbackTran();
                return new ExpeditionWriteResult(ExpeditionWriteOutcome.NotFound);
            }

            var existingSettlement = await database.Queryable<ExpeditionSettlementRow>()
                .TranLock(DbLockType.Wait)
                .Where(value => value.ExpeditionId == command.ExpeditionId.Value)
                .SingleAsync();
            if (existingSettlement is not null)
            {
                var replay = await LoadSettlementAsync(database, existingSettlement, cancellationToken);
                database.Ado.CommitTran();
                return new ExpeditionWriteResult(
                    ExpeditionWriteOutcome.AlreadyApplied,
                    Settlement: replay);
            }

            if (!string.Equals(row.Status, ActiveStatus, StringComparison.Ordinal) ||
                row.ActiveAccountId != command.AccountId)
            {
                database.Ado.RollbackTran();
                return new ExpeditionWriteResult(ExpeditionWriteOutcome.Conflict);
            }

            var aggregate = await RestoreActiveAsync(database, row, cancellationToken);
            var settlement = aggregate.Settle(command.RequestId, command.Reason, command.SettledAt).Summary;

            var grantOutcome = await ApplySettlementAssetsAsync(database, settlement, cancellationToken);
            if (grantOutcome != ExpeditionWriteOutcome.Applied)
            {
                database.Ado.RollbackTran();
                return new ExpeditionWriteResult(grantOutcome);
            }

            await SaveSettlementAsync(database, settlement);
            await database.Deleteable<ExpeditionAssetRow>()
                .Where(value => value.ExpeditionId == command.ExpeditionId.Value)
                .ExecuteCommandAsync();

            row.Status = SettledStatus;
            row.ActiveAccountId = null;
            row.SettledUtc = ToDatabaseTime(command.SettledAt);
            await database.Updateable(row)
                .UpdateColumns(value => new
                {
                    value.Status,
                    value.ActiveAccountId,
                    value.SettledUtc,
                    value.DeathCount
                })
                .ExecuteCommandAsync();

            database.Ado.CommitTran();
            return new ExpeditionWriteResult(
                ExpeditionWriteOutcome.Applied,
                Settlement: settlement);
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ExpeditionStorageException("Expedition settlement failed.", exception);
        }
    }

    private async Task<ExpeditionWriteResult> ResolveConcurrentStartAsync(
        StartExpeditionCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            using var database = factory.Create();
            var replay = await database.Queryable<ExpeditionRow>()
                .Where(row => row.AccountId == command.AccountId &&
                              row.StartRequestId == command.RequestId)
                .SingleAsync();
            if (replay is not null)
            {
                return new ExpeditionWriteResult(
                    ExpeditionWriteOutcome.AlreadyApplied,
                    await LoadSnapshotAsync(database, replay, cancellationToken));
            }

            var active = await database.Queryable<ExpeditionRow>()
                .Where(row => row.ActiveAccountId == command.AccountId && row.Status == ActiveStatus)
                .AnyAsync();
            if (active)
            {
                return new ExpeditionWriteResult(ExpeditionWriteOutcome.Conflict);
            }

            throw new ExpeditionStorageException("Expedition start collided with an unrelated unique key.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ExpeditionStorageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExpeditionStorageException("Expedition start replay read failed.", exception);
        }
    }

    private async Task<ExpeditionWriteOutcome> ApplySettlementAssetsAsync(
        ISqlSugarClient database,
        ExpeditionSettlementSummary settlement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var progression = await database.Queryable<AccountProgressionRow>()
            .TranLock(DbLockType.Wait)
            .Where(row => row.AccountId == settlement.AccountId)
            .SingleAsync();
        var profile = await database.Queryable<AccountProfileRow>()
            .TranLock(DbLockType.Wait)
            .Where(row => row.AccountId == settlement.AccountId)
            .SingleAsync();
        if (progression is null || profile is null)
        {
            return ExpeditionWriteOutcome.AccountMissing;
        }

        var now = ToDatabaseTime(settlement.SettledAt);
        var ledger = new List<CurrencyLedgerRow>();
        foreach (var asset in settlement.Assets.Where(value => value.Kind == ExpeditionAssetKind.Currency))
        {
            if (!config.TryGetCurrency(asset.AssetId, out _) ||
                !TryGrantCurrency(progression, asset.AssetId, asset.Quantity, out var balanceAfter))
            {
                return ExpeditionWriteOutcome.Conflict;
            }

            ledger.Add(new CurrencyLedgerRow
            {
                AccountId = settlement.AccountId,
                CurrencyId = asset.AssetId,
                Delta = asset.Quantity,
                BalanceAfter = balanceAfter,
                Reason = CurrencyLedgerReason.ExpeditionSettlement,
                ReferenceId = settlement.ExpeditionId.Value,
                CreatedUtc = now
            });
        }

        if (ledger.Count > 0)
        {
            progression.UpdatedUtc = now;
            await database.Updateable(progression)
                .UpdateColumns(value => new
                {
                    value.Copper,
                    value.Silk,
                    value.Gold,
                    value.UpdatedUtc
                })
                .ExecuteCommandAsync();
            await database.Insertable(ledger).ExecuteCommandAsync();
        }

        var inventory = await database.Queryable<AccountInventoryRow>()
            .TranLock(DbLockType.Wait)
            .Where(value => value.AccountId == settlement.AccountId)
            .ToListAsync();
        var byItem = inventory.ToDictionary(value => value.ItemId, StringComparer.Ordinal);
        var capacity = config.GetInventoryCapacity(profile.InventoryTier);
        var occupied = inventory.Count;
        var nextSlot = occupied == 0 ? 0 : inventory.Max(value => value.SlotIndex) + 1;
        var inserts = new List<AccountInventoryRow>();
        var updates = new List<AccountInventoryRow>();
        var mailbox = new List<AccountMailItemRow>();

        foreach (var asset in settlement.Assets.Where(value => value.Kind == ExpeditionAssetKind.Item))
        {
            if (!config.TryGetItem(asset.AssetId, out var item) || item is null || item.StackLimit <= 0)
            {
                return ExpeditionWriteOutcome.Conflict;
            }

            byItem.TryGetValue(asset.AssetId, out var slot);
            var available = slot is null
                ? occupied < capacity ? (long)item.StackLimit : 0L
                : Math.Max(0L, (long)item.StackLimit - slot.Quantity);
            var toInventory = Math.Min(asset.Quantity, available);
            var toMailbox = asset.Quantity - toInventory;

            if (toInventory > 0 && slot is null)
            {
                slot = new AccountInventoryRow
                {
                    AccountId = settlement.AccountId,
                    ItemId = asset.AssetId,
                    Quantity = toInventory,
                    SlotIndex = nextSlot++,
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                inserts.Add(slot);
                byItem.Add(asset.AssetId, slot);
                occupied++;
            }
            else if (toInventory > 0)
            {
                slot!.Quantity = checked(slot.Quantity + toInventory);
                slot.UpdatedUtc = now;
                updates.Add(slot);
            }

            if (toMailbox > 0)
            {
                mailbox.Add(new AccountMailItemRow
                {
                    AccountId = settlement.AccountId,
                    SourceKind = SettlementMailSource,
                    SourceId = settlement.ExpeditionId.Value,
                    ItemId = asset.AssetId,
                    Quantity = toMailbox,
                    CreatedUtc = now,
                    ClaimedUtc = null
                });
            }
        }

        if (inserts.Count > 0)
        {
            await database.Insertable(inserts).ExecuteCommandAsync();
        }

        foreach (var slot in updates)
        {
            await database.Updateable(slot)
                .UpdateColumns(value => new { value.Quantity, value.UpdatedUtc })
                .ExecuteCommandAsync();
        }

        if (mailbox.Count > 0)
        {
            await database.Insertable(mailbox).ExecuteCommandAsync();
        }

        return ExpeditionWriteOutcome.Applied;
    }

    private static async Task SaveSettlementAsync(
        ISqlSugarClient database,
        ExpeditionSettlementSummary settlement)
    {
        await database.Insertable(new ExpeditionSettlementRow
        {
            ExpeditionId = settlement.ExpeditionId.Value,
            AccountId = settlement.AccountId,
            RequestId = settlement.RequestId.Value,
            Reason = settlement.Reason.ToString(),
            DeathCount = settlement.DeathCount,
            SettledUtc = ToDatabaseTime(settlement.SettledAt)
        }).ExecuteCommandAsync();

        var assets = settlement.Assets.Select(asset => new ExpeditionSettlementAssetRow
        {
            ExpeditionId = settlement.ExpeditionId.Value,
            AssetKind = asset.Kind.ToString(),
            AssetId = asset.AssetId,
            AssetSource = asset.Source.ToString(),
            Quantity = asset.Quantity
        }).ToArray();
        if (assets.Length > 0)
        {
            await database.Insertable(assets).ExecuteCommandAsync();
        }
    }

    private static async Task<ExpeditionSettlementSummary> LoadSettlementAsync(
        ISqlSugarClient database,
        ExpeditionSettlementRow row,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await database.Queryable<ExpeditionSettlementAssetRow>()
            .Where(value => value.ExpeditionId == row.ExpeditionId)
            .ToListAsync();
        if (!Enum.TryParse<ExpeditionSettlementReason>(row.Reason, out var reason) ||
            !Enum.IsDefined(reason))
        {
            throw new InvalidDataException("Stored expedition settlement reason is invalid.");
        }

        return new ExpeditionSettlementSummary(
            new ExpeditionId(row.ExpeditionId),
            row.AccountId,
            new RequestId(row.RequestId),
            reason,
            FromDatabaseTime(row.SettledUtc),
            rows.Select(ToAsset).ToArray(),
            row.DeathCount);
    }

    private static async Task<ExpeditionRow?> LockExpeditionAsync(
        ISqlSugarClient database,
        ExpeditionId id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await database.Queryable<ExpeditionRow>()
            .TranLock(DbLockType.Wait)
            .Where(row => row.ExpeditionId == id.Value)
            .SingleAsync();
    }

    private static async Task<bool> HasEventAsync(
        ISqlSugarClient database,
        ExpeditionId id,
        string kind,
        string eventId) =>
        await database.Queryable<ExpeditionEventRow>()
            .Where(row => row.ExpeditionId == id.Value &&
                          row.EventKind == kind &&
                          row.EventId == eventId)
            .AnyAsync();

    private static async Task<ExpeditionEventRow?> FindEventAsync(
        ISqlSugarClient database,
        ExpeditionId id,
        string kind,
        string eventId) =>
        await database.Queryable<ExpeditionEventRow>()
            .Where(row => row.ExpeditionId == id.Value &&
                          row.EventKind == kind &&
                          row.EventId == eventId)
            .SingleAsync();

    private static async Task<ExpeditionAggregate> RestoreActiveAsync(
        ISqlSugarClient database,
        ExpeditionRow row,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(row.Status, ActiveStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only active expeditions can be restored.");
        }

        var assets = await LoadTemporaryAssetsAsync(database, row.ExpeditionId, cancellationToken);
        return ExpeditionAggregate.RestoreActive(
            new ExpeditionId(row.ExpeditionId),
            row.AccountId,
            row.EntryMapId,
            FromDatabaseTime(row.StartedUtc),
            assets,
            row.DeathCount);
    }

    private static async Task<ExpeditionSnapshot> LoadSnapshotAsync(
        ISqlSugarClient database,
        ExpeditionRow row,
        CancellationToken cancellationToken)
    {
        var assets = string.Equals(row.Status, ActiveStatus, StringComparison.Ordinal)
            ? await LoadTemporaryAssetsAsync(database, row.ExpeditionId, cancellationToken)
            : Array.Empty<ExpeditionAsset>();
        if (!Enum.TryParse<ExpeditionStatus>(row.Status, out var status) || !Enum.IsDefined(status))
        {
            throw new InvalidDataException("Stored expedition status is invalid.");
        }

        return new ExpeditionSnapshot(
            new ExpeditionId(row.ExpeditionId),
            row.AccountId,
            row.EntryMapId,
            FromDatabaseTime(row.StartedUtc),
            status,
            assets,
            row.DeathCount);
    }

    private static async Task<ExpeditionAsset[]> LoadTemporaryAssetsAsync(
        ISqlSugarClient database,
        string expeditionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await database.Queryable<ExpeditionAssetRow>()
            .Where(row => row.ExpeditionId == expeditionId)
            .ToListAsync();
        return rows.Select(ToAsset).ToArray();
    }

    private static async Task ReplaceTemporaryAssetsAsync(
        ISqlSugarClient database,
        ExpeditionAggregate aggregate,
        DateTimeOffset updatedAt)
    {
        await database.Deleteable<ExpeditionAssetRow>()
            .Where(row => row.ExpeditionId == aggregate.Id.Value)
            .ExecuteCommandAsync();

        var rows = aggregate.TemporaryAssets.Select(asset => new ExpeditionAssetRow
        {
            ExpeditionId = aggregate.Id.Value,
            AssetKind = asset.Kind.ToString(),
            AssetId = asset.AssetId,
            AssetSource = asset.Source.ToString(),
            Quantity = asset.Quantity,
            UpdatedUtc = ToDatabaseTime(updatedAt)
        }).ToArray();
        if (rows.Length > 0)
        {
            await database.Insertable(rows).ExecuteCommandAsync();
        }
    }

    private static ExpeditionAsset ToAsset(ExpeditionAssetRow row) =>
        ToAsset(row.AssetKind, row.AssetId, row.Quantity, row.AssetSource);

    private static ExpeditionAsset ToAsset(ExpeditionSettlementAssetRow row) =>
        ToAsset(row.AssetKind, row.AssetId, row.Quantity, row.AssetSource);

    private static ExpeditionAsset ToAsset(
        string kindValue,
        string assetId,
        long quantity,
        string sourceValue)
    {
        if (!Enum.TryParse<ExpeditionAssetKind>(kindValue, out var kind) || !Enum.IsDefined(kind) ||
            !Enum.TryParse<ExpeditionAssetSource>(sourceValue, out var source) || !Enum.IsDefined(source))
        {
            throw new InvalidDataException("Stored expedition asset discriminator is invalid.");
        }

        return new ExpeditionAsset(kind, assetId, quantity, source);
    }

    private static ExpeditionSnapshot ToSnapshot(ExpeditionAggregate aggregate) => new(
        aggregate.Id,
        aggregate.AccountId,
        aggregate.EntryMapId,
        aggregate.StartedAt,
        aggregate.Status,
        aggregate.TemporaryAssets,
        aggregate.DeathCount);

    private static bool MatchesActiveOwner(ExpeditionRow? row, long accountId) =>
        row is not null &&
        row.AccountId == accountId &&
        row.ActiveAccountId == accountId &&
        string.Equals(row.Status, ActiveStatus, StringComparison.Ordinal);

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

    private static string SerializeDeath(ExpeditionDeathResult result) =>
        JsonSerializer.Serialize(new DeathReplayPayload(
            result.OccurredAt,
            result.DeathCount,
            result.ClearedAssets.Select(asset => new AssetPayload(
                asset.Kind.ToString(),
                asset.AssetId,
                asset.Quantity,
                asset.Source.ToString())).ToArray()));

    private static ExpeditionDeathResult DeserializeDeath(ExpeditionId id, string payload)
    {
        var replay = JsonSerializer.Deserialize<DeathReplayPayload>(payload)
            ?? throw new InvalidDataException("Stored expedition death payload is empty.");
        return new ExpeditionDeathResult(
            id,
            replay.OccurredAt,
            replay.ClearedAssets.Select(asset => ToAsset(
                asset.Kind,
                asset.AssetId,
                asset.Quantity,
                asset.Source)).ToArray(),
            replay.DeathCount);
    }

    private static DateTime ToDatabaseTime(DateTimeOffset value) => value.UtcDateTime;

    private static DateTimeOffset FromDatabaseTime(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

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
            // 连接已经断开时，MySQL会自行终止未提交事务。
        }
    }

    private sealed record AssetPayload(
        string Kind,
        string AssetId,
        long Quantity,
        string Source);

    private sealed record DeathReplayPayload(
        DateTimeOffset OccurredAt,
        int DeathCount,
        IReadOnlyList<AssetPayload> ClearedAssets);
}
