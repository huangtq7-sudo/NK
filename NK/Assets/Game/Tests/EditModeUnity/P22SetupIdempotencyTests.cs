using System.IO;
using System.Linq;
using Naraka.EditorTools;
using Naraka.Features.Combat.View;
using Naraka.Features.Monster.View;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Naraka.Unity.EditMode.Tests
{
    /// <summary>
    /// P2.2 装配工具必须幂等。
    ///
    /// "重复执行不会产生重复对象"不能只写在注释里：这个测试先跑一次让工程稳定下来，
    /// 记下产物字节，再跑一次并逐字节比对。任何"每次都重建一遍"的实现都会在这里失败。
    /// </summary>
    public sealed class P22SetupIdempotencyTests
    {
        [Test]
        public void ApplyingTheCombatSetupTwiceChangesNothing()
        {
            // 第一次执行让工程进入已装配状态；比较的是第二次与第一次之间有没有漂移。
            P22CombatSetup.Apply();

            var prefabBefore = File.ReadAllBytes(P22CombatSetup.WolfPrefabPath);
            var sceneBefore = File.ReadAllBytes(P2SceneSetup.Map02ScenePath);

            P22CombatSetup.Apply();

            var prefabAfter = File.ReadAllBytes(P22CombatSetup.WolfPrefabPath);
            var sceneAfter = File.ReadAllBytes(P2SceneSetup.Map02ScenePath);

            Assert.That(prefabAfter, Is.EqualTo(prefabBefore), "灰盒狼 Prefab 必须逐字节相同。");
            Assert.That(sceneAfter, Is.EqualTo(sceneBefore), "地图二场景必须逐字节相同。");
        }

        [Test]
        public void TheCombatSceneHasExactlyOneOfEachSetupObject()
        {
            P22CombatSetup.Apply();

            var scene = EditorSceneManager.OpenScene(
                P2SceneSetup.Map02ScenePath, OpenSceneMode.Single);

            // 按整个场景数，不按根对象数。P2.3 把训练靶收进了 DevOnly 节点下，
            // 只数根对象会把"它被搭到子节点了"误报成"它不存在"；
            // 而且数全场景比只数根**更严**，它还能抓到建在子节点上的重复对象。
            Assert.That(
                CountInScene(scene, P22CombatSetup.SpawnerName),
                Is.EqualTo(1),
                "重复执行不得产生第二个生成点。");
            Assert.That(
                CountInScene(scene, P22CombatSetup.TrainingTargetName),
                Is.EqualTo(1),
                "重复执行不得产生第二个训练靶。");

            // NavigationArea 是灰盒导航底板，只属于灰盒回退场景；
            // 正式战斗场景的 NavMesh 烘在真实地形上，由
            // P23FormalWorldSceneTests.TheFormalCombatSceneHasABakedNavMeshOnTheRealTerrain 守。
            var graybox = EditorSceneManager.OpenScene(
                P2SceneSetup.Map02GrayboxScenePath, OpenSceneMode.Single);
            Assert.That(
                CountInScene(graybox, P22CombatSetup.NavigationRootName),
                Is.EqualTo(1),
                "灰盒回退场景必须保留恰好一个导航底板。");

            scene = EditorSceneManager.OpenScene(
                P2SceneSetup.Map02ScenePath, OpenSceneMode.Single);

            var spawner = FindInScene(scene, P22CombatSetup.SpawnerName)
                .GetComponent<MonsterSpawner>();
            Assert.That(spawner, Is.Not.Null);

            var training = FindInScene(scene, P22CombatSetup.TrainingTargetName)
                .GetComponent<CounterTrainingTarget>();
            Assert.That(training, Is.Not.Null);
            Assert.That(
                P22CombatSetup.TrainingTargetName,
                Does.Contain("DevOnly"),
                "开发测试对象必须在名字上标明自己不是正式内容。");
        }

        private static int CountInScene(UnityEngine.SceneManagement.Scene scene, string name)
        {
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform != null && transform.name == name)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static GameObject FindInScene(
            UnityEngine.SceneManagement.Scene scene, string name)
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

            Assert.Fail($"场景 {scene.name} 里找不到 {name}。");
            return null;
        }

        [Test]
        public void TheWolfPrefabIsAGrayboxNotFinalArt()
        {
            P22CombatSetup.Apply();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(P22CombatSetup.WolfPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.name, Does.StartWith("Graybox"), "正式狼模型尚未导入，替身必须叫 Graybox。");
            Assert.That(prefab.GetComponent<DuskshadowWolfView>(), Is.Not.Null);
            Assert.That(
                prefab.GetComponentInChildren<MeleeHitbox>(true),
                Is.Not.Null,
                "怪物必须有自己的命中盒，伤害才不会由动画事件直接结算。");
            Assert.That(
                prefab.GetComponentInChildren<MonsterWarningView>(true),
                Is.Not.Null,
                "红色预警必须有可绑定的表现对象。");
        }
    }
}
