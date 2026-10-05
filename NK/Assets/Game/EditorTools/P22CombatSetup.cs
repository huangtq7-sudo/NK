using System.IO;
using System.Text;
using Naraka.Features.Combat.Model;
using Naraka.Features.Combat.View;
using Naraka.Features.Monster.View;
using Naraka.Features.World.Controller;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Naraka.EditorTools
{
    /// <summary>
    /// P2.2 战斗装配：灰盒狼、生成点、导航与反击训练靶。
    ///
    /// 幂等是硬要求：只补缺失的对象与引用，已经存在的对象绝不重建，
    /// 已有 Transform 绝不重新摆位。因此用户手工调过的位置不会被覆盖。
    ///
    /// 它**只改 Map02_CombatGraybox**：不碰地图一、不碰玩家 Prefab、
    /// 不碰相机与出生点，也不打开任何第三方源场景。
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
            var wolfPrefab = EnsureWolfPrefab(report);
            EnsureCombatScene(wolfPrefab, report);

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
                agent.angularSpeed = 0f;
                agent.acceleration = 24f;
                agent.autoBraking = true;
                // 转向由 View 自己按配置角速度处理，交给 Agent 会和朝向投影打架。
                agent.updateRotation = false;

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

                var view = Ensure<GrayboxWolfView>(root);
                SetField(view, "faction", Faction.Enemy);
                SetField(view, "monsterId", "monster_wolf_duskshadow");
                SetField(view, "hitbox", hitbox);
                SetField(view, "warningRoot", warningTransform);
                SetField(view, "bodyRenderer", body.GetComponent<Renderer>());

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

            var spawnerObject = FindRoot(scene, SpawnerName);
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

            var trainingObject = FindRoot(scene, TrainingTargetName);
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

        private static void SetField(Object target, string field, object value)
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
                    property.objectReferenceValue = (Object)value;
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

        private static void SetLayerMask(Object target, string field, LayerMask mask)
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
