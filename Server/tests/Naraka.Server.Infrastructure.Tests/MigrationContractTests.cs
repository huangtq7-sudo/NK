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
