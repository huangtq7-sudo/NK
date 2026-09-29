using Naraka.EditorTools;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Naraka.Unity.EditMode.Tests
{
    /// <summary>
    /// 动画与配置之间的契约。
    ///
    /// 这些断言存在的原因是一个真实事故：初版把每个动作时长都写成了估计值
    /// （待机动作 3 秒 vs 实际 15.07 秒、Burst02 2 秒 vs 实际 5.5 秒），
    /// 状态机按配置时长结束动作，于是所有比配置长的动画都被拦腰截断。
    /// 时长只有一个真相 —— 动画片段本身。换动画之后这里会立刻失败，
    /// 而不是等到有人在游戏里看见动作被砍掉。
    /// </summary>
    public sealed class PlayerAnimationContractTests
    {
        private static PlayerTuningAsset LoadTuning()
        {
            var asset = AssetDatabase.LoadAssetAtPath<PlayerTuningAsset>(P2SceneSetup.PlayerTuningPath);
            Assert.That(asset, Is.Not.Null, $"找不到 {P2SceneSetup.PlayerTuningPath}。");
            return asset;
        }

        private static float ClipLength(string fbx)
        {
            var clip = P2AnimationSetup.LoadClip($"{P2AnimationSetup.AnimationRoot}/{fbx}.fbx");
            Assert.That(clip, Is.Not.Null, $"找不到动画 {fbx}。");
            return clip.length;
        }

        [TestCase("AM_Stand1_Action03_SEQ1", "idleVariationDurationSeconds")]
        [TestCase("Stop_Walk_R", "stopWalkSeconds")]
        [TestCase("Stop_Run_R", "stopRunSeconds")]
        [TestCase("Run_Turnback", "runTurnbackSeconds")]
        [TestCase("Behit_B_L", "hitStunSeconds")]
        [TestCase("AM_Death", "deathSeconds")]
        public void ConfiguredDurationMatchesTheClipLength(string fbx, string field)
        {
            var serialized = new SerializedObject(LoadTuning());
            var property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, $"PlayerTuningAsset 缺少字段 {field}。");

            Assert.That(
                property.floatValue, Is.EqualTo(ClipLength(fbx)).Within(0.001f),
                $"{field} 必须等于 {fbx} 的实际长度，否则动画会被拦腰截断。" +
                "请执行菜单 NARAKA/Setup/Rebuild Player Animator。");
        }

        [TestCase("Move_F", "dash")]
        [TestCase("Attack01", "combo1")]
        [TestCase("AM_Summon", "combo2")]
        [TestCase("Attack02", "combo3")]
        [TestCase("Attack10", "charge")]
        [TestCase("AM_Skill01", "skillF")]
        [TestCase("Attack04_1", "skillV")]
        [TestCase("Burst02", "spawnLobbyToMap01")]
        [TestCase("Burst01", "spawnMap01ToMap02")]
        public void ConfiguredAttackDurationMatchesTheClipLength(string fbx, string field)
        {
            var serialized = new SerializedObject(LoadTuning());
            var entry = serialized.FindProperty(field);
            Assert.That(entry, Is.Not.Null, $"PlayerTuningAsset 缺少字段 {field}。");
            var clipSeconds = entry.FindPropertyRelative("clipSeconds");

            Assert.That(
                clipSeconds.floatValue, Is.EqualTo(ClipLength(fbx)).Within(0.001f),
                $"{field}.clipSeconds 必须等于 {fbx} 的实际长度。");
        }

        [TestCase("combo1")]
        [TestCase("combo2")]
        [TestCase("combo3")]
        [TestCase("charge")]
        [TestCase("skillF")]
        [TestCase("skillV")]
        public void AttackWindowsStayInsideTheActionDuration(string field)
        {
            var serialized = new SerializedObject(LoadTuning());
            var entry = serialized.FindProperty(field);
            var duration = entry.FindPropertyRelative("clipSeconds").floatValue;

            foreach (var window in new[]
                     {
                         "hitWindowStart", "hitWindowEnd", "comboWindowStart", "comboWindowEnd"
                     })
            {
                var value = entry.FindPropertyRelative(window).floatValue;
                Assert.That(
                    value, Is.InRange(0f, duration + 0.001f),
                    $"{field}.{window} 落在动作时长之外，永远不会被触发。");
            }

            Assert.That(
                entry.FindPropertyRelative("hitWindowEnd").floatValue,
                Is.GreaterThan(entry.FindPropertyRelative("hitWindowStart").floatValue),
                $"{field} 的命中窗必须有正长度。");
        }

        [Test]
        public void LoopingClipsAreTheOnlyOnesMarkedLooping()
        {
            foreach (var (_, fbx) in P2AnimationSetup.StateToFbx)
            {
                var clip = P2AnimationSetup.LoadClip($"{P2AnimationSetup.AnimationRoot}/{fbx}.fbx");
                if (clip == null)
                {
                    continue;
                }

                var shouldLoop = System.Array.IndexOf(P2AnimationSetup.LoopingFbx, fbx) >= 0;
                Assert.That(
                    clip.isLooping, Is.EqualTo(shouldLoop),
                    $"{fbx} 的循环设置与映射表不一致（应为 {shouldLoop}）。");
            }
        }

        [Test]
        public void PlayerPrefabCancelsTheBakedRootMotion()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Settings/Player/Changli_Player.prefab");
            Assert.That(prefab, Is.Not.Null, "找不到玩家 Prefab。");

            var canceller = prefab.GetComponentInChildren<RootMotionCanceller>(true);
            Assert.That(
                canceller, Is.Not.Null,
                "玩家 Prefab 必须挂 RootMotionCanceller：" +
                "这些动画在根骨骼上烘焙了水平位移，不抵消就会在动作结束时回退一小步。");

            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(
                animator.applyRootMotion, Is.False,
                "Root Motion 不是位移真相，位移必须由代码驱动（ADR-0015）。");
        }

        [Test]
        public void BakedRootMotionIsLargeEnoughToMatter()
        {
            // 这条测试记录的是"为什么需要抵消器"这件事实本身。
            // 如果将来动画换成了原地动作，它会失败并提醒我们抵消器可以撤掉。
            var clip = P2AnimationSetup.LoadClip($"{P2AnimationSetup.AnimationRoot}/Move_F.fbx");
            Assert.That(clip, Is.Not.Null);

            var moved = false;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != "Root" || binding.propertyName != "m_LocalPosition.z")
                {
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                var delta = Mathf.Abs(curve.Evaluate(clip.length) - curve.Evaluate(0f));
                moved = delta > 1f;
            }

            Assert.That(
                moved, Is.True,
                "Move_F 仍然在根骨骼上烘焙了水平位移，因此 RootMotionCanceller 必须保留。");
        }
    }
}
