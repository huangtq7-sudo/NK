using Naraka.Server.Application.Health;

namespace Naraka.Server.Infrastructure.Health;

public sealed class EnvironmentDatabaseHealthProbe : IDatabaseHealthProbe
{
    public const string ConnectionStringVariable = "NARAKA_MYSQL_CONNECTION_STRING";

    public ValueTask<DatabaseHealthSnapshot> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configured = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(ConnectionStringVariable));

        var snapshot = configured
            ? new DatabaseHealthSnapshot(false, "Configured; SqlSugar/MySQL probe not integrated")
            : new DatabaseHealthSnapshot(false, $"Missing environment variable: {ConnectionStringVariable}");

        return ValueTask.FromResult(snapshot);
    }
}
