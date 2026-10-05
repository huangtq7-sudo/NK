using System;
using System.Collections.Generic;
using Naraka.Config;
using Naraka.Core.Application.Config;
using Naraka.Features.Combat.Model;
using Naraka.Features.Monster.Model;

namespace Naraka.Features.Monster.Controller
{
    /// <summary>
    /// 把配置目录里的怪物行转成 Model 能吃的纯数据快照。
    ///
    /// 数值只有一个来源：<c>Config/Source/monsters.csv</c> 与
    /// <c>monster_skills.csv</c>。任何一条都不允许散落在 MonoBehaviour 或 View 上。
    /// </summary>
    public static class MonsterTuningFactory
    {
        /// <summary>
        /// 从配置构造。缺少配置时返回 false，调用方必须显式处理，
        /// 而不是悄悄退回一份编造的数值。
        /// </summary>
        public static bool TryCreate(
            GameConfigCatalog catalog,
            string monsterId,
            out MonsterTuning tuning)
        {
            tuning = default;
            if (catalog == null || string.IsNullOrEmpty(monsterId))
            {
                return false;
            }

            if (!catalog.TryGetMonster(monsterId, out var monster) || monster == null)
            {
                return false;
            }

            tuning = Create(monster, catalog.GetMonsterSkills(monsterId));
            return true;
        }

        public static MonsterTuning Create(
            MonsterConfig monster,
            IReadOnlyList<MonsterSkillConfig> skills)
        {
            if (monster == null)
            {
                throw new ArgumentNullException(nameof(monster));
            }

            var converted = Array.Empty<MonsterAttackTuning>();
            if (skills != null && skills.Count > 0)
            {
                converted = new MonsterAttackTuning[skills.Count];
                for (var i = 0; i < skills.Count; i++)
                {
                    converted[i] = ToAttack(skills[i]);
                }
            }

            return new MonsterTuning(
                monsterId: monster.MonsterId,
                displayName: monster.DisplayName,
                maxHealth: monster.Health,
                maxArmor: monster.Armor,
                defense: monster.Defense,
                attack: monster.Attack,
                patrolSpeed: monster.PatrolSpeed,
                chaseSpeed: monster.ChaseSpeed,
                patrolRadius: monster.PatrolRadius,
                patrolPauseSeconds: monster.PatrolPauseSeconds,
                perceptionRadius: monster.PerceptionRadius,
                chaseRadius: monster.ChaseRadius,
                attackRange: monster.AttackRange,
                dormantDistance: monster.DormantDistance,
                decisionsPerSecond: monster.DecisionsPerSecond,
                phaseHealthRatio: monster.PhaseHealthRatio,
                hitStunSeconds: monster.HitStunSeconds,
                deathSeconds: monster.DeathSeconds,
                normalAttack: new MonsterAttackTuning(
                    skillId: string.Empty,
                    // 普通攻击没有颜色，因此永远不可反击。
                    colorTag: AttackColorTag.None,
                    counterable: false,
                    damageMultiplier: monster.NormalAttackMultiplier,
                    cooldownSeconds: monster.NormalAttackCooldownSeconds,
                    minRange: 0f,
                    maxRange: monster.AttackRange,
                    coneAngleDegrees: 0f,
                    requiresPhaseAtOrBelow: 1f,
                    warningSeconds: 0f,
                    windupSeconds: monster.NormalAttackWindupSeconds,
                    hitSeconds: monster.NormalAttackHitSeconds,
                    recoverySeconds: monster.NormalAttackRecoverySeconds,
                    recentUseSuppressionSeconds: 0f),
                skills: converted);
        }

        private static MonsterAttackTuning ToAttack(MonsterSkillConfig skill) =>
            new MonsterAttackTuning(
                skillId: skill.SkillId,
                colorTag: ToColorTag(skill.ColorTag),
                counterable: skill.Counterable,
                damageMultiplier: skill.DamageMultiplier,
                cooldownSeconds: skill.CooldownSeconds,
                minRange: skill.MinRange,
                maxRange: skill.MaxRange,
                coneAngleDegrees: skill.ConeAngleDegrees,
                requiresPhaseAtOrBelow: skill.RequiresPhaseAtOrBelow,
                warningSeconds: skill.WarningSeconds,
                windupSeconds: skill.WindupSeconds,
                hitSeconds: skill.HitSeconds,
                recoverySeconds: skill.RecoverySeconds,
                recentUseSuppressionSeconds: skill.RecentUseSuppressionSeconds);

        private static AttackColorTag ToColorTag(string colorTag)
        {
            if (string.Equals(colorTag, ConfigMonsterColorTag.Gold, StringComparison.Ordinal))
            {
                return AttackColorTag.Gold;
            }

            if (string.Equals(colorTag, ConfigMonsterColorTag.Red, StringComparison.Ordinal))
            {
                return AttackColorTag.Red;
            }

            return AttackColorTag.None;
        }
    }
}
