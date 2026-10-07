using Naraka.Server.Domain;

namespace Naraka.Server.Domain.Expeditions;

public enum ExpeditionStatus
{
    Active = 0,
    Settled = 1
}

/// <summary>
/// 单次远征的权威领域聚合。
///
/// 它只保存返回大厅前的临时资产；仓库、账号货币和任务直接奖励不属于这里。
/// 结算时应由基础设施在同一个数据库事务内完成：锁定远征、写入正式资产、
/// 写入SettlementSummary并将远征标记为已结算。
/// </summary>
public sealed class ExpeditionAggregate
{
    private readonly Dictionary<AssetKey, long> _temporaryAssets = new();
    private ExpeditionSettlementSummary? _settlement;

    private ExpeditionAggregate(
        ExpeditionId id,
        long accountId,
        string entryMapId,
        DateTimeOffset startedAt)
    {
        Id = id;
        AccountId = accountId;
        EntryMapId = entryMapId;
        StartedAt = startedAt;
        Status = ExpeditionStatus.Active;
    }

    public ExpeditionId Id { get; }

    public long AccountId { get; }

    public string EntryMapId { get; }

    public DateTimeOffset StartedAt { get; }

    public ExpeditionStatus Status { get; private set; }

    public int DeathCount { get; private set; }

    public ExpeditionSettlementSummary? Settlement => _settlement;

    public IReadOnlyList<ExpeditionAsset> TemporaryAssets => SnapshotAssets();

    public static ExpeditionAggregate Start(
        ExpeditionId id,
        long accountId,
        string entryMapId,
        DateTimeOffset startedAt)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("ExpeditionId cannot be the default value.", nameof(id));
        }

        if (accountId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(entryMapId) || entryMapId.Length > 64)
        {
            throw new ArgumentException("Entry MapId must contain 1-64 characters.", nameof(entryMapId));
        }

        return new ExpeditionAggregate(id, accountId, entryMapId, startedAt);
    }

    /// <summary>
    /// 只由服务端掉落系统调用。客户端不得提交任意AssetId或数量到此入口。
    /// </summary>
    public void AddMonsterDrop(ExpeditionAssetKind kind, string assetId, long quantity)
    {
        EnsureActive();
        var validated = new ExpeditionAsset(kind, assetId, quantity, ExpeditionAssetSource.MonsterDrop);
        var key = new AssetKey(validated.Kind, validated.AssetId, validated.Source);
        _temporaryAssets.TryGetValue(key, out var current);
        _temporaryAssets[key] = checked(current + validated.Quantity);
    }

    /// <summary>
    /// 死亡只清理本次远征未结算的怪物掉落，不结束远征，
    /// 也不触碰账号仓库、账号货币或任务直接奖励。
    /// </summary>
    public ExpeditionDeathResult RecordDeath(DateTimeOffset occurredAt)
    {
        EnsureActive();
        var cleared = SnapshotAssets(ExpeditionAssetSource.MonsterDrop);
        foreach (var key in _temporaryAssets.Keys
                     .Where(key => key.Source == ExpeditionAssetSource.MonsterDrop)
                     .ToArray())
        {
            _temporaryAssets.Remove(key);
        }

        DeathCount = checked(DeathCount + 1);
        return new ExpeditionDeathResult(Id, occurredAt, cleared, DeathCount);
    }

    /// <summary>
    /// 正常返回大厅和连接丢失共用同一结算规则。首次结算后，
    /// 无论重复请求是否携带同一RequestId，都只重放首次摘要。
    /// </summary>
    public ExpeditionSettlementResult Settle(
        RequestId requestId,
        ExpeditionSettlementReason reason,
        DateTimeOffset settledAt)
    {
        if (string.IsNullOrWhiteSpace(requestId.Value) || requestId.Value.Length > 64)
        {
            throw new ArgumentException("RequestId must contain 1-64 characters.", nameof(requestId));
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        if (_settlement is not null)
        {
            return new ExpeditionSettlementResult(_settlement, true);
        }

        EnsureActive();
        var assets = SnapshotAssets();
        _settlement = new ExpeditionSettlementSummary(
            Id,
            AccountId,
            requestId,
            reason,
            settledAt,
            assets,
            DeathCount);
        _temporaryAssets.Clear();
        Status = ExpeditionStatus.Settled;
        return new ExpeditionSettlementResult(_settlement, false);
    }

    private IReadOnlyList<ExpeditionAsset> SnapshotAssets(
        ExpeditionAssetSource? source = null) =>
        _temporaryAssets
            .Where(pair => source is null || pair.Key.Source == source)
            .OrderBy(pair => pair.Key.Kind)
            .ThenBy(pair => pair.Key.AssetId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Source)
            .Select(pair => new ExpeditionAsset(
                pair.Key.Kind,
                pair.Key.AssetId,
                pair.Value,
                pair.Key.Source))
            .ToArray();

    private void EnsureActive()
    {
        if (Status != ExpeditionStatus.Active)
        {
            throw new InvalidOperationException("The expedition is already settled.");
        }
    }

    private readonly record struct AssetKey(
        ExpeditionAssetKind Kind,
        string AssetId,
        ExpeditionAssetSource Source);
}
