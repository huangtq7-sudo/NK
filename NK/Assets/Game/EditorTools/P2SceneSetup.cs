using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cinemachine;
using Naraka.Boot;
using Naraka.Features.Account.View;
using Naraka.Features.Character.View;
using Naraka.Features.Combat.Model;
using Naraka.Features.Combat.View;
using Naraka.Features.Loading.View;
using Naraka.Features.World.Controller;
using Naraka.Features.World.View;
using Naraka.Infrastructure.Camera;
using Naraka.Infrastructure.Input;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Naraka.EditorTools
{
    /// <summary>
    /// P2 场景与预制体装配。
    ///
    /// 幂等性是硬要求：这个工具只补齐缺失的对象、组件与引用，
    /// 已经存在的对象绝不重建，已有的 Transform 绝不重新摆位。
    /// 因此用户在 Unity 里手工调过的出生点、传送门与灰盒摆放不会被覆盖。
    ///
    /// 第三方源场景（Aquarius 与 Pure Nature）只读：本工具从不打开、修改或保存它们。
    /// </summary>
    public static class P2SceneSetup
    {
        public const string BootScenePath = "Assets/Scenes/SampleScene.unity";
        public const string Map01ScenePath = "Assets/Game/Scenes/World/Map01_Task.unity";
        public const string Map02ScenePath = "Assets/Game/Scenes/World/Map02_CombatGraybox.unity";

        private const string SettingsDirectory = "Assets/Game/Settings";
        private const string PlayerSettingsDirectory = SettingsDirectory + "/Player";
        private const string InputAssetPath = SettingsDirectory + "/Input/NarakaPlayerControls.inputactions";
        public const string PlayerTuningPath = PlayerSettingsDirectory + "/PlayerTuning.asset";
        private const string CameraSettingsPath = PlayerSettingsDirectory + "/ThirdPersonCameraSettings.asset";
        private const string PlayerPrefabPath = PlayerSettingsDirectory + "/Changli_Player.prefab";
        private const string GraySurfaceMaterialPath = PlayerSettingsDirectory + "/GrayboxSurface.mat";
        private const string GrayDummyMaterialPath = PlayerSettingsDirectory + "/GrayboxDummy.mat";

        private const string AppRootName = "NarakaAppRoot";
        private const string LoadingUiName = "LoadingUI";
        private const string InputName = "PlayerInput";
        private const string CameraRigName = "CameraRig";
        private const string CameraPivotName = "CameraPivot";
        private const string VirtualCameraName = "ThirdPersonCamera";
        private const string WorldScopeName = "WorldSceneLifetimeScope";
        private const string WorldEntryName = "WorldSceneEntry";

        public const string PlayerLayerName = "Player";
        public const string EnemyLayerName = "Enemy";

        [MenuItem("NARAKA/Setup/Apply P2 Scene Setup")]
        public static void Apply()
        {
            var report = new StringBuilder();
            report.AppendLine("NARAKA P2 场景装配：");

            EnsureLayers(report);
            P2AnimationSetup.ApplyImportSettings(report);
            P2AnimationSetup.ResetUnusedClipSettings(report);
            var tuning = EnsureAsset<PlayerTuningAsset>(PlayerTuningPath, report);
            // 动作时长必须来自真实动画片段，不能是估计值：所有比配置长的动画都会被拦腰截断。
            P2AnimationSetup.SyncTuningFromClips(PlayerTuningPath, report);
            var controller = P2AnimationSetup.BuildController(report);
            var cameraSettings = EnsureAsset<ThirdPersonCameraSettings>(CameraSettingsPath, report);
            var playerPrefab = EnsurePlayerPrefab(controller, report);

            EnsureBootScene(tuning, cameraSettings, report);
            EnsureWorldScene(Map01ScenePath, WorldMapIds.Map01Task, playerPrefab, true, report);
            EnsureWorldScene(Map02ScenePath, WorldMapIds.Map02CombatGraybox, playerPrefab, false, report);
            EnsureBuildSettings(report);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// 只新增 Player 与 Enemy 两个空层，不动任何现有层。
        /// 相机遮挡与命中过滤都需要一个能把角色排除在外的层。
        /// </summary>
        private static void EnsureLayers(StringBuilder report)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
            if (asset == null)
            {
                report.AppendLine("  [层] 无法读取 TagManager.asset。");
                return;
            }

            var tagManager = new SerializedObject(asset);
            var layers = tagManager.FindProperty("layers");
            foreach (var name in new[] { PlayerLayerName, EnemyLayerName })
            {
                if (LayerMask.NameToLayer(name) >= 0)
                {
                    continue;
                }

                var assigned = false;
                for (var i = 8; i < layers.arraySize; i++)
                {
                    var element = layers.GetArrayElementAtIndex(i);
                    if (!string.IsNullOrEmpty(element.stringValue))
                    {
                        continue;
                    }

                    element.stringValue = name;
                    assigned = true;
                    report.AppendLine($"  [层] 新增层 {i}：{name}");
                    break;
                }

                if (!assigned)
                {
                    report.AppendLine($"  [层] 没有空闲层位可以放 {name}。");
                }
            }

            tagManager.ApplyModifiedProperties();
        }

        private static T EnsureAsset<T>(string path, StringBuilder report) where T : ScriptableObject
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            report.AppendLine($"  [配置] 新建 {path}");
            return asset;
        }

        /// <summary>
        /// 生成玩家 Prefab。
        ///
        /// 结构：根节点持 CharacterController 与业务组件，模型作为子节点。
        /// 只实例化正式角色模型，绝不实例化任何动画 FBX 里的重复模型，
        /// 也不让动画 FBX 的材质覆盖 Changli/Materials 下的正式材质。
        /// </summary>
        private static GameObject EnsurePlayerPrefab(AnimatorController controller, StringBuilder report)
        {
            Directory.CreateDirectory(PlayerSettingsDirectory);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var root = existing != null
                ? PrefabUtility.LoadPrefabContents(PlayerPrefabPath)
                : new GameObject("Changli_Player");

            try
            {
                root.layer = SafeLayer(PlayerLayerName);

                var characterController = Ensure<CharacterController>(root);
                characterController.center = new Vector3(0f, 0.9f, 0f);
                characterController.height = 1.8f;
                characterController.radius = 0.32f;
                characterController.slopeLimit = 50f;
                characterController.stepOffset = 0.35f;
                characterController.skinWidth = 0.02f;

                Ensure<CharacterControllerMotor>(root);
                var view = Ensure<PlayerCharacterView>(root);

                var model = FindChild(root.transform, "Model");
                if (model == null)
                {
                    var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(P2AnimationSetup.CharacterFbxPath);
                    if (fbx == null)
                    {
                        report.AppendLine($"  [玩家] 找不到角色模型 {P2AnimationSetup.CharacterFbxPath}。");
                    }
                    else
                    {
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
                        instance.name = "Model";
                        instance.transform.localPosition = Vector3.zero;
                        instance.transform.localRotation = Quaternion.identity;
                        model = instance.transform;
                        report.AppendLine("  [玩家] 实例化正式角色模型 Changli_TPose。");
                    }
                }

                if (model != null)
                {
                    // 不能用 ?? ：模型 FBX 的 animationType 非 None 但没有序列化 Animator 时，
                    // GetComponent 返回的是 Unity 的"伪 null"，?? 判定为非空会直接写进一个不存在的组件。
                    var animator = model.GetComponent<Animator>();
                    if (animator == null)
                    {
                        animator = model.gameObject.AddComponent<Animator>();
                    }

                    animator.runtimeAnimatorController = controller;
                    // 不使用 Root Motion：动作位移来自状态配置，Animator 不是位移真相。
                    animator.applyRootMotion = false;
                    animator.updateMode = AnimatorUpdateMode.Normal;
                    // AlwaysAnimate：玩家永远是镜头焦点，剔除它只会带来"动画偶尔不播"这类
                    // 极难复现的问题，省下的开销对单个角色毫无意义。
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    Ensure<PlayerAnimatorProjector>(model.gameObject);

                    // 动画在根骨骼上烘焙了水平位移，必须抵消掉，否则动作结束回到 Idle
                    // 时骨架会被插值拉回原点 —— 那就是"回退一小步"。
                    Ensure<RootMotionCanceller>(model.gameObject);
                }

                var hitboxTransform = FindChild(root.transform, "Hitbox");
                if (hitboxTransform == null)
                {
                    var hitboxObject = new GameObject("Hitbox");
                    hitboxObject.transform.SetParent(root.transform, false);
                    hitboxTransform = hitboxObject.transform;
                    report.AppendLine("  [玩家] 新增命中盒节点。");
                }

                var hitbox = Ensure<MeleeHitbox>(hitboxTransform.gameObject);
                SetPrivateField(hitbox, "owner", Faction.Player);
                SetPrivateLayerMask(hitbox, "targetLayers", LayerMaskFor(EnemyLayerName));
                SetPrivateField(view, "hitbox", hitbox);
                if (model != null)
                {
                    SetPrivateField(view, "animatorProjector", model.GetComponent<PlayerAnimatorProjector>());
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var overlay = Ensure<PlayerDebugOverlay>(root);
                SetPrivateField(overlay, "player", view);
#endif

                var saved = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                report.AppendLine($"  [玩家] 保存 {PlayerPrefabPath}");
                return saved;
            }
            finally
            {
                if (existing != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        /// <summary>
        /// 在 Bootstrap 场景补齐持久化 App Root。
        ///
        /// 加载界面从 P0ClientShell 移到这里：只有这样它才能在大厅卸载之后、
        /// 地图加载完成之前继续显示，而不是在切换瞬间变成黑屏。
        /// </summary>
        private static void EnsureBootScene(
            PlayerTuningAsset tuning,
            ThirdPersonCameraSettings cameraSettings,
            StringBuilder report)
        {
            var scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);

            var appRoot = GameObject.Find(AppRootName);
            if (appRoot == null)
            {
                appRoot = new GameObject(AppRootName);
                report.AppendLine($"  [Bootstrap] 新建 {AppRootName}");
            }

            var scope = Ensure<AppRootLifetimeScope>(appRoot);
            SetPrivateField(scope, "playerTuning", tuning);

            // 加载界面：自己的 UIDocument，sortingOrder 高于大厅面板，跨场景常驻。
            var loadingObject = FindOrCreateChild(appRoot.transform, LoadingUiName);
            var loadingDocument = Ensure<UIDocument>(loadingObject);
            loadingDocument.panelSettings =
                AssetDatabase.LoadAssetAtPath<PanelSettings>(SettingsDirectory + "/P0PanelSettings.asset");
            loadingDocument.sortingOrder = 100f;
            var loadingView = Ensure<LoadingView>(loadingObject);
            SetPrivateField(
                loadingView,
                "loadingLayout",
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    "Assets/Game/Features/Loading/View/UI/LoadingScreen.uxml"));

            // 旧的 LoadingView 挂在 P0ClientShell 上，会随 Bootstrap 场景一起销毁。
            var shell = UnityEngine.Object.FindObjectOfType<AccountView>();
            if (shell != null)
            {
                var stale = shell.GetComponent<LoadingView>();
                if (stale != null)
                {
                    UnityEngine.Object.DestroyImmediate(stale);
                    report.AppendLine("  [Bootstrap] 移除 P0ClientShell 上的旧 LoadingView（已迁移到持久根）。");
                }
            }

            // 输入
            var inputObject = FindOrCreateChild(appRoot.transform, InputName);
            var input = Ensure<NarakaPlayerInputProvider>(inputObject);
            SetPrivateField(
                input, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath));
            SetPrivateField(scope, "inputProvider", input);

            // 相机：轴心与虚拟相机都挂在持久根下，因此切场景不会累积第二台相机。
            var rigObject = FindOrCreateChild(appRoot.transform, CameraRigName);
            var rig = Ensure<ThirdPersonCameraRig>(rigObject);
            var pivot = FindOrCreateChild(rigObject.transform, CameraPivotName);
            var vcamObject = FindOrCreateChild(rigObject.transform, VirtualCameraName);
            var vcam = Ensure<CinemachineVirtualCamera>(vcamObject);
            if (vcam.GetCinemachineComponent<CinemachineTransposer>() == null)
            {
                vcam.AddCinemachineComponent<CinemachineTransposer>();
            }

            if (vcam.GetCinemachineComponent<CinemachineHardLookAt>() == null)
            {
                vcam.AddCinemachineComponent<CinemachineHardLookAt>();
            }

            Ensure<CinemachineCollider>(vcamObject);
            vcam.Follow = pivot.transform;
            vcam.LookAt = pivot.transform;
            vcam.Priority = 20;
            // 没有玩家之前不抢相机：大厅仍由场景里的 Main Camera 自己决定取景。
            vcamObject.SetActive(false);

            SetPrivateField(rig, "settings", cameraSettings);
            SetPrivateField(rig, "virtualCamera", vcam);
            SetPrivateField(rig, "pivot", pivot.transform);
            SetPrivateField(rig, "input", input);
            SetPrivateField(scope, "cameraRig", rig);
            rig.ApplySettings();

            // 大厅"开始游戏"的目标场景。P0 时期序列化的是并不存在的 "Map1"，
            // 只在它仍是那个失效值（或为空）时纠正，不覆盖用户自己填的场景名。
            var gameScope = UnityEngine.Object.FindObjectOfType<GameLifetimeScope>();
            if (gameScope != null)
            {
                var serialized = new SerializedObject(gameScope);
                var mapName = serialized.FindProperty("map1SceneName");
                if (mapName != null &&
                    (string.IsNullOrWhiteSpace(mapName.stringValue) || mapName.stringValue == "Map1"))
                {
                    mapName.stringValue = WorldMapIds.Map01Task;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    report.AppendLine($"  [Bootstrap] 开始游戏目标场景由 Map1 纠正为 {WorldMapIds.Map01Task}。");
                }
            }

            // Main Camera 必须活过场景切换，否则地图里没有渲染相机。
            var mainCamera = UnityEngine.Object.FindObjectOfType<UnityEngine.Camera>();
            if (mainCamera != null)
            {
                Ensure<CinemachineBrain>(mainCamera.gameObject);
                if (mainCamera.transform.parent == null)
                {
                    mainCamera.transform.SetParent(appRoot.transform, true);
                    report.AppendLine("  [Bootstrap] Main Camera 移入持久根，保留原世界位置。");
                }
            }
            else
            {
                report.AppendLine("  [Bootstrap] 场景里没有相机，地图将无法渲染。");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.AppendLine("  [Bootstrap] 持久化 App Root 装配完成。");
        }

        /// <summary>
        /// 生成或补齐一张自有地图场景。
        ///
        /// 只补缺失对象：已经存在的出生点、传送门与灰盒地面保持用户手工摆放的位置不变。
        /// </summary>
        private static void EnsureWorldScene(
            string scenePath,
            string mapId,
            GameObject playerPrefab,
            bool withPortal,
            StringBuilder report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath) ?? string.Empty);
            var isNew = !File.Exists(scenePath);
            var scene = isNew
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (isNew)
            {
                report.AppendLine($"  [场景] 新建 {scenePath}");
            }

            EnsureSceneLighting(scene, report);

            var scopeObject = FindRoot(scene, WorldScopeName) ?? new GameObject(WorldScopeName);
            Ensure<WorldSceneLifetimeScope>(scopeObject);

            var entryObject = FindRoot(scene, WorldEntryName) ?? new GameObject(WorldEntryName);
            var entry = Ensure<WorldSceneEntry>(entryObject);
            SetPrivateField(entry, "mapId", mapId);
            SetPrivateField(entry, "playerPrefab", playerPrefab);
            SetPrivateField(entry, "returnToMap01OnDeath", true);

            EnsureSpawnPoint(scene, "SpawnPoint_Entry", PlayerSpawnPoint.SpawnKind.Entry,
                new Vector3(0f, 0.1f, 0f), report);
            if (mapId == WorldMapIds.Map01Task)
            {
                EnsureSpawnPoint(scene, "SpawnPoint_Respawn", PlayerSpawnPoint.SpawnKind.Respawn,
                    new Vector3(-4f, 0.1f, 0f), report);
            }

            EnsureGround(scene, report);

            if (withPortal)
            {
                EnsurePortal(scene, report);
            }
            else
            {
                EnsureTrainingDummies(scene, report);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void EnsureSceneLighting(Scene scene, StringBuilder report)
        {
            if (FindRoot(scene, "Directional Light") != null)
            {
                return;
            }

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(48f, 140f, 0f);
            report.AppendLine("  [场景] 新增方向光。");
        }

        private static void EnsureSpawnPoint(
            Scene scene,
            string name,
            PlayerSpawnPoint.SpawnKind kind,
            Vector3 defaultPosition,
            StringBuilder report)
        {
            var existing = FindRoot(scene, name);
            if (existing != null)
            {
                // 已有出生点：只确保组件与种类正确，位置保持用户摆放的值。
                var point = Ensure<PlayerSpawnPoint>(existing);
                SetPrivateField(point, "kind", kind);
                return;
            }

            var spawn = new GameObject(name);
            spawn.transform.position = defaultPosition;
            var created = spawn.AddComponent<PlayerSpawnPoint>();
            SetPrivateField(created, "kind", kind);
            report.AppendLine($"  [场景] 新增出生点 {name}。");
        }

        private static void EnsureGround(Scene scene, StringBuilder report)
        {
            if (FindRoot(scene, "GrayboxGround") != null)
            {
                return;
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "GrayboxGround";
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
            ApplyMaterial(ground, GraySurfaceMaterialPath, new Color(0.42f, 0.45f, 0.48f));
            report.AppendLine("  [场景] 新增灰盒地面。");
        }

        private static void EnsurePortal(Scene scene, StringBuilder report)
        {
            if (FindRoot(scene, "Portal_To_Map02") != null)
            {
                return;
            }

            var portal = new GameObject("Portal_To_Map02");
            portal.transform.position = new Vector3(0f, 1.2f, 12f);
            var box = portal.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(3f, 2.4f, 1.2f);
            portal.AddComponent<MapPortal>();

            // 灰盒可视化：没有正式特效，先用一块半透明面片标出门在哪。
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "PortalMarker";
            marker.transform.SetParent(portal.transform, false);
            marker.transform.localScale = new Vector3(3f, 2.4f, 0.1f);
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<BoxCollider>());
            ApplyMaterial(marker, null, new Color(0.55f, 0.35f, 0.95f));
            report.AppendLine("  [场景] 新增地图二传送门。");
        }

        private static void EnsureTrainingDummies(Scene scene, StringBuilder report)
        {
            if (FindRoot(scene, "TrainingDummies") != null)
            {
                return;
            }

            var group = new GameObject("TrainingDummies");
            var offsets = new[]
            {
                new Vector3(-3f, 0f, 5f),
                new Vector3(0f, 0f, 6f),
                new Vector3(3f, 0f, 5f)
            };

            for (var i = 0; i < offsets.Length; i++)
            {
                var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                dummy.name = $"TrainingDummy_{i + 1}";
                dummy.transform.SetParent(group.transform, false);
                dummy.transform.localPosition = offsets[i] + new Vector3(0f, 1f, 0f);
                dummy.layer = SafeLayer(EnemyLayerName);
                ApplyMaterial(dummy, GrayDummyMaterialPath, new Color(0.72f, 0.72f, 0.76f));

                var component = dummy.AddComponent<TrainingDummy>();
                SetPrivateField(component, "faction", Faction.Enemy);
                SetPrivateField(component, "targetRenderer", dummy.GetComponent<Renderer>());
            }

            report.AppendLine($"  [场景] 新增 {offsets.Length} 个灰盒训练假人。");
        }

        /// <summary>
        /// Build Settings 幂等合并：保留用户已有条目与顺序，只补齐缺失的三张场景。
        /// 这里绝不整表覆盖 —— 那会在用户下一次点菜单时静默删掉他自己加的场景。
        /// </summary>
        public static void EnsureBuildSettings(StringBuilder report)
        {
            var required = new[] { BootScenePath, Map01ScenePath, Map02ScenePath };
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            var added = new List<string>();

            foreach (var path in required)
            {
                if (!File.Exists(path))
                {
                    report.AppendLine($"  [Build] 场景文件不存在，跳过：{path}");
                    continue;
                }

                var index = scenes.FindIndex(
                    s => string.Equals(s.path, path, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    if (!scenes[index].enabled)
                    {
                        scenes[index] = new EditorBuildSettingsScene(path, true);
                        report.AppendLine($"  [Build] 重新启用：{path}");
                    }

                    continue;
                }

                scenes.Add(new EditorBuildSettingsScene(path, true));
                added.Add(path);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            report.AppendLine(added.Count == 0
                ? $"  [Build] 场景列表已包含全部必需场景（共 {scenes.Count} 项）。"
                : $"  [Build] 新增 {added.Count} 项：{string.Join("、", added)}");
        }

        private static void ApplyMaterial(GameObject target, string materialPath, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            Material material = null;
            if (!string.IsNullOrEmpty(materialPath))
            {
                material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    material = new Material(shader);
                    material.SetColor("_BaseColor", color);
                    material.SetColor("_Color", color);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
            }

            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static Transform FindChild(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == name)
                {
                    return parent.GetChild(i);
                }
            }

            return null;
        }

        private static GameObject FindOrCreateChild(Transform parent, string name)
        {
            var child = FindChild(parent, name);
            if (child != null)
            {
                return child.gameObject;
            }

            var created = new GameObject(name);
            created.transform.SetParent(parent, false);
            return created;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            return null;
        }

        private static T Ensure<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static int SafeLayer(string name)
        {
            var layer = LayerMask.NameToLayer(name);
            return layer < 0 ? 0 : layer;
        }

        private static LayerMask LayerMaskFor(string name)
        {
            var layer = LayerMask.NameToLayer(name);
            return layer < 0 ? ~0 : 1 << layer;
        }

        private static void SetPrivateField(UnityEngine.Object target, string field, object value)
        {
            if (target == null)
            {
                return;
            }

            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"{target.GetType().Name} 缺少序列化字段 {field}。", target);
                return;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = (UnityEngine.Object)value;
                    break;
                case SerializedPropertyType.Enum:
                    property.enumValueIndex = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = Convert.ToString(value);
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.Integer:
                    property.intValue = Convert.ToInt32(value);
                    break;
                default:
                    Debug.LogWarning($"不支持写入字段类型 {property.propertyType}（{field}）。", target);
                    return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetPrivateLayerMask(UnityEngine.Object target, string field, LayerMask mask)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                return;
            }

            property.intValue = mask.value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
