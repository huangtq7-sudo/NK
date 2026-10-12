using MySqlConnector;
using Naraka.Server.Application.Accounts;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Expeditions;
using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Quests;
using Naraka.Server.Domain.Quests;
using Naraka.Server.Infrastructure.Config;
using Naraka.Server.Infrastructure.Persistence;
using Naraka.Server.Infrastructure.Persistence.Accounts;
using Naraka.Server.Infrastructure.Persistence.Expeditions;
using Naraka.Server.Infrastructure.Persistence.Progression;
using Naraka.Server.Infrastructure.Persistence.Quests;
using Naraka.Server.Infrastructure.Security;

// P4.1 任务真实数据库冒烟。创建并在结束时删除自己的 p4_ 测试账号；连接串只来自环境变量，不打印任何值。
// 覆盖：接取 → 对话目标 → 提交 → 两个不同 RequestId 并发领取只成功一次 → 同 RequestId 重放 →
// 新服务实例读取日志（模拟重登）→ 账号经验/等级/货币流水/经验流水/领取记录各只有一份。

const string QuestId = "main_c1_01";

var source = new EnvironmentMySqlConnectionStringSource();
if (!MySqlConnectionStringPolicy.TryNormalize(source.GetConnectionString(), out var connectionString, out var status))
{
    Console.Error.WriteLine(status);
    return 2;
}

// --cleanup <username>：删除 Unity 真实链路冒烟创建的 p4_live_ 测试账号及其全部数据。
// 只接受 p4_live_ 前缀，绝不删除其他账号。
if (args.Length == 2 && args[0] == "--cleanup")
{
    var target = args[1].Trim();
    if (!target.StartsWith("p4_live_", StringComparison.Ordinal) || target.Length > 64)
    {
        Console.Error.WriteLine("Cleanup only accepts p4_live_ test accounts.");
        return 12;
    }

    await using var lookup = new MySqlConnection(connectionString);
    await lookup.OpenAsync();
    await using var find = new MySqlCommand("SELECT account_id FROM accounts WHERE username = @username", lookup);
    find.Parameters.AddWithValue("@username", target);
    var found = await find.ExecuteScalarAsync();
    if (found is null)
    {
        Console.WriteLine("Live test account not found; nothing to remove.");
        return 0;
    }

    var liveAccountId = Convert.ToInt64(found);
    await using var expeditionQuery = new MySqlCommand("SELECT expedition_id FROM expeditions WHERE account_id = @accountId", lookup);
    expeditionQuery.Parameters.AddWithValue("@accountId", liveAccountId);
    var expeditionIds = new List<string>();
    await using (var reader = await expeditionQuery.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            expeditionIds.Add(reader.GetString(0));
        }
    }

    foreach (var expedition in expeditionIds.Skip(1))
    {
        await CleanupExpeditionAsync(connectionString, expedition);
    }

    await CleanupAsync(connectionString, liveAccountId, target, expeditionIds.FirstOrDefault());
    Console.WriteLine($"Live test account removed with {expeditionIds.Count} expedition(s).");
    return 0;
}

var configResult = new FileGameConfigSource().Load(GameConfig.RequiredSchemaVersion);
if (!configResult.IsSuccess || configResult.Config is null)
{
    Console.Error.WriteLine($"Game configuration is unavailable: {configResult.Status}.");
    return 3;
}

var config = configResult.Config;
if (!config.TryGetQuest(QuestId, out var quest) || quest is null)
{
    Console.Error.WriteLine("The first main quest is missing from the generated configuration.");
    return 3;
}

var objective = config.GetQuestObjectives(QuestId)[0];
var username = $"p4_smoke_{Guid.NewGuid():N}"[..32];
var password = $"Smoke-{Guid.NewGuid():N}!";
long? accountId = null;
string? expeditionId = null;
var clock = new SmokeClock(DateTimeOffset.UtcNow);

try
{
    var factory = new SqlSugarClientFactory(source);
    var accountService = new AccountService(new SqlSugarAccountRepository(factory), new Argon2idPasswordHasher());
    var registration = await accountService.RegisterAsync(username, password, CancellationToken.None);
    if (registration.Status != AccountRegistrationStatus.Success || registration.AccountId is null)
    {
        Console.Error.WriteLine($"Quest smoke registration failed: {registration.Status}.");
        return 4;
    }

    accountId = registration.AccountId.Value;
    var profiles = new AccountProfileService(new SqlSugarAccountProfileRepository(factory), config);
    var provisioned = await profiles.EnsureProvisionedAsync(accountId.Value, CancellationToken.None);
    if (provisioned.Status != LobbyOperationStatus.Success || provisioned.View is null)
    {
        Console.Error.WriteLine($"Quest smoke provisioning failed: {provisioned.Status}.");
        return 5;
    }

    var copperBefore = await ScalarAsync(connectionString, "SELECT copper FROM account_progression WHERE account_id = @accountId", accountId.Value);
    var bloodBefore = await ScalarAsync(
        connectionString,
        "SELECT COALESCE(SUM(quantity), 0) FROM account_inventory WHERE account_id = @accountId AND item_id = 'consumable_blood_pack'",
        accountId.Value);

    var expeditionRepository = new SqlSugarExpeditionRepository(factory, config);
    var expeditions = new ExpeditionService(expeditionRepository, new GuidExpeditionIdGenerator());
    var started = await expeditions.StartAsync(accountId.Value, "p4-smoke-start", CancellationToken.None);
    if (started.Status != LobbyOperationStatus.Success || started.Snapshot is null)
    {
        Console.Error.WriteLine($"Quest smoke expedition start failed: {started.Status}.");
        return 6;
    }

    expeditionId = started.Snapshot.ExpeditionId.Value;
    var repository = new SqlSugarQuestRepository(factory, config);
    var probe = new ExpeditionQuestProbe(expeditionRepository);
    QuestService NewService() => new(repository, probe, config, clock);
    var service = NewService();

    Require(await service.AcceptAsync(accountId.Value, "p4-smoke-accept", QuestId, quest.OfferDialogueId, default),
        QuestStatus.Accepted, "accept");
    clock.Advance(TimeSpan.FromSeconds(10));
    Require(await service.CompleteDialogueAsync(
            accountId.Value, "p4-smoke-talk", QuestId, objective.ObjectiveId, objective.TargetDialogueId, default),
        QuestStatus.ReadyToSubmit, "complete dialogue");
    clock.Advance(TimeSpan.FromSeconds(10));
    Require(await service.SubmitAsync(accountId.Value, "p4-smoke-submit", QuestId, quest.SubmitDialogueId, default),
        QuestStatus.RewardPending, "submit");
    clock.Advance(TimeSpan.FromSeconds(10));

    // 两个独立服务实例模拟两条连接，用不同 RequestId 并发领取。
    var claims = await Task.WhenAll(
        NewService().ClaimAsync(accountId.Value, "p4-smoke-claim-a", QuestId, default),
        NewService().ClaimAsync(accountId.Value, "p4-smoke-claim-b", QuestId, default));
    var successes = claims.Count(result => result.Status == LobbyOperationStatus.Success);
    var duplicates = claims.Count(result => result.Status == LobbyOperationStatus.AlreadyClaimed);
    if (successes != 1 || duplicates != 1)
    {
        Console.Error.WriteLine(
            $"Concurrent claims must succeed exactly once: {string.Join(',', claims.Select(result => result.Status))}.");
        return 7;
    }

    var winner = claims.Single(result => result.Status == LobbyOperationStatus.Success);
    var winnerRequest = ReferenceEquals(winner.Claim, claims[0].Claim) ? "p4-smoke-claim-a" : "p4-smoke-claim-b";
    clock.Advance(TimeSpan.FromSeconds(10));
    var replay = await NewService().ClaimAsync(accountId.Value, winnerRequest, QuestId, default);
    if (replay.Status != LobbyOperationStatus.Success || !replay.IsReplay ||
        replay.Claim?.AccountXpAfter != winner.Claim?.AccountXpAfter)
    {
        Console.Error.WriteLine($"Same RequestId claim replay failed: {replay.Status}, replay={replay.IsReplay}.");
        return 8;
    }

    clock.Advance(TimeSpan.FromSeconds(10));
    var relogin = await NewService().GetJournalAsync(accountId.Value, default);
    if (relogin.Journal?.Quests.SingleOrDefault()?.Status != QuestStatus.Claimed || relogin.Journal.AccountLevel != 2)
    {
        Console.Error.WriteLine("A fresh service did not restore the claimed quest and account level 2.");
        return 9;
    }

    var checks = new (string Name, string Sql, long Expected)[]
    {
        ("account_xp", "SELECT account_xp FROM account_profile WHERE account_id = @accountId", 100),
        ("lobby_level", "SELECT account_level FROM account_progression WHERE account_id = @accountId", 2),
        ("copper_delta", "SELECT copper FROM account_progression WHERE account_id = @accountId", copperBefore + 300),
        ("blood_pack_delta",
            "SELECT COALESCE(SUM(quantity), 0) FROM account_inventory WHERE account_id = @accountId AND item_id = 'consumable_blood_pack'",
            bloodBefore + 2),
        ("quest_ledger_rows",
            "SELECT COUNT(*) FROM currency_ledger WHERE account_id = @accountId AND reason = 'QuestReward'", 1),
        ("xp_ledger_rows", "SELECT COUNT(*) FROM account_xp_ledger WHERE account_id = @accountId", 1),
        ("reward_claim_rows",
            "SELECT COUNT(*) FROM account_reward_claims WHERE account_id = @accountId AND reward_kind = 'Quest'", 1),
        ("idempotency_rows",
            "SELECT COUNT(*) FROM idempotency_records WHERE account_id = @accountId AND operation LIKE 'quest.%'", 4)
    };
    foreach (var check in checks)
    {
        var actual = await ScalarAsync(connectionString, check.Sql, accountId.Value);
        if (actual != check.Expected)
        {
            Console.Error.WriteLine($"Database check {check.Name} expected {check.Expected}, found {actual}.");
            return 10;
        }
    }

    var settled = await expeditions.ReturnToLobbyAsync(
        accountId.Value, started.Snapshot.ExpeditionId, "p4-smoke-return", CancellationToken.None);
    if (settled.Status != LobbyOperationStatus.Success)
    {
        Console.Error.WriteLine($"Quest smoke expedition settlement failed: {settled.Status}.");
        return 11;
    }

    Console.WriteLine(
        "Quest smoke passed: accept, dialogue, submit, single concurrent claim, replay, restore, ledgers and level.");
    return 0;
}
finally
{
    if (accountId is not null)
    {
        await CleanupAsync(connectionString, accountId.Value, username, expeditionId);
        Console.WriteLine("Quest smoke account removed.");
    }
}

static void Require(QuestResult result, QuestStatus expected, string step)
{
    var actual = result.Journal?.Quests.SingleOrDefault()?.Status;
    if (result.Status != LobbyOperationStatus.Success || actual != expected)
    {
        throw new InvalidOperationException($"Quest smoke {step} failed: {result.Status}, status={actual}.");
    }
}

static async Task<long> ScalarAsync(string connectionString, string sql, long accountId)
{
    await using var connection = new MySqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new MySqlCommand(sql, connection);
    command.Parameters.AddWithValue("@accountId", accountId);
    return Convert.ToInt64(await command.ExecuteScalarAsync());
}

static async Task CleanupAsync(string connectionString, long accountId, string username, string? expeditionId)
{
    await using var connection = new MySqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    try
    {
        var statements = new List<string>();
        if (!string.IsNullOrWhiteSpace(expeditionId))
        {
            statements.AddRange(new[]
            {
                "DELETE FROM expedition_settlement_assets WHERE expedition_id = @expeditionId",
                "DELETE FROM expedition_settlements WHERE expedition_id = @expeditionId",
                "DELETE FROM expedition_events WHERE expedition_id = @expeditionId",
                "DELETE FROM expedition_assets WHERE expedition_id = @expeditionId",
                "DELETE FROM expeditions WHERE expedition_id = @expeditionId"
            });
        }

        statements.AddRange(new[]
        {
            "DELETE FROM account_quest_objectives WHERE account_id = @accountId",
            "DELETE FROM account_quests WHERE account_id = @accountId",
            "DELETE FROM account_xp_ledger WHERE account_id = @accountId",
            "DELETE FROM account_reward_claims WHERE account_id = @accountId",
            "DELETE FROM idempotency_records WHERE account_id = @accountId",
            "DELETE FROM account_mail_items WHERE account_id = @accountId",
            "DELETE FROM account_inventory WHERE account_id = @accountId",
            "DELETE FROM account_equipment WHERE account_id = @accountId",
            "DELETE FROM account_weapons WHERE account_id = @accountId",
            "DELETE FROM currency_ledger WHERE account_id = @accountId",
            "DELETE FROM account_grants WHERE account_id = @accountId",
            "DELETE FROM account_profile WHERE account_id = @accountId",
            "DELETE FROM account_progression WHERE account_id = @accountId",
            "DELETE FROM player_profiles WHERE account_id = @accountId",
            "DELETE FROM accounts WHERE account_id = @accountId AND username = @username"
        });

        foreach (var sql in statements)
        {
            await using var command = new MySqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@accountId", accountId);
            command.Parameters.AddWithValue("@username", username);
            command.Parameters.AddWithValue("@expeditionId", expeditionId ?? string.Empty);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

static async Task CleanupExpeditionAsync(string connectionString, string expeditionId)
{
    await using var connection = new MySqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    foreach (var sql in new[]
             {
                 "DELETE FROM expedition_settlement_assets WHERE expedition_id = @expeditionId",
                 "DELETE FROM expedition_settlements WHERE expedition_id = @expeditionId",
                 "DELETE FROM expedition_events WHERE expedition_id = @expeditionId",
                 "DELETE FROM expedition_assets WHERE expedition_id = @expeditionId",
                 "DELETE FROM expeditions WHERE expedition_id = @expeditionId"
             })
    {
        await using var command = new MySqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@expeditionId", expeditionId);
        await command.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
}

/// <summary>冒烟用的可推进时钟：用来越过对话目标与提交的最短时长，而不必真的等待。</summary>
internal sealed class SmokeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
