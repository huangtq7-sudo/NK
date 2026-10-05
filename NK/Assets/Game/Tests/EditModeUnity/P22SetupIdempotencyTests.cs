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
            var roots = scene.GetRootGameObjects();

            Assert.That(
                roots.Count(go => go.name == P22CombatSetup.SpawnerName),
                Is.EqualTo(1),
                "重复执行不得产生第二个生成点。");
            Assert.That(
                roots.Count(go => go.name == P22CombatSetup.TrainingTargetName),
                Is.EqualTo(1),
                "重复执行不得产生第二个训练靶。");
            Assert.That(
                roots.Count(go => go.name == P22CombatSetup.NavigationRootName),
                Is.EqualTo(1));

            var spawner = roots
                .First(go => go.name == P22CombatSetup.SpawnerName)
                .GetComponent<MonsterSpawner>();
            Assert.That(spawner, Is.Not.Null);

            var training = roots
                .First(go => go.name == P22CombatSetup.TrainingTargetName)
                .GetComponent<CounterTrainingTarget>();
            Assert.That(training, Is.Not.Null);
            Assert.That(
                P22CombatSetup.TrainingTargetName,
                Does.Contain("DevOnly"),
                "开发测试对象必须在名字上标明自己不是正式内容。");
        }

        [Test]
        public void TheWolfPrefabIsAGrayboxNotFinalArt()
        {
            P22CombatSetup.Apply();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(P22CombatSetup.WolfPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.name, Does.StartWith("Graybox"), "正式狼模型尚未导入，替身必须叫 Graybox。");
            Assert.That(prefab.GetComponent<GrayboxWolfView>(), Is.Not.Null);
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
