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

    /// <summary>一次命中请求。由 Controller 校验，不由 View 或 Animator Event 决定结果。</summary>
    public readonly struct HitRequest
    {
        public HitRequest(HitId hitId, Faction attacker, int targetId, Faction targetFaction, float damage)
        {
            HitId = hitId;
            Attacker = attacker;
            TargetId = targetId;
            TargetFaction = targetFaction;
            Damage = damage;
        }

        public HitId HitId { get; }

        public Faction Attacker { get; }

        public int TargetId { get; }

        public Faction TargetFaction { get; }

        public float Damage { get; }
    }

    public enum HitRejection
    {
        None = 0,
        InvalidHitId = 1,
        SameFaction = 2,
        AlreadyHitByThisAttack = 3,
        NonPositiveDamage = 4,
        TargetAlreadyDead = 5
    }

    public readonly struct HitResolution
    {
        public HitResolution(bool accepted, HitRejection rejection, float armorLost, float healthLost, bool killed)
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
