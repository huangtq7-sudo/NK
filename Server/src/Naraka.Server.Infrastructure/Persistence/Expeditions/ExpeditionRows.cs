using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Expeditions;

[SugarTable("expeditions")]
internal sealed class ExpeditionRow
{
    [SugarColumn(ColumnName = "expedition_id", IsPrimaryKey = true, Length = 64)]
    public string ExpeditionId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "account_id")]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "active_account_id", IsNullable = true)]
    public long? ActiveAccountId { get; set; }

    [SugarColumn(ColumnName = "start_request_id", Length = 64)]
    public string StartRequestId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "entry_map_id", Length = 64)]
    public string EntryMapId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "status", Length = 16)]
    public string Status { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "death_count")]
    public int DeathCount { get; set; }

    [SugarColumn(ColumnName = "started_utc")]
    public DateTime StartedUtc { get; set; }

    [SugarColumn(ColumnName = "settled_utc", IsNullable = true)]
    public DateTime? SettledUtc { get; set; }
}

[SugarTable("expedition_assets")]
internal sealed class ExpeditionAssetRow
{
    [SugarColumn(ColumnName = "expedition_id", IsPrimaryKey = true, Length = 64)]
    public string ExpeditionId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "asset_kind", IsPrimaryKey = true, Length = 16)]
    public string AssetKind { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "asset_id", IsPrimaryKey = true, Length = 64)]
    public string AssetId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "asset_source", IsPrimaryKey = true, Length = 32)]
    public string AssetSource { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "quantity")]
    public long Quantity { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

[SugarTable("expedition_events")]
internal sealed class ExpeditionEventRow
{
    [SugarColumn(ColumnName = "expedition_id", IsPrimaryKey = true, Length = 64)]
    public string ExpeditionId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "event_kind", IsPrimaryKey = true, Length = 24)]
    public string EventKind { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "event_id", IsPrimaryKey = true, Length = 64)]
    public string EventId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "result_payload", ColumnDataType = "MEDIUMTEXT")]
    public string ResultPayload { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "occurred_utc")]
    public DateTime OccurredUtc { get; set; }
}

[SugarTable("expedition_settlements")]
internal sealed class ExpeditionSettlementRow
{
    [SugarColumn(ColumnName = "expedition_id", IsPrimaryKey = true, Length = 64)]
    public string ExpeditionId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "account_id")]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "request_id", Length = 64)]
    public string RequestId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "reason", Length = 32)]
    public string Reason { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "death_count")]
    public int DeathCount { get; set; }

    [SugarColumn(ColumnName = "settled_utc")]
    public DateTime SettledUtc { get; set; }
}

[SugarTable("expedition_settlement_assets")]
internal sealed class ExpeditionSettlementAssetRow
{
    [SugarColumn(ColumnName = "expedition_id", IsPrimaryKey = true, Length = 64)]
    public string ExpeditionId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "asset_kind", IsPrimaryKey = true, Length = 16)]
    public string AssetKind { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "asset_id", IsPrimaryKey = true, Length = 64)]
    public string AssetId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "asset_source", IsPrimaryKey = true, Length = 32)]
    public string AssetSource { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "quantity")]
    public long Quantity { get; set; }
}

[SugarTable("account_mail_items")]
internal sealed class AccountMailItemRow
{
    [SugarColumn(ColumnName = "mail_item_id", IsPrimaryKey = true, IsIdentity = true)]
    public long MailItemId { get; set; }

    [SugarColumn(ColumnName = "account_id")]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "source_kind", Length = 32)]
    public string SourceKind { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "source_id", Length = 64)]
    public string SourceId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "item_id", Length = 64)]
    public string ItemId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "quantity")]
    public long Quantity { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "claimed_utc", IsNullable = true)]
    public DateTime? ClaimedUtc { get; set; }
}
