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

return 0;

static IReadOnlyList<string> SplitStatements(string sql) =>
    sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(statement => !string.IsNullOrWhiteSpace(statement))
        .ToArray();

internal sealed record MigrationFile(string Path, IReadOnlyList<string> Statements);
