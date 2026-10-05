namespace Naraka.Features.Combat.Model
{
    /// <summary>
    /// 权威伤害公式。玩家与怪物共用同一份实现，因此不可能出现
    /// "玩家这边按一套算、怪物那边按另一套算"的分叉。
    ///
    /// <code>
    /// RawDamage    = FinalAttack × SkillMultiplier
    /// AfterDefense = RawDamage × 100 / (100 + Defense)
    /// </code>
    ///
    /// 结算顺序固定为：先扣护甲耐久，溢出部分再扣生命，生命归零进入死亡。
    /// 霸体不免伤，重生保护直接免伤，死亡状态拒绝后续伤害 —— 这三条属于状态规则，
    /// 由各自的 Vitals 与状态机负责，不在公式里。
    /// </summary>
    public static class DamageFormula
    {
        /// <summary>扣防御之前的伤害。</summary>
        public static float Raw(float finalAttack, float skillMultiplier)
        {
            if (finalAttack <= 0f || skillMultiplier <= 0f)
            {
                return 0f;
            }

            return finalAttack * skillMultiplier;
        }

        /// <summary>
        /// 扣防御之后的伤害。防御为 0 时等于原值，这正是公式的边界情况，
        /// 因此不需要为"没有防御"写一条特例分支。
        /// </summary>
        public static float AfterDefense(float rawDamage, float defense)
        {
            if (rawDamage <= 0f)
            {
                return 0f;
            }

            // 负防御没有定义，钳到 0：否则 defense = -100 会造成除零。
            var effective = defense < 0f ? 0f : defense;
            return rawDamage * 100f / (100f + effective);
        }

        /// <summary>一步到位：攻击力、倍率与防御直接得到最终伤害。</summary>
        public static float Resolve(float finalAttack, float skillMultiplier, float defense) =>
            AfterDefense(Raw(finalAttack, skillMultiplier), defense);
    }

    /// <summary>护甲与生命的扣除结果。护甲耐久归零不销毁，可以在大厅修理。</summary>
    public readonly struct ArmorAbsorption
    {
        public ArmorAbsorption(float armorLost, float healthLost)
        {
            ArmorLost = armorLost;
            HealthLost = healthLost;
        }

        public float ArmorLost { get; }

        public float HealthLost { get; }

        public float Total => ArmorLost + HealthLost;

        /// <summary>
        /// 先扣护甲、溢出扣生命。护甲与生命都由调用方保管，这里只做纯计算，
        /// 因此玩家与怪物可以共用同一条吸收规则。
        /// </summary>
        public static ArmorAbsorption Absorb(float amount, float armor, float health)
        {
            if (amount <= 0f)
            {
                return default;
            }

            var armorLost = amount < armor ? amount : (armor < 0f ? 0f : armor);
            var overflow = amount - armorLost;
            var healthLost = overflow < health ? overflow : (health < 0f ? 0f : health);
            return new ArmorAbsorption(armorLost, healthLost);
        }
    }
}
