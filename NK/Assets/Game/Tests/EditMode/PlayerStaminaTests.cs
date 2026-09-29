using Naraka.Features.Character.Model;
using NUnit.Framework;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// 体力基线（2026-09-26 确认）：上限 20、Move_F 消耗 10、每秒恢复 5、
    /// 动作结束 0.75 秒后开始恢复、受击后额外暂停 0.5 秒。
    /// 这些断言就是基线本身，改动必须同步 NARAKA_完整玩法设计.md。
    /// </summary>
    public sealed class PlayerStaminaTests
    {
        private static StaminaTuning Baseline => PlayerTuning.CreateBaseline().Stamina;

        [Test]
        public void MaximumStaminaIsTwenty()
        {
            Assert.That(Baseline.Max, Is.EqualTo(20f));
            Assert.That(new StaminaModel(Baseline).Current, Is.EqualTo(20f));
        }

        [Test]
        public void DashCostsTen()
        {
            var stamina = new StaminaModel(Baseline);

            Assert.That(stamina.TrySpendDash(), Is.True);
            Assert.That(stamina.Current, Is.EqualTo(10f));
        }

        [Test]
        public void DashIsRejectedBelowTenStamina()
        {
            var stamina = new StaminaModel(Baseline);
            stamina.TrySpendDash();
            stamina.TrySpendDash();
            Assert.That(stamina.Current, Is.EqualTo(0f));

            Assert.That(stamina.CanSpendDash, Is.False);
            Assert.That(stamina.TrySpendDash(), Is.False, "体力不足 10 时必须拒绝 Move_F。");
            Assert.That(stamina.Current, Is.EqualTo(0f), "被拒绝时不得扣除任何体力。");
        }

        [Test]
        public void StaminaNeverGoesNegativeOrAboveMaximum()
        {
            var stamina = new StaminaModel(Baseline);
            for (var i = 0; i < 10; i++)
            {
                stamina.TrySpendDash();
            }

            Assert.That(stamina.Current, Is.GreaterThanOrEqualTo(0f));

            for (var i = 0; i < 1000; i++)
            {
                stamina.Tick(0.1f);
            }

            Assert.That(stamina.Current, Is.EqualTo(20f));
        }

        [Test]
        public void RegenerationWaitsSevenHundredFiftyMillisecondsAfterDash()
        {
            var stamina = new StaminaModel(Baseline);
            stamina.TrySpendDash();
            stamina.NotifyDashEnded();

            // 74 个 0.01 秒 = 0.74 秒，还在 0.75 秒延迟内，一点都不该恢复。
            Step(stamina, 74, 0.01f);
            Assert.That(stamina.Current, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(stamina.RegenBlockedSeconds, Is.GreaterThan(0f));

            // 再走 100 个 0.01 秒：第 1 个用完剩余延迟，其余 99 个按每秒 5 点恢复。
            Step(stamina, 100, 0.01f);
            Assert.That(stamina.Current, Is.EqualTo(10f + (0.99f * 5f)).Within(0.02f));
        }

        [Test]
        public void BeingHitAddsAnExtraHalfSecondPause()
        {
            var withoutHit = new StaminaModel(Baseline);
            withoutHit.TrySpendDash();
            withoutHit.NotifyDashEnded();
            Step(withoutHit, 125, 0.01f);

            var withHit = new StaminaModel(Baseline);
            withHit.TrySpendDash();
            withHit.NotifyDashEnded();
            withHit.NotifyDamaged();
            Step(withHit, 125, 0.01f);

            // 同样 1.25 秒，受击那一侧恰好少恢复 0.5 秒的量。
            Assert.That(withoutHit.Current - withHit.Current, Is.EqualTo(0.5f * 5f).Within(0.02f));
        }

        [Test]
        public void DashRejectionSurfacesAsAPresentationReason()
        {
            var core = new PlayerCore(PlayerTuning.CreateBaseline());

            // 连续两次冲刺把体力打空，第三次必须被拒绝并写出原因。
            DoDash(core);
            DoDash(core);

            var output = TapSprint(core);
            Assert.That(output.Rejection, Is.EqualTo(ActionRejection.InsufficientStamina));
            Assert.That(output.Action, Is.Not.EqualTo(ActionState.Dash));
        }

        [Test]
        public void ReviveRestoresStaminaToMaximum()
        {
            var core = new PlayerCore(PlayerTuning.CreateBaseline());
            DoDash(core);
            Assert.That(core.Stamina, Is.LessThan(20f));

            core.Respawn();

            Assert.That(core.Stamina, Is.EqualTo(20f), "死亡后体力恢复至最大值 20。");
        }

        /// <summary>按固定次数推进，用整数计次避免浮点累加导致多跑或少跑一帧。</summary>
        internal static void Step(StaminaModel stamina, int ticks, float step)
        {
            for (var i = 0; i < ticks; i++)
            {
                stamina.Tick(step);
            }
        }

        /// <summary>模拟一次完整的 Shift 点按：按下一帧、在阈值内释放。</summary>
        internal static PlayerFrameOutput TapSprint(PlayerCore core)
        {
            core.Tick(PlayerInputFrame.Idle.WithSprint(true), 0.016f);
            return core.Tick(PlayerInputFrame.Idle.WithSprint(false), 0.016f);
        }

        internal static void DoDash(PlayerCore core)
        {
            TapSprint(core);
            var tuning = core.Tuning;
            var elapsed = 0f;
            while (elapsed < tuning.Dash.RealDurationSeconds + 0.1f)
            {
                core.Tick(PlayerInputFrame.Idle, 0.016f);
                elapsed += 0.016f;
            }
        }
    }
}
