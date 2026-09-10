using System;
using Naraka.Config;

namespace Naraka.Core.Application.Bootstrap
{
    /// <summary>
    /// 当前服务器实际部署了哪些 P1 功能。
    ///
    /// 存在的理由：云端 Host 与本机 Host 的部署进度不同步。只部署到 P1.1-A 的旧云端
    /// 不认识后续 P1 协议，客户端一旦自动发送就会被对端当成非法帧而断开连接。
    /// 因此每个功能入口在发请求之前都必须先问一次这里。
    /// </summary>
    public interface IServerCapabilities
    {
        /// <summary>
        /// 服务器没有声明能力字段时为 true。此时按 P1.1-A 兼容模式运行：
        /// 版本预检、注册、登录、账号概要与进入大厅可用，其余入口一律提示"服务器功能尚未升级"。
        /// </summary>
        bool IsCompatibilityMode { get; }

        /// <summary>版本预检尚未成功时为 false。此时不应发送任何业务请求。</summary>
        bool IsResolved { get; }

        bool Has(string capability);
    }

    /// <summary>
    /// 能力集合的可写实现。只有 Bootstrap 版本预检允许写入，
    /// 其余模块通过 <see cref="IServerCapabilities"/> 只读访问。
    /// </summary>
    public sealed class ServerCapabilityRegistry : IServerCapabilities
    {
        private string[] _capabilities = Array.Empty<string>();

        public bool IsCompatibilityMode { get; private set; }

        public bool IsResolved { get; private set; }

        public bool Has(string capability) =>
            IsResolved && NarakaServerCapabilities.Contains(_capabilities, capability);

        /// <summary>
        /// 记录版本预检拿到的能力声明。
        ///
        /// <paramref name="declared"/> 为 null 或空表示服务器根本没有这个字段——那是旧云端的形状，
        /// 而不是"没有任何功能"。这两种情况必须区分：前者退回 P1.1-A 兼容集合，
        /// 后者会让玩家连大厅余额都看不到。
        /// </summary>
        public void Resolve(string[] declared)
        {
            if (declared == null || declared.Length == 0)
            {
                _capabilities = NarakaServerCapabilities.CompatibilityMode;
                IsCompatibilityMode = true;
                IsResolved = true;
                return;
            }

            var copy = new string[declared.Length];
            Array.Copy(declared, copy, declared.Length);
            _capabilities = copy;
            IsCompatibilityMode = false;
            IsResolved = true;
        }

        /// <summary>会话失效或重新预检时清空，避免沿用上一台服务器的能力集合。</summary>
        public void Reset()
        {
            _capabilities = Array.Empty<string>();
            IsCompatibilityMode = false;
            IsResolved = false;
        }
    }
}
