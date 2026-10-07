namespace Naraka.Server.Domain.Expeditions;

/// <summary>远征临时资产类型。物品与货币必须分开结算到库存与货币流水。</summary>
public enum ExpeditionAssetKind
{
    Item = 0,
    Currency = 1
}

/// <summary>临时资产来源。P3只接纳怪物掉落；任务奖励直接进入账号。</summary>
public enum ExpeditionAssetSource
{
    MonsterDrop = 0
}

/// <summary>按类型、配置Id与来源聚合的一项临时资产。</summary>
public sealed record ExpeditionAsset
{
    public ExpeditionAsset(
        ExpeditionAssetKind kind,
        string assetId,
        long quantity,
        ExpeditionAssetSource source)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (string.IsNullOrWhiteSpace(assetId) || assetId.Length > 64)
        {
            throw new ArgumentException("AssetId must contain 1-64 characters.", nameof(assetId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        Kind = kind;
        AssetId = assetId;
        Quantity = quantity;
        Source = source;
    }

    public ExpeditionAssetKind Kind { get; }

    public string AssetId { get; }

    public long Quantity { get; }

    public ExpeditionAssetSource Source { get; }
}
