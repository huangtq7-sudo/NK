namespace Naraka.Server.Application.Accounts;

public enum AccountRegistrationStatus
{
    Success,
    AlreadyExists,
    InvalidUsername,
    WeakPassword
}

public sealed record AccountRegistrationResult(AccountRegistrationStatus Status, long? AccountId = null);
