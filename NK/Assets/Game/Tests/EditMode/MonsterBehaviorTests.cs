using Naraka.Features.AI.Model;
using Naraka.Features.Combat.Model;
using Naraka.Features.Monster.Model;
using NUnit.Framework;

namespace Naraka.P2.Tests
{
    /// <summary>
    /// 暮影妖狼的行为规则：巡逻、感知、追击、普通攻击、脱战、休眠、
    /// 50% 阶段、红色吐息的解锁条件，以及"当前动作只有一份真相"。
    /// </summary>
    public sealed class MonsterBehaviorTests
    {
        private const float Frame = 1f / 60f;

        private static MonsterCore NewWolf() => new MonsterCore(MonsterTuning.CreateGrayboxWolf());

        private static MonsterFrameOutput Hold(MonsterCore core, MonsterSenses senses, float seconds)
        {
            var elapsed = 0f;
            var output = default(MonsterFrameOutput);
            while (elapsed < seconds)
            {
                output = core.Tick(in senses, Frame);
                elapsed += Frame;
            }

            return output;
        }

        [Test]
        public void GrayboxValuesAreMarkedAsUnbalanced()
        {
            var tuning = MonsterTuning.CreateGrayboxWolf();

            Assert.That(tuning.MonsterId, Is.EqualTo("monster_wolf_duskshadow"));
            Assert.That(tuning.PhaseHealthRatio, Is.EqualTo(0.5f));
            Assert.That(tuning.DecisionsPerSecond, Is.InRange(5, 10), "决策频率必须落在 5–10Hz。");
        }

        [Test]
        public void ItPatrolsWhenThereIsNoTarget()
        {
            var core = NewWolf();

            Hold(core, MonsterSenses.None, 0.5f);

            Assert.That(core.Intent, Is.EqualTo(MonsterIntent.Patrol));
            Assert.That(core.IsDormant, Is.False, "没有玩家时应该巡逻，而不是原地睡着。");
        }

        [Test]
        public void ItGoesDormantWhenThePlayerIsVeryFarAway()
        {
            var core = NewWolf();

            Hold(core, MonsterSenses.ToTarget(60f), 0.5f);

            Assert.That(core.Intent, Is.EqualTo(MonsterIntent.Dormant));
        }

        [Test]
        public void DormantLowersTheDecisionFrequency()
        {
            var core = NewWolf();
            var active = core.DecisionIntervalSeconds;

            Hold(core, MonsterSenses.ToTarget(60f), 1.5f);

            Assert.That(
                core.DecisionIntervalSeconds,
                Is.GreaterThan(active),
                "远离玩家之后必须降低决策频率，而不是只在注释里说降低。");
        }

        [Test]
        public void PerceivingThePlayerLeadsToChase()
        {
            var core = NewWolf();

            // 站在感知圈内但够不到：第一拍是"发现"，之后才是追击。
            // 只跑一个决策周期（6Hz 即 0.167 秒），否则第二次决策就已经转成追击了。
            var first = Hold(core, MonsterSenses.ToTarget(10f), 0.1f);
            Assert.That(first.Intent, Is.EqualTo(MonsterIntent.Perceive));

            var later = Hold(core, MonsterSenses.ToTarget(10f), 0.5f);
            Assert.That(later.Intent, Is.EqualTo(MonsterIntent.Chase));
            Assert.That(later.MoveTarget, Is.EqualTo(MonsterMoveTarget.Player));
            Assert.That(later.MoveSpeed, Is.EqualTo(core.Tuning.ChaseSpeed).Within(0.001f));
            Assert.That(later.FaceTarget, Is.True);
        }

        [Test]
        public void ItAttacksWhenThePlayerIsInRange()
        {
            var core = NewWolf();

            Hold(core, MonsterSenses.ToTarget(10f), 0.5f);
            Hold(core, MonsterSenses.ToTarget(2f), 0.4f);

            Assert.That(core.Action, Is.EqualTo(MonsterActionState.Attack));
        }

        [Test]
        public void ANormalAttackIsNeverCounterable()
        {
            var core = NewWolf();
            Hold(core, MonsterSenses.ToTarget(10f), 0.5f);

            var senses = MonsterSenses.ToTarget(2f);
            var attack = core.Tuning.NormalAttack;
            var output = Hold(core, senses, 0.4f + attack.HitWindowStart + 0.02f);

            Assert.That(core.Action, Is.EqualTo(MonsterActionState.Attack));
            Assert.That(output.ColorTag, Is.EqualTo(AttackColorTag.None));
            Assert.That(output.Counterable, Is.False, "普通攻击不能被反击。");
        }

        [Test]
        public void TheBreathIsLockedAboveHalfHealth()
        {
            var core = NewWolf();
            var senses = MonsterSenses.ToTarget(6f);

            Hold(core, senses, 2f);

            Assert.That(core.Phase, Is.EqualTo(MonsterPhase.Normal));
            Assert.That(core.SelectSkill(), Is.EqualTo(-1), "生命高于一半时不得释放吐息。");
            Assert.That(core.Action, Is.Not.EqualTo(MonsterActionState.Skill));
        }

        [Test]
        public void TheBreathUnlocksAtOrBelowHalfHealth()
        {
            var core = NewWolf();
            var senses = MonsterSenses.ToTarget(6f);
            Hold(core, senses, 0.5f);

            // 打到半血以下，用足够大的原始伤害跨过防御与护甲。
            core.ApplyRawDamage(RawToRemoveHalf(core));
            Assert.That(core.Phase, Is.EqualTo(MonsterPhase.Enraged));

            var output = Hold(core, senses, 2f);

            Assert.That(core.Action, Is.EqualTo(MonsterActionState.Skill));
            Assert.That(output.ColorTag, Is.EqualTo(AttackColorTag.Red));
            Assert.That(output.Counterable, Is.False, "红色技能不可反击。");
        }

        [Test]
        public void TheWarningComesBeforeTheHitWindow()
        {
            var skill = MonsterTuning.CreateGrayboxWolf().Skill(0);

            Assert.That(skill.WarningSeconds, Is.GreaterThan(0f));
            Assert.That(skill.IsWarningActive(0.01f), Is.True);
            Assert.That(skill.IsHitWindowOpen(0.01f), Is.False);
            Assert.That(
                skill.HitWindowStart,
                Is.GreaterThanOrEqualTo(skill.WarningSeconds),
                "预警必须先于伤害窗口。");
            Assert.That(skill.IsWarningActive(skill.HitWindowStart + 0.01f), Is.False);
            Assert.That(skill.IsHitWindowOpen(skill.HitWindowStart + 0.01f), Is.True);
        }

        [Test]
        public void SkillSelectionRespectsRangeCooldownAndSuppression()
        {
            var core = NewWolf();
            var close = MonsterSenses.ToTarget(6f);
            Hold(core, close, 0.5f);
            core.ApplyRawDamage(RawToRemoveHalf(core));

            // 超出技能射程就选不中它。
            Hold(core, MonsterSenses.ToTarget(20f), 0.3f);
            Assert.That(core.SelectSkill(), Is.EqualTo(-1), "超出射程不得选中技能。");

            // 回到射程内并放完一次，冷却与最近使用抑制都会挡住第二次。
            var skill = core.Tuning.Skill(0);
            Hold(core, close, skill.DurationSeconds + 0.5f);
            Assert.That(core.SelectSkill(), Is.EqualTo(-1), "冷却与最近使用抑制必须生效。");
        }

        [Test]
        public void ItDisengagesWhenThePlayerRunsFarEnoughAway()
        {
            var core = NewWolf();
            Hold(core, MonsterSenses.ToTarget(10f), 0.5f);
            Assert.That(core.Intent, Is.EqualTo(MonsterIntent.Chase));

            // 追出去之后离出生点已经有一段距离，脱战意味着要一路走回去。
            Hold(core, new MonsterSenses(true, true, 30f, 0f, 15f), 0.5f);

            Assert.That(core.Intent, Is.EqualTo(MonsterIntent.Recover));

            // 走回出生点附近之后恢复巡逻。
            Hold(core, new MonsterSenses(true, true, 30f, 0f, 1f), 0.5f);
            Assert.That(core.Intent, Is.EqualTo(MonsterIntent.Patrol));
        }

        [Test]
        public void TheBehaviorTreeCannotOverrideAnActionInProgress()
        {
            var core = NewWolf();
            Hold(core, MonsterSenses.ToTarget(10f), 0.5f);
            Hold(core, MonsterSenses.ToTarget(2f), 0.4f);
            Assert.That(core.Action, Is.EqualTo(MonsterActionState.Attack));

            var decisionsBefore = core.DecisionCount;

            // 玩家突然跑远：行为树想脱战，但正在挥出去的这一下必须打完。
            core.Tick(MonsterSenses.ToTarget(30f), Frame);

            Assert.That(core.Action, Is.EqualTo(MonsterActionState.Attack), "动作层是唯一的当前动作真相。");
            Assert.That(core.DecisionCount, Is.EqualTo(decisionsBefore), "动作进行中不再决策。");
        }

        [Test]
        public void SuperArmorSkipsHitStunButNotDamage()
        {
            var core = NewWolf();
            var senses = MonsterSenses.ToTarget(6f);
            Hold(core, senses, 0.5f);
            core.ApplyRawDamage(RawToRemoveHalf(core));
            Hold(core, senses, 1f);
            Assert.That(core.Action, Is.EqualTo(MonsterActionState.Skill));

            var healthBefore = core.Health;
            var result = core.ApplyRawDamage(200f);

            Assert.That(result.Total, Is.GreaterThan(0f), "霸体不免伤。");
            Assert.That(core.Health, Is.LessThan(healthBefore));
            Assert.That(core.Action, Is.EqualTo(MonsterActionState.Skill), "霸体免硬直。");
        }

        [Test]
        public void TakingDamageWithoutSuperArmorEntersHitStun()
        {
            var core = NewWolf();
            Hold(core, MonsterSenses.ToTarget(10f), 0.5f);

            core.ApplyRawDamage(200f);

            Assert.That(core.Action, Is.EqualTo(MonsterActionState.HitStun));
        }

        [Test]
        public void ThePlayerCanKillTheWolf()
        {
            var core = NewWolf();
            var senses = MonsterSenses.ToTarget(6f);
            Hold(core, senses, 0.2f);

            // 一整套三段连招的原始伤害（220 × (1.0 + 1.2 + 1.6)）。
            var combo = 220f * (1.0f + 1.2f + 1.6f);
            core.ApplyRawDamage(combo);
            core.ApplyRawDamage(combo);

            Assert.That(core.IsDead, Is.True, "玩家必须能杀死暮影妖狼。");
        }

        [Test]
        public void DeadWolvesRefuseEverything()
        {
            var core = NewWolf();
            core.ApplyRawDamage(100000f);
            Assert.That(core.IsDead, Is.True);

            var output = Hold(core, MonsterSenses.ToTarget(2f), 1f);

            Assert.That(output.Intent, Is.EqualTo(MonsterIntent.Dead));
            Assert.That(output.MoveSpeed, Is.Zero);
            Assert.That(output.HitWindowOpen, Is.False);
            Assert.That(core.ApplyRawDamage(500f).Total, Is.Zero, "死亡状态拒绝后续伤害。");
        }

        [Test]
        public void TheExecuteWindowOpensOnlyAfterACounterAndExpires()
        {
            var core = NewWolf();
            Assert.That(core.IsExecutable, Is.False);

            core.BeginExecuteWindow(1.5f);
            Assert.That(core.IsExecutable, Is.True);

            Hold(core, MonsterSenses.ToTarget(2f), 1.6f);

            Assert.That(core.IsExecutable, Is.False, "处决窗口必须会过期。");
        }

        [Test]
        public void DeadWolvesCannotEnterAnExecuteWindow()
        {
            var core = NewWolf();
            core.ApplyRawDamage(100000f);

            core.BeginExecuteWindow(1.5f);

            Assert.That(core.IsExecutable, Is.False);
        }

        [Test]
        public void TheBehaviorTreeDecidesAtTheConfiguredRateNotEveryFrame()
        {
            var tree = new BehaviorTree<int>(
                new ActionNode<int>("Ok", _ => BehaviorStatus.Success), 0.2f);

            var decisions = 0;
            for (var i = 0; i < 60; i++)
            {
                if (tree.Tick(0, Frame))
                {
                    decisions++;
                }
            }

            // 1 秒、0.2 秒一次：首帧立刻决策一次，之后每 0.2 秒一次。
            Assert.That(decisions, Is.InRange(5, 6));
            Assert.That(decisions, Is.LessThan(60), "行为树不能每帧决策。");
        }

        [Test]
        public void SelectorTakesTheFirstNonFailingBranch()
        {
            var order = string.Empty;
            var selector = new SelectorNode<int>(
                "Root",
                new ConditionNode<int>("No", _ => false),
                new ActionNode<int>("Yes", _ =>
                {
                    order += "Y";
                    return BehaviorStatus.Success;
                }),
                new ActionNode<int>("Never", _ =>
                {
                    order += "N";
                    return BehaviorStatus.Success;
                }));

            Assert.That(selector.Tick(0), Is.EqualTo(BehaviorStatus.Success));
            Assert.That(order, Is.EqualTo("Y"), "第一个不失败的分支就是结果，后面的不再执行。");
        }

        /// <summary>刚好足够把怪物打到半血以下的原始伤害。</summary>
        private static float RawToRemoveHalf(MonsterCore core)
        {
            var needed = core.MaxArmor + (core.MaxHealth * 0.55f);
            return needed * (100f + core.Tuning.Defense) / 100f;
        }
    }
}
