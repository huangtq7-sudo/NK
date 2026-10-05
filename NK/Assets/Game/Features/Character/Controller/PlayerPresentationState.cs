using Naraka.Core.Application.MVC;
using Naraka.Features.Character.Model;

namespace Naraka.Features.Character.Controller
{
    /// <summary>
    /// 战斗 HUD 可订阅的只读状态。本阶段不做 HUD 视觉，但状态必须先存在，
    /// 否则后续 HUD 只能去反查 Animator 或 Model，那会立刻制造第二份状态真相。
    /// </summary>
    public readonly struct PlayerPresentationState : IPresentationState
    {
        public PlayerPresentationState(
            bool isAlive,
            float health,
            float maxHealth,
            float armor,
            float maxArmor,
            float defense,
            float stamina,
            float maxStamina,
            float skillFCooldownRemaining,
            float skillFCooldownSeconds,
            float skillVCooldownRemaining,
            float skillVCooldownSeconds,
            LocomotionState locomotion,
            ActionState action,
            ReactionState reaction,
            PlayerOverlayFlags flags,
            ActionRejection rejection,
            int comboStep)
        {
            IsAlive = isAlive;
            Health = health;
            MaxHealth = maxHealth;
            Armor = armor;
            MaxArmor = maxArmor;
            Defense = defense;
            Stamina = stamina;
            MaxStamina = maxStamina;
            SkillFCooldownRemaining = skillFCooldownRemaining;
            SkillFCooldownSeconds = skillFCooldownSeconds;
            SkillVCooldownRemaining = skillVCooldownRemaining;
            SkillVCooldownSeconds = skillVCooldownSeconds;
            Locomotion = locomotion;
            Action = action;
            Reaction = reaction;
            Flags = flags;
            Rejection = rejection;
            ComboStep = comboStep;
        }

        public bool IsAlive { get; }

        public float Health { get; }

        public float MaxHealth { get; }

        public float Armor { get; }

        public float MaxArmor { get; }

        /// <summary>防御。最终伤害 = 原始伤害 × 100 / (100 + 防御)。</summary>
        public float Defense { get; }

        public float Stamina { get; }

        public float MaxStamina { get; }

        public float SkillFCooldownRemaining { get; }

        public float SkillFCooldownSeconds { get; }

        public float SkillVCooldownRemaining { get; }

        public float SkillVCooldownSeconds { get; }

        public LocomotionState Locomotion { get; }

        public ActionState Action { get; }

        public ReactionState Reaction { get; }

        public PlayerOverlayFlags Flags { get; }

        /// <summary>最近一次操作被拒绝的原因，供 HUD 与音效显示"体力不足""技能冷却中"。</summary>
        public ActionRejection Rejection { get; }

        public int ComboStep { get; }

        public float HealthRatio => MaxHealth <= 0f ? 0f : Health / MaxHealth;

        public float ArmorRatio => MaxArmor <= 0f ? 0f : Armor / MaxArmor;

        public float StaminaRatio => MaxStamina <= 0f ? 0f : Stamina / MaxStamina;

        /// <summary>霸体：只免硬直，不免伤害与死亡。</summary>
        public bool HasSuperArmor => (Flags & PlayerOverlayFlags.SuperArmor) != 0;

        /// <summary>无敌：本阶段只有处决过程会成立。</summary>
        public bool IsInvulnerable => (Flags & PlayerOverlayFlags.Invulnerable) != 0;

        /// <summary>重生保护中，直接免伤。</summary>
        public bool HasSpawnProtection => (Flags & PlayerOverlayFlags.SpawnProtection) != 0;

        /// <summary>反击判定窗开启中。</summary>
        public bool IsCounterWindowOpen => (Flags & PlayerOverlayFlags.CounterWindow) != 0;

        /// <summary>角色输入被锁定（加载、出场、受击硬直或死亡）。</summary>
        public bool IsInputLocked => (Flags & PlayerOverlayFlags.InputLocked) != 0;

        public static PlayerPresentationState FromCore(PlayerCore core, ActionRejection rejection) =>
            new PlayerPresentationState(
                !core.IsDead,
                core.Health,
                core.MaxHealth,
                core.Armor,
                core.MaxArmor,
                core.Defense,
                core.Stamina,
                core.MaxStamina,
                core.SkillFCooldownRemaining,
                core.SkillFCooldownSeconds,
                core.SkillVCooldownRemaining,
                core.SkillVCooldownSeconds,
                core.Locomotion,
                core.Action,
                core.Reaction,
                core.Flags,
                rejection,
                core.ComboStep);
    }
}
