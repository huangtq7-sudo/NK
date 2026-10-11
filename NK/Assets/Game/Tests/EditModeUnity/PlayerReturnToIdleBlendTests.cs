using System.Linq;
using Naraka.EditorTools;
using Naraka.EditorTools.Diag;
using Naraka.Features.Character.Model;
using Naraka.Features.Character.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Naraka.Unity.EditMode.Tests
{
    /// <summary>
    /// 动作结束回 Idle 不再"卡一下"。
    ///
    /// 测量结论（<c>NARAKA/Diag/Pre-P4 Idle Transition Scan</c> 与运行期记录）：
    /// 卡帧不是性能问题 —— 交接窗口内没有帧时尖峰，GC 为 0；也不是停在末帧、截断或根骨骼旋转。
    /// 是<b>姿态不衔接</b>：普攻第一/三段、F、V 与两段出场结束时右前臂停在同一个姿态，
    /// 与 Idle 首帧差约 127°，头发差约 100°（Q-026）。原来 0.1 秒的淡入要把手臂甩过这 127°，
    /// 折合约 1270°/s，运行期测得交接窗口内身体角速度 1300–1900°/s，是 Idle 稳定后的 50 倍左右。
    /// 普攻第二段与冲刺的结束姿态本来就与 Idle 吻合，所以它们一直是顺的。
    ///
    /// 代码能做的是把这段差值摊到更长的淡入里；真正的修复在美术端
    /// （让收招末姿态与 Idle 首帧衔接，或补一段回 Idle 的过渡动画）。
    /// </summary>
    public sealed class PlayerReturnToIdleBlendTests
    {
        /// <summary>
        /// 回 Idle 的过渡不应比动作自身收招的典型速度更快。
        /// 运行期实测：姿态衔接正常的那几段（普攻二、三段、F、出场）收招峰值是 565–622°/s。
        /// </summary>
        private const float MaxReturnDegreesPerSecond = 650f;

        private static readonly PlayerAnimation[] Actions =
        {
            PlayerAnimation.AttackCombo1, PlayerAnimation.AttackCombo2, PlayerAnimation.AttackCombo3,
            PlayerAnimation.Charge, PlayerAnimation.SkillF, PlayerAnimation.SkillV, PlayerAnimation.Dash,
            PlayerAnimation.HitStun, PlayerAnimation.IdleVariation,
            PlayerAnimation.StopWalk, PlayerAnimation.StopRun,
            PlayerAnimation.SpawnBurstLobbyToMap01, PlayerAnimation.SpawnBurstMap01ToMap02
        };

        [Test]
        public void ReturningToLocomotionFromAnActionUsesTheReturnBlend()
        {
            const float cross = 0.1f;
            const float snap = 0.02f;
            const float back = 0.25f;

            foreach (var from in Actions)
            {
                foreach (var to in new[] { PlayerAnimation.Idle, PlayerAnimation.Walk, PlayerAnimation.Run })
                {
                    Assert.That(
                        PlayerAnimatorProjector.FadeFor(from, to, cross, snap, back), Is.EqualTo(back),
                        $"{from} → {to} 是动作交还移动层，必须用回 Idle 淡入。持续按方向键时接的是走/跑，同样适用。");
                }
            }

            // 其余切换保持原值：只在测量证实有问题的地方改，不全局加大混合。
            Assert.That(PlayerAnimatorProjector.FadeFor(PlayerAnimation.Idle, PlayerAnimation.Walk, cross, snap, back), Is.EqualTo(cross));
            Assert.That(PlayerAnimatorProjector.FadeFor(PlayerAnimation.Walk, PlayerAnimation.Run, cross, snap, back), Is.EqualTo(cross));
            Assert.That(PlayerAnimatorProjector.FadeFor(PlayerAnimation.Run, PlayerAnimation.RunTurnback, cross, snap, back), Is.EqualTo(cross));
            Assert.That(PlayerAnimatorProjector.FadeFor(PlayerAnimation.RunTurnback, PlayerAnimation.Run, cross, snap, back), Is.EqualTo(cross));
            Assert.That(PlayerAnimatorProjector.FadeFor(PlayerAnimation.Idle, PlayerAnimation.AttackCombo1, cross, snap, back), Is.EqualTo(cross));

            // Idle → 待机动作 的淡出时长被 idle 循环约束引用，必须不变。
            Assert.That(PlayerAnimatorProjector.FadeFor(PlayerAnimation.Idle, PlayerAnimation.IdleVariation, cross, snap, back), Is.EqualTo(cross));

            // 受击、死亡与出场仍是快切。
            Assert.That(PlayerAnimatorProjector.FadeFor(PlayerAnimation.Idle, PlayerAnimation.HitStun, cross, snap, back), Is.EqualTo(snap));
            Assert.That(PlayerAnimatorProjector.FadeFor(PlayerAnimation.SkillV, PlayerAnimation.Death, cross, snap, back), Is.EqualTo(snap));
            Assert.That(PlayerAnimatorProjector.FadeFor(PlayerAnimation.Idle, PlayerAnimation.SpawnBurstLobbyToMap01, cross, snap, back), Is.EqualTo(snap));
        }

        [Test]
        public void NoReturnToIdleSnapsFasterThanTheActionsOwnMotion()
        {
            var fade = ReturnFadeSeconds();
            var gaps = PreP4IdleTransitionScan.MeasureReturnGaps();
            Assert.That(gaps, Is.Not.Empty, "没有测到任何回 Idle 的来源。");

            var offenders = gaps
                .Select(g => (g.State, Gap: Mathf.Max(g.HairGap, g.BodyGap)))
                .Where(g => g.Gap / fade > MaxReturnDegreesPerSecond)
                .Select(g => $"{g.State}：姿态差 {g.Gap:F1}° / 淡入 {fade:F2}s = {g.Gap / fade:F0}°/s")
                .ToArray();

            Assert.That(
                offenders, Is.Empty,
                $"回 Idle 的过渡比动作自身收招（约 {MaxReturnDegreesPerSecond:F0}°/s）还快，会被看成卡一下：\n" +
                string.Join("\n", offenders));
        }

        private static float ReturnFadeSeconds()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(P2SceneSetup.PlayerPrefabPath);
            Assert.That(prefab, Is.Not.Null, $"找不到 {P2SceneSetup.PlayerPrefabPath}。");
            var projector = prefab.GetComponentInChildren<PlayerAnimatorProjector>(true);
            Assert.That(projector, Is.Not.Null, "玩家 Prefab 上没有 PlayerAnimatorProjector。");
            return projector.ReturnFadeSeconds;
        }
    }
}
