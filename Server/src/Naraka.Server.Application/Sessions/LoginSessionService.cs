using Naraka.Server.Application.Accounts;

namespace Naraka.Server.Application.Sessions;

public sealed class LoginSessionService(
    AccountService accounts,
    IAuthenticatedSessionRegistry sessions)
{
    public async Task<LoginSessionResult> LoginAsync(
        ConnectionId connectionId,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var authentication = await accounts.AuthenticateAsync(username, password, cancellationToken);
        if (authentication.Status != AccountAuthenticationStatus.Success || authentication.AccountId is null)
        {
            return new LoginSessionResult(authentication.Status);
        }

        var session = sessions.Bind(connectionId, authentication.AccountId.Value);
        return new LoginSessionResult(AccountAuthenticationStatus.Success, session);
    }

    public bool Disconnect(ConnectionId connectionId) => sessions.Remove(connectionId);
}
