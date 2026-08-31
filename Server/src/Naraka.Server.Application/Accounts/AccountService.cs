namespace Naraka.Server.Application.Accounts;

public sealed class AccountService(IAccountRepository repository, IPasswordHasher passwordHasher)
{
    public async Task<AccountRegistrationResult> RegisterAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = username?.Trim() ?? string.Empty;
        if (normalizedUsername.Length is < 3 or > 64)
        {
            return new AccountRegistrationResult(AccountRegistrationStatus.InvalidUsername);
        }

        if (string.IsNullOrEmpty(password) || password.Length is < 10 or > 128)
        {
            return new AccountRegistrationResult(AccountRegistrationStatus.WeakPassword);
        }

        if (await repository.FindByUsernameAsync(normalizedUsername, cancellationToken) is not null)
        {
            return new AccountRegistrationResult(AccountRegistrationStatus.AlreadyExists);
        }

        var passwordHash = await passwordHasher.HashAsync(password, cancellationToken);
        var accountId = await repository.CreateAsync(
            new CreateAccountRecord(
                normalizedUsername,
                passwordHash.Hash,
                passwordHash.Salt,
                passwordHash.Parameters),
            cancellationToken);

        return new AccountRegistrationResult(AccountRegistrationStatus.Success, accountId);
    }

    public async Task<AccountAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = username?.Trim() ?? string.Empty;
        if (normalizedUsername.Length is < 3 or > 64 || string.IsNullOrEmpty(password))
        {
            return new AccountAuthenticationResult(AccountAuthenticationStatus.UserNotFound);
        }

        var account = await repository.FindByUsernameAsync(normalizedUsername, cancellationToken);
        if (account is null)
        {
            return new AccountAuthenticationResult(AccountAuthenticationStatus.UserNotFound);
        }

        if (account.Status != 0)
        {
            return new AccountAuthenticationResult(AccountAuthenticationStatus.Forbidden);
        }

        var verified = await passwordHasher.VerifyAsync(
            password,
            account.PasswordHash,
            account.PasswordSalt,
            account.PasswordParameters,
            cancellationToken);

        return verified
            ? new AccountAuthenticationResult(AccountAuthenticationStatus.Success, account.AccountId)
            : new AccountAuthenticationResult(AccountAuthenticationStatus.WrongPassword);
    }
}
