using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Naraka.Config;

namespace Naraka.ConfigCompiler;

/// <summary>生成物的内存表示。写盘与比对都基于同一份字节，因此 `--check` 与实际产出不会漂移。</summary>
public sealed record EmittedConfig(
    string ConfigVersion,
    string SchemaVersion,
    byte[] CatalogBytes,
    byte[] ManifestBytes);

/// <summary>
/// 把校验通过的目录序列化成规范 JSON。
/// 输出必须是确定性的：相同源表在任何机器上都要得到逐字节一致的结果，
/// 否则 CI 的"生成物与源表一致"门禁只会变成噪音。
/// </summary>
public static class ConfigEmitter
{
    /// <summary>结构版本。字段增删或语义变更时必须手工提升。</summary>
    public const string SchemaVersion = "1.0.0";

    public const string CatalogFileName = "naraka-config.json";
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        // 中日韩字符保持原样，HTML 敏感字符仍然转义：生成物既可读又可安全嵌入。
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static EmittedConfig Emit(NarakaConfigCatalog catalog, IReadOnlyList<SourceFileHash> sourceHashes)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sourceHashes);

        catalog.SchemaVersion = SchemaVersion;

        // ConfigVersion 由"不含版本号的正文"推导，避免版本号影响自身哈希。
        catalog.ConfigVersion = string.Empty;
        var contentHash = Sha256Hex(Serialize(catalog));
        catalog.ConfigVersion = "p1-config-" + contentHash[..12];

        var catalogBytes = Serialize(catalog);
        var manifest = new ConfigManifest
        {
            SchemaVersion = SchemaVersion,
            ConfigVersion = catalog.ConfigVersion,
            ContentSha256 = contentHash,
            CatalogSha256 = Sha256Hex(catalogBytes),
            Generator = "Naraka.ConfigCompiler",
            Sources = sourceHashes
                .OrderBy(source => source.FileName, StringComparer.Ordinal)
                .Select(source => new ConfigManifestSource
                {
                    FileName = source.FileName,
                    Sha256 = source.Sha256
                })
                .ToArray()
        };

        return new EmittedConfig(
            catalog.ConfigVersion,
            SchemaVersion,
            catalogBytes,
            Serialize(manifest));
    }

    /// <summary>
    /// UTF-8 无 BOM、LF 换行、无尾随空白。换行统一是关键：`WriteIndented` 在不同运行时
    /// 可能采用宿主换行符，那会让 Windows 与 Linux 生成不同字节。
    /// </summary>
    private static byte[] Serialize<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        return new UTF8Encoding(false).GetBytes(json + "\n");
    }

    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string Sha256Hex(string text) =>
        Sha256Hex(new UTF8Encoding(false).GetBytes(text));
}

public sealed record SourceFileHash(string FileName, string Sha256);

public sealed class ConfigManifest
{
    public string SchemaVersion { get; set; } = string.Empty;

    public string ConfigVersion { get; set; } = string.Empty;

    /// <summary>不含 ConfigVersion 字段的正文哈希。ConfigVersion 由它推导。</summary>
    public string ContentSha256 { get; set; } = string.Empty;

    /// <summary>最终写盘的 naraka-config.json 的哈希，用于客户端/服务端一致性比对。</summary>
    public string CatalogSha256 { get; set; } = string.Empty;

    public string Generator { get; set; } = string.Empty;

    public ConfigManifestSource[] Sources { get; set; } = Array.Empty<ConfigManifestSource>();
}

public sealed class ConfigManifestSource
{
    public string FileName { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;
}
