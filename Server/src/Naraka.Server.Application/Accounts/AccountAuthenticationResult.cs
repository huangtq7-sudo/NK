namespace Naraka.Server.Application.Accounts;

public enum AccountAuthenticationStatus
{
    Success,
    UserNotFound,
    WrongPassword,
    Forbidden
}

public sealed record AccountAuthenticationResult(AccountAuthenticationStatus Status, long? AccountId = null);
