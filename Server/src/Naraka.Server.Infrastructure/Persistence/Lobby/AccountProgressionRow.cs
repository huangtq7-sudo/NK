using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Lobby;

[SugarTable("account_progression")]
internal sealed class AccountProgressionRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "account_level")]
    public int AccountLevel { get; set; } = 1;

    [SugarColumn(ColumnName = "copper")]
    public long Copper { get; set; }

    [SugarColumn(ColumnName = "silk")]
    public long Silk { get; set; }

    [SugarColumn(ColumnName = "gold")]
    public long Gold { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}
