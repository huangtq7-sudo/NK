namespace Naraka.Server.Application.Accounts;

public interface IAccountRepository
{
    Task<AccountCredentialRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    Task<long> CreateAsync(CreateAccountRecord account, CancellationToken cancellationToken);
}
