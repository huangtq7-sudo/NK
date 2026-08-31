using Naraka.Server.Application.Bootstrap;

namespace Naraka.Server.Application.Tests;

public sealed class ConfigVersionManifestTests
{
    [Fact]
    public void CreateAcceptsOrderedClientVersionRange()
    {
        var manifest = ConfigVersionManifest.Create(
            "p0-config-1",
            "0.1",
            "0.2",
            "LegacyNetworkV1");

        Assert.Equal("p0-config-1", manifest.ConfigVersion);
        Assert.Equal("0.1", manifest.MinimumClientVersion);
        Assert.Equal("0.2", manifest.MaximumClientVersion);
        Assert.Equal("LegacyNetworkV1", manifest.ProtocolVersion);
    }

    [Fact]
    public void CreateRejectsReversedClientVersionRange()
    {
        Assert.Throws<InvalidOperationException>(() => ConfigVersionManifest.Create(
            "p0-config-1",
            "0.2",
            "0.1",
            "LegacyNetworkV1"));
    }
}
