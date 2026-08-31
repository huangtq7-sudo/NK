using System;
using Naraka.Core.Domain;

namespace Naraka.Features.Lobby.Model
{
    public sealed class LobbyModel : IModel
    {
        public string Username { get; private set; } = string.Empty;

        public long AccountId { get; private set; }

        public bool IsEntered => AccountId > 0;

        public void Enter(string username, long accountId)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("Username cannot be empty.", nameof(username));
            }

            if (accountId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(accountId));
            }

            Username = username;
            AccountId = accountId;
        }
    }
}
