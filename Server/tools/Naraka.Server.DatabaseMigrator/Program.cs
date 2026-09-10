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
    "AND table_name IN ('schema_migrations', 'accounts', 'player_profiles', 'account_progression', " +
    "'account_profile', 'account_grants', 'currency_ledger', 'idempotency_records', " +
    "'account_inventory', 'account_equipment', 'account_shop_purchases', 'account_weapons', " +
    "'account_gacha', 'gacha_orders', 'gacha_order_results', " +
    "'account_signin', 'account_signin_claims', 'account_reward_claims', " +
    "'account_achievements', 'account_achievement_state', 'account_reddot', " +
    "'account_friends', 'account_friend_requests', 'account_blocks', " +
    "'chat_conversations', 'chat_messages', 'chat_read_positions')",
    connection))
{
    var tableCount = Convert.ToInt32(await tableCommand.ExecuteScalarAsync());
    if (tableCount != 27)
    {
        Console.Error.WriteLine($"Schema verification failed: expected 27 tables, found {tableCount}.");
        return 4;
    }
}

await using (var versionCommand = new MySqlCommand(
    "SELECT COUNT(*) FROM schema_migrations WHERE version IN ('0001', '0002', '0003', '0004', '0005', '0006', '0007', '0008', '0009')",
    connection))
{
    var versionCount = Convert.ToInt32(await versionCommand.ExecuteScalarAsync());
    if (versionCount != 9)
    {
        Console.Error.WriteLine("Schema verification failed: migrations 0001-0009 are not all recorded.");
        return 5;
    }
}

// Every account must own exactly one progression row, otherwise the lobby read would report NotFound.
await using (var backfillCommand = new MySqlCommand(
    "SELECT COUNT(*) FROM accounts AS a " +
    "LEFT JOIN account_progression AS p ON p.account_id = a.account_id " +
    "WHERE p.account_id IS NULL",
    connection))
{
    var missing = Convert.ToInt32(await backfillCommand.ExecuteScalarAsync());
    if (missing != 0)
    {
        Console.Error.WriteLine($"Schema verification failed: {missing} accounts have no progression row.");
        return 6;
    }
}

Console.WriteLine("Verified schema: 27 tables, migrations 0001-0009 recorded, progression backfilled.");

return 0;

static IReadOnlyList<string> SplitStatements(string sql) =>
    sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(statement => !string.IsNullOrWhiteSpace(statement))
        .ToArray();

internal sealed record MigrationFile(string Path, IReadOnlyList<string> Statements);
