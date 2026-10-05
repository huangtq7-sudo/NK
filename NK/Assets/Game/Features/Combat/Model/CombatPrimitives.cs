namespace Naraka.Features.Combat.Model
{
    /// <summary>阵营。命中过滤的第一道条件：同阵营不互相结算。</summary>
    public enum Faction
    {
        Neutral = 0,
        Player = 1,
        Enemy = 2
    }

    /// <summary>
    /// 攻击的颜色语言。它是玩家侧的操作提示，也是反击判定的唯一依据：
    /// 金色可反击、红色不可反击、普通攻击没有颜色且永远不可反击。
    /// </summary>
    public enum AttackColorTag
    {
        /// <summary>普通攻击。没有颜色提示，不能被反击。</summary>
        None = 0,

        /// <summary>金色可反击技能。</summary>
        Gold = 1,

        /// <summary>红色不可反击技能。</summary>
        Red = 2
    }

    /// <summary>
    /// 一次攻击的唯一标识。同一次挥击在多帧内可能反复扫到同一个目标，
    /// 这个 ID 让命中层知道"还是刚才那一刀"，从而只结算一次。
    /// </summary>
    public readonly struct HitId
    {
        public HitId(int ownerId, int attackSequence)
        {
            OwnerId = ownerId;
            AttackSequence = attackSequence;
        }

        public int OwnerId { get; }

        public int AttackSequence { get; }

        public bool IsValid => AttackSequence > 0;

        public override int GetHashCode() => (OwnerId * 397) ^ AttackSequence;

        public override bool Equals(object obj) =>
            obj is HitId other && other.OwnerId == OwnerId && other.AttackSequence == AttackSequence;
    }

    /// <summary>
    /// 一次命中请求。由 Controller 校验，不由 View 或 Animator Event 决定结果。
    ///
    /// <see cref="RawDamage"/> 是 <c>FinalAttack × SkillMultiplier</c> 的结果，
    /// **还没有扣防御**：防御是受击方的属性，因此
    /// <c>AfterDefense = RawDamage × 100 / (100 + Defense)</c> 必须在受击方身上计算。
    /// 把防御留在攻击方会让同一刀对不同目标算出相同伤害。
    /// </summary>
    public readonly struct HitRequest
    {
        public HitRequest(
            HitId hitId,
            Faction attacker,
            int targetId,
            Faction targetFaction,
            float rawDamage,
            AttackColorTag colorTag = AttackColorTag.None,
            bool counterable = false)
        {
            HitId = hitId;
            Attacker = attacker;
            TargetId = targetId;
            TargetFaction = targetFaction;
            RawDamage = rawDamage;
            ColorTag = colorTag;
            // 普通攻击永远不可反击，这条规则不交给配置或调用方决定。
            Counterable = counterable && colorTag == AttackColorTag.Gold;
        }

        public HitId HitId { get; }

        public Faction Attacker { get; }

        public int TargetId { get; }

        public Faction TargetFaction { get; }

        /// <summary>扣防御之前的伤害。</summary>
        public float RawDamage { get; }

        public AttackColorTag ColorTag { get; }

        /// <summary>是否带 CounterableSkill 标签。只有金色技能才可能为真。</summary>
        public bool Counterable { get; }
    }

    public enum HitRejection
    {
        None = 0,
        InvalidHitId = 1,
        SameFaction = 2,
        AlreadyHitByThisAttack = 3,
        NonPositiveDamage = 4,
        TargetAlreadyDead = 5,

        /// <summary>被目标反击掉了，这一刀不产生任何伤害。</summary>
        Countered = 6
    }

    public readonly struct HitResolution
    {
        public HitResolution(
            bool accepted,
            HitRejection rejection,
            float armorLost,
            float healthLost,
            bool killed)
        {
            Accepted = accepted;
            Rejection = rejection;
            ArmorLost = armorLost;
            HealthLost = healthLost;
            Killed = killed;
        }

        public bool Accepted { get; }

        public HitRejection Rejection { get; }

        public float ArmorLost { get; }

        public float HealthLost { get; }

        public bool Killed { get; }

        public static HitResolution Rejected(HitRejection reason) =>
            new HitResolution(false, reason, 0f, 0f, false);
    }
}
