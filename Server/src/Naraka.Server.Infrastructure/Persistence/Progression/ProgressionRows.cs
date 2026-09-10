using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Progression;

[SugarTable("account_profile")]
internal sealed class AccountProfileRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "avatar_id", Length = 64)]
    public string AvatarId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "avatar_frame_id", Length = 64)]
    public string AvatarFrameId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "selected_hero_id", Length = 64)]
    public string SelectedHeroId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "selected_weapon_id", Length = 64)]
    public string SelectedWeaponId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "selected_pet_id", Length = 64)]
    public string SelectedPetId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "account_xp")]
    public long AccountXp { get; set; }

    [SugarColumn(ColumnName = "inventory_tier")]
    public int InventoryTier { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

[SugarTable("account_grants")]
internal sealed class AccountGrantRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "grant_key", IsPrimaryKey = true, Length = 64)]
    public string GrantKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "granted_utc")]
    public DateTime GrantedUtc { get; set; }
}

/// <summary>
/// 不可变货币流水。只插入，永不更新或删除：一旦允许改写，余额与流水就无法互相印证。
/// </summary>
[SugarTable("currency_ledger")]
internal sealed class CurrencyLedgerRow
{
    [SugarColumn(ColumnName = "ledger_id", IsPrimaryKey = true, IsIdentity = true)]
    public long LedgerId { get; set; }

    [SugarColumn(ColumnName = "account_id")]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "currency_id", Length = 32)]
    public string CurrencyId { get; set; } = string.Empty;

    /// <summary>有符号变动量。收入为正，消费为负。</summary>
    [SugarColumn(ColumnName = "delta")]
    public long Delta { get; set; }

    [SugarColumn(ColumnName = "balance_after")]
    public long BalanceAfter { get; set; }

    [SugarColumn(ColumnName = "reason", Length = 64)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>订单号或请求号。与账号、货币、原因共同构成唯一键，重复写入会被数据库拒绝。</summary>
    [SugarColumn(ColumnName = "reference_id", Length = 64)]
    public string ReferenceId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }
}

[SugarTable("idempotency_records")]
internal sealed class IdempotencyRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "request_id", IsPrimaryKey = true, Length = 64)]
    public string RequestId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "operation", Length = 64)]
    public string Operation { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "status_code")]
    public int StatusCode { get; set; }

    [SugarColumn(ColumnName = "response_payload", ColumnDataType = "MEDIUMTEXT")]
    public string ResponsePayload { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }
}
