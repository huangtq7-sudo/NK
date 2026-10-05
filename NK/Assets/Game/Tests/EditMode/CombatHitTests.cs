using Naraka.Features.Character.Model;
using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using NUnit.Framework;

namespace Naraka.P2.Tests
{
    /// <summary>灰盒命中链：阵营过滤、单次攻击去重与伤害结算。</summary>
    public sealed class CombatHitTests
    {
        private const int Attacker = 1001;
        private const int Target = 2002;

        private static HitRequest Request(int sequence, float damage = 100f, int targetId = Target) =>
            new HitRequest(
                new HitId(Attacker, sequence), Faction.Player, targetId, Faction.Enemy, damage);

        [Test]
        public void ASingleAttackHitsAGivenTargetOnlyOnce()
        {
            var resolver = new HitResolver();
            var dummy = new DamageableModel(1000f);

            var first = resolver.Resolve(Request(1), Apply(dummy));
            var second = resolver.Resolve(Request(1), Apply(dummy));
            var third = resolver.Resolve(Request(1), Apply(dummy));

            Assert.That(first.Accepted, Is.True);
            Assert.That(second.Accepted, Is.False);
            Assert.That(second.Rejection, Is.EqualTo(HitRejection.AlreadyHitByThisAttack));
            Assert.That(third.Accepted, Is.False);
            Assert.That(dummy.Health, Is.EqualTo(900f), "同一次攻击只能扣一次血。");
        }

        [Test]
        public void ANewAttackHitsTheSameTargetAgain()
        {
            var resolver = new HitResolver();
            var dummy = new DamageableModel(1000f);

            resolver.Resolve(Request(1), Apply(dummy));
            resolver.ReleaseAttack(new HitId(Attacker, 1));
            var second = resolver.Resolve(Request(2), Apply(dummy));

            Assert.That(second.Accepted, Is.True);
            Assert.That(dummy.Health, Is.EqualTo(800f));
        }

        [Test]
        public void OneAttackCanHitSeveralDistinctTargets()
        {
            var resolver = new HitResolver();
            var a = new DamageableModel(1000f);
            var b = new DamageableModel(1000f);

            Assert.That(resolver.Resolve(Request(1, targetId: 10), Apply(a)).Accepted, Is.True);
            Assert.That(resolver.Resolve(Request(1, targetId: 11), Apply(b)).Accepted, Is.True);
            Assert.That(a.Health, Is.EqualTo(900f));
            Assert.That(b.Health, Is.EqualTo(900f));
        }

        [Test]
        public void SameFactionIsRejected()
        {
            var resolver = new HitResolver();
            var dummy = new DamageableModel(1000f);
            var friendlyFire = new HitRequest(
                new HitId(Attacker, 1), Faction.Player, Target, Faction.Player, 100f);

            var result = resolver.Resolve(friendlyFire, Apply(dummy));

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Rejection, Is.EqualTo(HitRejection.SameFaction));
            Assert.That(dummy.Health, Is.EqualTo(1000f));
        }

        [Test]
        public void InvalidHitIdIsRejected()
        {
            var resolver = new HitResolver();
            var dummy = new DamageableModel(1000f);
            var invalid = new HitRequest(default, Faction.Player, Target, Faction.Enemy, 100f);

            var result = resolver.Resolve(invalid, Apply(dummy));

            Assert.That(result.Rejection, Is.EqualTo(HitRejection.InvalidHitId));
        }

        [Test]
        public void DeadTargetsStopTakingDamage()
        {
            var resolver = new HitResolver();
            var dummy = new DamageableModel(100f);

            var lethal = resolver.Resolve(Request(1, 250f), Apply(dummy));
            resolver.ReleaseAttack(new HitId(Attacker, 1));
            var afterDeath = resolver.Resolve(Request(2, 250f), Apply(dummy));

            Assert.That(lethal.Accepted, Is.True);
            Assert.That(lethal.Killed, Is.True);
            Assert.That(lethal.HealthLost, Is.EqualTo(100f), "扣血不能超过剩余生命。");
            Assert.That(afterDeath.Accepted, Is.False);
            Assert.That(afterDeath.Rejection, Is.EqualTo(HitRejection.TargetAlreadyDead));
        }

        [Test]
        public void AHitAbsorbedEntirelyByArmorStillCounts()
        {
            var resolver = new HitResolver();
            var dummy = new DamageableModel(1000f, maxArmor: 500f);

            var result = resolver.Resolve(Request(1, 200f), Apply(dummy));

            Assert.That(result.Accepted, Is.True, "护甲吃满伤害也是一次真实命中。");
            Assert.That(result.ArmorLost, Is.EqualTo(200f));
            Assert.That(result.HealthLost, Is.EqualTo(0f));
        }

        [Test]
        public void ACounteredHitDealsNoDamageAndIsReported()
        {
            var resolver = new HitResolver();
            var counteredIds = 0;
            resolver.HitLanded += _ => counteredIds++;

            var result = resolver.Resolve(
                Request(1, 200f),
                (in HitRequest _) => DamageApplication.AsCountered());

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Rejection, Is.EqualTo(HitRejection.Countered));
            Assert.That(counteredIds, Is.Zero, "被反击的一刀不产生命中事实。");
        }

        [Test]
        public void HitLandedCarriesTheTargetFactionSoTheHudCanTellWhoWasHit()
        {
            var resolver = new HitResolver();
            var dummy = new DamageableModel(1000f);
            HitLandedEvent captured = default;
            resolver.HitLanded += hit => captured = hit;

            resolver.Resolve(Request(1, 120f), Apply(dummy));

            Assert.That(captured.TargetFaction, Is.EqualTo(Faction.Enemy));
            Assert.That(captured.Damage, Is.EqualTo(120f));
        }

        [Test]
        public void ANormalAttackCanNeverBeFlaggedCounterable()
        {
            var request = new HitRequest(
                new HitId(Attacker, 1),
                Faction.Enemy,
                Target,
                Faction.Player,
                100f,
                AttackColorTag.None,
                counterable: true);

            Assert.That(request.Counterable, Is.False, "普通攻击永远不可反击。");
        }

        [Test]
        public void ARedSkillCanNeverBeFlaggedCounterable()
        {
            var request = new HitRequest(
                new HitId(Attacker, 1),
                Faction.Enemy,
                Target,
                Faction.Player,
                100f,
                AttackColorTag.Red,
                counterable: true);

            Assert.That(request.Counterable, Is.False, "红色技能不可反击。");
        }

        [Test]
        public void ReleasingAnAttackFreesItsDeduplicationEntry()
        {
            var registry = new HitRegistry();
            var hit = new HitId(Attacker, 7);

            Assert.That(registry.TryRegister(hit, Target), Is.True);
            Assert.That(registry.TryRegister(hit, Target), Is.False);
            Assert.That(registry.TrackedAttackCount, Is.EqualTo(1));

            registry.Release(hit);

            Assert.That(registry.TrackedAttackCount, Is.EqualTo(0));
            Assert.That(registry.HasHit(hit, Target), Is.False);
        }

        [Test]
        public void ArmorAbsorbsDamageBeforeHealth()
        {
            // 防御 0：最终伤害等于原始伤害，这是公式的边界情况。
            var vitals = new PlayerVitals(new VitalsTuning(1000f, 500f, 0f, 0.5f));

            var partial = vitals.ApplyRawDamage(200f);
            Assert.That(partial.ArmorLost, Is.EqualTo(200f));
            Assert.That(partial.HealthLost, Is.EqualTo(0f));

            var overflow = vitals.ApplyRawDamage(400f);
            Assert.That(overflow.ArmorLost, Is.EqualTo(300f), "护甲耗尽后溢出部分才扣生命。");
            Assert.That(overflow.HealthLost, Is.EqualTo(100f));
            Assert.That(vitals.Armor, Is.EqualTo(0f), "护甲归零但不销毁。");
        }

        private static ApplyDamageDelegate Apply(DamageableModel model) => (in HitRequest request) =>
        {
            var result = model.ApplyRawDamage(request.RawDamage);
            return new DamageApplication(result.ArmorLost, result.HealthLost, result.Died);
        };
    }
}
