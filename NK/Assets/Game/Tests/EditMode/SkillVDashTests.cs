using System.Collections.Generic;
using System.Linq;
using Naraka.Features.Character.Model;
using NUnit.Framework;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// V 技能：原地蓄势，然后看得见轨迹的高速冲刺。
    ///
    /// 最初的实现把 17.838 米从第 0 帧起匀速铺满 0.9 秒片段时间，结果动画还在原地蓄势时
    /// 角色就已经开始滑，而且整段被平均摊慢 —— 用户看到的是"慢滑"。
    ///
    /// 动画根骨骼的实测节奏（Pre-P4 Skill V Motion Scan）：片段 0 → 0.333 秒原地蓄势，
    /// 然后在两帧内窜出 13 米 —— 照搬动画就成了闪现，用户要的是能看到轨迹的冲刺。
    /// 因此蓄势时机取自动画，冲刺时长由用户定为 0.2 秒（真实时间）：
    /// 片段 0.333 → 0.633 秒，起步快、匀减速收住，远早于 1.518 秒的命中窗。
    ///
    /// Model 只给出"冲刺进行到哪"（进度 0 → 1）与总距离；路径的撞墙、贴地、悬崖与怪物
    /// 由 View 层在冲刺开始时一次性校验，此后每帧沿这条已校验的路径前进。
    /// </summary>
    public sealed class SkillVDashTests
    {
        private const float Frame = 1f / 60f;

        private static PlayerCore NewCore() => new PlayerCore(PlayerTuning.CreateBaseline());

        [Test]
        public void SkillVHoldsStillThroughTheWindupThenDashesVisiblyForTwoTenthsOfASecond()
        {
            var core = NewCore();
            var v = core.Tuning.SkillV.Action;
            var windupReal = v.DisplacementStartClipSeconds / v.Playback.MainSpeed;
            var dashReal = (v.DisplacementClipSeconds - v.DisplacementStartClipSeconds) / v.Playback.MainSpeed;

            var frames = Cast(core, Frame);

            Assert.That(frames.All(f => f.ForwardSpeed == 0f), Is.True, "V 不得再按帧速度滑行。");

            var dashing = frames.Where(f => f.DashDistance > 0f).ToList();
            Assert.That(dashing, Is.Not.Empty, "V 必须冲刺。");
            Assert.That(
                frames.Where(f => f.Elapsed < windupReal - Frame).All(f => f.DashDistance == 0f), Is.True,
                "蓄势期间必须原地不动。");
            Assert.That(dashing[0].Elapsed, Is.EqualTo(windupReal).Within(2f * Frame), "冲刺必须在蓄势结束时起步。");
            Assert.That(dashing.Last().Progress, Is.EqualTo(1f), "冲刺必须冲完。");
            Assert.That(
                dashing.Last().Elapsed - dashing[0].Elapsed, Is.EqualTo(dashReal).Within(2f * Frame),
                "冲刺本身持续约 0.2 秒。");
            Assert.That(dashing.All(f => f.DashDistance == v.ForwardDisplacement), Is.True);

            // 防止退化成闪现：60 帧下要有一串看得见的中间位置。
            Assert.That(
                dashing.Count(f => f.Progress > 0f && f.Progress < 1f), Is.GreaterThanOrEqualTo(10),
                "冲刺必须有足够的中间帧才看得见轨迹。");

            for (var i = 1; i < dashing.Count; i++)
            {
                Assert.That(dashing[i].Progress, Is.GreaterThanOrEqualTo(dashing[i - 1].Progress), "进度不得回退。");
            }

            // 起步快、末尾收住。
            var firstStep = dashing[1].Progress - dashing[0].Progress;
            var lastStep = dashing[dashing.Count - 1].Progress - dashing[dashing.Count - 2].Progress;
            Assert.That(firstStep, Is.GreaterThan(lastStep), "冲刺应起步快、末尾收住。");
        }

        [TestCase(0.1f, TestName = "SkillVDashFinishesExactlyOnceAt10Fps")]
        [TestCase(1f / 30f, TestName = "SkillVDashFinishesExactlyOnceAt30Fps")]
        [TestCase(1f / 60f, TestName = "SkillVDashFinishesExactlyOnceAt60Fps")]
        [TestCase(1f / 144f, TestName = "SkillVDashFinishesExactlyOnceAt144Fps")]
        [TestCase(1f / 240f, TestName = "SkillVDashFinishesExactlyOnceAt240Fps")]
        public void SkillVDashFinishesExactlyOnceAtAnyFrameRate(float deltaSeconds)
        {
            // 低帧率时一帧就能跨过冲刺终点，也必须恰好给出一次进度 1，View 才能落到终点。
            var frames = Cast(NewCore(), deltaSeconds);
            var dashing = frames.Where(f => f.DashDistance > 0f).ToList();

            Assert.That(dashing, Is.Not.Empty);
            Assert.That(dashing.Count(f => f.Progress >= 1f), Is.EqualTo(1));
            Assert.That(dashing.Last().Progress, Is.EqualTo(1f));
            Assert.That(dashing.All(f => f.Progress >= 0f && f.Progress <= 1f), Is.True);
        }

        [Test]
        public void DeathDuringTheWindupCancelsTheDash()
        {
            var core = NewCore();
            var v = core.Tuning.SkillV.Action;
            core.Tick(PlayerInputFrame.Idle.WithSkillV(true), Frame);
            PlayerLocomotionTests.Hold(
                core, PlayerInputFrame.Idle, 0.5f * v.DisplacementStartClipSeconds / v.Playback.MainSpeed);
            core.ApplyDamage(Lethal(core));
            Assert.That(core.Reaction, Is.EqualTo(ReactionState.Death));

            Assert.That(DashFramesWithin(core, 3f), Is.Zero, "蓄势中死亡，V 不得再冲刺。");
        }

        [Test]
        public void DeathMidDashStopsTheDash()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithSkillV(true), Frame);
            var guard = 0;
            while (core.Tick(PlayerInputFrame.Idle, Frame).DashDistance <= 0f && guard++ < 1000)
            {
            }

            core.ApplyDamage(Lethal(core));
            Assert.That(core.Reaction, Is.EqualTo(ReactionState.Death));

            Assert.That(DashFramesWithin(core, 3f), Is.Zero, "冲刺途中死亡，必须停在原地，不得继续冲。");
        }

        [Test]
        public void ASecondCastDashesAgain()
        {
            var core = NewCore();
            var finishes = Cast(core, Frame).Count(f => f.Progress >= 1f);

            PlayerLocomotionTests.Hold(core, PlayerInputFrame.Idle, core.Tuning.SkillV.CooldownSeconds + 1f);
            finishes += Cast(core, Frame).Count(f => f.Progress >= 1f);

            Assert.That(finishes, Is.EqualTo(2), "每一次施放都必须重新冲刺。");
        }

        [Test]
        public void DashStaysAContinuousBurstOverItsFullDistance()
        {
            // 本项只改 V。Move_F 仍然是 0.45 秒片段时间内的连续冲刺，总位移不变。
            var dash = PlayerTuning.CreateBaseline().Dash;
            Assert.That(dash.PathDash, Is.False, "Move_F 不得被连带改成路径冲刺。");

            var travelled = 0f;
            const float step = 1f / 480f;
            for (var elapsed = 0f; elapsed < dash.RealDurationSeconds; elapsed += step)
            {
                travelled += dash.ForwardSpeedAt(elapsed) * step;
            }

            Assert.That(travelled, Is.EqualTo(dash.ForwardDisplacement).Within(0.2f));

            var core = NewCore();
            PlayerStaminaTests.TapSprint(core);
            Assert.That(core.Action, Is.EqualTo(ActionState.Dash));
            var guard = 0;
            while (core.Action == ActionState.Dash && guard++ < 20000)
            {
                Assert.That(core.Tick(PlayerInputFrame.Idle, Frame).DashDistance, Is.EqualTo(0f));
            }
        }

        [Test]
        public void SkillVKeepsItsCombatRules()
        {
            var skill = PlayerTuning.CreateBaseline().SkillV;
            var v = skill.Action;

            Assert.That(v.PathDash, Is.True);
            Assert.That(v.ForwardDisplacement, Is.EqualTo(17.838f).Within(0.0001f));
            Assert.That(v.DisplacementStartClipSeconds, Is.EqualTo(0.333f).Within(0.0001f), "蓄势时机取自动画根骨骼。");
            Assert.That(
                (v.DisplacementClipSeconds - v.DisplacementStartClipSeconds) / v.Playback.MainSpeed,
                Is.EqualTo(0.2f).Within(0.0001f), "冲刺时长 0.2 秒（真实时间）。");

            // 只换位移方式，伤害、范围、冷却与命中窗保持原值。
            Assert.That(v.HitWindowStart, Is.EqualTo(1.518f).Within(0.0001f));
            Assert.That(v.HitWindowEnd, Is.EqualTo(2.404f).Within(0.0001f));
            Assert.That(v.SkillMultiplier, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(skill.CooldownSeconds, Is.EqualTo(40f));
            Assert.That(skill.Radius, Is.EqualTo(6f));
            Assert.That(v.DisplacementClipSeconds, Is.LessThan(v.HitWindowStart), "冲完之后才开命中窗，伤害落在终点。");

            for (var t = 0f; t < v.RealDurationSeconds; t += Frame)
            {
                Assert.That(v.ForwardSpeedAt(t), Is.EqualTo(0f), "路径冲刺不经过按帧速度。");
            }
        }

        private struct DashFrame
        {
            public float Elapsed;
            public float ForwardSpeed;
            public float DashDistance;
            public float Progress;
        }

        private static List<DashFrame> Cast(PlayerCore core, float deltaSeconds)
        {
            core.Tick(PlayerInputFrame.Idle.WithSkillV(true), deltaSeconds);
            Assert.That(core.Action, Is.EqualTo(ActionState.SkillV));

            var frames = new List<DashFrame>();
            var elapsed = 0f;
            var guard = 0;
            while (core.Action == ActionState.SkillV && guard++ < 20000)
            {
                var output = core.Tick(PlayerInputFrame.Idle, deltaSeconds);
                elapsed += deltaSeconds;
                frames.Add(new DashFrame
                {
                    Elapsed = elapsed,
                    ForwardSpeed = output.ForwardSpeed,
                    DashDistance = output.DashDistance,
                    Progress = output.DashProgress
                });
            }

            return frames;
        }

        private static int DashFramesWithin(PlayerCore core, float seconds)
        {
            var count = 0;
            for (var elapsed = 0f; elapsed < seconds; elapsed += Frame)
            {
                if (core.Tick(PlayerInputFrame.Idle, Frame).DashDistance > 0f)
                {
                    count++;
                }
            }

            return count;
        }

        private static float Lethal(PlayerCore core) =>
            (core.MaxHealth + core.MaxArmor) * (100f + core.Defense) / 100f;
    }
}
