using Naraka.Server.Infrastructure.Health;
using Naraka.Server.Infrastructure.Persistence;

namespace Naraka.Server.Infrastructure.Tests;

public sealed class MySqlConfigurationTests
{
    [Fact]
    public void PolicyRejectsMissingConfigurationWithoutASecret()
    {
        Assert.False(MySqlConnectionStringPolicy.TryNormalize(null, out var normalized, out var status));
        Assert.Empty(normalized);
        Assert.Contains(EnvironmentMySqlConnectionStringSource.VariableName, status, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyRejectsIncompleteConfigurationWithoutEchoingInput()
    {
        const string raw = "Server=127.0.0.1;Password=fixture-only";

        Assert.False(MySqlConnectionStringPolicy.TryNormalize(raw, out _, out var status));
        Assert.DoesNotContain("fixture-only", status, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyNormalizesACompleteLocalConfiguration()
    {
        const string raw = "Server=127.0.0.1;Port=3306;Database=naraka_test;User ID=naraka_test;Password=fixture-only;Connection Timeout=30";

        Assert.True(MySqlConnectionStringPolicy.TryNormalize(raw, out var normalized, out var status));
        Assert.Equal("Configured", status);
        Assert.Contains("Connection Timeout=5", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthProbeReportsMissingConfigurationWithoutOpeningANetworkConnection()
    {
        var probe = new MySqlDatabaseHealthProbe(new StubSource(null));

        var snapshot = await probe.CheckAsync(CancellationToken.None);

        Assert.False(snapshot.IsReady);
        Assert.Contains(EnvironmentMySqlConnectionStringSource.VariableName, snapshot.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlSugarFactoryDoesNotOpenAConnectionDuringConstruction()
    {
        const string raw = "Server=127.0.0.1;Port=3306;Database=naraka_test;User ID=naraka_test;Password=fixture-only";
        var factory = new SqlSugarClientFactory(new StubSource(raw));

        using var client = factory.Create();

        Assert.NotNull(client);
    }

    private sealed class StubSource(string? value) : IMySqlConnectionStringSource
    {
        public string? GetConnectionString() => value;
    }
}
