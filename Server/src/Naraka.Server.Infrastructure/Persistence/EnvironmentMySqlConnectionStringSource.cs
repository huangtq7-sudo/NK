namespace Naraka.Server.Infrastructure.Persistence;

public sealed class EnvironmentMySqlConnectionStringSource : IMySqlConnectionStringSource
{
    public const string VariableName = "NARAKA_MYSQL_CONNECTION_STRING";

    public string? GetConnectionString() =>
        Environment.GetEnvironmentVariable(VariableName);
}
