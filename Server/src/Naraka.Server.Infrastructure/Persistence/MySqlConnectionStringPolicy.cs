using MySqlConnector;

namespace Naraka.Server.Infrastructure.Persistence;

public static class MySqlConnectionStringPolicy
{
    private const uint MaximumHealthCheckTimeoutSeconds = 5;

    public static bool TryNormalize(string? raw, out string normalized, out string status)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            status = $"Missing environment variable: {EnvironmentMySqlConnectionStringSource.VariableName}";
            return false;
        }

        try
        {
            var builder = new MySqlConnectionStringBuilder(raw);
            if (string.IsNullOrWhiteSpace(builder.Server) ||
                string.IsNullOrWhiteSpace(builder.Database) ||
                string.IsNullOrWhiteSpace(builder.UserID))
            {
                status = "Invalid MySQL configuration: Server, Database and User ID are required";
                return false;
            }

            builder.Pooling = true;
            if (builder.ConnectionTimeout == 0 || builder.ConnectionTimeout > MaximumHealthCheckTimeoutSeconds)
            {
                builder.ConnectionTimeout = MaximumHealthCheckTimeoutSeconds;
            }

            normalized = builder.ConnectionString;
            status = "Configured";
            return true;
        }
        catch (ArgumentException)
        {
            status = "Invalid MySQL connection string";
            return false;
        }
    }
}
