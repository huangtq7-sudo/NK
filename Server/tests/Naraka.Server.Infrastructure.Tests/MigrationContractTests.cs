namespace Naraka.Server.Infrastructure.Tests;

public sealed class MigrationContractTests
{
    private static readonly string Migration = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Migrations", "0001_p0_identity.sql"));

    [Fact]
    public void InitialMigrationIsNonDestructiveAndIdempotent()
    {
        Assert.DoesNotContain("DROP ", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS accounts", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS player_profiles", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", Migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InitialMigrationStoresPasswordMaterialWithoutPlaintextPasswordColumn()
    {
        Assert.Contains("password_hash VARBINARY", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("password_salt VARBINARY", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" password VARCHAR", Migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InitialMigrationUsesMySql57CompatibleIdentitySyntax()
    {
        Assert.Contains("AUTO_INCREMENT", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE INDEX IF NOT EXISTS", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CHECK (", Migration, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ProgressionMigrationContractTests
{
    private static readonly string Migration = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Migrations", "0002_p1_account_progression.sql"));

    [Fact]
    public void ProgressionMigrationIsNonDestructiveAndLeavesP0TablesAlone()
    {
        Assert.DoesNotContain("DROP ", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS account_progression", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", Migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgressionMigrationEnforcesNonNegativeBalancesWithUnsignedColumns()
    {
        // MySQL 5.7 parses but ignores CHECK, so UNSIGNED is the only column level guard available.
        Assert.DoesNotContain("CHECK (", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("copper BIGINT UNSIGNED NOT NULL DEFAULT 0", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("silk BIGINT UNSIGNED NOT NULL DEFAULT 0", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gold BIGINT UNSIGNED NOT NULL DEFAULT 0", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("account_level INT UNSIGNED NOT NULL DEFAULT 1", Migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgressionMigrationBackfillsExistingAccountsAndRecordsItsOwnVersion()
    {
        Assert.Contains("LEFT JOIN account_progression", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IS NULL", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'0002'", Migration, StringComparison.Ordinal);
        Assert.DoesNotContain("'0001'", Migration, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationSplitsIntoThreeExecutableStatements()
    {
        // The migrator splits on ';', so a stray semicolon inside a comment would corrupt a statement.
        var statements = Migration
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => !string.IsNullOrWhiteSpace(statement))
            .ToArray();

        Assert.Equal(3, statements.Length);
        Assert.All(statements, statement => Assert.Contains(
            statement.Split('\n').Select(line => line.Trim()),
            line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal)));
    }
}

/// <summary>
/// 迁移 0003 建立 P1 的账号资料、一次性赠送、不可变货币流水与幂等记录。
/// 这些断言存在的理由：0001 与 0002 已经部署到云端，任何对它们的改动都会让线上库与仓库分叉，
/// 而 MySQL 5.7 没有 ADD COLUMN IF NOT EXISTS，一条 ALTER 会让重复执行直接失败。
/// </summary>
public sealed class AccountFoundationMigrationContractTests
{
    private static readonly string Migration = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Migrations", "0003_p1_account_foundation.sql"));

    /// <summary>
    /// 去掉 `--` 注释行后的可执行 SQL。禁用语法的断言必须针对真正会执行的语句，
    /// 否则说明"本迁移不使用 ALTER TABLE"的注释本身就会让测试失败。
    /// </summary>
    private static readonly string ExecutableSql = string.Join(
        '\n',
        Migration.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    [Fact]
    public void MigrationIsAdditiveAndRepeatable()
    {
        Assert.DoesNotContain("DROP ", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", ExecutableSql, StringComparison.OrdinalIgnoreCase);

        // 不能有独立的 UPDATE 语句改写既有行。"ON DUPLICATE KEY UPDATE" 与 "ON UPDATE RESTRICT"
        // 都包含 UPDATE 这个词，因此这里按行首判断，而不是简单的子串包含。
        Assert.DoesNotContain(
            ExecutableSql.Split('\n').Select(line => line.TrimStart()),
            line => line.StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MigrationCreatesEveryP1FoundationTable()
    {
        Assert.Contains("CREATE TABLE IF NOT EXISTS account_profile", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS account_grants", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS currency_ledger", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS idempotency_records", Migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StarterGrantCanOnlyBeRecordedOncePerAccount()
    {
        // 主键 (account_id, grant_key) 是初始赠送幂等性的最终保障：
        // 即使两个并发登录都读到"尚未赠送"，第二次插入也会撞主键并整体回滚。
        Assert.Contains("PRIMARY KEY (account_id, grant_key)", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "UNIQUE KEY uq_currency_ledger_reference (account_id, currency_id, reason, reference_id)",
            Migration,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LedgerKeepsSignedDeltaAndUnsignedBalance()
    {
        // 变动量必须有符号（消费为负），余额必须无符号（永不为负）。
        Assert.Contains("delta BIGINT NOT NULL", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("balance_after BIGINT UNSIGNED NOT NULL", Migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delta BIGINT UNSIGNED", Migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationUsesMySql57CompatibleSyntaxOnly()
    {
        Assert.DoesNotContain("CHECK (", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE INDEX IF NOT EXISTS", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IF NOT EXISTS (", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ENGINE=InnoDB", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationRecordsOnlyItsOwnVersion()
    {
        Assert.Contains("'0003'", ExecutableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'0001'", ExecutableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'0002'", ExecutableSql, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationSplitsIntoFiveExecutableStatements()
    {
        // 迁移器按 ';' 拆分，注释里的分号会破坏语句边界。
        var statements = Migration
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => !string.IsNullOrWhiteSpace(statement))
            .ToArray();

        Assert.Equal(5, statements.Length);
    }

    [Fact]
    public void EarlierMigrationsAreNeverEdited()
    {
        // 0001 与 0002 已经部署到云端。这两个哈希把"不修改已部署迁移"变成可执行的约束。
        Assert.Equal(
            "b2a1eddc0f7ae2b1c953ec086862d4f753ebc86616521f12d5e229405d9108c0",
            Sha256Of("0001_p0_identity.sql"));
        Assert.Equal(
            "fa9370a90e433e8f73341b956174393dc53724492425698492239e28db76787e",
            Sha256Of("0002_p1_account_progression.sql"));
    }

    private static string Sha256Of(string fileName)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Migrations", fileName));
        var text = System.Text.Encoding.UTF8.GetString(bytes)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        return Convert
            .ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
    }
}

/// <summary>迁移 0004 建立堆叠仓库与战斗负载槽。</summary>
public sealed class InventoryMigrationContractTests
{
    private static readonly string Migration = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Migrations", "0004_p1_inventory.sql"));

    private static readonly string ExecutableSql = string.Join(
        '\n',
        Migration.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    [Fact]
    public void MigrationIsAdditiveAndRepeatable()
    {
        Assert.DoesNotContain("DROP ", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS account_inventory", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS account_equipment", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OneItemTypeOccupiesOneRowAndQuantityCannotGoNegative()
    {
        // 堆叠模型：主键是 (账号, 物品)，因此同一种物品不可能出现两行。
        Assert.Contains("PRIMARY KEY (account_id, item_id)", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quantity BIGINT UNSIGNED NOT NULL", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateSoulstoneInTheBattleLoadIsBlockedByAUniqueKey()
    {
        Assert.Contains(
            "UNIQUE KEY uq_account_equipment_item (account_id, slot_kind, item_id)",
            ExecutableSql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationUsesMySql57CompatibleSyntaxOnly()
    {
        Assert.DoesNotContain("CHECK (", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE INDEX IF NOT EXISTS", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ENGINE=InnoDB", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationRecordsOnlyItsOwnVersion()
    {
        Assert.Contains("'0004'", ExecutableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'0003'", ExecutableSql, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationSplitsIntoThreeExecutableStatements()
    {
        var statements = Migration
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => !string.IsNullOrWhiteSpace(statement))
            .ToArray();

        Assert.Equal(3, statements.Length);
    }
}

/// <summary>迁移 0005 建立商店限购计数。</summary>
public sealed class ShopMigrationContractTests
{
    private static readonly string Migration = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Migrations", "0005_p1_shop.sql"));

    private static readonly string ExecutableSql = string.Join(
        '\n',
        Migration.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    [Fact]
    public void MigrationIsAdditiveAndRepeatable()
    {
        Assert.DoesNotContain("DROP ", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "CREATE TABLE IF NOT EXISTS account_shop_purchases", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PurchaseCounterIsPerAccountAndProduct()
    {
        Assert.Contains("PRIMARY KEY (account_id, product_id)", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("purchased_total BIGINT UNSIGNED NOT NULL", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationRecordsOnlyItsOwnVersion()
    {
        Assert.Contains("'0005'", ExecutableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'0004'", ExecutableSql, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationSplitsIntoTwoExecutableStatements()
    {
        var statements = Migration
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => !string.IsNullOrWhiteSpace(statement))
            .ToArray();

        Assert.Equal(2, statements.Length);
    }
}

/// <summary>迁移 0006 建立每账号唯一的武器实例与强化进度。</summary>
public sealed class WeaponMigrationContractTests
{
    private static readonly string Migration = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Migrations", "0006_p1_weapons.sql"));

    private static readonly string ExecutableSql = string.Join(
        '\n',
        Migration.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    [Fact]
    public void MigrationIsAdditiveAndRepeatable()
    {
        Assert.DoesNotContain("DROP ", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS account_weapons", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EachWeaponTypeExistsExactlyOncePerAccount()
    {
        // 主键 (账号, 武器) 且没有数量列：武器不是可堆叠的仓库物品。
        Assert.Contains("PRIMARY KEY (account_id, weapon_id)", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quantity", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("level INT UNSIGNED NOT NULL", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("proficiency BIGINT UNSIGNED NOT NULL", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kill_count BIGINT UNSIGNED NOT NULL", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationHardcodesNoWeaponId()
    {
        // 武器 ID 来自生成配置；写进迁移会让新增一把武器变成一次 schema 变更。
        Assert.DoesNotContain("weapon_longsword", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("weapon_tachi", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationRecordsOnlyItsOwnVersion()
    {
        Assert.Contains("'0006'", ExecutableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'0005'", ExecutableSql, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationSplitsIntoTwoExecutableStatements()
    {
        var statements = Migration
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => !string.IsNullOrWhiteSpace(statement))
            .ToArray();

        Assert.Equal(2, statements.Length);
    }
}

/// <summary>迁移 0007 建立抽奖订单、结果与保底计数。</summary>
public sealed class GachaMigrationContractTests
{
    private static readonly string Migration = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Migrations", "0007_p1_gacha.sql"));

    private static readonly string ExecutableSql = string.Join(
        '\n',
        Migration.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    [Fact]
    public void MigrationIsAdditiveAndRepeatable()
    {
        Assert.DoesNotContain("DROP ", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS account_gacha", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS gacha_orders", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "CREATE TABLE IF NOT EXISTS gacha_order_results", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrderIdIsThePrimaryKeySoAPullCannotHappenTwice()
    {
        Assert.Contains("PRIMARY KEY (account_id, order_id)", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "PRIMARY KEY (account_id, order_id, sequence)", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnshownOrdersAreIndexedSoRecoveryIsCheap()
    {
        // 断线恢复要按 shown_utc IS NULL 查询，没有索引会随着历史订单增长而变慢。
        Assert.Contains("shown_utc DATETIME(6) NULL", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "KEY ix_gacha_orders_unshown (account_id, shown_utc)", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationRecordsOnlyItsOwnVersion()
    {
        Assert.Contains("'0007'", ExecutableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'0006'", ExecutableSql, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationSplitsIntoFourExecutableStatements()
    {
        var statements = Migration
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => !string.IsNullOrWhiteSpace(statement))
            .ToArray();

        Assert.Equal(4, statements.Length);
    }
}

/// <summary>迁移 0008 建立签到、一次性奖励领取、成就与红点。</summary>
public sealed class ProgressionRewardsMigrationContractTests
{
    private static readonly string Migration = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Migrations", "0008_p1_progression_rewards.sql"));

    private static readonly string ExecutableSql = string.Join(
        '\n',
        Migration.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    [Fact]
    public void MigrationIsAdditiveAndRepeatable()
    {
        Assert.DoesNotContain("DROP ", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        foreach (var table in new[]
                 {
                     "account_signin", "account_signin_claims", "account_reward_claims",
                     "account_achievements", "account_achievement_state", "account_reddot"
                 })
        {
            Assert.Contains(
                "CREATE TABLE IF NOT EXISTS " + table, ExecutableSql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ClaimingTwiceIsBlockedByPrimaryKeys()
    {
        // 重复领取由主键拒绝，而不是靠应用层记得检查。
        Assert.Contains(
            "PRIMARY KEY (account_id, cycle_start_day, day_index)",
            ExecutableSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "PRIMARY KEY (account_id, reward_kind, reward_key)",
            ExecutableSql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AchievementExperienceLivesApartFromAccountExperience()
    {
        // 成就经验有自己的表和列；账号经验只在 account_profile 里，两者永不互相写入。
        Assert.Contains("achievement_xp BIGINT UNSIGNED NOT NULL", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account_xp", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedDotsStoreVersionAndSeenVersionRatherThanABoolean()
    {
        Assert.Contains("version BIGINT UNSIGNED NOT NULL", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("seen_version BIGINT UNSIGNED NOT NULL", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("has_red_dot", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationRecordsOnlyItsOwnVersion()
    {
        Assert.Contains("'0008'", ExecutableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'0007'", ExecutableSql, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationSplitsIntoSevenExecutableStatements()
    {
        var statements = Migration
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => !string.IsNullOrWhiteSpace(statement))
            .ToArray();

        Assert.Equal(7, statements.Length);
    }
}

/// <summary>
/// P1.8 社交迁移。
///
/// 关注点是"关系不会写成单向"：好友表用 (账号, 好友账号) 主键并成对写入，
/// 会话用 (低账号, 高账号) 唯一键，已读位置按账号分行。
/// </summary>
public sealed class SocialMigrationContractTests
{
    private static readonly string Migration = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Migrations", "0009_p1_social.sql"));

    private static readonly string ExecutableSql = string.Join(
        '\n',
        Migration.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    [Fact]
    public void MigrationIsAdditiveAndRepeatable()
    {
        Assert.DoesNotContain("DROP ", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON DUPLICATE KEY UPDATE", ExecutableSql, StringComparison.OrdinalIgnoreCase);
        foreach (var table in new[]
                 {
                     "account_friends", "account_friend_requests", "account_blocks",
                     "chat_conversations", "chat_messages", "chat_read_positions"
                 })
        {
            Assert.Contains(
                "CREATE TABLE IF NOT EXISTS " + table, ExecutableSql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SendingTheSameRequestTwiceIsBlockedByThePrimaryKey()
    {
        Assert.Contains(
            "PRIMARY KEY (requester_account_id, target_account_id)",
            ExecutableSql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AConversationExistsOnlyOncePerAccountPair()
    {
        // 低-高规范化加唯一键，因此谁先打开会话都只会有一行。
        Assert.Contains(
            "UNIQUE KEY uq_chat_conversations_pair (low_account_id, high_account_id)",
            ExecutableSql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EachSideTracksItsOwnReadPosition()
    {
        Assert.Contains(
            "PRIMARY KEY (conversation_id, account_id)",
            ExecutableSql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MessageBodyLengthMatchesTheServiceLimit()
    {
        // 列宽与 SocialService.MaxMessageLength 一致：服务端拒绝超长消息而不是让数据库截断。
        Assert.Contains("body VARCHAR(512) NOT NULL", ExecutableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MigrationRecordsOnlyItsOwnVersion()
    {
        Assert.Contains("'0009'", ExecutableSql, StringComparison.Ordinal);
        Assert.DoesNotContain("'0008'", ExecutableSql, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationSplitsIntoSevenExecutableStatements()
    {
        var statements = Migration
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => !string.IsNullOrWhiteSpace(statement))
            .ToArray();

        Assert.Equal(7, statements.Length);
    }
}
