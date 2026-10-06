using System;
using System.Collections.Generic;

namespace Naraka.Core.Application.Scenes
{
    /// <summary>
    /// 稳定的业务地图 ID。
    ///
    /// 这些值是**业务标识**，会进入未来的远征记录、服务端协议与数据库，
    /// 因此它们必须与 Unity 场景文件名无关：改一个场景文件名不该让历史远征数据失效，
    /// 把灰盒场景换成正式场景也不该改变地图的业务身份。
    ///
    /// 映射到 Unity 场景名是 <see cref="IWorldSceneCatalog"/> 的职责，
    /// 只在场景加载边界发生一次。
    /// </summary>
    public static class WorldMapIds
    {
        /// <summary>任务地图（正式环境为 High Elves Sanctuary）。</summary>
        public const string Map01 = "Map01";

        /// <summary>战斗地图（正式环境为 Pure Nature）。</summary>
        public const string Map02 = "Map02";
    }

    /// <summary>
    /// Unity 场景名。只有场景加载边界、Editor 装配工具与场景契约测试可以使用这些常量；
    /// 业务状态一律使用 <see cref="WorldMapIds"/>。
    /// </summary>
    public static class WorldSceneNames
    {
        /// <summary>Bootstrap 场景，始终是构建列表的第一项。</summary>
        public const string Boot = "SampleScene";

        /// <summary>正式任务场景。</summary>
        public const string Map01Task = "Map01_Task";

        /// <summary>正式战斗场景。</summary>
        public const string Map02Combat = "Map02_Combat";

        /// <summary>
        /// 灰盒战斗场景。保留为**开发回退资产**，不参与正常流程流转：
        /// 它不是任何 <see cref="WorldMapIds"/> 的解析结果，因此正式路径无法走到它。
        /// </summary>
        public const string Map02CombatGrayboxFallback = "Map02_CombatGraybox";
    }

    /// <summary>
    /// 把稳定业务地图 ID 解析成 Unity 场景名。
    ///
    /// 这是 MapId 与场景名之间**唯一**的转换点。Controller 只持有 MapId，
    /// <c>ISceneLoader</c> 只接受场景名，两者之间的翻译在这里完成一次。
    /// </summary>
    public interface IWorldSceneCatalog
    {
        /// <summary>本目录登记的全部业务地图 ID。</summary>
        IReadOnlyList<string> MapIds { get; }

        /// <summary>解析失败时抛出，不返回空串：拿一个空场景名去加载只会在更远的地方炸。</summary>
        string ResolveSceneName(string mapId);

        bool TryResolveSceneName(string mapId, out string sceneName);

        /// <summary>给定场景名是否属于本目录登记的正式运行场景。</summary>
        bool IsRuntimeScene(string sceneName);
    }

    /// <summary>
    /// 只读的 MapId 到场景名映射。
    ///
    /// <see cref="Default"/> 是正式流程使用的那一份，测试直接断言它，
    /// 因此"Map01 映射到 Map01_Task"这件事只有一个权威来源。
    /// </summary>
    public sealed class WorldSceneCatalog : IWorldSceneCatalog
    {
        private readonly Dictionary<string, string> _mapIdToSceneName;
        private readonly string[] _mapIds;

        public WorldSceneCatalog(IEnumerable<KeyValuePair<string, string>> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            _mapIdToSceneName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    throw new ArgumentException("地图 ID 不能为空。", nameof(entries));
                }

                if (string.IsNullOrWhiteSpace(entry.Value))
                {
                    throw new ArgumentException($"地图 {entry.Key} 的场景名不能为空。", nameof(entries));
                }

                if (_mapIdToSceneName.ContainsKey(entry.Key))
                {
                    throw new ArgumentException($"地图 ID {entry.Key} 重复登记。", nameof(entries));
                }

                _mapIdToSceneName.Add(entry.Key, entry.Value);
            }

            var ids = new string[_mapIdToSceneName.Count];
            _mapIdToSceneName.Keys.CopyTo(ids, 0);
            _mapIds = ids;
        }

        /// <summary>正式流程使用的映射。灰盒战斗场景**刻意不在**这里。</summary>
        public static WorldSceneCatalog Default { get; } = new WorldSceneCatalog(
            new[]
            {
                new KeyValuePair<string, string>(WorldMapIds.Map01, WorldSceneNames.Map01Task),
                new KeyValuePair<string, string>(WorldMapIds.Map02, WorldSceneNames.Map02Combat)
            });

        public IReadOnlyList<string> MapIds => _mapIds;

        public string ResolveSceneName(string mapId)
        {
            if (TryResolveSceneName(mapId, out var sceneName))
            {
                return sceneName;
            }

            throw new ArgumentException(
                $"未登记的地图 ID '{mapId}'。正式流程只认 {string.Join("、", _mapIds)}。",
                nameof(mapId));
        }

        public bool TryResolveSceneName(string mapId, out string sceneName)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                sceneName = string.Empty;
                return false;
            }

            return _mapIdToSceneName.TryGetValue(mapId, out sceneName);
        }

        public bool IsRuntimeScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            foreach (var pair in _mapIdToSceneName)
            {
                if (string.Equals(pair.Value, sceneName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
