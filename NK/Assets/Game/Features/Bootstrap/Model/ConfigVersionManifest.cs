using System;

namespace Naraka.Features.Bootstrap.Model
{
    /// <summary>
    /// 服务器 Bootstrap 预检响应。
    ///
    /// 前四个字段是 P0 已经部署的契约，参与兼容性判定。
    /// <see cref="ServerCapabilities"/> 是可选新增字段：只部署到 P1.1-A 的旧云端不会返回它，
    /// 此时数组为空，客户端按兼容模式运行。它<b>不</b>参与版本兼容性判定，
    /// 否则旧云端会因为缺少一个新字段而无法登录。
    /// </summary>
    public readonly struct ConfigVersionManifest
    {
        public ConfigVersionManifest(
            string configVersion,
            string minimumClientVersion,
            string maximumClientVersion,
            string protocolVersion,
            string[] serverCapabilities = null)
        {
            ConfigVersion = configVersion ?? string.Empty;
            MinimumClientVersion = minimumClientVersion ?? string.Empty;
            MaximumClientVersion = maximumClientVersion ?? string.Empty;
            ProtocolVersion = protocolVersion ?? string.Empty;
            ServerCapabilities = serverCapabilities ?? Array.Empty<string>();
        }

        public string ConfigVersion { get; }

        public string MinimumClientVersion { get; }

        public string MaximumClientVersion { get; }

        public string ProtocolVersion { get; }

        /// <summary>服务器声明的功能能力。空数组表示服务器没有该字段（旧云端）。</summary>
        public string[] ServerCapabilities { get; }
    }
}
