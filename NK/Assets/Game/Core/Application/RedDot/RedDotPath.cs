using System;
using System.Collections.Generic;

namespace Naraka.Core.Application.RedDot
{
    /// <summary>
    /// 红点路径。
    ///
    /// 路径本身就是树：<c>Social/UnreadChat/c42</c> 的祖先是 <c>Social/UnreadChat</c> 与 <c>Social</c>。
    /// 因此不需要单独维护父子指针，只要按 <c>/</c> 切分即可得到需要一起变脏的祖先。
    /// </summary>
    public static class RedDotPath
    {
        public const char Separator = '/';

        /// <summary>大厅根节点。整个树只有这一个根，界面上的入口都挂在它下面。</summary>
        public const string Lobby = "Lobby";

        public const string Inventory = "Lobby/Inventory";
        public const string InventoryNewItem = "Lobby/Inventory/NewItem";

        public const string Forge = "Lobby/Forge";
        public const string ForgeUpgradeAvailable = "Lobby/Forge/UpgradeAvailable";

        public const string Gacha = "Lobby/Gacha";
        public const string GachaUnshownResult = "Lobby/Gacha/UnshownResult";

        public const string SignIn = "Lobby/SignIn";
        public const string SignInDailyClaim = "Lobby/SignIn/DailyClaim";
        public const string SignInMilestoneClaim = "Lobby/SignIn/MilestoneClaim";

        public const string AccountLevel = "Lobby/AccountLevel";
        public const string AccountLevelClaimableReward = "Lobby/AccountLevel/ClaimableReward";

        public const string Achievement = "Lobby/Achievement";
        public const string AchievementClaimableReward = "Lobby/Achievement/ClaimableReward";

        public const string Social = "Lobby/Social";
        public const string SocialFriendRequest = "Lobby/Social/FriendRequest";
        public const string SocialUnreadChat = "Lobby/Social/UnreadChat";

        /// <summary>一段一对一会话的未读节点。ConversationId 作为最后一段。</summary>
        public static string UnreadChat(string conversationId) =>
            string.IsNullOrEmpty(conversationId)
                ? SocialUnreadChat
                : SocialUnreadChat + Separator + conversationId;

        /// <summary>
        /// 由叶子路径回溯到根，返回自身与全部祖先。
        /// 顺序是从叶子到根，调用方按这个顺序把版本号往上推即可。
        /// </summary>
        public static IReadOnlyList<string> SelfAndAncestors(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return Array.Empty<string>();
            }

            var nodes = new List<string>(4) { path };
            var index = path.LastIndexOf(Separator);
            while (index > 0)
            {
                path = path.Substring(0, index);
                nodes.Add(path);
                index = path.LastIndexOf(Separator);
            }

            return nodes;
        }

        /// <summary>是否是另一个节点的后代。用于界面按前缀订阅一整棵子树。</summary>
        public static bool IsDescendantOf(string path, string ancestor)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(ancestor))
            {
                return false;
            }

            return path.Length > ancestor.Length &&
                   path[ancestor.Length] == Separator &&
                   string.CompareOrdinal(path, 0, ancestor, 0, ancestor.Length) == 0;
        }
    }
}
