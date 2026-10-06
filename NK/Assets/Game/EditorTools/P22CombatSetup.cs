using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Naraka.Features.Combat.Model;
using Naraka.Features.Combat.View;
using Naraka.Features.Monster.Model;
using Naraka.Features.Monster.View;
using Naraka.Features.World.Controller;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Naraka.EditorTools
{
    /// <summary>
    /// P2.2 战斗装配：正式暮影妖狼、开发回退用的灰盒替身、生成点、导航与反击训练靶。
    ///
    /// 幂等是硬要求：只补缺失的对象与引用，已经存在的对象绝不重建，
    /// 已有 Transform 绝不重新摆位。因此用户手工调过的位置不会被覆盖。
    ///
    /// 它**只改 Map02_CombatGraybox**：不碰地图一、不碰玩家 Prefab、
    /// 不碰相机与出生点，也不打开任何第三方源场景。
    ///
    /// 2026-10-05 起正式模型已导入，场景生成的是
    /// <see cref="DuskshadowWolfAssets.PrefabPath"/>。方块替身仍然会被生成，
    /// 但只作为**开发回退资产**：正式资源万一缺失时还能跑通战斗闭环，
    /// 并且既有的"替身必须叫 Graybox"契约测试仍然成立。正式场景不引用它。
    /// </summary>
    public static class P22CombatSetup
    {
        private const string MonsterSettingsDirectory = "Assets/Game/Settings/Monster";
        public const string WolfPrefabPath = MonsterSettingsDirectory + "/GrayboxWolf.prefab";
        private const string WolfMaterialPath = MonsterSettingsDirectory + "/GrayboxWolfBody.mat";
        private const string WarningMaterialPath = MonsterSettingsDirectory + "/GrayboxWarning.mat";
        private const string TrainingMaterialPath = MonsterSettingsDirectory + "/GrayboxCounterTarget.mat";

        public const string SpawnerName = "GrayboxWolfSpawner";
        public const string TrainingTargetName = "CounterTrainingTarget_DevOnly";
        public const string NavigationRootName = "NavigationArea";

        [MenuItem("NARAKA/Setup/Apply P2.2 Combat Setup")]
        public static void Apply()
        {
            var report = new StringBuilder();
            report.AppendLine("NARAKA P2.2 战斗装配：");

            Directory.CreateDirectory(MonsterSettingsDirectory);

            // 两个 Prefab 都生成：正式狼进场景，方块替身留作开发回退。
            var grayboxPrefab = EnsureWolfPrefab(report);
            var officialPrefab = EnsureDuskshadowWolfPrefab(report);
            EnsureCombatScene(officialPrefab ?? grayboxPrefab, report);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// 生成灰盒狼 Prefab。
        ///
        /// 它刻意是一个最简单的方块：正式的 Polygonal Wolf 模型尚未导入，
        /// 做一个"看起来像狼"的替身只会让人误以为美术已经接进来了。
        /// </summary>
        private static GameObject EnsureWolfPrefab(StringBuilder report)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(WolfPrefabPath);
            var root = existing != null
                ? PrefabUtility.LoadPrefabContents(WolfPrefabPath)
                : new GameObject("GrayboxWolf");

            try
            {
                root.layer = SafeLayer(P2SceneSetup.EnemyLayerName);

                // 碰撞体：玩家命中盒靠它扫到这只狼。
                var capsule = Ensure<CapsuleCollider>(root);
                capsule.height = 1.6f;
                capsule.radius = 0.6f;
                capsule.center = new Vector3(0f, 0.8f, 0f);

                var agent = Ensure<NavMeshAgent>(root);
                agent.radius = 0.6f;
                agent.height = 1.6f;
                agent.baseOffset = 0f;
                // 角速度必须是 0：转向由 View 按配置角速度处理，Agent 一旦能转就会和
                // 朝向投影打架。注意 `updateRotation` 在 2021.3 里不是序列化字段
                // （Prefab 里没有 m_UpdateRotation），写在装配工具里是空操作，
                // 因此那一项由 DuskshadowWolfView 在运行期关闭。
                agent.angularSpeed = 0f;
                agent.acceleration = 24f;
                agent.autoBraking = true;

                var body = FindChild(root.transform, "Body");
                if (body == null)
                {
                    var created = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    created.name = "Body";
                    created.transform.SetParent(root.transform, false);
                    created.transform.localPosition = new Vector3(0f, 0.8f, 0f);
                    created.transform.localScale = new Vector3(0.9f, 1.1f, 1.8f);
                    Object.DestroyImmediate(created.GetComponent<BoxCollider>());
                    body = created.transform;
                    report.AppendLine("  [狼] 新增灰盒本体方块。");
                }

                ApplyMaterial(body.gameObject, WolfMaterialPath, new Color(0.36f, 0.38f, 0.45f));

                var hitboxTransform = FindOrCreateChild(root.transform, "Hitbox");
                var hitbox = Ensure<MeleeHitbox>(hitboxTransform);
                SetField(hitbox, "owner", Faction.Enemy);
                SetField(hitbox, "localOffset", new Vector3(0f, 0.8f, 1.2f));
                SetField(hitbox, "radius", 1.4f);
                SetLayerMask(hitbox, "targetLayers", LayerMaskFor(P2SceneSetup.PlayerLayerName));

                var warningTransform = FindChild(root.transform, "Warning");
                if (warningTransform == null)
                {
                    var created = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    created.name = "Warning";
                    created.transform.SetParent(root.transform, false);
                    created.transform.localPosition = new Vector3(0f, 0.05f, 3.5f);
                    created.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    created.transform.localScale = new Vector3(6f, 7f, 1f);
                    Object.DestroyImmediate(created.GetComponent<MeshCollider>());
                    warningTransform = created.transform;
                    report.AppendLine("  [狼] 新增技能预警面片。");
                }

                ApplyMaterial(warningTransform.gameObject, WarningMaterialPath, new Color(0.95f, 0.2f, 0.18f));
                var warning = Ensure<MonsterWarningView>(warningTransform);
                SetField(warning, "warningRenderer", warningTransform.GetComponent<Renderer>());

                var view = Ensure<DuskshadowWolfView>(root);
                SetField(view, "faction", Faction.Enemy);
                SetField(view, "monsterId", "monster_wolf_duskshadow");
                SetField(view, "hitbox", hitbox);
                SetField(view, "warningRoot", warningTransform);
                // 字段在正式模型接入时改成了数组（正式狼可能有多个 Renderer）。
                // 方块替身只有一个，但也要走同一条写入路径。
                SetObjectArray(view, "bodyRenderers", new UnityEngine.Object[] { body.GetComponent<Renderer>() });

                var saved = PrefabUtility.SaveAsPrefabAsset(root, WolfPrefabPath);
                report.AppendLine($"  [狼] 保存 {WolfPrefabPath}");
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
                    Object.DestroyImmediate(root);
                }
            }
        }

        // ------------------------------------------------------------ 正式暮影妖狼

        /// <summary>
        /// 生成正式暮影妖狼 Prefab。
        ///
        /// 结构是"项目自有的根 + 第三方模型作视觉子物体"：
        ///
        /// <code>
        /// DuskshadowWolf            [DuskshadowWolfView] [CapsuleCollider] [NavMeshAgent]
        ///                           [MonsterAnimatorProjector]
        /// ├── Model                 第三方 Polygonal Wolf.FBX 的嵌套 Prefab 实例
        /// │                         [Animator] controller = DuskshadowWolf.controller
        /// ├── Hitbox                [MeleeHitbox]   项目自有
        /// └── Warning               [MonsterWarningView] 项目自有
        /// </code>
        ///
        /// 第三方模型自带 0 个碰撞体（已实测），因此命中与阻挡只能用项目自有对象 ——
        /// 这正好符合"不依赖第三方模型自带碰撞体"的要求，不需要额外剥离什么。
        ///
        /// 正式资源缺失时返回 null，场景退回灰盒替身，而不是生成一个没有模型的空壳。
        /// </summary>
        private static GameObject EnsureDuskshadowWolfPrefab(StringBuilder report)
        {
            if (!DuskshadowWolfAssets.ModelIsImported)
            {
                report.AppendLine(
                    $"  [正式狼] 找不到 {DuskshadowWolfAssets.ModelPath}，" +
                    "本次不生成正式 Prefab，场景继续使用灰盒替身。");
                return null;
            }

            var material = EnsureDuskshadowMaterial(report);
            var controller = EnsureDuskshadowController(report);

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(DuskshadowWolfAssets.PrefabPath);
            var root = existing != null
                ? PrefabUtility.LoadPrefabContents(DuskshadowWolfAssets.PrefabPath)
                : new GameObject("DuskshadowWolf");

            try
            {
                root.layer = SafeLayer(P2SceneSetup.EnemyLayerName);

                // 碰撞体按实测几何摆：体长沿 Z（1.54）、体宽 0.50、肩高 0.69、脚底 ≈0。
                // 方块替身那套 1.6 高的立方体尺寸对真狼来说又高又窄，不能照抄。
                var capsule = Ensure<CapsuleCollider>(root);
                capsule.direction = 2;
                capsule.height = 1.5f;
                capsule.radius = 0.33f;
                capsule.center = new Vector3(0f, 0.42f, 0f);

                var agent = Ensure<NavMeshAgent>(root);
                agent.radius = 0.35f;
                agent.height = 0.9f;
                agent.baseOffset = 0f;
                // 同上：序列化的角速度为 0，`updateRotation` 由 View 在运行期关闭。
                agent.angularSpeed = 0f;
                agent.acceleration = 24f;
                agent.autoBraking = true;

                var model = FindChild(root.transform, DuskshadowWolfAssets.ModelChildName);
                if (model == null)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(
                        DuskshadowWolfAssets.ModelPath);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    instance.name = DuskshadowWolfAssets.ModelChildName;
                    instance.transform.SetParent(root.transform, false);
                    model = instance.transform;
                    report.AppendLine("  [正式狼] 新增正式模型视觉子物体（第三方 FBX 的嵌套实例）。");
                }

                // 材质：第三方源材质用的是内建 Standard，URP 下是粉色。
                // 只建项目自有的 URP 适配材质并指过去，**不改第三方 .mat**。
                var modelRenderers = model.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in modelRenderers)
                {
                    if (renderer.sharedMaterial != material)
                    {
                        renderer.sharedMaterial = material;
                    }
                }

                var animator = model.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = model.gameObject.AddComponent<Animator>();
                }

                animator.runtimeAnimatorController = controller;
                // 移动由 NavMeshAgent 驱动，动画不得再推世界坐标。
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                var projector = Ensure<MonsterAnimatorProjector>(root);
                SetField(projector, "animator", animator);

                var hitboxTransform = FindOrCreateChild(root.transform, "Hitbox");
                var hitbox = Ensure<MeleeHitbox>(hitboxTransform);
                SetField(hitbox, "owner", Faction.Enemy);
                // 前向偏移 1.2 与半径 1.4 与灰盒替身**完全一致**：这两项决定咬击能不能
                // 打到玩家，动它们等于动已验收的战斗手感。只把高度从 0.8 降到 0.5，
                // 让判定球对齐实测的嘴部高度（RigJaw y≈0.40、RigHead y≈0.57）。
                SetField(hitbox, "localOffset", new Vector3(0f, 0.5f, 1.2f));
                SetField(hitbox, "radius", 1.4f);
                SetLayerMask(hitbox, "targetLayers", LayerMaskFor(P2SceneSetup.PlayerLayerName));

                var warningTransform = FindChild(root.transform, "Warning");
                if (warningTransform == null)
                {
                    var created = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    created.name = "Warning";
                    created.transform.SetParent(root.transform, false);
                    created.transform.localPosition = new Vector3(0f, 0.05f, 3.5f);
                    created.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    created.transform.localScale = new Vector3(6f, 7f, 1f);
                    Object.DestroyImmediate(created.GetComponent<MeshCollider>());
                    warningTransform = created.transform;
                    report.AppendLine("  [正式狼] 新增技能预警面片（项目自有，不依赖第三方资源）。");
                }

                ApplyMaterial(
                    warningTransform.gameObject, WarningMaterialPath, new Color(0.95f, 0.2f, 0.18f));
                var warning = Ensure<MonsterWarningView>(warningTransform);
                SetField(warning, "warningRenderer", warningTransform.GetComponent<Renderer>());

                var view = Ensure<DuskshadowWolfView>(root);
                SetField(view, "faction", Faction.Enemy);
                SetField(view, "monsterId", "monster_wolf_duskshadow");
                SetField(view, "hitbox", hitbox);
                SetField(view, "warningRoot", warningTransform);
                SetField(view, "animatorProjector", projector);
                SetObjectArray(view, "bodyRenderers", modelRenderers);

                // 颜色反馈改成"正常=不染色"：正式模型有贴图，
                // 方块替身那套灰蓝 normalColor 会把整只狼压暗。
                // 其余三档保留为轻度叠色，仍然能看出狂暴/可处决/死亡。
                SetColor(view, "normalColor", Color.white);
                SetColor(view, "enragedColor", new Color(1f, 0.55f, 0.5f));
                SetColor(view, "executableColor", new Color(1f, 0.92f, 0.62f));
                SetColor(view, "deadColor", new Color(0.45f, 0.45f, 0.48f));

                var saved = PrefabUtility.SaveAsPrefabAsset(root, DuskshadowWolfAssets.PrefabPath);
                report.AppendLine(
                    $"  [正式狼] 保存 {DuskshadowWolfAssets.PrefabPath}" +
                    $"（{modelRenderers.Length} 个 Renderer、Animator Root Motion 关闭）");
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
                    Object.DestroyImmediate(root);
                }
            }
        }

        /// <summary>
        /// 项目自有的 URP 适配材质。
        ///
        /// 第三方 `Polygonal Wolf Black.mat` 用的是内建 `Standard` Shader，
        /// URP 下会渲染成粉色。按规范只在项目目录里建一份 URP Lit 材质并引用同样的贴图，
        /// **不修改第三方源材质**，这样素材包升级时能直接对比。
        /// 原 Shader：`Standard`；替换后：`Universal Render Pipeline/Lit`。
        /// </summary>
        private static Material EnsureDuskshadowMaterial(StringBuilder report)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                report.AppendLine("  [正式狼] 找不到 URP Lit Shader，退回 Standard。");
                shader = Shader.Find("Standard");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(DuskshadowWolfAssets.MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, DuskshadowWolfAssets.MaterialPath);
                report.AppendLine(
                    $"  [正式狼] 新建 URP 适配材质 {DuskshadowWolfAssets.MaterialPath}" +
                    "（第三方源材质的 Standard Shader 在 URP 下是粉色）。");
            }

            if (material.shader != shader)
            {
                material.shader = shader;
            }

            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
                DuskshadowWolfAssets.BaseTexturePath);
            var glow = AssetDatabase.LoadAssetAtPath<Texture2D>(
                DuskshadowWolfAssets.GlowTexturePath);

            SetTexture(material, "_BaseMap", baseMap);
            SetTexture(material, "_MainTex", baseMap);
            SetTexture(material, "_EmissionMap", glow);
            SetMaterialColor(material, "_BaseColor", Color.white);
            SetMaterialColor(material, "_Color", Color.white);

            if (glow != null)
            {
                // 狼眼与身上的发光纹理靠 Emission 表达；不开关键字的话这张图不会生效。
                if (!material.IsKeywordEnabled("_EMISSION"))
                {
                    material.EnableKeyword("_EMISSION");
                }

                SetMaterialColor(material, "_EmissionColor", Color.white);
                if (material.globalIlluminationFlags != MaterialGlobalIlluminationFlags.RealtimeEmissive)
                {
                    material.globalIlluminationFlags =
                        MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }

            return material;
        }

        /// <summary>
        /// 正式狼的 Animator Controller。
        ///
        /// 与玩家完全同一套做法：每个动作各占一个 State，**0 个 Parameter、0 条 Transition**。
        /// Animator 因此既不可能自己跳状态，也不可能被反查成"当前动作"的来源 ——
        /// 那个真相在 <c>MonsterCore</c> 的动作层（ADR-0018）。
        ///
        /// 播放速度一律保持 1。实测片段长度与配置动作时长的差在 10–14%
        /// （咬击 1.167 vs 1.30、吐息 1.333 vs 1.55、受击 0.667 vs 0.60、死亡 2.000 vs 2.00），
        /// 要不要调速是观感取舍，按"不擅自发明速度值"的既定做法交人工验收决定。
        /// </summary>
        private static AnimatorController EnsureDuskshadowController(StringBuilder report)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                DuskshadowWolfAssets.ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(
                    DuskshadowWolfAssets.ControllerPath);
                report.AppendLine($"  [正式狼] 新建 {DuskshadowWolfAssets.ControllerPath}");
            }

            if (controller.layers == null || controller.layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
            }

            var machine = controller.layers[0].stateMachine;
            if (machine == null)
            {
                machine = new AnimatorStateMachine
                {
                    name = controller.layers[0].name,
                    hideFlags = HideFlags.HideInHierarchy
                };
                AssetDatabase.AddObjectToAsset(machine, controller);
                var layers = controller.layers;
                layers[0].stateMachine = machine;
                controller.layers = layers;
            }

            var states = machine.states.ToDictionary(s => s.state.name, s => s.state);
            var expected = new HashSet<string>(System.StringComparer.Ordinal);
            var missing = new List<string>();
            var column = 0;

            foreach (var (animation, fbx) in DuskshadowWolfAssets.AnimationToFbx)
            {
                var stateName = MonsterAnimatorProjector.StateNameFor(animation);
                expected.Add(stateName);

                var clip = LoadAnimationClip(DuskshadowWolfAssets.AnimationFbxPath(fbx));
                if (clip == null)
                {
                    missing.Add(fbx);
                    continue;
                }

                if (!states.TryGetValue(stateName, out var state))
                {
                    state = machine.AddState(
                        stateName,
                        new Vector3(260f + (column % 2 * 260f), 60f + (column / 2 * 70f), 0f));
                    states[stateName] = state;
                }

                state.motion = clip;
                state.writeDefaultValues = false;
                state.speed = 1f;
                column++;
            }

            // 孤儿 State：投影层按名字缓存 Hash，没人能寻址的 State 留着只会误导人。
            var orphans = machine.states
                .Where(child => !expected.Contains(child.state.name))
                .Select(child => child.state.name)
                .ToList();
            foreach (var orphan in orphans)
            {
                foreach (var child in machine.states)
                {
                    if (string.Equals(child.state.name, orphan, System.StringComparison.Ordinal))
                    {
                        machine.RemoveState(child.state);
                        break;
                    }
                }

                states.Remove(orphan);
                report.AppendLine($"  [正式狼] 移除孤儿 State：{orphan}");
            }

            if (states.TryGetValue(MonsterAnimatorProjector.StateNames.Idle, out var idle))
            {
                machine.defaultState = idle;
            }

            // Animator 不做任何自己的状态判断：清掉可能残留的过渡与参数。
            foreach (var child in machine.states)
            {
                var transitions = child.state.transitions;
                for (var i = transitions.Length - 1; i >= 0; i--)
                {
                    child.state.RemoveTransition(transitions[i]);
                }
            }

            var anyState = machine.anyStateTransitions;
            for (var i = anyState.Length - 1; i >= 0; i--)
            {
                machine.RemoveAnyStateTransition(anyState[i]);
            }

            var entry = machine.entryTransitions;
            for (var i = entry.Length - 1; i >= 0; i--)
            {
                machine.RemoveEntryTransition(entry[i]);
            }

            var parameters = controller.parameters;
            for (var i = parameters.Length - 1; i >= 0; i--)
            {
                controller.RemoveParameter(i);
            }

            if (missing.Count > 0)
            {
                report.AppendLine($"  [正式狼] 缺少动画片段：{string.Join("、", missing)}");
            }

            report.AppendLine(
                $"  [正式狼] Animator {machine.states.Length} 个 State、" +
                $"{controller.parameters.Length} 个 Parameter、0 条 Transition。");
            return controller;
        }

        private static AnimationClip LoadAnimationClip(string assetPath)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset is AnimationClip clip &&
                    !clip.name.StartsWith("__preview__", System.StringComparison.Ordinal))
                {
                    return clip;
                }
            }

            return null;
        }

        private static void EnsureCombatScene(GameObject wolfPrefab, StringBuilder report)
        {
            if (!File.Exists(P2SceneSetup.Map02ScenePath))
            {
                report.AppendLine(
                    $"  [场景] {P2SceneSetup.Map02ScenePath} 不存在，" +
                    "请先执行 NARAKA/Setup/Apply P2 Scene Setup。");
                return;
            }

            var scene = EditorSceneManager.OpenScene(P2SceneSetup.Map02ScenePath, OpenSceneMode.Single);

            var spawnerObject = FindInScene(scene, SpawnerName);
            if (spawnerObject == null)
            {
                spawnerObject = new GameObject(SpawnerName);
                // 放在出生点 30 单位外：感知半径是 14，休眠距离是 40，
                // 因此狼会在原地巡逻而不是在出场动画（输入锁定 4.8 秒）期间就扑上来。
                // 玩家必须自己走近才会被发现，这既是设计意图，也让地图二的
                // 出场流程不受战斗干扰。
                spawnerObject.transform.position = new Vector3(0f, 0f, 30f);
                report.AppendLine($"  [场景] 新增 {SpawnerName}。");
            }

            var spawner = Ensure<MonsterSpawner>(spawnerObject);
            SetField(spawner, "monsterPrefab", wolfPrefab);
            SetField(spawner, "maxAlive", 1);
            // 本阶段不做刷新：死了就没了，重新进场景才会再有一只。
            SetField(spawner, "respawnSeconds", 0f);

            var trainingObject = FindInScene(scene, TrainingTargetName);
            if (trainingObject == null)
            {
                trainingObject = new GameObject(TrainingTargetName);
                trainingObject.transform.position = new Vector3(-8f, 0f, 8f);
                report.AppendLine($"  [场景] 新增开发用反击训练靶 {TrainingTargetName}。");
            }

            trainingObject.layer = SafeLayer(P2SceneSetup.EnemyLayerName);
            var trainingCollider = Ensure<CapsuleCollider>(trainingObject);
            trainingCollider.height = 2f;
            trainingCollider.radius = 0.6f;
            trainingCollider.center = new Vector3(0f, 1f, 0f);

            var trainingBody = FindChild(trainingObject.transform, "Body");
            if (trainingBody == null)
            {
                var created = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                created.name = "Body";
                created.transform.SetParent(trainingObject.transform, false);
                created.transform.localPosition = new Vector3(0f, 1f, 0f);
                Object.DestroyImmediate(created.GetComponent<CapsuleCollider>());
                trainingBody = created.transform;
            }

            ApplyMaterial(trainingBody.gameObject, TrainingMaterialPath, new Color(0.45f, 0.45f, 0.5f));

            var trainingHitboxTransform = FindOrCreateChild(trainingObject.transform, "Hitbox");
            var trainingHitbox = Ensure<MeleeHitbox>(trainingHitboxTransform);
            SetField(trainingHitbox, "owner", Faction.Enemy);
            SetField(trainingHitbox, "localOffset", new Vector3(0f, 1f, 1.6f));
            SetField(trainingHitbox, "radius", 2.4f);
            SetLayerMask(trainingHitbox, "targetLayers", LayerMaskFor(P2SceneSetup.PlayerLayerName));

            var training = Ensure<CounterTrainingTarget>(trainingObject);
            SetField(training, "faction", Faction.Enemy);
            SetField(training, "hitbox", trainingHitbox);
            SetField(training, "bodyRenderer", trainingBody.GetComponent<Renderer>());

            EnsureNavigation(scene, report);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.AppendLine("  [场景] Map02_CombatGraybox 战斗装配完成。");
        }

        /// <summary>
        /// 导航区域。
        ///
        /// 地面标成 Navigation Static 之后烘焙一次 NavMesh；
        /// 已经有导航数据就什么都不做，因此重复执行不会产生新的烘焙结果。
        /// 用的是 Unity 2021.3 自带的 NavMesh，没有引入任何第三方 AI 包。
        /// </summary>
        private static void EnsureNavigation(Scene scene, StringBuilder report)
        {
            var ground = FindRoot(scene, "GrayboxGround");
            if (ground == null)
            {
                report.AppendLine("  [导航] 场景里没有 GrayboxGround，跳过导航烘焙。");
                return;
            }

            var marker = FindRoot(scene, NavigationRootName);
            if (marker == null)
            {
                marker = new GameObject(NavigationRootName);
                marker.transform.position = ground.transform.position;
                report.AppendLine($"  [导航] 新增 {NavigationRootName} 标记对象。");
            }

            var flags = GameObjectUtility.GetStaticEditorFlags(ground);
            if ((flags & StaticEditorFlags.NavigationStatic) == 0)
            {
                GameObjectUtility.SetStaticEditorFlags(
                    ground, flags | StaticEditorFlags.NavigationStatic);
                report.AppendLine("  [导航] GrayboxGround 标记为 Navigation Static。");
            }

            var triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.vertices != null && triangulation.vertices.Length > 0)
            {
                report.AppendLine("  [导航] 已有 NavMesh 数据，跳过烘焙（保持幂等）。");
                return;
            }

            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
            report.AppendLine("  [导航] 已烘焙 NavMesh。");
        }

        // ------------------------------------------------------------------ 工具

        private static T Ensure<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static T Ensure<T>(Transform target) where T : Component => Ensure<T>(target.gameObject);

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

        private static Transform FindOrCreateChild(Transform parent, string name)
        {
            var child = FindChild(parent, name);
            if (child != null)
            {
                return child;
            }

            var created = new GameObject(name);
            created.transform.SetParent(parent, false);
            return created.transform;
        }

        /// <summary>
        /// 在整个场景里按名字找对象，包括子节点与停用对象。
        ///
        /// 不能只找根对象：P2.3 把训练靶这类开发用对象收进了 DevOnly 节点下，
        /// 只找根的话装配工具会以为它不存在，然后再建一个 ——
        /// 这正是幂等性测试抱住的那个缺陷。
        /// </summary>
        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform != null && transform.name == name)
                    {
                        return transform.gameObject;
                    }
                }
            }

            return null;
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

        private static void ApplyMaterial(GameObject target, string materialPath, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                material.SetColor("_BaseColor", color);
                material.SetColor("_Color", color);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            renderer.sharedMaterial = material;
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

        private static void SetField(UnityEngine.Object target, string field, object value)
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
                    property.enumValueIndex = System.Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = System.Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = System.Convert.ToString(value);
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = System.Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.Integer:
                    property.intValue = System.Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = (Vector3)value;
                    break;
                default:
                    Debug.LogWarning($"不支持写入字段类型 {property.propertyType}（{field}）。", target);
                    return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>写入对象数组字段。颜色反馈要覆盖正式模型的全部 Renderer。</summary>
        private static void SetObjectArray(UnityEngine.Object target, string field, UnityEngine.Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null || !property.isArray)
            {
                Debug.LogWarning($"{target.GetType().Name} 缺少数组字段 {field}。", target);
                return;
            }

            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetColor(UnityEngine.Object target, string field, Color value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"{target.GetType().Name} 缺少颜色字段 {field}。", target);
                return;
            }

            property.colorValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (texture == null || !material.HasProperty(property))
            {
                return;
            }

            if (material.GetTexture(property) != texture)
            {
                material.SetTexture(property, texture);
            }
        }

        private static void SetMaterialColor(Material material, string property, Color color)
        {
            if (!material.HasProperty(property))
            {
                return;
            }

            if (material.GetColor(property) != color)
            {
                material.SetColor(property, color);
            }
        }

        private static void SetLayerMask(UnityEngine.Object target, string field, LayerMask mask)
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
