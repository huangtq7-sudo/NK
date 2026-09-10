// <auto-mirrored />
// 权威源文件：Shared/Config/NarakaServerCapabilities.cs
// 由 Tools/Config/Naraka.ConfigCompiler 镜像复制到
// NK/Assets/Game/Generated/Config/NarakaServerCapabilities.cs。
// 不要单独修改任一副本；`--check` 会在两份副本不一致时以非 0 退出码失败。
//
// 这些字符串会出现在 Bootstrap 预检响应里，属于线级契约：只能追加，不能改名或复用。
#nullable disable

using System;

namespace Naraka.Config
{
    /// <summary>
    /// 服务端功能能力声明。Host 通过 <c>GET /bootstrap/config-version</c> 的可选
    /// <c>serverCapabilities</c> 字段告诉客户端自己实际部署了哪些 P1 功能。
    ///
    /// 该字段是向后兼容的可选字段：只部署到 P1.1-A 的旧云端不会返回它，
    /// 客户端此时必须按 <see cref="CompatibilityMode"/> 运行，不得自动发送旧云端不认识的协议。
    /// </summary>
    public static class NarakaServerCapabilities
    {
        /// <summary>账号等级与三种货币只读快照（协议 19/20）。P1.1-A 已经部署到云端。</summary>
        public const string LobbyAccountSummary = "lobby.account.summary";

        /// <summary>头像、头像框与昵称等账号资料的读取与持久化。</summary>
        public const string AccountProfile = "account.profile";

        /// <summary>英雄、兵器与宠物的拥有状态与选择。</summary>
        public const string Loadout = "loadout";

        /// <summary>仓库、堆叠物品、装备方案、整理与扩容。</summary>
        public const string Inventory = "inventory";

        /// <summary>商店目录、限购与购买事务。</summary>
        public const string Shop = "shop";

        /// <summary>锻造与唯一武器强化。</summary>
        public const string Forge = "forge";

        /// <summary>抽奖订单、保底与结果恢复。</summary>
        public const string Gacha = "gacha";

        /// <summary>签到、补签与连续签到奖励。</summary>
        public const string SignIn = "signin";

        /// <summary>账号等级奖励领取。</summary>
        public const string AccountReward = "account.reward";

        /// <summary>成就进度与成就奖励领取。</summary>
        public const string Achievement = "achievement";

        /// <summary>好友申请、在线状态与一对一文字聊天。</summary>
        public const string Social = "social";

        /// <summary>红点 Version/SeenVersion 同步。</summary>
        public const string RedDot = "reddot";

        /// <summary>
        /// 旧云端（只部署到 P1.1-A）没有返回能力字段时客户端使用的兼容集合。
        /// 只包含旧云端确实实现过的协议，因此登录、进入大厅与账号概要仍然可用，
        /// 其余入口一律显示"服务器功能尚未升级"，绝不发送未知协议。
        /// </summary>
        public static readonly string[] CompatibilityMode = { LobbyAccountSummary };

        /// <summary>
        /// 已登记的全部能力名。它是一份<b>登记表</b>，用于校验字符串合法，
        /// <b>不代表</b>某个 Host 真的实现了它们。
        /// </summary>
        public static readonly string[] Full =
        {
            LobbyAccountSummary,
            AccountProfile,
            Loadout,
            Inventory,
            Shop,
            Forge,
            Gacha,
            SignIn,
            AccountReward,
            Achievement,
            Social,
            RedDot
        };

        /// <summary>
        /// 本仓库 Host <b>当前真正实现</b>的能力。Host 只能声明这一份。
        ///
        /// 声明一个尚未实现的能力比不声明更糟：客户端会因此发送传输层不认识的协议，
        /// 而兼容门控存在的意义正是避免这件事。新增一个切片时，
        /// 只有服务端真的处理它的协议了，才把对应常量加进来。
        /// </summary>
        public static readonly string[] Implemented =
        {
            LobbyAccountSummary,
            AccountProfile,
            Loadout,
            Inventory,
            Shop,
            Forge,
            Gacha,
            SignIn,
            AccountReward,
            Achievement,
            Social,
            RedDot
        };

        public static bool Contains(string[] capabilities, string capability)
        {
            if (capabilities == null || capability == null)
            {
                return false;
            }

            for (var i = 0; i < capabilities.Length; i++)
            {
                if (string.Equals(capabilities[i], capability, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
