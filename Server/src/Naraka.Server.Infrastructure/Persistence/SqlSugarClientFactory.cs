using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence;

/// <summary>
/// Infrastructure-only factory for future repositories. Application and Domain never receive SqlSugar types.
/// </summary>
public sealed class SqlSugarClientFactory(IMySqlConnectionStringSource source)
{
    public SqlSugarClient Create()
    {
        if (!MySqlConnectionStringPolicy.TryNormalize(source.GetConnectionString(), out var connectionString, out var status))
        {
            throw new InvalidOperationException(status);
        }

        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.MySql,
            InitKeyType = InitKeyType.Attribute,
            IsAutoCloseConnection = true
        });
    }
}
