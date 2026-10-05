using Naraka.Features.Combat.Model;

namespace Naraka.Features.Monster.Model
{
    /// <summary>
    /// 一次攻击动作的时间轴：预警 → 前摇 → 命中窗 → 后摇。
    ///
    /// 预警必须真的排在伤害之前，因此它是时间轴的第一段而不是一个平行开关。
    /// 命中窗的真相在这里，不在动画事件上。
    /// </summary>
    public readonly struct MonsterAttackTuning
    {
        public MonsterAttackTuning(
            string skillId,
            AttackColorTag colorTag,
            bool counterable,
            float damageMultiplier,
            float cooldownSeconds,
            float minRange,
            float maxRange,
            float coneAngleDegrees,
            float requiresPhaseAtOrBelow,
            float warningSeconds,
            float windupSeconds,
            float hitSeconds,
            float recoverySeconds,
            float recentUseSuppressionSeconds)
        {
            SkillId = skillId ?? string.Empty;
            ColorTag = colorTag;
            // 普通攻击永远不可反击，这条规则不交给配置决定。
            Counterable = counterable && colorTag == AttackColorTag.Gold;
            DamageMultiplier = damageMultiplier;
            CooldownSeconds = cooldownSeconds;
            MinRange = minRange;
            MaxRange = maxRange;
            ConeAngleDegrees = coneAngleDegrees;
            RequiresPhaseAtOrBelow = requiresPhaseAtOrBelow <= 0f ? 1f : requiresPhaseAtOrBelow;
            WarningSeconds = warningSeconds < 0f ? 0f : warningSeconds;
            WindupSeconds = windupSeconds < 0f ? 0f : windupSeconds;
            HitSeconds = hitSeconds <= 0f ? 0.01f : hitSeconds;
            RecoverySeconds = recoverySeconds < 0f ? 0f : recoverySeconds;
            RecentUseSuppressionSeconds =
                recentUseSuppressionSeconds < 0f ? 0f : recentUseSuppressionSeconds;
        }

        /// <summary>稳定技能 ID。普通攻击为空串。</summary>
        public string SkillId { get; }

        public AttackColorTag ColorTag { get; }

        /// <summary>是否带 CounterableSkill 标签。只有金色技能才可能为真。</summary>
        public bool Counterable { get; }

        public float DamageMultiplier { get; }

        public float CooldownSeconds { get; }

        public float MinRange { get; }

        public float MaxRange { get; }

        /// <summary>锥形角度，0 表示不是锥形。</summary>
        public float ConeAngleDegrees { get; }

        /// <summary>生命比例低于或等于这个值才允许使用。1 表示任何阶段都可以。</summary>
        public float RequiresPhaseAtOrBelow { get; }

        public float WarningSeconds { get; }

        public float WindupSeconds { get; }

        public float HitSeconds { get; }

        public float RecoverySeconds { get; }

        /// <summary>最近使用抑制：刚放过的技能在这段时间内不再被选中。</summary>
        public float RecentUseSuppressionSeconds { get; }

        /// <summary>命中窗开始时间（动作内时间）。</summary>
        public float HitWindowStart => WarningSeconds + WindupSeconds;

        public float HitWindowEnd => HitWindowStart + HitSeconds;

        public float DurationSeconds => HitWindowEnd + RecoverySeconds;

        public bool IsHitWindowOpen(float elapsed) =>
            elapsed >= HitWindowStart && elapsed <= HitWindowEnd;

        /// <summary>
        /// 预警是否显示中。它在命中窗打开之前结束，因此玩家一定先看到红光再挨打。
        /// </summary>
        public bool IsWarningActive(float elapsed) =>
            WarningSeconds > 0f && elapsed >= 0f && elapsed < HitWindowStart;

        /// <summary>距离是否落在可用区间内。</summary>
        public bool IsInRange(float distance) => distance >= MinRange && distance <= MaxRange;

        /// <summary>是否只有进入阶段之后才能用。</summary>
        public bool RequiresPhase => RequiresPhaseAtOrBelow < 1f;

        /// <summary>按当前生命比例判断是否解锁。</summary>
        public bool IsUnlockedAt(float healthRatio) => healthRatio <= RequiresPhaseAtOrBelow;

        /// <summary>没有技能的空位。</summary>
        public bool IsValid => DamageMultiplier > 0f;
    }

    /// <summary>
    /// 怪物配置快照。纯数据：Model 不认识 ScriptableObject、CSV 或 JSON，
    /// 由 Controller 把配置转成这个结构再传进来。
    /// </summary>
    public readonly struct MonsterTuning
    {
        private readonly MonsterAttackTuning[] _skills;

        public MonsterTuning(
            string monsterId,
            string displayName,
            float maxHealth,
            float maxArmor,
            float defense,
            float attack,
            float patrolSpeed,
            float chaseSpeed,
            float patrolRadius,
            float patrolPauseSeconds,
            float perceptionRadius,
            float chaseRadius,
            float attackRange,
            float dormantDistance,
            int decisionsPerSecond,
            float phaseHealthRatio,
            float hitStunSeconds,
            float deathSeconds,
            MonsterAttackTuning normalAttack,
            MonsterAttackTuning[] skills)
        {
            MonsterId = monsterId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            MaxHealth = maxHealth <= 0f ? 1f : maxHealth;
            MaxArmor = maxArmor < 0f ? 0f : maxArmor;
            Defense = defense < 0f ? 0f : defense;
            Attack = attack;
            PatrolSpeed = patrolSpeed;
            ChaseSpeed = chaseSpeed;
            PatrolRadius = patrolRadius;
            PatrolPauseSeconds = patrolPauseSeconds;
            PerceptionRadius = perceptionRadius;
            ChaseRadius = chaseRadius;
            AttackRange = attackRange;
            DormantDistance = dormantDistance;
            DecisionsPerSecond = decisionsPerSecond <= 0 ? 5 : decisionsPerSecond;
            PhaseHealthRatio = phaseHealthRatio;
            HitStunSeconds = hitStunSeconds;
            DeathSeconds = deathSeconds;
            NormalAttack = normalAttack;
            _skills = skills ?? System.Array.Empty<MonsterAttackTuning>();
        }

        public string MonsterId { get; }

        public string DisplayName { get; }

        public float MaxHealth { get; }

        public float MaxArmor { get; }

        public float Defense { get; }

        /// <summary>怪物攻击力。原始伤害 = Attack × 技能倍率。</summary>
        public float Attack { get; }

        public float PatrolSpeed { get; }

        public float ChaseSpeed { get; }

        public float PatrolRadius { get; }

        public float PatrolPauseSeconds { get; }

        public float PerceptionRadius { get; }

        /// <summary>脱离追击的距离。</summary>
        public float ChaseRadius { get; }

        public float AttackRange { get; }

        public float DormantDistance { get; }

        public int DecisionsPerSecond { get; }

        public float PhaseHealthRatio { get; }

        public float HitStunSeconds { get; }

        public float DeathSeconds { get; }

        /// <summary>普通攻击。颜色为 None 且永远不可反击。</summary>
        public MonsterAttackTuning NormalAttack { get; }

        public int SkillCount => _skills.Length;

        public MonsterAttackTuning Skill(int index) =>
            index >= 0 && index < _skills.Length ? _skills[index] : default;

        /// <summary>行为树的决策间隔（秒）。</summary>
        public float DecisionIntervalSeconds => 1f / DecisionsPerSecond;

        /// <summary>
        /// 休眠时的决策间隔。刻意拉长到 1 秒：休眠的意义就是"不再高频思考"，
        /// 但也不能完全停下来，否则玩家走回来时它永远醒不过来。
        /// </summary>
        public float DormantDecisionIntervalSeconds => 1f;

        /// <summary>
        /// 本阶段唯一一只灰盒怪的兜底数值。真实数值来自
        /// <c>Config/Source/monsters.csv</c>，这里只是没有配置时不至于崩溃的保险，
        /// 同时也是 EditMode 测试的期望值。**全部是 P2 灰盒调试值，不是平衡结果。**
        /// </summary>
        public static MonsterTuning CreateGrayboxWolf() => new MonsterTuning(
            monsterId: "monster_wolf_duskshadow",
            displayName: "暮影妖狼",
            maxHealth: 600f,
            maxArmor: 100f,
            defense: 20f,
            attack: 60f,
            patrolSpeed: 2.4f,
            chaseSpeed: 6f,
            patrolRadius: 6f,
            patrolPauseSeconds: 2f,
            perceptionRadius: 14f,
            chaseRadius: 22f,
            attackRange: 3.2f,
            dormantDistance: 40f,
            decisionsPerSecond: 6,
            phaseHealthRatio: 0.5f,
            hitStunSeconds: 0.6f,
            deathSeconds: 2f,
            normalAttack: new MonsterAttackTuning(
                skillId: string.Empty,
                colorTag: AttackColorTag.None,
                counterable: false,
                damageMultiplier: 1f,
                cooldownSeconds: 2f,
                minRange: 0f,
                maxRange: 3.2f,
                coneAngleDegrees: 0f,
                requiresPhaseAtOrBelow: 1f,
                warningSeconds: 0f,
                windupSeconds: 0.45f,
                hitSeconds: 0.25f,
                recoverySeconds: 0.6f,
                recentUseSuppressionSeconds: 0f),
            skills: new[]
            {
                new MonsterAttackTuning(
                    skillId: "monster_skill_wolf_breath",
                    colorTag: AttackColorTag.Red,
                    counterable: false,
                    damageMultiplier: 1.8f,
                    cooldownSeconds: 12f,
                    minRange: 0f,
                    maxRange: 9f,
                    coneAngleDegrees: 60f,
                    requiresPhaseAtOrBelow: 0.5f,
                    warningSeconds: 0.8f,
                    windupSeconds: 0.35f,
                    hitSeconds: 0.3f,
                    recoverySeconds: 0.9f,
                    recentUseSuppressionSeconds: 6f)
            });
    }
}
