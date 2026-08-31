using System;
using Naraka.Core.Domain;

namespace Naraka.Features.Bootstrap.Model
{
    public enum ConfigCompatibilityStatus
    {
        Compatible,
        InvalidManifest,
        ConfigVersionMismatch,
        ClientVersionTooOld,
        ClientVersionTooNew,
        ProtocolVersionMismatch
    }

    public readonly struct ConfigCompatibilityResult
    {
        public ConfigCompatibilityResult(ConfigCompatibilityStatus status)
        {
            Status = status;
        }

        public ConfigCompatibilityStatus Status { get; }

        public bool IsCompatible => Status == ConfigCompatibilityStatus.Compatible;
    }

    public sealed class ConfigVersionModel : IModel
    {
        public ConfigVersionModel(string clientVersion, string configVersion, string protocolVersion)
        {
            ClientVersion = RequireValue(clientVersion, nameof(clientVersion));
            ConfigVersion = RequireValue(configVersion, nameof(configVersion));
            ProtocolVersion = RequireValue(protocolVersion, nameof(protocolVersion));
        }

        public string ClientVersion { get; }

        public string ConfigVersion { get; }

        public string ProtocolVersion { get; }

        public ConfigCompatibilityResult Evaluate(ConfigVersionManifest manifest)
        {
            if (!TryParseVersion(ClientVersion, out var clientVersion) ||
                !TryParseVersion(manifest.MinimumClientVersion, out var minimumVersion) ||
                !TryParseVersion(manifest.MaximumClientVersion, out var maximumVersion) ||
                minimumVersion > maximumVersion ||
                string.IsNullOrWhiteSpace(manifest.ConfigVersion) ||
                string.IsNullOrWhiteSpace(manifest.ProtocolVersion))
            {
                return new ConfigCompatibilityResult(ConfigCompatibilityStatus.InvalidManifest);
            }

            if (!string.Equals(ConfigVersion, manifest.ConfigVersion, StringComparison.Ordinal))
            {
                return new ConfigCompatibilityResult(ConfigCompatibilityStatus.ConfigVersionMismatch);
            }

            if (clientVersion < minimumVersion)
            {
                return new ConfigCompatibilityResult(ConfigCompatibilityStatus.ClientVersionTooOld);
            }

            if (clientVersion > maximumVersion)
            {
                return new ConfigCompatibilityResult(ConfigCompatibilityStatus.ClientVersionTooNew);
            }

            return !string.Equals(ProtocolVersion, manifest.ProtocolVersion, StringComparison.Ordinal)
                ? new ConfigCompatibilityResult(ConfigCompatibilityStatus.ProtocolVersionMismatch)
                : new ConfigCompatibilityResult(ConfigCompatibilityStatus.Compatible);
        }

        private static string RequireValue(string value, string parameterName)
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0 || normalized.Length > 64)
            {
                throw new ArgumentException("Version values must contain 1-64 characters.", parameterName);
            }

            return normalized;
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            var normalized = value?.Trim() ?? string.Empty;
            version = null;
            return normalized.Length <= 32 && Version.TryParse(normalized, out version);
        }
    }
}
