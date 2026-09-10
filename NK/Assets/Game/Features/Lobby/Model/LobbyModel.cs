using System;
using Naraka.Core.Domain;

namespace Naraka.Features.Lobby.Model
{
    public sealed class LobbyModel : IModel
    {
        public string Username { get; private set; } = string.Empty;

        public long AccountId { get; private set; }

        public bool IsEntered => AccountId > 0;

        /// <summary>是否已经拿到过一次服务端账号概要。为 false 时界面必须保持占位。</summary>
        public bool HasAccountSnapshot { get; private set; }

        public LobbyAccountSnapshot AccountSnapshot { get; private set; }

        /// <summary>是否已经拿到过一次服务端账号资料（头像、出战选择、经验与容量档位）。</summary>
        public bool HasProfile { get; private set; }

        public LobbyProfileSnapshot Profile { get; private set; }

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

            var switchingAccount = AccountId != accountId;
            Username = username;
            AccountId = accountId;

            // 换账号时上一份概要不再适用；同账号重进大厅保留已有数据，避免界面闪回占位。
            if (switchingAccount)
            {
                HasAccountSnapshot = false;
                AccountSnapshot = default;
                HasProfile = false;
                Profile = default;
            }
        }

        /// <summary>
        /// 写入一次成功的服务端概要。加载失败时调用方不得调用本方法，
        /// 上一次成功的数据必须原样保留。
        /// </summary>
        public void ApplyAccountSnapshot(LobbyAccountSnapshot snapshot)
        {
            if (!IsEntered)
            {
                throw new InvalidOperationException("Cannot apply an account snapshot before entering the lobby.");
            }

            AccountSnapshot = snapshot;
            HasAccountSnapshot = true;
        }

        /// <summary>
        /// 写入一次成功的服务端资料。加载或保存失败时调用方不得调用本方法，
        /// 上一次成功的数据必须原样保留。
        /// </summary>
        public void ApplyProfile(LobbyProfileSnapshot profile)
        {
            if (!IsEntered)
            {
                throw new InvalidOperationException("Cannot apply a profile before entering the lobby.");
            }

            Profile = profile;
            HasProfile = true;
        }
    }
}
