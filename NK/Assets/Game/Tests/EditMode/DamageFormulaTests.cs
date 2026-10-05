using Naraka.Features.Character.Model;
using Naraka.Features.Combat.Model;
using Naraka.Features.Monster.Model;
using NUnit.Framework;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// 权威伤害公式与结算顺序。
    ///
    /// <code>
    /// RawDamage    = FinalAttack × SkillMultiplier
    /// AfterDefense = RawDamage × 100 / (100 + Defense)
    /// </code>
    ///
    /// 玩家与怪物共用同一份实现，因此这里的断言同时守住两边。
    /// </summary>
    public sealed class DamageFormulaTests
    {
        [Test]
        public void RawDamageIsAttackTimesMultiplier()
        {
            Assert.That(DamageFormula.Raw(220f, 1.5f), Is.EqualTo(330f).Within(0.001f));
            Assert.That(DamageFormula.Raw(100f, 1f), Is.EqualTo(100f).Within(0.001f));
        }

        [Test]
        public void ZeroDefenseLeavesDamageUnchanged()
        {
            // 防御 0 是公式的边界情况，不需要特例分支。
            Assert.That(DamageFormula.AfterDefense(300f, 0f), Is.EqualTo(300f).Within(0.001f));
        }

        [Test]
        public void DefenseReducesDamageByTheAuthoritativeRatio()
        {
            // 防御 100 正好把伤害减半：100/(100+100)。
            Assert.That(DamageFormula.AfterDefense(300f, 100f), Is.EqualTo(150f).Within(0.001f));
            // 防御 20：300 × 100/120 = 250。
            Assert.That(DamageFormula.AfterDefense(300f, 20f), Is.EqualTo(250f).Within(0.001f));
        }

        [Test]
        public void DefenseNeverIncreasesDamage()
        {
            // 负防御没有定义；钳到 0 而不是让它变成增伤或除零。
            Assert.That(DamageFormula.AfterDefense(300f, -500f), Is.EqualTo(300f).Within(0.001f));
        }

        [Test]
        public void ArmorAbsorbsFirstAndOverflowGoesToHealth()
        {
            var absorbed = ArmorAbsorption.Absorb(120f, armor: 100f, health: 500f);

            Assert.That(absorbed.ArmorLost, Is.EqualTo(100f));
            Assert.That(absorbed.HealthLost, Is.EqualTo(20f));
            Assert.That(absorbed.Total, Is.EqualTo(120f));
        }

        [Test]
        public void AbsorptionNeverExceedsWhatIsLeft()
        {
            var absorbed = ArmorAbsorption.Absorb(10000f, armor: 100f, health: 500f);

            Assert.That(absorbed.ArmorLost, Is.EqualTo(100f));
            Assert.That(absorbed.HealthLost, Is.EqualTo(500f), "扣血不能超过剩余生命。");
        }

        [Test]
        public void PlayerAndMonsterUseTheSameFormula()
        {
            var player = new PlayerVitals(new VitalsTuning(1000f, 0f, 20f, 0.5f));
            var monster = new MonsterVitals(MonsterTuning.CreateGrayboxWolf());

            var playerResult = player.ApplyRawDamage(300f);

            // 灰盒狼的防御也是 20，因此同一份原始伤害必须得到同一个结果。
            Assert.That(monster.Defense, Is.EqualTo(20f));
            var monsterResult = monster.ApplyRawDamage(300f);

            Assert.That(playerResult.Total, Is.EqualTo(250f).Within(0.01f));
            Assert.That(monsterResult.Total, Is.EqualTo(250f).Within(0.01f));
        }

        [Test]
        public void SpawnProtectionIsFullImmunity()
        {
            var core = new PlayerCore(PlayerTuning.CreateBaseline());
            core.Respawn();

            var result = core.ApplyDamage(500f);

            Assert.That(result.Total, Is.Zero, "重生保护期间直接免伤。");
            Assert.That(core.Health, Is.EqualTo(core.MaxHealth));
        }

        [Test]
        public void SuperArmorDoesNotReduceDamage()
        {
            var tuning = PlayerTuning.CreateBaseline();
            var withArmor = new PlayerCore(tuning);
            var without = new PlayerCore(tuning);

            // V 技能立刻带上蓝色霸体。
            Advance(withArmor, 3.1f);
            Advance(without, 3.1f);
            withArmor.Tick(PlayerInputFrame.Idle.WithSkillV(true), 1f / 60f);
            Assert.That(
                (withArmor.Flags & PlayerOverlayFlags.SuperArmor),
                Is.Not.EqualTo(PlayerOverlayFlags.None));

            var armored = withArmor.ApplyDamage(400f);
            var bare = without.ApplyDamage(400f);

            Assert.That(armored.Total, Is.EqualTo(bare.Total).Within(0.01f), "霸体不免伤。");
            Assert.That(withArmor.Reaction, Is.EqualTo(ReactionState.None), "霸体免硬直。");
            Assert.That(without.Reaction, Is.EqualTo(ReactionState.HitStun));
        }

        [Test]
        public void DeadTargetsRejectFurtherDamage()
        {
            var core = new PlayerCore(PlayerTuning.CreateBaseline());
            Advance(core, 3.1f);

            core.ApplyDamage((core.MaxHealth + core.MaxArmor) * (100f + core.Defense) / 100f);
            Assert.That(core.IsDead, Is.True);

            var after = core.ApplyDamage(500f);
            Assert.That(after.Total, Is.Zero, "死亡状态拒绝后续伤害。");
        }

        private static void Advance(PlayerCore core, float seconds)
        {
            const float step = 1f / 60f;
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                core.Tick(PlayerInputFrame.Idle, step);
                elapsed += step;
            }
        }
    }
}
