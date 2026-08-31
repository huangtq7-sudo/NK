using System.Security.Cryptography;

namespace Naraka.Server.LegacyNetworkV1.Protocol;

/// <summary>
/// Byte-compatible implementation of the frozen legacy Rijndael/PasswordDeriveBytes payload codec.
/// This codec exists only at the LegacyNetworkV1 boundary; new business protocols must not use it.
/// </summary>
public static class LegacyAesCodec
{
    private static readonly byte[] InitializationVector =
        Convert.FromBase64String("Rkb4jvUy/ye7Cd7k89QQgQ==");

    private static readonly byte[] Salt =
        Convert.FromBase64String("gsf4jvkyhye5/d7k8OrLgM==");

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        if (plaintext.IsEmpty)
        {
            throw new ArgumentException("Legacy plaintext must not be empty.", nameof(plaintext));
        }

        using var aes = CreateAlgorithm(passphrase);
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext.ToArray(), 0, plaintext.Length);
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> ciphertext, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        if (ciphertext.IsEmpty)
        {
            throw new ArgumentException("Legacy ciphertext must not be empty.", nameof(ciphertext));
        }

        using var aes = CreateAlgorithm(passphrase);
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext.ToArray(), 0, ciphertext.Length);
    }

    private static Aes CreateAlgorithm(string passphrase)
    {
#pragma warning disable SYSLIB0021 // Required for byte compatibility with the frozen .NET Framework implementation.
        using var keyDerivation = new PasswordDeriveBytes(passphrase, Salt);
#pragma warning restore SYSLIB0021

        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = keyDerivation.GetBytes(32);
        aes.IV = InitializationVector;
        return aes;
    }
}
