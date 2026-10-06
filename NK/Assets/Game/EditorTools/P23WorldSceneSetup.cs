using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Naraka.Core.Application.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Naraka.EditorTools
{
    /// <summary>
    /// P2.3 正式运行场景派生。
    ///
    /// 把第三方演示场景的**环境**接进项目自有的运行场景，业务层仍由
    /// <see cref="P2SceneSetup.EnsureWorldBusinessLayer"/> 装配。
    ///
    /// 第三方源场景严格只读。派生用的是 Unity 的"另存为"语义：
    /// <c>OpenScene(源场景)</c> 把它读进内存，改完之后
    /// <c>SaveScene(scene, 项目路径)</c> 写到**另一个**路径。
    /// 源 <c>.unity</c> 文件从头到尾没有被写过一次，因此烘焙光照、反射探针、
    /// 光照探针与一万多个 Prefab 实例的修改列表全都原样保留 ——
    /// 这是"把对象搬到新场景"做不到的：搬运会断开 LightingDataAsset 的绑定。
    ///
    /// 幂等性：只有目标场景**还没有**环境时才从源场景派生。已经派生过的场景只做
    /// 业务层补齐，因此重复执行不会产生第二套摄像机、玩家、灯光、出生点、传送门或怪物，
    /// 也不会冲掉用户在 Unity 里手工调过的出生点位置。
    /// 需要彻底重建时使用 Rebuild 菜单项，那是显式操作。
    ///
    /// 失败安全：派生路径在"环境已就位 + 业务已装配"之后**一次性**写盘。
    /// 中途任何一步抛异常，目标场景文件都还是上一次的样子，不会留下半成品。
    /// </summary>
    public static class P23WorldSceneSetup
    {
        /// <summary>任务地图的正式环境源场景（只读）。</summary>
        public const string HighElvesSourceScenePath =
            "Assets/Aquarius Fantasy - High Elves/Demo Scenes/High Elves Sanctuary/" +
            "High Elves Sanctuary.unity";

        /// <summary>战斗地图的正式环境源场景（只读）。</summary>
        public const string PureNatureSourceScenePath =
            "Assets/PureNature/Scenes/Scene_Demo/Scene_Demo.unity";

        /// <summary>项目自有的环境容器。它的存在就是"本场景已经派生过"的标记。</summary>
        public const string EnvironmentRootName = "Environment";

        /// <summary>
        /// 判定"还在装配工具默认位置"的半径。装配工具把新建对象摆在
        /// 世界原点附近（最远的是重生点 x = -4），而地形中心在几百米外。
        /// </summary>
        private const float DefaultPositionRadius = 20f;

        /// <summary>一个经人确认过的绝对摆位。</summary>
        private readonly struct Placement
        {
            public Placement(Vector3 position, Vector3 eulerAngles)
            {
                Position = position;
                EulerAngles = eulerAngles;
            }

            public Vector3 Position { get; }

            public Vector3 EulerAngles { get; }
        }

        /// <summary>
        /// 用户在 Unity 里实际看过画面之后定下的权威摆位（绝对世界坐标）。
        ///
        /// 这些值优先于地形采样：采样只是个起点猜测（靠近地形中心），
        /// 而这些是人看过实际环境之后给的。因此它们每次执行都会被强制应用，
        /// 不走"只落还在原点附近的对象"那条规则 —— 工具现在是这些坐标的主。
        /// 要改就改这张表，而不是在 Unity 里拖 —— 否则下次装配会被摆回来。
        ///
        /// 按地图分开：`SpawnPoint_Entry` 两张图都有，而它们的坐标没有任何关系。
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, Placement>> AuthoredPlacements =
            new Dictionary<string, Dictionary<string, Placement>>(StringComparer.Ordinal)
            {
                {
                    WorldMapIds.Map01,
                    new Dictionary<string, Placement>(StringComparer.Ordinal)
                    {
                        // 朝向 y = -90 指向 -X。传送门相对出生点是 Δx = -124.9、Δz = +15.5，
                        // 主方向就是 -X，因此角色出场时正对着它。
                        {
                            "SpawnPoint_Entry",
                            new Placement(new Vector3(510f, 35f, 587f), new Vector3(0f, -90f, 0f))
                        },
                        {
                            "SpawnPoint_Respawn",
                            new Placement(new Vector3(510f, 35f, 587f), new Vector3(0f, -90f, 0f))
                        },
                        {
                            "Portal_To_Map02",
                            new Placement(new Vector3(385.1f, 36.1f, 602.5f), Vector3.zero)
                        }
                    }
                }
            };

        /// <summary>开发用对象的统一父节点。正式流程不依赖它，默认整体停用。</summary>
        public const string DevOnlyRootName = "DevOnly_TrainingArea";

        /// <summary>
        /// 正式战斗场景的烘焙 NavMesh。Unity 把场景 NavMesh 放在与场景同名的目录里。
        /// </summary>
        public const string CombatNavMeshAssetPath =
            "Assets/Game/Scenes/World/Map02_Combat/NavMesh.asset";

        /// <summary>
        /// 必须从派生场景里剔除的演示控制逻辑。
        ///
        /// 只列**控制**类脚本。环境里的装饰性动画（<c>ElvenRotator</c>、
        /// <c>BobbingObject</c> 让法阵旋转、浮空物上下浮动）属于环境呈现本身，
        /// 刻意保留：它们不碰任何业务状态，删掉只会让圣殿变成静物。
        /// </summary>
        public static readonly string[] DemoControlScriptNames =
        {
            // Pure Nature 的演示漫游相机。
            "FreeCamera",

            // High Elves 的演示第一人称角色控制器：读鼠标、驱动自己的 CharacterController、
            // 处理跳跃与重力。它活在正式任务场景里会和 NARAKA 自己的玩家抢输入与镜头。
            // 第一轮派生漏了它 —— 当时只剥了相机、AudioListener 与漫游脚本，
            // 而这个演示角色是整个 Prefab 实例，相机只是它的一个子对象。
            "DemoCharacter"
        };

        [MenuItem("NARAKA/Setup/Apply P2.3 Formal World Scenes")]
        public static void Apply()
        {
            Run(forceRederive: false);
        }

        [MenuItem("NARAKA/Setup/Rebuild P2.3 Formal World Scenes From Source")]
        public static void Rebuild()
        {
            if (!EditorUtility.DisplayDialog(
                    "重建正式运行场景",
                    "将从第三方源场景重新派生 Map01_Task 与 Map02_Combat。\n" +
                    "场景内手工调整过的出生点、传送门与怪物生成点位置会被重置。\n" +
                    "第三方源场景不会被修改。",
                    "重建",
                    "取消"))
            {
                return;
            }

            Run(forceRederive: true);
        }

        private static void Run(bool forceRederive)
        {
            var report = new StringBuilder();
            report.AppendLine("NARAKA P2.3 正式运行场景派生：");

            try
            {
                var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    P2SceneSetup.PlayerPrefabPath);
                if (playerPrefab == null)
                {
                    throw new InvalidOperationException(
                        $"玩家 Prefab 不存在：{P2SceneSetup.PlayerPrefabPath}。" +
                        "请先运行 NARAKA/Setup/Apply P2 Scene Setup。");
                }

                EnsureFormalScene(
                    sourceScenePath: HighElvesSourceScenePath,
                    targetScenePath: P2SceneSetup.Map01ScenePath,
                    mapId: WorldMapIds.Map01,
                    playerPrefab: playerPrefab,
                    withPortal: true,
                    forceRederive: forceRederive,
                    report: report);

                EnsureFormalScene(
                    sourceScenePath: PureNatureSourceScenePath,
                    targetScenePath: P2SceneSetup.Map02ScenePath,
                    mapId: WorldMapIds.Map02,
                    playerPrefab: playerPrefab,
                    withPortal: false,
                    forceRederive: forceRederive,
                    report: report);

                // 正式战斗场景的战斗装配仍由 P2.2 的工具负责（它已经指向
                // Map02_Combat），这里只是把它串进同一个入口，避免人工记住谁先谁后。
                P22CombatSetup.Apply();
                FinalizeCombatScene(report);

                P2SceneSetup.EnsureBuildSettings(report);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                report.AppendLine("  完成。");
                Debug.Log(report.ToString());
            }
            catch (Exception exception)
            {
                report.AppendLine($"  [失败] {exception.Message}");
                report.AppendLine("  目标场景文件未被改写。");
                Debug.LogError(report.ToString());
                throw;
            }
        }

        private static void EnsureFormalScene(
            string sourceScenePath,
            string targetScenePath,
            string mapId,
            GameObject playerPrefab,
            bool withPortal,
            bool forceRederive,
            StringBuilder report)
        {
            report.AppendLine($"  [{mapId}] 目标 {targetScenePath}");

            if (!File.Exists(sourceScenePath))
            {
                throw new InvalidOperationException(
                    $"第三方源场景不存在：{sourceScenePath}");
            }

            var alreadyDerived = TargetHasEnvironment(targetScenePath);
            if (alreadyDerived && !forceRederive)
            {
                // 环境已就位：只补业务层，绝不重新派生，因此手工摆位不会被冲掉。
                var scene = EditorSceneManager.OpenScene(targetScenePath, OpenSceneMode.Single);
                P2SceneSetup.EnsureWorldBusinessLayer(
                    scene, mapId, playerPrefab,
                    withPortal: withPortal, withGrayboxEnvironment: false, report);
                RemoveGrayboxEnvironment(scene, report);

                // 权威摆位在这条路径上也要应用：场景已经派生过之后才收到的
                // 坐标修正，否则永远落不到位。
                GroundBusinessObjects(scene, mapId, report);

                // 演示内容清理在这条路径上也跑一遍，因此工具是**自愈**的：
                // 就算某次派生没摔干净（比如当时试图删 Prefab 实例内部的组件而没生效），
                // 下一次执行也会把演示相机与 AudioListener 停用，
                // 不需要用户去跳 Rebuild。
                StripDemoContent(scene, report);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, targetScenePath);
                report.AppendLine("    环境已存在，本次只补齐业务层（未重新派生）。");
                return;
            }

            DeriveFromSource(
                sourceScenePath, targetScenePath, mapId, playerPrefab, withPortal, report);
        }

        /// <summary>
        /// 从第三方源场景派生。源场景只被读取：改动写到另一个路径，
        /// 因此源 <c>.unity</c> 文件不会被 Unity 写回。
        /// </summary>
        private static void DeriveFromSource(
            string sourceScenePath,
            string targetScenePath,
            string mapId,
            GameObject playerPrefab,
            bool withPortal,
            StringBuilder report)
        {
            var sourceHashBefore = FileFingerprint(sourceScenePath);

            Directory.CreateDirectory(Path.GetDirectoryName(targetScenePath) ?? string.Empty);

            // 读源场景进内存。注意：此后绝不调用无参的 SaveScene(scene)。
            var scene = EditorSceneManager.OpenScene(sourceScenePath, OpenSceneMode.Single);
            report.AppendLine($"    从 {Path.GetFileName(sourceScenePath)} 读入环境。");

            // 1) 把环境收进项目自有的容器节点，便于与业务对象区分。
            // OpenScene(..., Single) 之后被打开的场景就是活动场景，
            // 因此新建对象直接落在它里。
            var environment = new GameObject(EnvironmentRootName);
            var collected = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == environment)
                {
                    continue;
                }

                root.transform.SetParent(environment.transform, worldPositionStays: true);
                collected++;
            }

            report.AppendLine($"    环境根对象 {collected} 个已收进 {EnvironmentRootName}。");

            // 2) 清掉演示用的控制内容。
            StripDemoContent(scene, report);

            // 3) 业务层。环境已经就位，因此出生点可以落在真实地形上。
            P2SceneSetup.EnsureWorldBusinessLayer(
                scene, mapId, playerPrefab,
                withPortal: withPortal, withGrayboxEnvironment: false, report);
            GroundBusinessObjects(scene, mapId, report);

            // 4) 一次性写到项目自有路径。目标已存在时 SaveScene 覆盖内容但保留 .meta，
            //    因此场景 GUID 不变，Build Settings 的引用不会断。
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, targetScenePath))
            {
                throw new InvalidOperationException($"保存派生场景失败：{targetScenePath}");
            }

            report.AppendLine($"    已写出 {targetScenePath}");

            // 5) 证明源场景没有被改写，而不是"相信它没被改写"。
            var sourceHashAfter = FileFingerprint(sourceScenePath);
            if (!string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"第三方源场景被改写了：{sourceScenePath}。这是不允许的。");
            }

            report.AppendLine("    源场景字节数与修改时间未变，保持只读。");
        }

        /// <summary>
        /// 清理演示内容。原则是只保留环境呈现所需的东西，
        /// 不把演示项目的控制逻辑带进 NARAKA。
        /// </summary>
        private static void StripDemoContent(Scene scene, StringBuilder report)
        {
            var removedObjects = new List<string>();
            var removedComponents = new List<string>();

            // 先收集再销毁。演示相机或 EventSystem 自己就可能是场景根对象，
            // 边遍历边销毁会让循环继续解引用一个已经死掉的对象。
            var objectsToDestroy = new List<GameObject>();
            var componentsToDestroy = new List<Component>();

            foreach (var root in scene.GetRootGameObjects())
            {
                // 相机：持久化 App Root 里已经有唯一的一台，演示相机必须整个移除，
                // 否则进场会出现两台相机与两个 AudioListener。
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                {
                    Schedule(camera, "Camera",
                        objectsToDestroy, componentsToDestroy, removedObjects, removedComponents);

                    // URP 的附加相机数据跟着相机走，留着没意义。
                    var urpData = camera.GetComponent("UniversalAdditionalCameraData") as Component;
                    if (urpData != null)
                    {
                        componentsToDestroy.Add(urpData);
                    }
                }

                // EventSystem：项目自己的 UI 已经有一个。
                foreach (var events in root.GetComponentsInChildren<EventSystem>(true))
                {
                    Schedule(events, "EventSystem",
                        objectsToDestroy, componentsToDestroy, removedObjects, removedComponents);
                }

                // AudioListener：同一时刻只允许存在一个，否则 Unity 直接报警。
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true))
                {
                    componentsToDestroy.Add(listener);
                    removedComponents.Add($"{listener.gameObject.name}.AudioListener");
                }

                // 演示控制脚本。null 组件是 Missing Script，由 RemoveMissingScripts 处理。
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null)
                    {
                        continue;
                    }

                    var typeName = behaviour.GetType().Name;
                    if (Array.IndexOf(DemoControlScriptNames, typeName) < 0)
                    {
                        continue;
                    }

                    // 走 Schedule 而不是直接删组件：演示角色是一整个 Prefab 实例，
                    // 只摘掉脚本会把那个对象留在场景里（连带它的网格与碰撞体）。
                    // Schedule 会在"实例根"这种情况下整体移除它。
                    Schedule(behaviour, typeName,
                        objectsToDestroy, componentsToDestroy,
                        removedObjects, removedComponents);
                }
            }

            foreach (var component in componentsToDestroy)
            {
                if (component != null)
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }

            foreach (var target in objectsToDestroy)
            {
                if (target != null)
                {
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }

            RemoveDemoPrefabInstances(scene, report);
            TameRealtimeReflectionProbes(scene, report);

            var missingInsidePrefabs = new List<string>();
            var missing = RemoveMissingScripts(scene, missingInsidePrefabs);

            report.AppendLine(removedObjects.Count == 0
                ? "    演示对象：无需移除。"
                : $"    移除演示对象 {removedObjects.Count} 个：{string.Join("、", removedObjects)}");
            report.AppendLine(removedComponents.Count == 0
                ? "    演示组件：无需移除。"
                : $"    移除演示组件 {removedComponents.Count} 个：{string.Join("、", removedComponents)}");
            report.AppendLine($"    清理 Missing Script {missing} 处。");
            if (missingInsidePrefabs.Count > 0)
            {
                report.AppendLine(
                    $"    [注意] 还有 {missingInsidePrefabs.Count} 处 Missing Script 在第三方 Prefab 实例内部，" +
                    $"改它们就要改只读的第三方资产，因此保留并上报：" +
                    $"{string.Join("、", missingInsidePrefabs.Distinct().Take(8))}");
            }
        }

        /// <summary>
        /// 排定一个演示组件的清理方式。
        ///
        /// Unity **不允许**销毁 Prefab 实例内部的 GameObject，而演示场景里的相机
        /// 恰好就藏在某个 Prefab 实例里。拆包 Prefab 不是选项：
        /// 那会把整条引用展开成内联数据，场景体积会爆。
        ///
        /// 移除**组件**却是允许的，Unity 把它记作实例的"已移除组件"覆写。
        /// 所以：Prefab 内部只摔组件，普通场景对象摔整个对象。
        /// 两种做法对"运行时不存在第二台相机"这个结论是等价的。
        /// </summary>
        private static void Schedule(
            Component component,
            string label,
            List<GameObject> objectsToDestroy,
            List<Component> componentsToDestroy,
            List<string> removedObjects,
            List<string> removedComponents)
        {
            if (component == null)
            {
                return;
            }

            var target = component.gameObject;

            // Prefab 实例的**最外层根**是可以整个删掉的（删一个实例是合法操作）；
            // 不允许删的是实例**内部**的子对象。先判断这一层，
            // 否则演示角色只会被停用而不是真的移除，资产闭环里还会留着它。
            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(target) as GameObject;
            if (outermost != null && outermost == target)
            {
                objectsToDestroy.Add(target);
                removedObjects.Add($"{target.name}（{label}，Prefab 实例根，整体移除）");
                return;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(target))
            {
                // 删组件在这里不管用：实测发现 Save-As 之后
                // m_RemovedComponents 全部是空的，移除没有被记下来，
                // 相机因此仍然跟着 Prefab 资产回来。
                // 停用对象和禁用组件都是**属性覆写**，这两样是能稳定序列化的。
                // 停用的相机不渲染、停用的 AudioListener 不工作，
                // "进场不会出现第二台相机"这个结论与删掉它等价。
                var behaviour = component as Behaviour;
                if (behaviour != null)
                {
                    behaviour.enabled = false;
                    PersistChange(behaviour);
                }

                target.SetActive(false);
                PersistChange(target.transform);
                removedComponents.Add($"{target.name}.{label}（Prefab 实例内，已停用）");
                return;
            }

            objectsToDestroy.Add(target);
            removedObjects.Add($"{target.name}（{label}）");
        }

        /// <summary>
        /// 清掉场景自有对象上的 Missing Script。
        ///
        /// Prefab 实例内部的 Missing Script 不在这里处理：那来自第三方 Prefab
        /// 资产本身，要改就得改第三方资产，而第三方资产是只读的。
        /// 因此这种情况只统计并报告，由人决定怎么办，不静默吞掉。
        /// </summary>
        /// <summary>
        /// 移除源自第三方演示目录的 Prefab 实例。
        ///
        /// 按**来源 Prefab 的路径**判定，而不是按脚本：演示角色的脚本一旦被摘掉，
        /// "有没有演示脚本"就再也认不出它了，而那个对象连着网格和碰撞体还留在场景里。
        /// 这正是第一轮漏掉 High Elves 的 AQM_FPS_Character 的原因。
        ///
        /// 规则是"住在演示目录里的 Prefab 就是演示内容"。High Elves 的
        /// `Demo Scenes/` 下只有这一个 Prefab，其余是地形层、光照贴图与反射探针，
        /// 都不是 Prefab，因此这条规则不会误伤环境。
        /// </summary>
        private static void RemoveDemoPrefabInstances(Scene scene, StringBuilder report)
        {
            var targets = new List<GameObject>();
            var names = new List<string>();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform == null)
                    {
                        continue;
                    }

                    var instanceRoot =
                        PrefabUtility.GetOutermostPrefabInstanceRoot(transform.gameObject)
                            as GameObject;
                    if (instanceRoot == null || instanceRoot != transform.gameObject)
                    {
                        continue;
                    }

                    var assetPath =
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
                    if (!IsDemoPrefabPath(assetPath))
                    {
                        continue;
                    }

                    if (!targets.Contains(instanceRoot))
                    {
                        targets.Add(instanceRoot);
                        names.Add($"{instanceRoot.name}（{assetPath}）");
                    }
                }
            }

            foreach (var target in targets)
            {
                if (target != null)
                {
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }

            report.AppendLine(targets.Count == 0
                ? "    演示 Prefab 实例：无需移除。"
                : $"    移除演示 Prefab 实例 {targets.Count} 个：{string.Join("、", names)}");
        }

        /// <summary>来源 Prefab 是否住在第三方素材包的演示目录里。</summary>
        private static bool IsDemoPrefabPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var normalized = assetPath.Replace('\\', '/');
            foreach (var root in P2SceneSetup.ThirdPartySourceRoots)
            {
                if (!normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (normalized.IndexOf("/Demo Scenes/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalized.IndexOf("/Demo/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 把演示场景的**逐帧实时反射探针**改成不自动刷新。
        ///
        /// 两个理由，哪个单独成立都够：
        ///
        /// 1. 开发规范要求清掉"不需要的高消耗实时灯光或后处理"。
        ///    演示场景为了展示水面把探针设成每帧重渲，而游戏的 GPU 预算是 14 ms，
        ///    每帧重渲一个立方贴图是已知的大开销。
        /// 2. 它会让无图形模式的 PlayMode 自动化**直接崩溃**：
        ///    Null Device 下 `TickRealtimeProbes` → `RenderOffscreenCameras` 会挂掉 Unity。
        ///    而本阶段必须能跑 PlayMode 场景流转测试。
        ///
        /// 代价要写清楚：水面反射因此不再实时更新。环境本身是静态的，
        /// 预期影响很小，但这属于视觉变更，已列进人工验收清单让用户定。
        /// </summary>
        private static void TameRealtimeReflectionProbes(Scene scene, StringBuilder report)
        {
            var tamed = new List<string>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var probe in root.GetComponentsInChildren<ReflectionProbe>(true))
                {
                    if (probe == null ||
                        probe.mode != UnityEngine.Rendering.ReflectionProbeMode.Realtime ||
                        probe.refreshMode == UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting)
                    {
                        continue;
                    }

                    probe.refreshMode =
                        UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
                    PersistChange(probe);
                    tamed.Add(probe.gameObject.name);
                }
            }

            report.AppendLine(tamed.Count == 0
                ? "    实时反射探针：无需处理。"
                : $"    {tamed.Count} 个逐帧实时反射探针改为不自动刷新：" +
                  $"{string.Join("、", tamed)}。");
        }

        /// <summary>
        /// 让一个脚本改动真的被写进场景。
        ///
        /// 这是本轮踩到的一个坑，值得写下来：在 Inspector 里改属性时
        /// Unity 会自动把它记成 Prefab 实例的覆写，但**用脚本改不会**。
        /// 不调 <c>RecordPrefabInstancePropertyModifications</c> 的话，改动只存在内存里，
        /// SaveScene 之后场景文件里一点痕迹都没有 ——
        /// 表现就是"工具报告说改了，重新打开又没改"。
        /// </summary>
        private static void PersistChange(Component component)
        {
            if (component == null)
            {
                return;
            }

            EditorUtility.SetDirty(component);
            if (PrefabUtility.IsPartOfPrefabInstance(component))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }

        private static int RemoveMissingScripts(Scene scene, List<string> insidePrefabs)
        {
            var removed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform == null)
                    {
                        continue;
                    }

                    var target = transform.gameObject;
                    if (!HasMissingScript(target))
                    {
                        continue;
                    }

                    if (PrefabUtility.IsPartOfPrefabInstance(target))
                    {
                        insidePrefabs.Add(target.name);
                        continue;
                    }

                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(target);
                }
            }

            return removed;
        }

        private static bool HasMissingScript(GameObject target)
        {
            var components = target.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 正式环境里不该再有灰盒地面。它只属于灰盒回退场景。
        /// </summary>
        private static void RemoveGrayboxEnvironment(Scene scene, StringBuilder report)
        {
            var removed = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "GrayboxGround")
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    removed++;
                }
            }

            if (removed > 0)
            {
                report.AppendLine($"    移除灰盒地面 {removed} 个：正式环境自带地形。");
            }
        }

        /// <summary>
        /// 把出生点、重生点与传送门落到真实地形表面上。
        ///
        /// 坐标不是写死的：取地形归一化位置再采样高度，因此换地形素材也成立，
        /// 并且满足"玩家不穿地、不悬空"。
        /// </summary>
        private static void GroundBusinessObjects(Scene scene, string mapId, StringBuilder report)
        {
            AuthoredPlacements.TryGetValue(mapId, out var authored);
            var placed = new List<string>();
            if (authored != null)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (!authored.TryGetValue(root.name, out var placement))
                    {
                        continue;
                    }

                    root.transform.position = placement.Position;
                    root.transform.rotation = Quaternion.Euler(placement.EulerAngles);
                    LiftMarkerOntoTheGround(root);
                    placed.Add(root.name);
                }

                report.AppendLine(placed.Count == 0
                    ? "    权威摆位：本场景没有匹配的对象。"
                    : $"    {placed.Count} 个对象按用户确认的绝对坐标摆位：" +
                      $"{string.Join("、", placed)}。");
            }

            var terrain = FindTerrain(scene);
            if (terrain == null)
            {
                report.AppendLine("    [警告] 场景里没有 Terrain，业务对象保持默认坐标。");
                return;
            }

            // 归一化锚点刻意靠近地形中心：演示地形的边缘常常是陡坡或水面。
            var anchors = new Dictionary<string, Vector2>(StringComparer.Ordinal)
            {
                { "SpawnPoint_Entry", new Vector2(0.50f, 0.50f) },
                { "SpawnPoint_Respawn", new Vector2(0.47f, 0.50f) },
                { "Portal_To_Map02", new Vector2(0.55f, 0.53f) },
                { P22CombatSetup.SpawnerName, new Vector2(0.54f, 0.50f) },
                { P22CombatSetup.TrainingTargetName, new Vector2(0.46f, 0.53f) }
            };

            var grounded = 0;
            var kept = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!anchors.TryGetValue(root.name, out var normalized))
                {
                    continue;
                }

                // 权威摆位已经定过的对象不再采样地形。
                if (placed.Contains(root.name))
                {
                    continue;
                }

                // 只落那些还停在装配工具默认位置（世界原点附近）的对象。
                // 一旦落到 1000×1000 地形中心，它就离原点很远，之后每次执行都会被跳过 ——
                // 否则重复执行会把用户在 Unity 里手工调过的出生点摆回去。
                if (root.transform.position.sqrMagnitude > (DefaultPositionRadius * DefaultPositionRadius))
                {
                    kept++;
                    continue;
                }

                root.transform.position = SurfacePoint(terrain, normalized);
                grounded++;
            }

            report.AppendLine(
                $"    {grounded} 个业务对象已落到地形表面（Terrain {terrain.name}，" +
                $"尺寸 {terrain.terrainData.size.x:F0}×{terrain.terrainData.size.z:F0}），" +
                $"{kept} 个保持已有位置。");
        }

        /// <summary>
        /// 把灰盒可视化面片抬到地面之上。
        ///
        /// 传送门的标识方块是以父级原点为中心的，高 2.4，
        /// 而父级原点就在地表上 —— 所以它有一半埋在地下，
        /// 远一点看就更难找。把它沿本地 Y 抬半个身位，整块就站在地面上。
        /// 这只是开发期的可视化，不是触发器：触发体在父级上，位置不变。
        /// </summary>
        private static void LiftMarkerOntoTheGround(GameObject portalRoot)
        {
            var marker = portalRoot.transform.Find("PortalMarker");
            if (marker == null)
            {
                return;
            }

            var half = marker.localScale.y * 0.5f;
            var local = marker.localPosition;
            if (Mathf.Approximately(local.y, half))
            {
                return;
            }

            marker.localPosition = new Vector3(local.x, half, local.z);
        }

        private static Vector3 SurfacePoint(Terrain terrain, Vector2 normalized)
        {
            var size = terrain.terrainData.size;
            var origin = terrain.transform.position;
            var world = new Vector3(
                origin.x + (size.x * normalized.x),
                0f,
                origin.z + (size.z * normalized.y));

            // SampleHeight 返回相对地形原点的高度，所以要把原点的 y 加回去。
            world.y = origin.y + terrain.SampleHeight(world) + 0.1f;
            return world;
        }

        private static Terrain FindTerrain(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var terrain = root.GetComponentInChildren<Terrain>(true);
                if (terrain != null)
                {
                    return terrain;
                }
            }

            return null;
        }

        /// <summary>
        /// 目标场景是否已经派生过。用文本扫描而不是打开场景：
        /// 打开一个 38 MB 的场景只为了问一个是非题太贵了。
        /// </summary>
        private static bool TargetHasEnvironment(string targetScenePath)
        {
            if (!File.Exists(targetScenePath))
            {
                return false;
            }

            foreach (var line in File.ReadLines(targetScenePath))
            {
                if (line.EndsWith("m_Name: " + EnvironmentRootName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 战斗场景的最后一道：把战斗对象落到地形上、把开发用对象关进 DevOnly
        /// 并停用，然后在真实地形上烘一次 NavMesh。
        ///
        /// NavMesh 必须建在正式战斗地形上：灰盒地板上的寻路永远成功，
        /// "路径被地形阻断之后怪物怎么办"这类缺陷在灰盒上不可能复现。
        ///
        /// 只把地形标为导航静止，不标植被：Unity 2021.3 自带的场景烘焙按
        /// NavigationStatic 标记取范围，把一万多株草木都算进去既慢又没意义 ——
        /// 狼并不应该被一丛草挡住。
        /// </summary>
        private static void FinalizeCombatScene(StringBuilder report)
        {
            var scene = EditorSceneManager.OpenScene(
                P2SceneSetup.Map02ScenePath, OpenSceneMode.Single);

            GroundBusinessObjects(scene, WorldMapIds.Map02, report);
            EnsureDevOnlyRoot(scene, report);

            var terrain = FindTerrain(scene);
            if (terrain != null)
            {
                GameObjectUtility.SetStaticEditorFlags(
                    terrain.gameObject, StaticEditorFlags.NavigationStatic);
            }

            // NavMesh 是烘焙产物，不是每次跑工具都要重做的事。
            // 整张 1000×1000 地形烘一次要 4 分钟，把它放进幂等路径会让
            // 每次装配和每次跑测试都多等 4 分钟，而结果是一样的。
            // 重烘是显式操作：Rebuild 菜单项，或者删掉这份资产。
            if (File.Exists(CombatNavMeshAssetPath))
            {
                report.AppendLine(
                    $"    NavMesh 已存在，跳过烘焙（{CombatNavMeshAssetPath}）。" +
                    "需要重烘时删掉它或使用 Rebuild 菜单。");
            }
            else
            {
                var started = DateTime.UtcNow;
                UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
                // 场景已经打开，用同步的场景烘焙就行。
                UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
                var elapsed = (DateTime.UtcNow - started).TotalSeconds;
                report.AppendLine(
                    $"    NavMesh 已在正式战斗地形上烘焙（耗时 {elapsed:F1} 秒）。");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, P2SceneSetup.Map02ScenePath);
        }

        /// <summary>
        /// 训练假人这类开发用对象集中到一个明确的 DevOnly 节点下并默认停用，
        /// 因此正式流程不会跳出三个灰色假人，而它们又随时能被打开排查。
        /// </summary>
        private static void EnsureDevOnlyRoot(Scene scene, StringBuilder report)
        {
            var devOnlyNames = new[]
            {
                P22CombatSetup.TrainingTargetName,
                "TrainingDummies"
            };

            GameObject devRoot = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == DevOnlyRootName)
                {
                    devRoot = root;
                    break;
                }
            }

            // 先去重。装配工具曾经只找根对象，因此把训练靶收进 DevOnly 之后
            // 它又在根上建了第二个。查找已经改成全场景搜索，
            // 但已经被造出来的重复对象还得清掉一次。
            var duplicates = 0;
            foreach (var name in devOnlyNames)
            {
                var found = new List<GameObject>();
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (transform != null && transform.name == name)
                        {
                            found.Add(transform.gameObject);
                        }
                    }
                }

                // 保留已经在 DevOnly 下的那个，其余删掉。
                var keep = found.FirstOrDefault(
                    g => g.transform.parent != null && g.transform.parent.name == DevOnlyRootName)
                           ?? found.FirstOrDefault();
                foreach (var extra in found)
                {
                    if (extra != keep)
                    {
                        UnityEngine.Object.DestroyImmediate(extra);
                        duplicates++;
                    }
                }
            }

            if (duplicates > 0)
            {
                report.AppendLine($"    清掉重复的开发用对象 {duplicates} 个。");
            }

            var moved = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (Array.IndexOf(devOnlyNames, root.name) < 0 || root.name == DevOnlyRootName)
                {
                    continue;
                }

                if (devRoot == null)
                {
                    devRoot = new GameObject(DevOnlyRootName);
                }

                root.transform.SetParent(devRoot.transform, worldPositionStays: true);
                moved++;
            }

            if (devRoot == null)
            {
                return;
            }

            // 停用整个节点：停用对象不进渲染、不跑 Update、也不被
            // FindObjectOfType 找到，因此它对正式流程是透明的。
            devRoot.SetActive(false);
            report.AppendLine(
                $"    {moved} 个开发用对象已关进 {DevOnlyRootName}（默认停用）。");
        }

        /// <summary>源文件指纹。只读字节数与写入时间，不读内容：38 MB 哈希一次太慢。</summary>
        private static string FileFingerprint(string path)
        {
            var info = new FileInfo(path);
            return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
    }
}
