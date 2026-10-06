using System.Collections.Generic;
using System.IO;
using System.Linq;
using Naraka.EditorTools;
using Naraka.Features.Combat.View;
using Naraka.Features.Monster.Model;
using Naraka.Features.Monster.View;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace Naraka.Unity.EditMode.Tests
{
    /// <summary>
    /// 正式暮影妖狼接入后的结构契约。
    ///
    /// 这些断言存在的理由：第三方资源刚接进来时，"看起来导进来了其实有一半是坏的"
    /// 在 Inspector 里翻不出来 —— URP 粉色材质、Animator 自己带 Transition、
    /// Root Motion 没关、场景里其实还引用着方块替身、演示场景被塞进 Build Settings。
    /// 肉眼验收看的是观感，这些要靠测试。
    /// </summary>
    public sealed class DuskshadowWolfSetupTests
    {
        private static GameObject LoadOfficialPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DuskshadowWolfAssets.PrefabPath);
            Assert.That(
                prefab, Is.Not.Null,
                $"找不到正式狼 Prefab {DuskshadowWolfAssets.PrefabPath}。" +
                "先执行 NARAKA/Setup/Apply P2.2 Combat Setup。");
            return prefab;
        }

        [Test]
        public void TheOfficialWolfPrefabExistsAndUsesTheImportedModel()
        {
            Assert.That(
                DuskshadowWolfAssets.ModelIsImported, Is.True,
                $"正式模型 {DuskshadowWolfAssets.ModelPath} 必须已经导入。");

            var prefab = LoadOfficialPrefab();
            var model = prefab.transform.Find(DuskshadowWolfAssets.ModelChildName);
            Assert.That(model, Is.Not.Null, "正式 Prefab 必须把第三方模型作为视觉子物体。");

            var skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(
                skinned.Length, Is.GreaterThan(0),
                "视觉子物体必须带蒙皮网格，否则场上什么都看不到。");
        }

        [Test]
        public void TheSceneSpawnerPointsAtTheOfficialWolfNotTheGraybox()
        {
            P22CombatSetup.Apply();

            var scene = EditorSceneManager.OpenScene(
                P2SceneSetup.Map02ScenePath, OpenSceneMode.Single);
            var spawnerObject = scene.GetRootGameObjects()
                .FirstOrDefault(go => go.name == P22CombatSetup.SpawnerName);
            Assert.That(spawnerObject, Is.Not.Null);

            var spawner = spawnerObject.GetComponent<MonsterSpawner>();
            Assert.That(spawner, Is.Not.Null);

            var prefabProperty = new SerializedObject(spawner).FindProperty("monsterPrefab");
            Assert.That(prefabProperty, Is.Not.Null);
            var referenced = prefabProperty.objectReferenceValue as GameObject;
            Assert.That(referenced, Is.Not.Null, "生成点必须引用一个 Prefab。");

            var path = AssetDatabase.GetAssetPath(referenced);
            Assert.That(
                path, Is.EqualTo(DuskshadowWolfAssets.PrefabPath),
                "正式场景必须引用正式狼。方块替身只能作为开发回退资产，不得出现在正式场景里。");
            Assert.That(
                referenced.name, Does.Not.StartWith("Graybox"),
                "场景里引用的不能是灰盒替身。");
        }

        [Test]
        public void TheOfficialPrefabHasNoActiveCubeGrayboxBody()
        {
            var prefab = LoadOfficialPrefab();

            var cubes = new List<string>();
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !filter.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (filter.sharedMesh.name == "Cube")
                {
                    cubes.Add(filter.gameObject.name);
                }
            }

            Assert.That(
                cubes, Is.Empty,
                "正式狼不得保留灰盒方块本体，否则场上会同时出现模型和一个方块：" +
                string.Join("、", cubes));

            Assert.That(
                prefab.transform.Find("Body"), Is.Null,
                "方块替身的 Body 子物体不应该出现在正式 Prefab 里。");
        }

        [Test]
        public void TheOfficialPrefabHasAnAnimatorWithRootMotionDisabled()
        {
            var prefab = LoadOfficialPrefab();

            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null, "正式 Prefab 必须有 Animator。");
            Assert.That(
                animator.runtimeAnimatorController, Is.Not.Null,
                "Animator 必须绑定项目自有的 Controller。");
            Assert.That(
                AssetDatabase.GetAssetPath(animator.runtimeAnimatorController),
                Is.EqualTo(DuskshadowWolfAssets.ControllerPath),
                "必须用项目自有的 Controller，不能用素材包里的演示 Controller。");

            // 移动由 NavMeshAgent 驱动。Root Motion 打开就会变成双倍位移。
            Assert.That(
                animator.applyRootMotion, Is.False,
                "Apply Root Motion 必须关闭：移动由 NavMeshAgent 驱动。");

            var projector = prefab.GetComponentInChildren<MonsterAnimatorProjector>(true);
            Assert.That(projector, Is.Not.Null, "必须有单向投影组件，Animator 不得自己决定状态。");
        }

        [Test]
        public void EveryRequiredAnimationClipIsImported()
        {
            foreach (var (animation, fbx) in DuskshadowWolfAssets.AnimationToFbx)
            {
                var path = DuskshadowWolfAssets.AnimationFbxPath(fbx);
                Assert.That(
                    File.Exists(path), Is.True,
                    $"{animation} 需要的动画文件缺失：{path}");

                var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                Assert.That(clip, Is.Not.Null, $"{path} 里没有可用的 AnimationClip。");
                Assert.That(clip.length, Is.GreaterThan(0f), $"{animation} 的片段长度必须大于 0。");
            }
        }

        [Test]
        public void EveryAnimationMapsToItsOwnAnimatorStateAndTheAnimatorDecidesNothing()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                DuskshadowWolfAssets.ControllerPath);
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.layers.Length, Is.EqualTo(1));

            var machine = controller.layers[0].stateMachine;
            var states = machine.states.ToDictionary(s => s.state.name, s => s.state);

            Assert.That(
                states.Count, Is.EqualTo(DuskshadowWolfAssets.AnimationToFbx.Length),
                "State 数量必须与动画映射表一致，不能有孤儿 State。");

            foreach (var (animation, fbx) in DuskshadowWolfAssets.AnimationToFbx)
            {
                var stateName = MonsterAnimatorProjector.StateNameFor(animation);
                Assert.That(
                    states.ContainsKey(stateName), Is.True,
                    $"{animation} 缺少对应的 Animator State「{stateName}」。");

                var motion = states[stateName].motion;
                Assert.That(motion, Is.Not.Null, $"State「{stateName}」没有绑定动画。");
                Assert.That(
                    AssetDatabase.GetAssetPath(motion),
                    Is.EqualTo(DuskshadowWolfAssets.AnimationFbxPath(fbx)),
                    $"State「{stateName}」绑错了动画来源。");
            }

            // Animator 不做任何自己的状态判断：它只是投影的画布。
            Assert.That(
                controller.parameters.Length, Is.EqualTo(0),
                "Animator 不得有 Parameter，否则它就成了第二份当前动作真相。");
            Assert.That(
                machine.anyStateTransitions.Length, Is.EqualTo(0),
                "Animator 不得有 Any State 过渡。");
            Assert.That(
                machine.states.Sum(s => s.state.transitions.Length), Is.EqualTo(0),
                "Animator 不得有任何 State 过渡。");
            Assert.That(
                machine.defaultState, Is.Not.Null.And.Property("name")
                    .EqualTo(MonsterAnimatorProjector.StateNames.Idle),
                "默认状态必须是 Idle，否则初始化时会先播一个随机动作。");
        }

        [Test]
        public void EveryRendererHasAUsableUrpMaterial()
        {
            var prefab = LoadOfficialPrefab();
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0));

            foreach (var renderer in renderers)
            {
                Assert.That(
                    renderer.sharedMaterial, Is.Not.Null,
                    $"{renderer.name} 没有材质（Missing Material）。");

                var shader = renderer.sharedMaterial.shader;
                Assert.That(shader, Is.Not.Null, $"{renderer.name} 的材质丢了 Shader。");
                Assert.That(
                    shader.name, Does.StartWith("Universal Render Pipeline/"),
                    $"{renderer.name} 用的是 {shader.name}；" +
                    "URP 下非 URP Shader 会渲染成粉色，必须用项目自有的 URP 适配材质。");
            }

            // 第三方源材质必须保持原样：适配层是**新建**的，不是改写第三方资产。
            var thirdParty = AssetDatabase.LoadAssetAtPath<Material>(
                DuskshadowWolfAssets.ThirdPartyMaterialPath);
            Assert.That(thirdParty, Is.Not.Null);
            Assert.That(
                thirdParty.shader.name, Is.EqualTo("Standard"),
                "第三方源材质应当仍然是 Standard —— 我们只新建 URP 适配材质，不改第三方资产。");

            var adapted = AssetDatabase.LoadAssetAtPath<Material>(DuskshadowWolfAssets.MaterialPath);
            Assert.That(adapted, Is.Not.Null, "项目自有的 URP 适配材质必须存在。");
            Assert.That(
                adapted.GetTexture("_BaseMap"), Is.Not.Null,
                "适配材质必须引用第三方的 Base 贴图，否则狼是纯色的。");
        }

        [Test]
        public void ColliderNavMeshAgentHitboxAndWarningAreAllWired()
        {
            var prefab = LoadOfficialPrefab();

            var capsule = prefab.GetComponent<CapsuleCollider>();
            Assert.That(capsule, Is.Not.Null, "必须有项目自有的碰撞体，玩家命中盒才扫得到。");
            Assert.That(
                capsule.height, Is.GreaterThan(0f).And.LessThan(4f),
                "碰撞体高度必须落在实测体型范围内。");

            var agent = prefab.GetComponent<NavMeshAgent>();
            Assert.That(agent, Is.Not.Null, "地面单位必须有 NavMeshAgent。");

            // 断言的是**序列化**的角速度，而不是 `updateRotation`：
            // 后者在 Unity 2021.3 里不是序列化字段（Prefab 里没有 m_UpdateRotation），
            // 在 Prefab 上断言它只会断言 Unity 的运行期默认值。
            // 角速度为 0 才是真正拦住 Agent 自己转向的那一道，
            // `updateRotation` 由 DuskshadowWolfView 在 Awake 里关闭。
            Assert.That(
                agent.angularSpeed, Is.EqualTo(0f),
                "Agent 的角速度必须是 0：转向由 View 按配置角速度处理，" +
                "Agent 一旦能转就会和朝向投影打架。");

            var hitbox = prefab.GetComponentInChildren<MeleeHitbox>(true);
            Assert.That(hitbox, Is.Not.Null, "怪物必须有自己的命中盒，伤害不能由动画事件结算。");

            var warning = prefab.GetComponentInChildren<MonsterWarningView>(true);
            Assert.That(warning, Is.Not.Null, "红色预警必须有可绑定的表现对象。");

            // 第三方模型自带 0 个碰撞体（已实测），所以上面这些必须都是项目自有对象。
            var model = prefab.transform.Find(DuskshadowWolfAssets.ModelChildName);
            Assert.That(
                model.GetComponentsInChildren<Collider>(true), Is.Empty,
                "命中与阻挡不得依赖第三方模型自带的碰撞体。");

            var view = prefab.GetComponent<DuskshadowWolfView>();
            Assert.That(view, Is.Not.Null);

            var serialized = new SerializedObject(view);
            foreach (var field in new[] { "hitbox", "warningRoot", "animatorProjector" })
            {
                var property = serialized.FindProperty(field);
                Assert.That(property, Is.Not.Null, $"View 缺少字段 {field}。");
                Assert.That(
                    property.objectReferenceValue, Is.Not.Null,
                    $"View 的 {field} 没有在 Prefab 上绑定。");
            }

            var renderers = serialized.FindProperty("bodyRenderers");
            Assert.That(renderers, Is.Not.Null);
            Assert.That(
                renderers.arraySize, Is.GreaterThan(0),
                "颜色反馈必须绑定到身体 Renderer 上，只绑第一个会让其他部位不变色。");
            for (var i = 0; i < renderers.arraySize; i++)
            {
                Assert.That(
                    renderers.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null,
                    $"bodyRenderers[{i}] 是空引用。");
            }
        }

        [Test]
        public void ApplyingTheSetupTwiceLeavesEveryOfficialProductByteIdentical()
        {
            // 第一次让工程进入已装配状态；比较的是第二次与第一次之间有没有漂移。
            P22CombatSetup.Apply();

            var products = new[]
            {
                DuskshadowWolfAssets.PrefabPath,
                DuskshadowWolfAssets.ControllerPath,
                DuskshadowWolfAssets.MaterialPath,
                P22CombatSetup.WolfPrefabPath,
                P2SceneSetup.Map02ScenePath
            };

            var before = products.ToDictionary(p => p, File.ReadAllBytes);

            P22CombatSetup.Apply();

            foreach (var path in products)
            {
                Assert.That(
                    File.ReadAllBytes(path), Is.EqualTo(before[path]),
                    $"{path} 在第二次装配后发生了变化，工具不是幂等的。");
            }
        }

        [Test]
        public void TheCombatSceneHasNoDuplicateWolvesSpawnersOrMissingScripts()
        {
            P22CombatSetup.Apply();

            var scene = EditorSceneManager.OpenScene(
                P2SceneSetup.Map02ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();

            Assert.That(
                roots.Count(go => go.name == P22CombatSetup.SpawnerName), Is.EqualTo(1),
                "不得出现第二个生成点。");
            Assert.That(
                roots.Count(go => go.GetComponent<DuskshadowWolfView>() != null), Is.EqualTo(0),
                "狼只能由生成点在运行期生成，场景里不该预先摆一只。");

            var missing = new List<string>();
            foreach (var root in roots)
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var components = transform.GetComponents<Component>();
                    for (var i = 0; i < components.Length; i++)
                    {
                        if (components[i] == null)
                        {
                            missing.Add(transform.name);
                        }
                    }
                }
            }

            Assert.That(
                missing, Is.Empty,
                "场景里有 Missing Script：" + string.Join("、", missing.Distinct()));
        }

        [Test]
        public void NoThirdPartyDemoSceneIsImportedOrInBuildSettings()
        {
            var demoScenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith("Assets/Polygonal"))
                .ToArray();
            Assert.That(
                demoScenes, Is.Empty,
                "素材包的演示场景不得被导入：" + string.Join("、", demoScenes));

            foreach (var scene in EditorBuildSettings.scenes)
            {
                Assert.That(
                    scene.path, Does.Not.StartWith("Assets/Polygonal"),
                    $"Build Settings 里出现了第三方场景：{scene.path}");
            }

            var demoControllers = AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith("Assets/Polygonal"))
                .ToArray();
            Assert.That(
                demoControllers, Is.Empty,
                "素材包的演示 Animator 不得被导入：" + string.Join("、", demoControllers));

            var subFolders = AssetDatabase.IsValidFolder("Assets/Polygonal Creatures Pack")
                ? AssetDatabase.GetSubFolders("Assets/Polygonal Creatures Pack")
                : new string[0];
            Assert.That(
                subFolders.Length, Is.EqualTo(1),
                "本轮只导入暮影妖狼，其余九类怪物属于 P4：" + string.Join("、", subFolders));
        }
    }
}
