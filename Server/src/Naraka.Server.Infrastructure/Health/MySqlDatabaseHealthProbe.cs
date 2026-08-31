using MySqlConnector;
using Naraka.Server.Application.Health;
using Naraka.Server.Infrastructure.Persistence;

namespace Naraka.Server.Infrastructure.Health;

public sealed class MySqlDatabaseHealthProbe(IMySqlConnectionStringSource source) : IDatabaseHealthProbe
{
    public async ValueTask<DatabaseHealthSnapshot> CheckAsync(CancellationToken cancellationToken)
    {
        if (!MySqlConnectionStringPolicy.TryNormalize(source.GetConnectionString(), out var connectionString, out var status))
        {
            return new DatabaseHealthSnapshot(false, status);
        }

        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) == 1
                ? new DatabaseHealthSnapshot(true, "MySQL reachable")
                : new DatabaseHealthSnapshot(false, "MySQL probe returned an unexpected result");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MySqlException exception)
        {
            return new DatabaseHealthSnapshot(false, $"MySQL unavailable (error {exception.Number})");
        }
        catch (TimeoutException)
        {
            return new DatabaseHealthSnapshot(false, "MySQL health check timed out");
        }
    }
}
