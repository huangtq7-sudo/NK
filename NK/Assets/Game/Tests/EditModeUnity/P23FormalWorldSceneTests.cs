using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Naraka.Boot;
using Naraka.Core.Application.Scenes;
using Naraka.EditorTools;
using Naraka.Features.Monster.View;
using Naraka.Features.World.View;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Naraka.Unity.EditMode.Tests
{
    /// <summary>
    /// P2.3 正式运行场景的结构契约。
    ///
    /// 这些断言存在的理由跟正式狼那一轮一样：接第三方环境时，
    /// "场景能打开"离"场景是对的"差得很远 —— 演示相机跟着进来、两个 AudioListener、
    /// 两盏方向光、Missing Script、URP 粉色材质、业务流程其实还指着灰盒场景。
    /// 这些在 Inspector 里翻不出来，只能靠测试守。
    ///
    /// 人工验收负责观感、移动、镜头、传送与战斗；结构归这里。
    ///
    /// 实现上有一条必须守住的纪律：正式战斗场景有 38.9 MB，打开一次很贵。
    /// 因此两个场景各只打开**一次**，在 <see cref="CaptureScenes"/> 里拍成快照，
    /// 每条测试断言快照而不是重新打开场景。
    /// </summary>
    public sealed class P23FormalWorldSceneTests
    {
        private static readonly string[] FormalScenePaths =
        {
            P2SceneSetup.Map01ScenePath,
            P2SceneSetup.Map02ScenePath
        };

        private Dictionary<string, SceneSnapshot> _snapshots;

        /// <summary>一个正式场景的结构快照。打开一次，之后所有断言都看它。</summary>
        private sealed class SceneSnapshot
        {
            public string Path;
            public int WorldScopes;
            public int WorldEntries;
            public string EntryMapId;
            public int LiveCameras;
            public int LiveAudioListeners;
            public int DirectionalLights;
            public int Terrains;
            public int EntrySpawns;
            public int RespawnSpawns;
            public bool HasEnvironmentRoot;
            public bool HasGrayboxGround;
            public int Spawners;
            public int AutoRefreshingRealtimeProbes;
            // 只存**值**，不存 Transform 引用：打开下一个场景会把上一个场景的
            // 对象全部销毁，等测试读到时引用已经是死的。
            public bool HasEntrySpawn;
            public Vector3 EntrySpawnPosition;
            public float EntrySpawnYaw;
            public bool HasRespawnSpawn;
            public Vector3 RespawnPosition;
            public float RespawnYaw;
            public bool HasPortal;
            public Vector3 PortalPosition;
            public bool HasPortalMarker;
            public float PortalMarkerLocalY;
            public float PortalMarkerHeight;
            public string SpawnerPrefabPath;
            public readonly List<string> MissingScripts = new List<string>();
            public readonly List<string> MaterialOffenders = new List<string>();
        }

        [OneTimeSetUp]
        public void CaptureScenes()
        {
            _snapshots = new Dictionary<string, SceneSnapshot>(StringComparer.Ordinal);
            foreach (var path in FormalScenePaths)
            {
                Assert.That(
                    File.Exists(path), Is.True,
                    $"正式运行场景不存在：{path}。" +
                    "先执行 NARAKA/Setup/Apply P2.3 Formal World Scenes。");
                _snapshots[path] = Capture(
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single), path);
            }
        }

        private static SceneSnapshot Capture(Scene scene, string path)
        {
            var snapshot = new SceneSnapshot { Path = path };
            var roots = scene.GetRootGameObjects();

            snapshot.HasEnvironmentRoot =
                roots.Any(g => g.name == P23WorldSceneSetup.EnvironmentRootName);
            snapshot.HasGrayboxGround = roots.Any(g => g.name == "GrayboxGround");

            snapshot.WorldScopes = CountOf<WorldSceneLifetimeScope>(roots);
            // 只数**会真的生效**的。演示相机藏在 Prefab 实例内部，
            // 而 Unity 不允许销毁 Prefab 实例内部的 GameObject，删组件也并未被
            // 序列化下来（实测 m_RemovedComponents 全空）。因此工具改成停用它们，
            // 而这正好就是要求本身：停用的相机不渲染、停用的监听器不工作。
            snapshot.LiveCameras = All<Camera>(roots).Count(IsLive);
            snapshot.LiveAudioListeners = All<AudioListener>(roots).Count(IsLive);
            snapshot.Terrains = CountOf<Terrain>(roots);

            snapshot.DirectionalLights = All<Light>(roots)
                .Count(l => l.type == LightType.Directional);

            var entries = All<WorldSceneEntry>(roots).ToList();
            snapshot.WorldEntries = entries.Count;
            snapshot.EntryMapId = entries.Count > 0 ? entries[0].MapId : null;

            var spawnPoints = All<PlayerSpawnPoint>(roots).ToList();
            snapshot.EntrySpawns =
                spawnPoints.Count(p => p.Kind == PlayerSpawnPoint.SpawnKind.Entry);
            snapshot.RespawnSpawns =
                spawnPoints.Count(p => p.Kind == PlayerSpawnPoint.SpawnKind.Respawn);

            var entry = spawnPoints
                .FirstOrDefault(sp => sp.Kind == PlayerSpawnPoint.SpawnKind.Entry);
            if (entry != null)
            {
                snapshot.HasEntrySpawn = true;
                snapshot.EntrySpawnPosition = entry.transform.position;
                snapshot.EntrySpawnYaw = entry.transform.rotation.eulerAngles.y;
            }

            var respawn = spawnPoints
                .FirstOrDefault(sp => sp.Kind == PlayerSpawnPoint.SpawnKind.Respawn);
            if (respawn != null)
            {
                snapshot.HasRespawnSpawn = true;
                snapshot.RespawnPosition = respawn.transform.position;
                snapshot.RespawnYaw = respawn.transform.rotation.eulerAngles.y;
            }

            var portal = roots.FirstOrDefault(g => g.name == "Portal_To_Map02");
            if (portal != null)
            {
                snapshot.HasPortal = true;
                snapshot.PortalPosition = portal.transform.position;
                var portalMarker = portal.transform.Find("PortalMarker");
                if (portalMarker != null)
                {
                    snapshot.HasPortalMarker = true;
                    snapshot.PortalMarkerLocalY = portalMarker.localPosition.y;
                    snapshot.PortalMarkerHeight = portalMarker.localScale.y;
                }
            }

            var spawners = All<MonsterSpawner>(roots).ToList();
            snapshot.Spawners = spawners.Count;
            if (spawners.Count > 0)
            {
                var prefab = GetSpawnerPrefab(spawners[0]);
                snapshot.SpawnerPrefabPath =
                    prefab == null ? null : AssetDatabase.GetAssetPath(prefab);
            }

            snapshot.AutoRefreshingRealtimeProbes = All<ReflectionProbe>(roots).Count(
                probe => probe.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime &&
                         probe.refreshMode !=
                         UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting);

            CollectMissingScripts(roots, snapshot.MissingScripts);
            CollectMaterialOffenders(roots, snapshot.MaterialOffenders);
            return snapshot;
        }

        // ------------------------------------------------------------ MapId 与场景名分离

        [Test]
        public void StableMapIdsAreNotUnitySceneNames()
        {
            // 这是整条约束的根：业务 ID 一旦等于场景文件名，
            // 换美术素材就会改变未来远征记录与数据库里的地图身份。
            foreach (var mapId in WorldSceneCatalog.Default.MapIds)
            {
                Assert.That(
                    mapId, Is.Not.EqualTo(WorldSceneCatalog.Default.ResolveSceneName(mapId)),
                    $"业务 MapId {mapId} 与 Unity 场景名相同，两者必须分离。");
            }

            Assert.That(WorldMapIds.Map01, Is.EqualTo("Map01"));
            Assert.That(WorldMapIds.Map02, Is.EqualTo("Map02"));
        }

        [Test]
        public void Map01ResolvesToTheFormalTaskScene()
        {
            Assert.That(
                WorldSceneCatalog.Default.ResolveSceneName(WorldMapIds.Map01),
                Is.EqualTo(WorldSceneNames.Map01Task));
            Assert.That(
                Path.GetFileNameWithoutExtension(P2SceneSetup.Map01ScenePath),
                Is.EqualTo(WorldSceneNames.Map01Task),
                "场景常量与目录登记的场景名必须一致。");
        }

        [Test]
        public void Map02ResolvesToTheFormalCombatScene()
        {
            Assert.That(
                WorldSceneCatalog.Default.ResolveSceneName(WorldMapIds.Map02),
                Is.EqualTo(WorldSceneNames.Map02Combat));
            Assert.That(
                Path.GetFileNameWithoutExtension(P2SceneSetup.Map02ScenePath),
                Is.EqualTo(WorldSceneNames.Map02Combat));
        }

        [Test]
        public void AnUnknownMapIdIsRejectedRatherThanSilentlyIgnored()
        {
            // 未登记的 MapId 必须抛，而不是返回空串：
            // 拿一个空场景名去加载只会在更远的地方炸，那时候已经看不出根因了。
            Assert.Throws<ArgumentException>(
                () => WorldSceneCatalog.Default.ResolveSceneName("Map99"));
            Assert.That(
                WorldSceneCatalog.Default.TryResolveSceneName("Map99", out _), Is.False);
        }

        [Test]
        public void NoFormalFlowCanStillReachTheGrayboxCombatScene()
        {
            // 1) 目录解析不出灰盒场景，因此 WorldFlowController 无论传什么 MapId 都到不了它。
            foreach (var mapId in WorldSceneCatalog.Default.MapIds)
            {
                Assert.That(
                    WorldSceneCatalog.Default.ResolveSceneName(mapId),
                    Is.Not.EqualTo(WorldSceneNames.Map02CombatGrayboxFallback),
                    "灰盒战斗场景不能是任何业务 MapId 的解析结果。");
            }

            Assert.That(
                WorldSceneCatalog.Default.IsRuntimeScene(
                    WorldSceneNames.Map02CombatGrayboxFallback),
                Is.False);

            // 2) 它在构建列表里必须是禁用状态：禁用后不计入
            //    SceneManager.sceneCountInBuildSettings，而 UnitySceneLoader 会先查这张表，
            //    因此运行时根本找不到它。这比"约定不要用"硬得多。
            var graybox = EditorBuildSettings.scenes.FirstOrDefault(
                s => string.Equals(s.path, P2SceneSetup.Map02GrayboxScenePath,
                    StringComparison.OrdinalIgnoreCase));
            if (graybox != null)
            {
                Assert.That(
                    graybox.enabled, Is.False,
                    "灰盒战斗场景只能作为开发回退资产保留，不得在构建列表里启用。");
            }
        }

        [Test]
        public void ThirdPartyDemoScenesNeverEnterBuildSettings()
        {
            var offenders = EditorBuildSettings.scenes
                .Where(s => P2SceneSetup.IsThirdPartyDemoScene(s.path))
                .Select(s => s.path)
                .ToArray();

            Assert.That(
                offenders, Is.Empty,
                "第三方 Demo 源场景进入了构建列表：" + string.Join("、", offenders));
        }

        [Test]
        public void TheBootSceneIsStillTheFirstEntryAndBothFormalScenesAreEnabled()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.That(scenes.Length, Is.GreaterThan(0));
            Assert.That(
                Path.GetFileNameWithoutExtension(scenes[0].path),
                Is.EqualTo(WorldSceneNames.Boot),
                "Bootstrap 必须是第一项，否则启动进的是地图而不是登录。");
            Assert.That(scenes[0].enabled, Is.True);

            foreach (var path in FormalScenePaths)
            {
                var entry = scenes.FirstOrDefault(
                    s => string.Equals(s.path, path, StringComparison.OrdinalIgnoreCase));
                Assert.That(entry, Is.Not.Null, $"{path} 不在构建列表里。");
                Assert.That(entry.enabled, Is.True, $"{path} 在构建列表里被禁用了。");
            }
        }

        // ------------------------------------------------------------ 场景结构

        [Test]
        public void BothFormalScenesCarryTheRequiredWorldComponents()
        {
            foreach (var snapshot in Snapshots())
            {
                Assert.That(
                    snapshot.WorldScopes, Is.EqualTo(1),
                    $"{snapshot.Path} 必须且只能有一个 WorldSceneLifetimeScope。");
                Assert.That(
                    snapshot.WorldEntries, Is.EqualTo(1),
                    $"{snapshot.Path} 必须且只能有一个 WorldSceneEntry。");
                Assert.That(
                    snapshot.EntryMapId,
                    Is.EqualTo(WorldMapIds.Map01).Or.EqualTo(WorldMapIds.Map02),
                    $"{snapshot.Path} 的 WorldSceneEntry 必须持有稳定业务 MapId，不是场景名。");
            }
        }

        [Test]
        public void BothFormalScenesUseTheRealEnvironmentInsteadOfGraybox()
        {
            foreach (var snapshot in Snapshots())
            {
                Assert.That(
                    snapshot.HasEnvironmentRoot, Is.True,
                    $"{snapshot.Path} 缺少 {P23WorldSceneSetup.EnvironmentRootName} 根节点，" +
                    "正式环境尚未派生。");
                Assert.That(
                    snapshot.HasGrayboxGround, Is.False,
                    $"{snapshot.Path} 仍然带着灰盒地面。");
                Assert.That(
                    snapshot.Terrains, Is.GreaterThan(0),
                    $"{snapshot.Path} 的正式环境里没有地形，玩家会掉下去。");
            }
        }

        [Test]
        public void BothFormalScenesHaveExactlyOnePlayerEntrySpawn()
        {
            foreach (var snapshot in Snapshots())
            {
                Assert.That(
                    snapshot.EntrySpawns, Is.EqualTo(1),
                    $"{snapshot.Path} 的入口出生点数量是 {snapshot.EntrySpawns}，" +
                    "必须恰好一个，否则玩家可能生成在两个地方。");
            }
        }

        [Test]
        public void TheTaskSceneHasADeathRespawnPoint()
        {
            Assert.That(
                _snapshots[P2SceneSetup.Map01ScenePath].RespawnSpawns, Is.EqualTo(1),
                "死亡返回地图一必须有且只有一个重生点。");
        }

        [Test]
        public void NeitherFormalSceneSavesADuplicateCameraOrAudioListener()
        {
            // 相机与 AudioListener 都归持久化 App Root 所有。
            // 演示场景各自带了一套，派生时必须剥掉，否则进场就是两台相机两个监听器。
            foreach (var snapshot in Snapshots())
            {
                Assert.That(
                    snapshot.LiveCameras, Is.Zero,
                    $"{snapshot.Path} 里有 {snapshot.LiveCameras} 台生效的相机；" +
                    "相机只能来自持久化 App Root。");
                Assert.That(
                    snapshot.LiveAudioListeners, Is.Zero,
                    $"{snapshot.Path} 里有 {snapshot.LiveAudioListeners} 个生效的 AudioListener；" +
                    "同一时刻只允许存在一个。");
            }
        }

        [Test]
        public void NeitherFormalSceneHasMoreThanOneDirectionalLight()
        {
            foreach (var snapshot in Snapshots())
            {
                Assert.That(
                    snapshot.DirectionalLights, Is.EqualTo(1),
                    $"{snapshot.Path} 有 {snapshot.DirectionalLights} 盏方向光，必须恰好一盏。");
            }
        }

        [Test]
        public void TheFormalCombatSceneSpawnsTheOfficialWolfNotTheGraybox()
        {
            var snapshot = _snapshots[P2SceneSetup.Map02ScenePath];
            Assert.That(snapshot.Spawners, Is.EqualTo(1), "正式战斗场景必须且只能有一个怪物生成器。");
            Assert.That(
                snapshot.SpawnerPrefabPath,
                Is.EqualTo(DuskshadowWolfAssets.PrefabPath),
                "正式战斗场景必须引用正式暮影妖狼 Prefab，而不是方块替身。");
        }

        [Test]
        public void TheFormalCombatSceneHasABakedNavMeshOnTheRealTerrain()
        {
            // 灰盒地板上的寻路永远成功，因此"路径被地形阻断"这类缺陷在灰盒上不可能复现。
            Assert.That(
                File.Exists(P23WorldSceneSetup.CombatNavMeshAssetPath), Is.True,
                $"正式战斗场景缺少烘焙 NavMesh：{P23WorldSceneSetup.CombatNavMeshAssetPath}");
            Assert.That(
                new FileInfo(P23WorldSceneSetup.CombatNavMeshAssetPath).Length,
                Is.GreaterThan(1024),
                "NavMesh 资产太小，看起来是一次空烘焙。");
        }

        [Test]
        public void TheTaskSceneUsesTheAuthoredSpawnAndPortalCoordinates()
        {
            // 这些坐标是用户在 Unity 里实际看过画面之后定下来的，
            // 不是地形采样猜的。装配工具现在是它们的主，
            // 因此必须有条断言盯着 —— 否则下次改工具时很容易把它们碰掉。
            var snapshot = _snapshots[P2SceneSetup.Map01ScenePath];

            Assert.That(snapshot.HasEntrySpawn, Is.True, "任务场景缺入口出生点。");
            Assert.That(snapshot.HasRespawnSpawn, Is.True, "任务场景缺死亡重生点。");
            Assert.That(snapshot.HasPortal, Is.True, "任务场景缺传送门。");

            AssertPosition(snapshot.EntrySpawnPosition, new Vector3(510f, 35f, 587f), "入口出生点");
            AssertPosition(snapshot.RespawnPosition, new Vector3(510f, 35f, 587f), "死亡重生点");
            AssertPosition(
                snapshot.PortalPosition, new Vector3(385.1f, 36.1f, 602.5f), "传送门");

            // 角色必须朝向传送门。y = -90 指向 -X，而传送门相对出生点
            // 是 Δx = -124.9、Δz = +15.5，主方向正是 -X。
            Assert.That(
                Mathf.DeltaAngle(snapshot.EntrySpawnYaw, -90f),
                Is.EqualTo(0f).Within(0.5f),
                "入口出生点的朝向必须是 y = -90。");
            Assert.That(
                Mathf.DeltaAngle(snapshot.RespawnYaw, -90f),
                Is.EqualTo(0f).Within(0.5f),
                "死亡重生点的朝向必须是 y = -90。");

            // 灰盒标识方块不得半埋在地下，否则主要的"看不到传送点"就没改掉。
            if (snapshot.HasPortalMarker)
            {
                Assert.That(
                    snapshot.PortalMarkerLocalY,
                    Is.EqualTo(snapshot.PortalMarkerHeight * 0.5f).Within(0.01f),
                    "传送门标识方块必须站在地面上，而不是以原点为中心半埋着。");
            }
        }

        private static void AssertPosition(Vector3 actual, Vector3 expected, string label)
        {
            Assert.That(
                Vector3.Distance(actual, expected), Is.LessThan(0.01f),
                $"{label}的位置是 {actual}，应为 {expected}。");
        }

        [Test]
        public void NoFormalSceneAutoRefreshesARealtimeReflectionProbe()
        {
            // 演示场景为了展示水面把反射探针设成每帧重渲。
            // 一是 GPU 预算只有 14 ms，二是无图形模式下它会直接崩溃 Unity
            // （TickRealtimeProbes → RenderOffscreenCameras），而本阶段必须能跑
            // PlayMode 场景流转测试。这条断言同时是性能护栏和自动化护栏。
            foreach (var snapshot in Snapshots())
            {
                Assert.That(
                    snapshot.AutoRefreshingRealtimeProbes, Is.Zero,
                    $"{snapshot.Path} 里有 {snapshot.AutoRefreshingRealtimeProbes} " +
                    "个会自动刷新的实时反射探针。");
            }
        }

        [Test]
        public void NoFormalSceneContainsAMissingScript()
        {
            foreach (var snapshot in Snapshots())
            {
                Assert.That(
                    snapshot.MissingScripts, Is.Empty,
                    $"{snapshot.Path} 存在 Missing Script：" +
                    string.Join("、", snapshot.MissingScripts.Take(10)));
            }
        }

        [Test]
        public void TheFormalEnvironmentHasNoBrokenOrPinkMaterials()
        {
            // URP 下最常见的"看起来导进来了其实是坏的"就是粉色：
            // 材质丢失，或者 shader 是内建管线的，URP 渲染不出来。
            foreach (var snapshot in Snapshots())
            {
                Assert.That(
                    snapshot.MaterialOffenders, Is.Empty,
                    $"{snapshot.Path} 存在坏材质或粉色材质：" +
                    string.Join("、", snapshot.MaterialOffenders.Take(12)));
            }
        }

        [Test]
        public void TheDerivationToolIsIdempotent()
        {
            // 重复执行不得产生第二套摄像机、玩家、灯光、出生点、传送门或怪物。
            // 比较文件哈希而不是字节数组：这两个场景一共 41 MB，
            // 让断言框架去逐元素比较四千万个字节既慢又没必要。
            var before = FormalScenePaths.ToDictionary(p => p, Sha256);

            P23WorldSceneSetup.Apply();
            AssetDatabase.Refresh();

            foreach (var path in FormalScenePaths)
            {
                Assert.That(
                    Sha256(path), Is.EqualTo(before[path]),
                    $"再执行一次装配后 {path} 发生了变化，说明工具不是幂等的。");
            }
        }

        // ------------------------------------------------------------ 辅助

        private IEnumerable<SceneSnapshot> Snapshots() =>
            FormalScenePaths.Select(p => _snapshots[p]);

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream));
            }
        }

        /// <summary>组件是否真的会在运行时生效（对象激活且组件未被禁用）。</summary>
        private static bool IsLive(Component component)
        {
            if (component == null || !component.gameObject.activeInHierarchy)
            {
                return false;
            }

            var behaviour = component as Behaviour;
            return behaviour == null || behaviour.enabled;
        }

        private static IEnumerable<T> All<T>(GameObject[] roots) where T : Component =>
            roots.SelectMany(r => r.GetComponentsInChildren<T>(true)).Where(c => c != null);

        private static int CountOf<T>(GameObject[] roots) where T : Component =>
            All<T>(roots).Count();

        private static void CollectMissingScripts(GameObject[] roots, List<string> into)
        {
            foreach (var root in roots)
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform == null)
                    {
                        continue;
                    }

                    var components = transform.GetComponents<Component>();
                    for (var i = 0; i < components.Length; i++)
                    {
                        if (components[i] == null)
                        {
                            into.Add(GetPath(transform));
                            break;
                        }
                    }
                }
            }
        }

        private static void CollectMaterialOffenders(GameObject[] roots, List<string> into)
        {
            foreach (var renderer in All<Renderer>(roots))
            {
                var materials = renderer.sharedMaterials;

                // 只检查**真的会被画**的槽位。PureNature 的 Fx 与瀑布对象出厂就带着
                // 比子网格数更多的材质槽，多出来的那个是空的。
                // 那是第三方资产的原样，渲染时被忽略，不会变粉色，
                // 也不是本轮派生造成的。把它们当缺陷报出来只会淡化真正的问题。
                var usedSlots = UsedMaterialSlots(renderer, materials.Length);

                for (var i = 0; i < usedSlots; i++)
                {
                    var material = materials[i];
                    if (material == null)
                    {
                        into.Add($"{GetPath(renderer.transform)}[{i}] 材质为空");
                        continue;
                    }

                    if (material.shader == null)
                    {
                        into.Add($"{material.name} 的 shader 为空");
                        continue;
                    }

                    var shaderName = material.shader.name;
                    if (shaderName.Contains("InternalErrorShader"))
                    {
                        into.Add($"{material.name} 使用了错误 shader");
                        continue;
                    }

                    // 内建管线的 Standard 在 URP 下就是粉的。
                    if (shaderName == "Standard" || shaderName == "Standard (Specular setup)")
                    {
                        into.Add($"{material.name} 仍然是内建 Standard");
                    }
                }
            }
        }

        /// <summary>
        /// 这个 Renderer 有多少个材质槽是**真的会被画**的。
        ///
        /// 空材质并不一定是缺陷，有两种合法情况必须排除，
        /// 否则第三方素材的正常形态会淡化真正的粉色问题：
        ///
        /// - 粒子系统渲染器的第二个槽是**拖尾材质**，不开拖尾时本来就是空的。
        ///   PureNature 的 Snow、Butterflies、Eyes、TreeLeaves 全是这种。
        /// - 网格渲染器的材质槽可以比子网格数多，多出来的渲染时直接忽略。
        /// </summary>
        private static int UsedMaterialSlots(Renderer renderer, int slotCount)
        {
            if (renderer is ParticleSystemRenderer)
            {
                return Math.Min(slotCount, 1);
            }

            Mesh mesh = null;
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                mesh = skinned.sharedMesh;
            }
            else if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                mesh = filter == null ? null : filter.sharedMesh;
            }

            return mesh == null ? slotCount : Math.Min(slotCount, mesh.subMeshCount);
        }

        private static GameObject GetSpawnerPrefab(MonsterSpawner spawner)
        {
            var field = typeof(MonsterSpawner).GetField(
                "monsterPrefab",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "MonsterSpawner 的 monsterPrefab 字段改名了。");
            return field.GetValue(spawner) as GameObject;
        }

        private static string GetPath(Transform transform)
        {
            var parts = new List<string>();
            var current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
