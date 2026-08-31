using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Naraka.Server.Application.Accounts;

namespace Naraka.Server.Infrastructure.Security;

public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    public const string CurrentParameters = "v=19;m=65536;t=3;p=2;h=32";

    private const int SaltLength = 32;
    private const int HashLength = 32;
    private const int MemorySizeKiB = 65_536;
    private const int Iterations = 3;
    private const int Parallelism = 2;

    public async ValueTask<PasswordHashResult> HashAsync(
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        cancellationToken.ThrowIfCancellationRequested();

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = await DeriveAsync(password, salt, cancellationToken);
        return new PasswordHashResult(hash, salt, CurrentParameters);
    }

    public async ValueTask<bool> VerifyAsync(
        string password,
        byte[] expectedHash,
        byte[] salt,
        string parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);
        ArgumentNullException.ThrowIfNull(salt);
        if (!string.Equals(parameters, CurrentParameters, StringComparison.Ordinal) ||
            expectedHash.Length != HashLength ||
            salt.Length != SaltLength)
        {
            return false;
        }

        var actualHash = await DeriveAsync(password, salt, cancellationToken);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actualHash);
        }
    }

    private static async Task<byte[]> DeriveAsync(
        string password,
        byte[] salt,
        CancellationToken cancellationToken)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = MemorySizeKiB,
                Iterations = Iterations,
                DegreeOfParallelism = Parallelism
            };

            var hash = await argon2.GetBytesAsync(HashLength);
            cancellationToken.ThrowIfCancellationRequested();
            return hash;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
