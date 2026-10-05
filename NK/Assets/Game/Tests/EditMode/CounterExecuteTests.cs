using Naraka.Features.Character.Model;
using Naraka.Features.Combat.Model;
using NUnit.Framework;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// 反击与处决的领域规则。
    ///
    /// 全部在纯 Model 层断言：0.2 秒判定窗、0.5 秒失败后摇、只接受金色可反击技能、
    /// 成功后 2 秒霸体与目标 1.5 秒处决窗口、处决全程无敌且伤害为 FinalAttack × 1.5。
    /// </summary>
    public sealed class CounterExecuteTests
    {
        private const float Frame = 1f / 60f;

        private static PlayerCore NewCore()
        {
            var core = new PlayerCore(PlayerTuning.CreateBaseline());
            // 跳过 3 秒重生保护，否则任何来袭攻击都被直接免掉。
            Hold(core, PlayerInputFrame.Idle, 3.1f);
            return core;
        }

        private static void Hold(PlayerCore core, PlayerInputFrame input, float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                core.Tick(input, Frame);
                elapsed += Frame;
            }
        }

        [Test]
        public void CounterBaselineMatchesTheConfirmedRules()
        {
            var counter = PlayerTuning.CreateBaseline().Counter;

            Assert.That(counter.WindowSeconds, Is.EqualTo(0.2f), "反击判定窗为 0.2 秒。");
            Assert.That(counter.FailRecoverySeconds, Is.EqualTo(0.5f), "失败后摇为 0.5 秒。");
            Assert.That(counter.SuccessSuperArmorSeconds, Is.EqualTo(2f));
            Assert.That(counter.ExecuteWindowSeconds, Is.EqualTo(1.5f));
            Assert.That(PlayerTuning.CreateBaseline().Execute.DamageMultiplier, Is.EqualTo(1.5f));
        }

        [Test]
        public void SpaceStartsACounterWithoutStaminaOrCooldown()
        {
            var core = NewCore();
            var staminaBefore = core.Stamina;

            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);

            Assert.That(core.Action, Is.EqualTo(ActionState.Counter));
            Assert.That(core.IsCounterWindowOpen, Is.True);
            Assert.That(core.Stamina, Is.EqualTo(staminaBefore).Within(0.001f), "反击不消耗体力。");
        }

        [Test]
        public void TheCounterWindowClosesAfterTwoTenthsOfASecond()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);

            Hold(core, PlayerInputFrame.Idle, 0.25f);

            Assert.That(core.IsCounterWindowOpen, Is.False, "判定窗只有 0.2 秒。");
            Assert.That(core.Action, Is.EqualTo(ActionState.Counter), "窗口关闭后进入失败后摇。");
        }

        [Test]
        public void AFailedCounterCostsHalfASecondOfRecovery()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);

            Hold(core, PlayerInputFrame.Idle, 0.65f);
            Assert.That(core.Action, Is.EqualTo(ActionState.Counter), "0.2 + 0.5 秒之前还在后摇里。");

            Hold(core, PlayerInputFrame.Idle, 0.12f);
            Assert.That(core.Action, Is.EqualTo(ActionState.None));
        }

        [Test]
        public void ThereIsNoCooldownBetweenCounters()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);
            Hold(core, PlayerInputFrame.Idle, 0.75f);
            Assert.That(core.Action, Is.EqualTo(ActionState.None));

            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);

            Assert.That(core.Action, Is.EqualTo(ActionState.Counter), "反击没有冷却。");
        }

        [Test]
        public void OnlyCounterableSkillsCanBeCountered()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);

            var result = core.ApplyIncomingAttack(IncomingAttack.Gold(200f, attackerId: 42));

            Assert.That(result.Countered, Is.True);
            Assert.That(result.Damage.Total, Is.Zero, "反击成功不吃任何伤害。");
            Assert.That(core.ConsumeCounterSuccess(out var attackerId), Is.True);
            Assert.That(attackerId, Is.EqualTo(42));
        }

        [Test]
        public void NormalAttacksCannotBeCountered()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);

            var result = core.ApplyIncomingAttack(IncomingAttack.Normal(200f, attackerId: 42));

            Assert.That(result.Countered, Is.False, "普通攻击不能反击。");
            Assert.That(result.Damage.Total, Is.GreaterThan(0f));
        }

        [Test]
        public void RedSkillsCannotBeCountered()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);

            var result = core.ApplyIncomingAttack(IncomingAttack.Red(200f, attackerId: 42));

            Assert.That(result.Countered, Is.False, "红色不可反击技能不能反击。");
            Assert.That(result.Damage.Total, Is.GreaterThan(0f));
        }

        [Test]
        public void AGoldSkillOutsideTheWindowIsNotCountered()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);
            Hold(core, PlayerInputFrame.Idle, 0.25f);

            var result = core.ApplyIncomingAttack(IncomingAttack.Gold(200f, attackerId: 42));

            Assert.That(result.Countered, Is.False, "窗口关闭之后打进来的金色技能照常生效。");
        }

        [Test]
        public void ASuccessfulCounterGrantsTwoSecondsOfSuperArmor()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);
            core.ApplyIncomingAttack(IncomingAttack.Gold(200f, attackerId: 42));

            Assert.That(
                (core.Flags & PlayerOverlayFlags.SuperArmor),
                Is.Not.EqualTo(PlayerOverlayFlags.None));
            Assert.That(core.Action, Is.EqualTo(ActionState.None), "成功之后不再走失败后摇。");

            // 霸体期间挨打不进硬直，但照常掉血。
            core.ApplyIncomingAttack(IncomingAttack.Normal(200f));
            Assert.That(core.Reaction, Is.EqualTo(ReactionState.None));

            Hold(core, PlayerInputFrame.Idle, 2.1f);
            Assert.That(
                (core.Flags & PlayerOverlayFlags.SuperArmor),
                Is.EqualTo(PlayerOverlayFlags.None),
                "霸体只有 2 秒。");
        }

        [Test]
        public void CounterSuccessIsReportedOnlyOnce()
        {
            var core = NewCore();
            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);
            core.ApplyIncomingAttack(IncomingAttack.Gold(200f, attackerId: 7));

            Assert.That(core.ConsumeCounterSuccess(out _), Is.True);
            Assert.That(
                core.ConsumeCounterSuccess(out _),
                Is.False,
                "同一次反击不能让目标反复进入处决窗口。");
        }

        [Test]
        public void ExecuteReplacesTheNormalAttackWhenATargetIsExecutable()
        {
            var core = NewCore();
            var ready = PlayerInputFrame.Idle.WithExecutableTarget(true);

            core.Tick(ready.WithAttack(true), Frame);
            core.Tick(ready.WithAttack(false), Frame);

            Assert.That(core.Action, Is.EqualTo(ActionState.Execute));
        }

        [Test]
        public void WithoutAnExecutableTargetTheSameInputStartsTheCombo()
        {
            var core = NewCore();

            core.Tick(PlayerInputFrame.Idle.WithAttack(true), Frame);
            core.Tick(PlayerInputFrame.Idle.WithAttack(false), Frame);

            Assert.That(core.Action, Is.EqualTo(ActionState.AttackCombo1));
        }

        [Test]
        public void ExecuteIsInvulnerableForItsWholeDuration()
        {
            var core = NewCore();
            var ready = PlayerInputFrame.Idle.WithExecutableTarget(true);
            core.Tick(ready.WithAttack(true), Frame);
            core.Tick(ready.WithAttack(false), Frame);
            Assert.That(core.Action, Is.EqualTo(ActionState.Execute));

            var healthBefore = core.Health;
            var armorBefore = core.Armor;
            var elapsed = 0f;
            var duration = core.Tuning.Execute.DurationSeconds;
            while (elapsed < duration - Frame)
            {
                Assert.That(
                    (core.Flags & PlayerOverlayFlags.Invulnerable),
                    Is.Not.EqualTo(PlayerOverlayFlags.None));
                core.ApplyIncomingAttack(IncomingAttack.Red(400f));
                core.Tick(PlayerInputFrame.Idle, Frame);
                elapsed += Frame;
            }

            Assert.That(core.Health, Is.EqualTo(healthBefore), "处决过程全程无敌。");
            Assert.That(core.Armor, Is.EqualTo(armorBefore));
        }

        [Test]
        public void ExecuteDealsOneAndAHalfTimesFinalAttack()
        {
            var core = NewCore();
            var ready = PlayerInputFrame.Idle.WithExecutableTarget(true);
            core.Tick(ready.WithAttack(true), Frame);
            core.Tick(ready.WithAttack(false), Frame);

            var duration = core.Tuning.Execute.DurationSeconds;
            var observed = 0f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                var output = core.Tick(PlayerInputFrame.Idle, Frame);
                if (output.HitWindowOpen)
                {
                    observed = output.AttackDamage;
                }

                elapsed += Frame;
            }

            Assert.That(
                observed,
                Is.EqualTo(core.Tuning.FinalAttack * 1.5f).Within(0.01f),
                "处决基础伤害为 FinalAttack × 1.5。");
        }

        [Test]
        public void AttackDamageUsesTheAuthoritativeFormula()
        {
            var tuning = PlayerTuning.CreateBaseline();

            Assert.That(
                tuning.Combo.Step1.RawDamage(tuning.FinalAttack),
                Is.EqualTo(tuning.FinalAttack * tuning.Combo.Step1.SkillMultiplier).Within(0.001f));
            Assert.That(
                tuning.SkillF.Action.SkillMultiplier,
                Is.EqualTo(1.2f),
                "F 技能倍率与 hero_skills.csv 的 1.2 一致。");
            Assert.That(
                tuning.SkillV.Action.SkillMultiplier,
                Is.EqualTo(2.5f),
                "V 技能倍率与 hero_skills.csv 的 2.5 一致。");
        }

        [Test]
        public void CounterCannotStartWhileDead()
        {
            var core = NewCore();
            core.ApplyDamage((core.MaxHealth + core.MaxArmor) * (100f + core.Defense) / 100f);
            Assert.That(core.IsDead, Is.True);

            core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);

            Assert.That(core.Action, Is.EqualTo(ActionState.None));
        }

        [Test]
        public void CounterCarriesNoColorOfItsOwnSoItCannotBeChained()
        {
            // 反击不是攻击：它没有命中窗，也不产生伤害。
            var core = NewCore();
            var output = core.Tick(PlayerInputFrame.Idle.WithCounter(true), Frame);

            Assert.That(output.HitWindowOpen, Is.False);
            Assert.That(output.AttackDamage, Is.Zero);
        }
    }
}
