using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Accounts;

[SugarTable("accounts")]
internal sealed class AccountRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true, IsIdentity = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "username")]
    public string Username { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "password_hash")]
    public byte[] PasswordHash { get; set; } = [];

    [SugarColumn(ColumnName = "password_salt")]
    public byte[] PasswordSalt { get; set; } = [];

    [SugarColumn(ColumnName = "password_parameters")]
    public string PasswordParameters { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "status")]
    public byte Status { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "last_login_utc", IsNullable = true)]
    public DateTime? LastLoginUtc { get; set; }
}
