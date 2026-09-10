using System;
using System.IO;
using Naraka.Config;
using Naraka.Core.Application.Config;
using UnityEngine;

namespace Naraka.Infrastructure.Config
{
    /// <summary>
    /// 从 <c>StreamingAssets/Config/naraka-config.json</c> 读取由 Naraka.ConfigCompiler 生成的规范配置。
    ///
    /// 客户端与服务端读取的是同一份生成物，因此二者的 <c>ConfigVersion</c> 与内容哈希必然一致；
    /// 但客户端只把它用于展示，价格、概率与奖励结果始终以服务端返回值为准。
    ///
    /// 选择 StreamingAssets 而不是 Resources/Addressables，是因为编辑器与 Windows 独立播放器
    /// 都能直接按文件读取，无需引入额外的资源管线依赖。
    /// </summary>
    public sealed class StreamingAssetsGameConfigProvider : IGameConfigProvider
    {
        public const string RequiredSchemaVersion = "1.0.0";
        private const string RelativePath = "Config/naraka-config.json";

        public StreamingAssetsGameConfigProvider(string absolutePath = null)
        {
            var path = string.IsNullOrEmpty(absolutePath)
                ? Path.Combine(UnityEngine.Application.streamingAssetsPath, RelativePath)
                : absolutePath;

            Load(path);
        }

        public bool IsLoaded => Catalog != null;

        public string LoadError { get; private set; } = string.Empty;

        public GameConfigCatalog Catalog { get; private set; }

        private void Load(string path)
        {
            string json;
            try
            {
                if (!File.Exists(path))
                {
                    // 只报相对路径：完整部署路径没有诊断价值，也不该进入玩家可见的日志。
                    LoadError = "未找到配置文件 " + RelativePath + "，请先运行配置编译器。";
                    return;
                }

                json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            catch (IOException exception)
            {
                LoadError = "读取配置文件失败：" + exception.Message;
                return;
            }

            NarakaConfigCatalog catalog;
            try
            {
                catalog = JsonUtility.FromJson<NarakaConfigCatalog>(json);
            }
            catch (ArgumentException exception)
            {
                LoadError = "配置文件格式错误：" + exception.Message;
                return;
            }

            if (catalog == null)
            {
                LoadError = "配置文件内容为空。";
                return;
            }

            if (!string.Equals(catalog.SchemaVersion, RequiredSchemaVersion, StringComparison.Ordinal))
            {
                LoadError = "配置结构版本为 " + catalog.SchemaVersion +
                            "，客户端要求 " + RequiredSchemaVersion + "。";
                return;
            }

            // 空表意味着 JsonUtility 静默丢弃了内容（例如字段名不匹配）。
            // 这种情况必须显式失败，否则界面会显示成"没有任何英雄"。
            if (catalog.Heroes == null || catalog.Heroes.Length == 0 ||
                catalog.Currencies == null || catalog.Currencies.Length == 0)
            {
                LoadError = "配置文件缺少英雄或货币定义。";
                return;
            }

            Catalog = new GameConfigCatalog(catalog);
            LoadError = string.Empty;
        }
    }
}
