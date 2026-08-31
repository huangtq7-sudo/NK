using System.Threading;
using Cysharp.Threading.Tasks;

namespace Naraka.Features.Account.Controller
{
    public readonly struct AccountAuthenticatedEvent
    {
        public AccountAuthenticatedEvent(string username, long accountId)
        {
            Username = username ?? string.Empty;
            AccountId = accountId;
        }

        public string Username { get; }

        public long AccountId { get; }
    }

    public enum AccountRegistrationStatus
    {
        Success,
        Failed,
        AlreadyExists,
        Forbidden
    }

    public enum AccountLoginStatus
    {
        Success,
        Failed,
        WrongPassword,
        UserNotFound,
        Timeout
    }

    public readonly struct AccountRegistrationResult
    {
        public AccountRegistrationResult(AccountRegistrationStatus status)
        {
            Status = status;
        }

        public AccountRegistrationStatus Status { get; }
    }

    public readonly struct AccountLoginResult
    {
        public AccountLoginResult(AccountLoginStatus status, long accountId = 0)
        {
            Status = status;
            AccountId = accountId;
        }

        public AccountLoginStatus Status { get; }

        public long AccountId { get; }
    }

    public interface IAccountGateway
    {
        UniTask<AccountRegistrationResult> RegisterAsync(
            string username,
            string password,
            CancellationToken cancellationToken);

        UniTask<AccountLoginResult> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken);
    }
}
