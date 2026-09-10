using Naraka.Config;

namespace Naraka.Server.Application.Bootstrap;

/// <summary>
/// Bootstrap 预检响应。
///
/// 前四个字段延续 P0 建立的序列化形状；ConfigVersion 会随不兼容应用契约同步提升，
/// 使不理解新业务消息的客户端或 Host 在预检阶段停止，而不是进入不安全的半兼容状态。
///
/// <see cref="ServerCapabilities"/> 是向后兼容的<b>可选</b>新增字段：只部署到 P1.1-A 的旧 Host
/// 不会返回它；仅当版本门禁匹配时，客户端才按
/// <see cref="NarakaServerCapabilities.CompatibilityMode"/> 运行。
/// </summary>
public sealed record ConfigVersionManifest(
    string ConfigVersion,
    string MinimumClientVersion,
    string MaximumClientVersion,
    string ProtocolVersion,
    IReadOnlyList<string> ServerCapabilities)
{
    public static ConfigVersionManifest Create(
        string? configVersion,
        string? minimumClientVersion,
        string? maximumClientVersion,
        string? protocolVersion,
        IReadOnlyList<string>? serverCapabilities = null)
    {
        var manifest = new ConfigVersionManifest(
            Normalize(configVersion, nameof(configVersion)),
            Normalize(minimumClientVersion, nameof(minimumClientVersion)),
            Normalize(maximumClientVersion, nameof(maximumClientVersion)),
            Normalize(protocolVersion, nameof(protocolVersion)),
            NormalizeCapabilities(serverCapabilities));

        if (!Version.TryParse(manifest.MinimumClientVersion, out var minimum) ||
            !Version.TryParse(manifest.MaximumClientVersion, out var maximum) ||
            minimum > maximum)
        {
            throw new InvalidOperationException(
                "Bootstrap client-version range is invalid.");
        }

        return manifest;
    }

    /// <summary>
    /// 能力值必须来自 <see cref="NarakaServerCapabilities"/> 的已登记常量。
    /// 拼错的能力名会让客户端永久停在"服务器功能尚未升级"，而且不会有任何报错，
    /// 因此在启动时就拒绝，而不是留到运行时。
    /// </summary>
    private static IReadOnlyList<string> NormalizeCapabilities(IReadOnlyList<string>? capabilities)
    {
        if (capabilities is null || capabilities.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(capabilities.Count);
        foreach (var capability in capabilities)
        {
            var value = capability?.Trim() ?? string.Empty;
            if (!NarakaServerCapabilities.Contains(NarakaServerCapabilities.Full, value))
            {
                throw new InvalidOperationException(
                    $"Bootstrap capability '{value}' is not a registered NarakaServerCapabilities value.");
            }

            if (!normalized.Contains(value, StringComparer.Ordinal))
            {
                normalized.Add(value);
            }
        }

        return normalized;
    }

    private static string Normalize(string? value, string name)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 64)
        {
            throw new InvalidOperationException(
                $"Bootstrap value '{name}' must contain 1-64 characters.");
        }

        return normalized;
    }
}
