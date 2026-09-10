using System.Text;
using Naraka.Config;
using Naraka.ConfigCompiler;
using Naraka.ConfigCompiler.Csv;

namespace Naraka.ConfigCompiler.Tests;

/// <summary>
/// 在一份可写的临时源表副本上运行真实编译器。测试通过篡改单张 CSV 来证明校验规则确实生效，
/// 而不是只断言"当前仓库恰好没有错误"。
/// </summary>
public sealed class ConfigCompilerTests : IDisposable
{
    private readonly string _root;
    private readonly ConfigBuildPaths _paths;

    public ConfigCompilerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "naraka-config-tests", Guid.NewGuid().ToString("N"));
        var repositoryRoot = RepositoryLocator.Find();

        CopyDirectory(
            Path.Combine(repositoryRoot, "Config", "Source"),
            Path.Combine(_root, "Config", "Source"));
        Directory.CreateDirectory(Path.Combine(_root, "Shared", "Config"));
        foreach (var name in ConfigBuildPaths.MirroredFileNames)
        {
            File.Copy(
                Path.Combine(repositoryRoot, "Shared", "Config", name),
                Path.Combine(_root, "Shared", "Config", name));
        }

        _paths = new ConfigBuildPaths(_root);
    }

    [Fact]
    public void CleanSourceCompilesWithoutDiagnostics()
    {
        var result = ConfigBuild.Run(_paths, checkOnly: false);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Differences);
        Assert.True(result.Succeeded);
        Assert.StartsWith("p1-config-", result.Emitted!.ConfigVersion, StringComparison.Ordinal);
        Assert.Equal(ConfigEmitter.SchemaVersion, result.Emitted.SchemaVersion);
    }

    [Fact]
    public void RepeatedCompilationProducesIdenticalBytes()
    {
        var first = ConfigBuild.Run(_paths, checkOnly: false).Emitted!;
        var second = ConfigBuild.Run(_paths, checkOnly: false).Emitted!;

        Assert.Equal(first.CatalogBytes, second.CatalogBytes);
        Assert.Equal(first.ManifestBytes, second.ManifestBytes);
        Assert.Equal(first.ConfigVersion, second.ConfigVersion);
    }

    [Fact]
    public void ReorderedSourceRowsProduceIdenticalBytes()
    {
        var baseline = ConfigBuild.Run(_paths, checkOnly: false).Emitted!;

        // 稳定 ID 排序意味着源表行序不影响生成物；否则 Excel 里挪一行就会产生假 diff。
        var path = SourcePath("items.csv");
        var lines = ReadLines(path);
        var reordered = new List<string> { lines[0] };
        reordered.AddRange(lines.Skip(1).Reverse());
        WriteLines(path, reordered);

        var reorderedResult = ConfigBuild.Run(_paths, checkOnly: false).Emitted!;

        Assert.Equal(baseline.CatalogBytes, reorderedResult.CatalogBytes);
    }

    [Fact]
    public void SecondCompilationLeavesNoDifferencesForCheck()
    {
        ConfigBuild.Run(_paths, checkOnly: false);

        var check = ConfigBuild.Run(_paths, checkOnly: true);

        Assert.Empty(check.Differences);
        Assert.True(check.Succeeded);
    }

    [Fact]
    public void CheckReportsStaleOutput()
    {
        ConfigBuild.Run(_paths, checkOnly: false);
        AppendRow("pets.csv", "pet_extra,额外宠物,3,pet_lingyu,0,Default,仅用于验证生成物过期检测。");

        var check = ConfigBuild.Run(_paths, checkOnly: true);

        Assert.False(check.Succeeded);
        Assert.NotEmpty(check.Differences);
    }

    [Fact]
    public void DuplicateItemIdIsRejected()
    {
        AppendRow("items.csv", "mat_ore_basic,重复矿石,Material,White,999,9990,material_ore_basic,Copper,5,重复 ID。");

        AssertDiagnosticContains("items.csv", "ItemId 重复");
    }

    [Fact]
    public void MissingForeignKeyIsRejected()
    {
        AppendRow(
            "shop_products.csv",
            "shop_missing_item,item_does_not_exist,Material,Copper,10,0,9990,1");

        AssertDiagnosticContains("shop_products.csv", "不存在");
    }

    [Fact]
    public void NegativePriceIsRejected()
    {
        ReplaceCell("shop_products.csv", "shop_blood_pack", column: 4, value: "-10");

        AssertDiagnosticContains("shop_products.csv", "UnitPrice 不能为负");
    }

    [Fact]
    public void NonPositiveStackLimitIsRejected()
    {
        ReplaceCell("items.csv", "mat_ore_basic", column: 4, value: "0");

        AssertDiagnosticContains("items.csv", "StackLimit 必须大于 0");
    }

    [Fact]
    public void NonPositiveGachaWeightIsRejected()
    {
        ReplaceCell("gacha_entries.csv", "gacha_std_dragon_core", column: 5, value: "0", keyColumn: 1);

        AssertDiagnosticContains("gacha_entries.csv", "Weight 必须大于 0");
    }

    [Fact]
    public void UnknownQualityIsRejected()
    {
        ReplaceCell("gacha_entries.csv", "gacha_std_dragon_core", column: 4, value: "Rainbow", keyColumn: 1);

        AssertDiagnosticContains("gacha_entries.csv", "品质 'Rainbow' 非法");
    }

    [Fact]
    public void PityQualityWithoutMatchingRewardIsRejected()
    {
        // 把唯一的红色奖励降成金色后，20 抽红色保底就没有任何奖励可以兑现。
        ReplaceCell("gacha_entries.csv", "gacha_std_dragon_core", column: 4, value: "Gold", keyColumn: 1);

        AssertDiagnosticContains("gacha_entries.csv", "硬保底需要 'Red' 及以上品质的有效奖励");
    }

    [Fact]
    public void WeaponLevelGapIsRejected()
    {
        RemoveRow("weapon_levels.csv", line => line.StartsWith("weapon_longsword,7,", StringComparison.Ordinal));

        AssertDiagnosticContains("weapon_levels.csv", "必须是 1..20 连续且不重复");
    }

    [Fact]
    public void DuplicateWeaponLevelIsRejected()
    {
        AppendRow("weapon_levels.csv", "weapon_longsword,7,999,1.00");

        AssertDiagnosticContains("weapon_levels.csv", "等级存在重复");
    }

    [Fact]
    public void ForgeRecipeWithUnknownMaterialIsRejected()
    {
        ReplaceCell("forge_recipes.csv", "forge_ls_01_02", column: 6, value: "mat_not_real");

        AssertDiagnosticContains("forge_recipes.csv", "引用了不存在的 ItemId");
    }

    [Fact]
    public void MissingSignInDayIsRejected()
    {
        RemoveRow("signin_rewards.csv", line => line.StartsWith("4,", StringComparison.Ordinal));

        AssertDiagnosticContains("signin_rewards.csv", "必须覆盖第 1 至第 7 天");
    }

    [Fact]
    public void AccountLevelXpMustIncrease()
    {
        ReplaceCell("account_level_rewards.csv", "5", column: 1, value: "0");

        AssertDiagnosticContains("account_level_rewards.csv", "必须大于上一等级");
    }

    [Fact]
    public void UnregisteredSourceFileIsRejected()
    {
        File.WriteAllText(SourcePath("mystery_table.csv"), "Id\nx\n", new UTF8Encoding(false));

        AssertDiagnosticContains("mystery_table.csv", "未在 CatalogLoader.SourceFiles 中登记");
    }

    [Fact]
    public void EveryQualityUsedByGachaIsOneOfTheFiveTiers()
    {
        var result = ConfigBuild.Run(_paths, checkOnly: false);
        var catalog = ReadCatalog(result.Emitted!.CatalogBytes);

        Assert.All(catalog.GachaEntries, entry => Assert.InRange(ConfigQuality.IndexOf(entry.Quality), 0, 4));
        Assert.Equal(5, ConfigQuality.Ordered.Length);
    }

    [Fact]
    public void EveryCurrencyGrantsOneThousandOnAccountCreation()
    {
        var result = ConfigBuild.Run(_paths, checkOnly: false);
        var catalog = ReadCatalog(result.Emitted!.CatalogBytes);

        Assert.Equal(3, catalog.Currencies.Length);
        Assert.All(catalog.Currencies, currency => Assert.Equal(1000L, currency.StarterGrant));
    }

    private static NarakaConfigCatalog ReadCatalog(byte[] bytes) =>
        System.Text.Json.JsonSerializer.Deserialize<NarakaConfigCatalog>(
            bytes,
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true })
        ?? throw new InvalidOperationException("生成的目录无法反序列化。");

    private void AssertDiagnosticContains(string fileName, string fragment)
    {
        var result = ConfigBuild.Run(_paths, checkOnly: false);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.FileName == fileName &&
                          diagnostic.Message.Contains(fragment, StringComparison.Ordinal));
    }

    private string SourcePath(string fileName) => Path.Combine(_paths.SourceDirectory, fileName);

    private static List<string> ReadLines(string path) =>
        new UTF8Encoding(false)
            .GetString(StripBom(File.ReadAllBytes(path)))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n')
            .Split('\n')
            .ToList();

    private static void WriteLines(string path, IEnumerable<string> lines)
    {
        var payload = new UTF8Encoding(false).GetBytes(string.Join('\n', lines) + "\n");
        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(payload).ToArray());
    }

    private static byte[] StripBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes[3..] : bytes;

    private void AppendRow(string fileName, string row)
    {
        var path = SourcePath(fileName);
        var lines = ReadLines(path);
        lines.Add(row);
        WriteLines(path, lines);
    }

    private void RemoveRow(string fileName, Func<string, bool> predicate)
    {
        var path = SourcePath(fileName);
        var lines = ReadLines(path);
        var removed = lines.RemoveAll(line => predicate(line));
        Assert.True(removed > 0, $"{fileName} 中没有匹配的行可供删除。");
        WriteLines(path, lines);
    }

    private void ReplaceCell(string fileName, string key, int column, string value, int keyColumn = 0)
    {
        var path = SourcePath(fileName);
        var lines = ReadLines(path);
        var replaced = false;
        for (var i = 1; i < lines.Count; i++)
        {
            var cells = lines[i].Split(',');
            if (cells.Length <= Math.Max(column, keyColumn) ||
                !string.Equals(cells[keyColumn], key, StringComparison.Ordinal))
            {
                continue;
            }

            cells[column] = value;
            lines[i] = string.Join(',', cells);
            replaced = true;
            break;
        }

        Assert.True(replaced, $"{fileName} 中没有找到键 '{key}'。");
        WriteLines(path, lines);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

/// <summary>从测试输出目录向上找到含 global.json 的仓库根目录。</summary>
internal static class RepositoryLocator
{
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("未能定位仓库根目录。");
    }
}
