namespace Naraka.Server.Application.Bootstrap;

public sealed record ConfigVersionManifest(
    string ConfigVersion,
    string MinimumClientVersion,
    string MaximumClientVersion,
    string ProtocolVersion)
{
    public static ConfigVersionManifest Create(
        string? configVersion,
        string? minimumClientVersion,
        string? maximumClientVersion,
        string? protocolVersion)
    {
        var manifest = new ConfigVersionManifest(
            Normalize(configVersion, nameof(configVersion)),
            Normalize(minimumClientVersion, nameof(minimumClientVersion)),
            Normalize(maximumClientVersion, nameof(maximumClientVersion)),
            Normalize(protocolVersion, nameof(protocolVersion)));

        if (!Version.TryParse(manifest.MinimumClientVersion, out var minimum) ||
            !Version.TryParse(manifest.MaximumClientVersion, out var maximum) ||
            minimum > maximum)
        {
            throw new InvalidOperationException(
                "Bootstrap client-version range is invalid.");
        }

        return manifest;
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
