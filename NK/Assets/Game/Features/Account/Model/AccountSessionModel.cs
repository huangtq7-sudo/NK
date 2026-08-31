using System;
using Naraka.Core.Domain;

namespace Naraka.Features.Account.Model
{
    public sealed class AccountSessionModel : IModel
    {
        public string Username { get; private set; } = string.Empty;

        public long AccountId { get; private set; }

        public bool IsAuthenticated => AccountId > 0;

        public void Authenticate(string username, long accountId)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("Username cannot be empty.", nameof(username));
            }

            if (accountId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(accountId));
            }

            Username = username.Trim();
            AccountId = accountId;
        }

        public void Clear()
        {
            Username = string.Empty;
            AccountId = 0;
        }
    }
}
