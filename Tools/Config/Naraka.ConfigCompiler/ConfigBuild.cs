using System.Text;
using Naraka.Config;
using Naraka.ConfigCompiler.Csv;

namespace Naraka.ConfigCompiler;

/// <summary>一次完整编译的输入路径。全部由仓库根目录推导，避免调用方各自拼路径。</summary>
public sealed class ConfigBuildPaths
{
    public ConfigBuildPaths(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        SourceDirectory = Path.Combine(RepositoryRoot, "Config", "Source");
        SharedOutputDirectory = Path.Combine(RepositoryRoot, "Shared", "Generated", "Config");
        UnityOutputDirectory = Path.Combine(RepositoryRoot, "NK", "Assets", "StreamingAssets", "Config");
        MirroredSourceFiles = MirroredFileNames
            .Select(name => (
                Source: Path.Combine(RepositoryRoot, "Shared", "Config", name),
                Mirror: Path.Combine(RepositoryRoot, "NK", "Assets", "Game", "Generated", "Config", name)))
            .ToArray();
    }

    /// <summary>
    /// 双端共享、由编译器镜像到 Unity 的源文件。放在这里而不是各自复制，
    /// 是因为这些文件同时定义了配置模型与线级能力字符串，任何漂移都会造成两端不一致。
    /// </summary>
    public static readonly string[] MirroredFileNames =
    {
        "NarakaConfigModels.cs",
        "NarakaServerCapabilities.cs"
    };

    public IReadOnlyList<(string Source, string Mirror)> MirroredSourceFiles { get; }

    public string RepositoryRoot { get; }

    public string SourceDirectory { get; }

    /// <summary>规范生成物。服务端把这里的文件作为 Content 复制到输出目录。</summary>
    public string SharedOutputDirectory { get; }

    /// <summary>Unity 运行时副本。StreamingAssets 在编辑器与 Windows 独立播放器中都可直接按文件读取。</summary>
    public string UnityOutputDirectory { get; }

    public IEnumerable<string> OutputDirectories
    {
        get
        {
            yield return SharedOutputDirectory;
            yield return UnityOutputDirectory;
        }
    }
}

public sealed record ConfigBuildResult(
    bool Succeeded,
    EmittedConfig? Emitted,
    IReadOnlyList<ConfigDiagnostic> Diagnostics,
    IReadOnlyList<string> Differences);

/// <summary>
/// 读取 → 校验 → 生成 → 写盘/比对的完整流程。写盘与 `--check` 走同一条路径，
/// 因此"检查通过"与"重新生成不会产生 diff"是同一件事。
/// </summary>
public static class ConfigBuild
{
    public static ConfigBuildResult Run(ConfigBuildPaths paths, bool checkOnly)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var diagnostics = new DiagnosticBag();
        RequireKnownSourceFiles(paths.SourceDirectory, diagnostics);

        var catalog = CatalogLoader.Load(paths.SourceDirectory, diagnostics);
        if (!diagnostics.HasErrors)
        {
            CatalogValidator.Validate(catalog, diagnostics);
        }

        if (diagnostics.HasErrors)
        {
            return new ConfigBuildResult(false, null, diagnostics.Sorted(), Array.Empty<string>());
        }

        var emitted = ConfigEmitter.Emit(catalog, ReadSourceHashes(paths.SourceDirectory));

        var differences = new List<string>();
        foreach (var directory in paths.OutputDirectories)
        {
            Apply(Path.Combine(directory, ConfigEmitter.CatalogFileName), emitted.CatalogBytes, checkOnly, differences);
            Apply(Path.Combine(directory, ConfigEmitter.ManifestFileName), emitted.ManifestBytes, checkOnly, differences);
        }

        foreach (var (source, mirror) in paths.MirroredSourceFiles)
        {
            Apply(mirror, File.ReadAllBytes(source), checkOnly, differences);
        }

        return new ConfigBuildResult(
            differences.Count == 0,
            emitted,
            Array.Empty<ConfigDiagnostic>(),
            differences);
    }

    /// <summary>
    /// 源目录中出现未登记的 CSV 时必须失败：一张没人读取的表会让维护者以为改动已经生效。
    /// </summary>
    private static void RequireKnownSourceFiles(string sourceDirectory, DiagnosticBag diagnostics)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            diagnostics.Add("Config/Source", 0, $"源表目录不存在：{sourceDirectory}");
            return;
        }

        var known = new HashSet<string>(CatalogLoader.SourceFiles, StringComparer.Ordinal);
        var actual = Directory.GetFiles(sourceDirectory, "*.csv")
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        foreach (var extra in actual.Where(name => !known.Contains(name)))
        {
            diagnostics.Add(extra, 0, "源表未在 CatalogLoader.SourceFiles 中登记，不会被编译。");
        }

        foreach (var missing in CatalogLoader.SourceFiles.Where(name => !actual.Contains(name)))
        {
            diagnostics.Add(missing, 0, "登记过的源表文件缺失。");
        }
    }

    private static IReadOnlyList<SourceFileHash> ReadSourceHashes(string sourceDirectory) =>
        CatalogLoader.SourceFiles
            .Select(name => new SourceFileHash(
                name,
                ConfigEmitter.Sha256Hex(NormalizeSource(File.ReadAllBytes(Path.Combine(sourceDirectory, name))))))
            .ToArray();

    /// <summary>
    /// 源表哈希在计算前剥离 BOM 并统一换行。Excel 与 Git 的换行处理各不相同，
    /// 若不归一，同一份内容会因为检出方式不同而产生不同哈希。
    /// </summary>
    private static byte[] NormalizeSource(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
        }

        var text = new UTF8Encoding(false).GetString(bytes)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .TrimEnd('\n');

        return new UTF8Encoding(false).GetBytes(text + "\n");
    }

    private static void Apply(string path, byte[] content, bool checkOnly, ICollection<string> differences)
    {
        var existing = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if (existing is not null && existing.AsSpan().SequenceEqual(content))
        {
            return;
        }

        if (checkOnly)
        {
            differences.Add(existing is null
                ? $"{path}: 生成物缺失。"
                : $"{path}: 生成物与源表不一致。");
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, content);
    }
}
