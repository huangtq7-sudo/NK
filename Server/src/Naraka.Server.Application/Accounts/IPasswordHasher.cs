namespace Naraka.Server.Application.Accounts;

public interface IPasswordHasher
{
    ValueTask<PasswordHashResult> HashAsync(string password, CancellationToken cancellationToken);

    ValueTask<bool> VerifyAsync(
        string password,
        byte[] expectedHash,
        byte[] salt,
        string parameters,
        CancellationToken cancellationToken);
}
