namespace Naraka.Features.Bootstrap.Model
{
    public readonly struct ConfigVersionManifest
    {
        public ConfigVersionManifest(
            string configVersion,
            string minimumClientVersion,
            string maximumClientVersion,
            string protocolVersion)
        {
            ConfigVersion = configVersion ?? string.Empty;
            MinimumClientVersion = minimumClientVersion ?? string.Empty;
            MaximumClientVersion = maximumClientVersion ?? string.Empty;
            ProtocolVersion = protocolVersion ?? string.Empty;
        }

        public string ConfigVersion { get; }

        public string MinimumClientVersion { get; }

        public string MaximumClientVersion { get; }

        public string ProtocolVersion { get; }
    }
}
