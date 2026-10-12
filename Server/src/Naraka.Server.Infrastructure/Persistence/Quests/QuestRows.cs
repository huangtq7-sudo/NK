using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Quests;

[SugarTable("account_quests")]
internal sealed class AccountQuestRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "quest_instance_key", IsPrimaryKey = true, Length = 96)]
    public string QuestInstanceKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "quest_id", Length = 64)]
    public string QuestId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "status", Length = 16)]
    public string Status { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "accepted_utc")]
    public DateTime AcceptedUtc { get; set; }

    [SugarColumn(ColumnName = "step_started_utc")]
    public DateTime StepStartedUtc { get; set; }

    [SugarColumn(ColumnName = "submitted_utc", IsNullable = true)]
    public DateTime? SubmittedUtc { get; set; }

    [SugarColumn(ColumnName = "claimed_utc", IsNullable = true)]
    public DateTime? ClaimedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

[SugarTable("account_quest_objectives")]
internal sealed class AccountQuestObjectiveRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "quest_instance_key", IsPrimaryKey = true, Length = 96)]
    public string QuestInstanceKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "objective_id", IsPrimaryKey = true, Length = 64)]
    public string ObjectiveId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "progress")]
    public int Progress { get; set; }

    [SugarColumn(ColumnName = "completed_utc", IsNullable = true)]
    public DateTime? CompletedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>账号经验流水。只插入：任何时候都能回答"这些经验是哪个任务给的"。</summary>
[SugarTable("account_xp_ledger")]
internal sealed class AccountXpLedgerRow
{
    [SugarColumn(ColumnName = "ledger_id", IsPrimaryKey = true, IsIdentity = true)]
    public long LedgerId { get; set; }

    [SugarColumn(ColumnName = "account_id")]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "source", Length = 32)]
    public string Source { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "reference_id", Length = 96)]
    public string ReferenceId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "delta")]
    public long Delta { get; set; }

    [SugarColumn(ColumnName = "xp_after")]
    public long XpAfter { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }
}
