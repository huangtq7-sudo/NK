using MySqlConnector;
using Naraka.Server.Application.Accounts;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Expeditions;
using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Progression;
using Naraka.Server.Domain.Expeditions;
using Naraka.Server.Infrastructure.Config;
using Naraka.Server.Infrastructure.Persistence;
using Naraka.Server.Infrastructure.Persistence.Accounts;
using Naraka.Server.Infrastructure.Persistence.Expeditions;
using Naraka.Server.Infrastructure.Persistence.Inventory;
using Naraka.Server.Infrastructure.Persistence.Progression;
using Naraka.Server.Infrastructure.Security;

var source = new EnvironmentMySqlConnectionStringSource();
if (!MySqlConnectionStringPolicy.TryNormalize(source.GetConnectionString(), out var connectionString, out var status))
{
    Console.Error.WriteLine(status);
    return 2;
}

var configResult = new FileGameConfigSource().Load(GameConfig.RequiredSchemaVersion);
if (!configResult.IsSuccess || configResult.Config is null)
{
    Console.Error.WriteLine($"Game configuration is unavailable: {configResult.Status}.");
    return 3;
}

var config = configResult.Config;
var item = config.Catalog.Items.First();
var username = $"p3_smoke_{Guid.NewGuid():N}";
var password = $"Smoke-{Guid.NewGuid():N}!";
long? accountId = null;
string? expeditionId = null;

try
{
    var factory = new SqlSugarClientFactory(source);
    var accountService = new AccountService(
        new SqlSugarAccountRepository(factory),
        new Argon2idPasswordHasher());
    var registration = await accountService.RegisterAsync(username, password, CancellationToken.None);
    if (registration.Status != AccountRegistrationStatus.Success || registration.AccountId is null)
    {
        Console.Error.WriteLine($"Expedition smoke registration failed: {registration.Status}.");
        return 4;
    }

    accountId = registration.AccountId.Value;
    var profileService = new AccountProfileService(
        new SqlSugarAccountProfileRepository(factory),
        config);
    var provisioned = await profileService.EnsureProvisionedAsync(accountId.Value, CancellationToken.None);
    if (provisioned.Status != LobbyOperationStatus.Success || provisioned.View is null)
    {
        Console.Error.WriteLine($"Expedition smoke provisioning failed: {provisioned.Status}.");
        return 5;
    }

    var repository = new SqlSugarExpeditionRepository(factory, config);
    var service = new ExpeditionService(repository, new GuidExpeditionIdGenerator());
    var started = await service.StartAsync(accountId.Value, "p3-smoke-start", CancellationToken.None);
    if (started.Status != LobbyOperationStatus.Success || started.Snapshot is null)
    {
        Console.Error.WriteLine($"Expedition smoke start failed: {started.Status}.");
        return 6;
    }

    expeditionId = started.Snapshot.ExpeditionId.Value;
    var firstDrops = await service.GrantMonsterDropsAsync(
        accountId.Value,
        started.Snapshot.ExpeditionId,
        "p3-smoke-drop-before-death",
        new[]
        {
            new ExpeditionAsset(
                ExpeditionAssetKind.Item,
                item.ItemId,
                2,
                ExpeditionAssetSource.MonsterDrop),
            new ExpeditionAsset(
                ExpeditionAssetKind.Currency,
                "Copper",
                5,
                ExpeditionAssetSource.MonsterDrop)
        },
        CancellationToken.None);
    if (firstDrops.Status != LobbyOperationStatus.Success)
    {
        Console.Error.WriteLine($"Expedition smoke first drop failed: {firstDrops.Status}.");
        return 7;
    }

    var death = await service.RecordDeathAsync(
        accountId.Value,
        started.Snapshot.ExpeditionId,
        "p3-smoke-death",
        CancellationToken.None);
    if (death.Status != LobbyOperationStatus.Success || death.Death?.ClearedAssets.Count != 2)
    {
        Console.Error.WriteLine($"Expedition smoke death failed: {death.Status}.");
        return 8;
    }

    var secondDrops = await service.GrantMonsterDropsAsync(
        accountId.Value,
        started.Snapshot.ExpeditionId,
        "p3-smoke-drop-after-death",
        new[]
        {
            new ExpeditionAsset(
                ExpeditionAssetKind.Item,
                item.ItemId,
                item.StackLimit + 2L,
                ExpeditionAssetSource.MonsterDrop),
            new ExpeditionAsset(
                ExpeditionAssetKind.Currency,
                "Copper",
                7,
                ExpeditionAssetSource.MonsterDrop)
        },
        CancellationToken.None);
    if (secondDrops.Status != LobbyOperationStatus.Success)
    {
        Console.Error.WriteLine($"Expedition smoke second drop failed: {secondDrops.Status}.");
        return 9;
    }

    var settled = await service.ReturnToLobbyAsync(
        accountId.Value,
        started.Snapshot.ExpeditionId,
        "p3-smoke-settle",
        CancellationToken.None);
    var replayed = await service.ReturnToLobbyAsync(
        accountId.Value,
        started.Snapshot.ExpeditionId,
        "p3-smoke-settle-retry",
        CancellationToken.None);
    if (settled.Status != LobbyOperationStatus.Success ||
        settled.Settlement is null ||
        replayed.Status != LobbyOperationStatus.Success ||
        !replayed.IsReplay ||
        replayed.Settlement is null ||
        !SettlementMatches(replayed.Settlement, settled.Settlement))
    {
        Console.Error.WriteLine(
            $"Expedition smoke settlement or replay failed: " +
            $"settled={settled.Status}, settledSummary={settled.Settlement is not null}, " +
            $"replayed={replayed.Status}, isReplay={replayed.IsReplay}, " +
            $"replayedSummary={replayed.Settlement is not null}, " +
            $"summaryMatch={settled.Settlement is not null && replayed.Settlement is not null && SettlementMatches(replayed.Settlement, settled.Settlement)}, " +
            $"differences={DescribeSettlementDifferences(settled.Settlement, replayed.Settlement)}.");
        return 10;
    }

    var profileAfter = await profileService.GetProfileAsync(accountId.Value, CancellationToken.None);
    var inventory = await new SqlSugarInventoryRepository(factory)
        .ListSlotsAsync(accountId.Value, CancellationToken.None);
    var itemSlot = inventory.SingleOrDefault(slot => slot.ItemId == item.ItemId);
    var mailboxQuantity = await ReadMailboxQuantityAsync(
        connectionString,
        accountId.Value,
        started.Snapshot.ExpeditionId.Value,
        item.ItemId);
    if (profileAfter.View is null ||
        profileAfter.View.Copper != provisioned.View.Copper + 7 ||
        itemSlot?.Quantity != item.StackLimit ||
        mailboxQuantity != 2)
    {
        Console.Error.WriteLine("Expedition smoke grant verification failed.");
        return 11;
    }

    Console.WriteLine(
        "Expedition smoke passed: death cleanup, atomic settlement, inventory overflow, currency ledger and replay.");
    return 0;
}
finally
{
    if (accountId is not null)
    {
        await CleanupAsync(connectionString, accountId.Value, username, expeditionId);
        Console.WriteLine("Expedition smoke fixture removed.");
    }
}

static async Task<long> ReadMailboxQuantityAsync(
    string connectionString,
    long accountId,
    string expeditionId,
    string itemId)
{
    await using var connection = new MySqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new MySqlCommand(
        "SELECT COALESCE(SUM(quantity), 0) FROM account_mail_items " +
        "WHERE account_id = @accountId AND source_kind = 'ExpeditionSettlement' " +
        "AND source_id = @sourceId AND item_id = @itemId AND claimed_utc IS NULL",
        connection);
    command.Parameters.AddWithValue("@accountId", accountId);
    command.Parameters.AddWithValue("@sourceId", expeditionId);
    command.Parameters.AddWithValue("@itemId", itemId);
    return Convert.ToInt64(await command.ExecuteScalarAsync());
}

static bool SettlementMatches(
    Naraka.Server.Domain.Expeditions.ExpeditionSettlementSummary left,
    Naraka.Server.Domain.Expeditions.ExpeditionSettlementSummary right) =>
    left.ExpeditionId == right.ExpeditionId &&
    left.AccountId == right.AccountId &&
    left.RequestId == right.RequestId &&
    left.Reason == right.Reason &&
    left.SettledAt == right.SettledAt &&
    left.DeathCount == right.DeathCount &&
    left.Assets.SequenceEqual(right.Assets);

static string DescribeSettlementDifferences(
    Naraka.Server.Domain.Expeditions.ExpeditionSettlementSummary? first,
    Naraka.Server.Domain.Expeditions.ExpeditionSettlementSummary? replay)
{
    if (first is null || replay is null)
    {
        return "missing-summary";
    }

    var differences = new List<string>();
    if (first.ExpeditionId != replay.ExpeditionId) differences.Add("expedition-id");
    if (first.AccountId != replay.AccountId) differences.Add("account-id");
    if (first.RequestId != replay.RequestId) differences.Add("request-id");
    if (first.Reason != replay.Reason) differences.Add("reason");
    if (first.SettledAt != replay.SettledAt)
    {
        differences.Add($"settled-at-ticks:{first.SettledAt.UtcTicks - replay.SettledAt.UtcTicks}");
    }
    if (first.DeathCount != replay.DeathCount) differences.Add("death-count");
    if (!first.Assets.SequenceEqual(replay.Assets))
    {
        differences.Add(
            $"assets:first=[{DescribeAssets(first.Assets)}],replay=[{DescribeAssets(replay.Assets)}]");
    }
    return differences.Count == 0 ? "none" : string.Join(',', differences);
}

static string DescribeAssets(IReadOnlyList<ExpeditionAsset> assets) =>
    string.Join(
        '|',
        assets.Select(asset =>
            $"{asset.Kind}:{asset.AssetId}:{asset.Source}:{asset.Quantity}"));

static async Task CleanupAsync(
    string connectionString,
    long accountId,
    string username,
    string? expeditionId)
{
    await using var connection = new MySqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    try
    {
        if (!string.IsNullOrWhiteSpace(expeditionId))
        {
            foreach (var sql in new[]
                     {
                         "DELETE FROM expedition_settlement_assets WHERE expedition_id = @expeditionId",
                         "DELETE FROM expedition_settlements WHERE expedition_id = @expeditionId",
                         "DELETE FROM expedition_events WHERE expedition_id = @expeditionId",
                         "DELETE FROM expedition_assets WHERE expedition_id = @expeditionId",
                         "DELETE FROM expeditions WHERE expedition_id = @expeditionId"
                     })
            {
                await ExecuteDeleteAsync(connection, transaction, sql, accountId, username, expeditionId);
            }
        }

        foreach (var sql in new[]
                 {
                     "DELETE FROM account_mail_items WHERE account_id = @accountId",
                     "DELETE FROM account_inventory WHERE account_id = @accountId",
                     "DELETE FROM currency_ledger WHERE account_id = @accountId",
                     "DELETE FROM account_grants WHERE account_id = @accountId",
                     "DELETE FROM account_profile WHERE account_id = @accountId",
                     "DELETE FROM account_progression WHERE account_id = @accountId",
                     "DELETE FROM player_profiles WHERE account_id = @accountId",
                     "DELETE FROM accounts WHERE account_id = @accountId AND username = @username"
                 })
        {
            await ExecuteDeleteAsync(connection, transaction, sql, accountId, username, expeditionId);
        }

        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

static async Task ExecuteDeleteAsync(
    MySqlConnection connection,
    MySqlTransaction transaction,
    string sql,
    long accountId,
    string username,
    string? expeditionId)
{
    await using var command = new MySqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("@accountId", accountId);
    command.Parameters.AddWithValue("@username", username);
    command.Parameters.AddWithValue("@expeditionId", expeditionId ?? string.Empty);
    await command.ExecuteNonQueryAsync();
}
