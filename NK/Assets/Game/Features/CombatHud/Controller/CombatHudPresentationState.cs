using System;
using Naraka.Core.Application.MVC;
using Naraka.Features.Character.Model;
using Naraka.Features.Combat.Model;
using Naraka.Features.Monster.Model;

namespace Naraka.Features.CombatHud.Controller
{
    /// <summary>
    /// 战斗 HUD 的**唯一**只读状态。
    ///
    /// HUD 上的每一个数字都只能来自这里：View 不去读 Model、不去读 Animator、
    /// 也不去场景里找怪物。用户负责 HUD 的视觉与挂载，绑定脚本只做
    /// "把这个结构里的字段写到 Text/Image/Slider 上"。
    /// </summary>
    public readonly struct CombatHudPresentationState
        : IPresentationState, IEquatable<CombatHudPresentationState>
    {
        public CombatHudPresentationState(
            float playerHealth,
            float playerMaxHealth,
            float playerArmor,
            float playerMaxArmor,
            float playerStamina,
            float playerMaxStamina,
            float skillFCooldownRemaining,
            float skillFCooldownSeconds,
            float skillVCooldownRemaining,
            float skillVCooldownSeconds,
            ActionRejection rejection,
            bool playerIsAlive,
            PlayerOverlayFlags playerFlags,
            bool hasTarget,
            string targetName,
            float targetHealth,
            float targetMaxHealth,
            float targetArmor,
            float targetMaxArmor,
            bool targetIsAlive,
            MonsterPhase targetPhase,
            AttackColorTag targetWarning,
            float targetExecuteWindowRemaining)
        {
            PlayerHealth = playerHealth;
            PlayerMaxHealth = playerMaxHealth;
            PlayerArmor = playerArmor;
            PlayerMaxArmor = playerMaxArmor;
            PlayerStamina = playerStamina;
            PlayerMaxStamina = playerMaxStamina;
            SkillFCooldownRemaining = skillFCooldownRemaining;
            SkillFCooldownSeconds = skillFCooldownSeconds;
            SkillVCooldownRemaining = skillVCooldownRemaining;
            SkillVCooldownSeconds = skillVCooldownSeconds;
            Rejection = rejection;
            PlayerIsAlive = playerIsAlive;
            PlayerFlags = playerFlags;
            HasTarget = hasTarget;
            TargetName = targetName ?? string.Empty;
            TargetHealth = targetHealth;
            TargetMaxHealth = targetMaxHealth;
            TargetArmor = targetArmor;
            TargetMaxArmor = targetMaxArmor;
            TargetIsAlive = targetIsAlive;
            TargetPhase = targetPhase;
            TargetWarning = targetWarning;
            TargetExecuteWindowRemaining = targetExecuteWindowRemaining;
        }

        public float PlayerHealth { get; }

        public float PlayerMaxHealth { get; }

        public float PlayerArmor { get; }

        public float PlayerMaxArmor { get; }

        public float PlayerStamina { get; }

        public float PlayerMaxStamina { get; }

        public float SkillFCooldownRemaining { get; }

        public float SkillFCooldownSeconds { get; }

        public float SkillVCooldownRemaining { get; }

        public float SkillVCooldownSeconds { get; }

        /// <summary>最近一次操作被拒绝的原因，供"体力不足""技能冷却中"提示使用。</summary>
        public ActionRejection Rejection { get; }

        public bool PlayerIsAlive { get; }

        public PlayerOverlayFlags PlayerFlags { get; }

        /// <summary>当前有没有目标。为 false 时怪物血条必须整体隐藏。</summary>
        public bool HasTarget { get; }

        public string TargetName { get; }

        public float TargetHealth { get; }

        public float TargetMaxHealth { get; }

        public float TargetArmor { get; }

        public float TargetMaxArmor { get; }

        public bool TargetIsAlive { get; }

        public MonsterPhase TargetPhase { get; }

        /// <summary>
        /// 怪物技能预警类型。<see cref="AttackColorTag.None"/> 表示没有预警；
        /// 金色可反击、红色不可反击。
        /// </summary>
        public AttackColorTag TargetWarning { get; }

        public float TargetExecuteWindowRemaining { get; }

        public float PlayerHealthRatio => Ratio(PlayerHealth, PlayerMaxHealth);

        public float PlayerArmorRatio => Ratio(PlayerArmor, PlayerMaxArmor);

        public float PlayerStaminaRatio => Ratio(PlayerStamina, PlayerMaxStamina);

        /// <summary>冷却遮罩用的填充比例：1 表示完全冷却中，0 表示可用。</summary>
        public float SkillFCooldownRatio => Ratio(SkillFCooldownRemaining, SkillFCooldownSeconds);

        public float SkillVCooldownRatio => Ratio(SkillVCooldownRemaining, SkillVCooldownSeconds);

        public float TargetHealthRatio => Ratio(TargetHealth, TargetMaxHealth);

        public float TargetArmorRatio => Ratio(TargetArmor, TargetMaxArmor);

        public bool TargetIsExecutable => TargetExecuteWindowRemaining > 0f;

        public bool PlayerHasSuperArmor => (PlayerFlags & PlayerOverlayFlags.SuperArmor) != 0;

        public bool PlayerIsInvulnerable => (PlayerFlags & PlayerOverlayFlags.Invulnerable) != 0;

        public bool PlayerCounterWindowOpen => (PlayerFlags & PlayerOverlayFlags.CounterWindow) != 0;

        private static float Ratio(float value, float max)
        {
            if (max <= 0f)
            {
                return 0f;
            }

            var ratio = value / max;
            return ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
        }

        /// <summary>
        /// 显式实现相等比较。默认的 <c>ValueType.Equals</c> 走反射并且会装箱，
        /// 而这个结构每帧都要和上一帧比一次，那正是"稳定态 0 GC"要避免的分配。
        /// </summary>
        public bool Equals(CombatHudPresentationState other) =>
            PlayerHealth.Equals(other.PlayerHealth) &&
            PlayerMaxHealth.Equals(other.PlayerMaxHealth) &&
            PlayerArmor.Equals(other.PlayerArmor) &&
            PlayerMaxArmor.Equals(other.PlayerMaxArmor) &&
            PlayerStamina.Equals(other.PlayerStamina) &&
            PlayerMaxStamina.Equals(other.PlayerMaxStamina) &&
            SkillFCooldownRemaining.Equals(other.SkillFCooldownRemaining) &&
            SkillFCooldownSeconds.Equals(other.SkillFCooldownSeconds) &&
            SkillVCooldownRemaining.Equals(other.SkillVCooldownRemaining) &&
            SkillVCooldownSeconds.Equals(other.SkillVCooldownSeconds) &&
            Rejection == other.Rejection &&
            PlayerIsAlive == other.PlayerIsAlive &&
            PlayerFlags == other.PlayerFlags &&
            HasTarget == other.HasTarget &&
            string.Equals(TargetName, other.TargetName, StringComparison.Ordinal) &&
            TargetHealth.Equals(other.TargetHealth) &&
            TargetMaxHealth.Equals(other.TargetMaxHealth) &&
            TargetArmor.Equals(other.TargetArmor) &&
            TargetMaxArmor.Equals(other.TargetMaxArmor) &&
            TargetIsAlive == other.TargetIsAlive &&
            TargetPhase == other.TargetPhase &&
            TargetWarning == other.TargetWarning &&
            TargetExecuteWindowRemaining.Equals(other.TargetExecuteWindowRemaining);

        public override bool Equals(object obj) =>
            obj is CombatHudPresentationState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = PlayerHealth.GetHashCode();
                hash = (hash * 397) ^ PlayerArmor.GetHashCode();
                hash = (hash * 397) ^ PlayerStamina.GetHashCode();
                hash = (hash * 397) ^ (int)Rejection;
                hash = (hash * 397) ^ (int)PlayerFlags;
                hash = (hash * 397) ^ HasTarget.GetHashCode();
                hash = (hash * 397) ^ TargetHealth.GetHashCode();
                hash = (hash * 397) ^ (int)TargetWarning;
                return hash;
            }
        }
    }

    /// <summary>一次受击反馈。它是"已经发生的事实"，订阅者只做表现。</summary>
    public readonly struct CombatFeedback
    {
        public CombatFeedback(int targetId, float armorLost, float healthLost, bool killed)
        {
            TargetId = targetId;
            ArmorLost = armorLost;
            HealthLost = healthLost;
            Killed = killed;
        }

        public int TargetId { get; }

        public float ArmorLost { get; }

        public float HealthLost { get; }

        public float Total => ArmorLost + HealthLost;

        public bool Killed { get; }
    }
}
