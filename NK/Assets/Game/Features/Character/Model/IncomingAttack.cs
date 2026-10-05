using Naraka.Features.Combat.Model;

namespace Naraka.Features.Character.Model
{
    /// <summary>
    /// 打到玩家身上的一次攻击。这是纯数据：它带着"这一刀长什么样"进来，
    /// 至于能不能被反击、扣多少、会不会硬直，全部由 <see cref="PlayerCore"/> 判定。
    /// </summary>
    public readonly struct IncomingAttack
    {
        public IncomingAttack(
            float rawDamage,
            AttackColorTag colorTag = AttackColorTag.None,
            bool counterable = false,
            int attackerId = 0)
        {
            RawDamage = rawDamage;
            ColorTag = colorTag;
            // 普通攻击永远不可反击。这条规则在入口处就钉死，
            // 避免任何调用方通过"传一个 counterable=true 的普通攻击"绕过它。
            Counterable = counterable && colorTag == AttackColorTag.Gold;
            AttackerId = attackerId;
        }

        /// <summary>扣防御之前的伤害。</summary>
        public float RawDamage { get; }

        public AttackColorTag ColorTag { get; }

        /// <summary>是否带 CounterableSkill 标签。</summary>
        public bool Counterable { get; }

        /// <summary>攻击者的稳定标识。反击成功后由它决定谁进入处决窗口。</summary>
        public int AttackerId { get; }

        /// <summary>没有颜色的普通攻击。</summary>
        public static IncomingAttack Normal(float rawDamage, int attackerId = 0) =>
            new IncomingAttack(rawDamage, AttackColorTag.None, false, attackerId);

        /// <summary>金色可反击技能。</summary>
        public static IncomingAttack Gold(float rawDamage, int attackerId = 0) =>
            new IncomingAttack(rawDamage, AttackColorTag.Gold, true, attackerId);

        /// <summary>红色不可反击技能。</summary>
        public static IncomingAttack Red(float rawDamage, int attackerId = 0) =>
            new IncomingAttack(rawDamage, AttackColorTag.Red, false, attackerId);
    }

    /// <summary>一次来袭攻击的结算结果。</summary>
    public readonly struct IncomingAttackResult
    {
        public IncomingAttackResult(bool countered, VitalsDamageResult damage, int counteredAttackerId)
        {
            Countered = countered;
            Damage = damage;
            CounteredAttackerId = counteredAttackerId;
        }

        /// <summary>被反击掉了，没有造成任何伤害。</summary>
        public bool Countered { get; }

        public VitalsDamageResult Damage { get; }

        /// <summary>被反击的攻击者标识，未反击时为 0。</summary>
        public int CounteredAttackerId { get; }
    }
}
