using Naraka.Config;
using Naraka.Core.Application.Bootstrap;

namespace Naraka.P0.Tests
{
    /// <summary>
    /// 测试用能力集合构造器。
    ///
    /// 默认给出完整能力，因此现有大厅测试关注的仍是自己的业务分支；
    /// 兼容模式与"服务器功能尚未升级"分支由专门的测试显式构造。
    /// </summary>
    internal static class LobbyTestCapabilities
    {
        /// <summary>新 Host：声明了全部 P1 能力。</summary>
        public static IServerCapabilities Full()
        {
            var registry = new ServerCapabilityRegistry();
            registry.Resolve(NarakaServerCapabilities.Full);
            return registry;
        }

        /// <summary>旧云端：Bootstrap 响应里没有能力字段，退回 P1.1-A 兼容集合。</summary>
        public static IServerCapabilities LegacyCloud()
        {
            var registry = new ServerCapabilityRegistry();
            registry.Resolve(null);
            return registry;
        }

        /// <summary>版本预检尚未完成。任何业务入口都不应放行。</summary>
        public static IServerCapabilities Unresolved() => new ServerCapabilityRegistry();
    }
}
