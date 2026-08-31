using MySqlConnector;
using Naraka.Server.Infrastructure.Persistence;

var dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
var migrationDirectory = Path.Combine(AppContext.BaseDirectory, "Migrations");
var migrationFiles = Directory.GetFiles(migrationDirectory, "*.sql").Order(StringComparer.Ordinal).ToArray();

if (migrationFiles.Length == 0)
{
    Console.Error.WriteLine("No database migrations were found.");
    return 2;
}

var migrations = migrationFiles
    .Select(path => new MigrationFile(path, SplitStatements(File.ReadAllText(path))))
    .ToArray();

if (dryRun)
{
    foreach (var migration in migrations)
    {
        Console.WriteLine($"Validated {Path.GetFileName(migration.Path)} ({migration.Statements.Count} statements).");
    }

    return 0;
}

var source = new EnvironmentMySqlConnectionStringSource();
if (!MySqlConnectionStringPolicy.TryNormalize(source.GetConnectionString(), out var connectionString, out var status))
{
    Console.Error.WriteLine(status);
    return 3;
}

await using var connection = new MySqlConnection(connectionString);
await connection.OpenAsync();
foreach (var migration in migrations)
{
    foreach (var statement in migration.Statements)
    {
        await using var command = new MySqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync();
    }

    Console.WriteLine($"Applied {Path.GetFileName(migration.Path)}.");
}

await using (var tableCommand = new MySqlCommand(
    "SELECT COUNT(*) FROM information_schema.tables " +
    "WHERE table_schema = DATABASE() " +
    "AND table_name IN ('schema_migrations', 'accounts', 'player_profiles')",
    connection))
{
    var tableCount = Convert.ToInt32(await tableCommand.ExecuteScalarAsync());
    if (tableCount != 3)
    {
        Console.Error.WriteLine($"Schema verification failed: expected 3 P0 tables, found {tableCount}.");
        return 4;
    }
}

await using (var versionCommand = new MySqlCommand(
    "SELECT COUNT(*) FROM schema_migrations WHERE version = '0001'",
    connection))
{
    var versionCount = Convert.ToInt32(await versionCommand.ExecuteScalarAsync());
    if (versionCount != 1)
    {
        Console.Error.WriteLine("Schema verification failed: migration 0001 is not recorded.");
        return 5;
    }
}

Console.WriteLine("Verified P0 schema: 3 tables, migration 0001 recorded.");

return 0;

static IReadOnlyList<string> SplitStatements(string sql) =>
    sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(statement => !string.IsNullOrWhiteSpace(statement))
        .ToArray();

internal sealed record MigrationFile(string Path, IReadOnlyList<string> Statements);
