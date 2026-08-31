using Naraka.Server.Infrastructure.Security;

namespace Naraka.Server.Infrastructure.Tests;

public sealed class Argon2idPasswordHasherTests
{
    [Fact]
    public async Task HashUsesIndependentSaltAndVerifiesInFixedConfiguration()
    {
        var hasher = new Argon2idPasswordHasher();

        var first = await hasher.HashAsync("correct-horse-battery", CancellationToken.None);
        var second = await hasher.HashAsync("correct-horse-battery", CancellationToken.None);

        Assert.Equal(32, first.Hash.Length);
        Assert.Equal(32, first.Salt.Length);
        Assert.Equal(Argon2idPasswordHasher.CurrentParameters, first.Parameters);
        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.True(await hasher.VerifyAsync(
            "correct-horse-battery",
            first.Hash,
            first.Salt,
            first.Parameters,
            CancellationToken.None));
        Assert.False(await hasher.VerifyAsync(
            "wrong-password",
            first.Hash,
            first.Salt,
            first.Parameters,
            CancellationToken.None));
    }

    [Fact]
    public async Task VerifyRejectsUnknownParametersBeforeDerivation()
    {
        var hasher = new Argon2idPasswordHasher();

        var verified = await hasher.VerifyAsync(
            "correct-horse-battery",
            new byte[32],
            new byte[32],
            "v=19;m=1;t=1;p=1;h=32",
            CancellationToken.None);

        Assert.False(verified);
    }
}
