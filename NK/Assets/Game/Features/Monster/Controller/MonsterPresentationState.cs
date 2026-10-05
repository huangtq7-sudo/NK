using Naraka.Core.Application.MVC;
using Naraka.Features.Combat.Model;
using Naraka.Features.Monster.Model;

namespace Naraka.Features.Monster.Controller
{
    /// <summary>
    /// 怪物的只读展示状态。战斗 HUD 的怪物血条、预警提示与处决提示
    /// 只订阅它，绝不去读 Animator、NavMeshAgent 或 Model。
    /// </summary>
    public readonly struct MonsterPresentationState : IPresentationState
    {
        public MonsterPresentationState(
            string monsterId,
            string displayName,
            bool isAlive,
            float health,
            float maxHealth,
            float armor,
            float maxArmor,
            MonsterPhase phase,
            MonsterActionState action,
            MonsterIntent intent,
            MonsterOverlayFlags flags,
            AttackColorTag warningColorTag,
            float executeWindowRemaining)
        {
            MonsterId = monsterId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            IsAlive = isAlive;
            Health = health;
            MaxHealth = maxHealth;
            Armor = armor;
            MaxArmor = maxArmor;
            Phase = phase;
            Action = action;
            Intent = intent;
            Flags = flags;
            WarningColorTag = warningColorTag;
            ExecuteWindowRemaining = executeWindowRemaining;
        }

        public string MonsterId { get; }

        public string DisplayName { get; }

        public bool IsAlive { get; }

        public float Health { get; }

        public float MaxHealth { get; }

        public float Armor { get; }

        public float MaxArmor { get; }

        public MonsterPhase Phase { get; }

        public MonsterActionState Action { get; }

        public MonsterIntent Intent { get; }

        public MonsterOverlayFlags Flags { get; }

        /// <summary>
        /// 当前预警的颜色。<see cref="AttackColorTag.None"/> 表示没有预警。
        /// 金色可反击、红色不可反击 —— HUD 直接按它选颜色，不去猜技能 ID。
        /// </summary>
        public AttackColorTag WarningColorTag { get; }

        public float ExecuteWindowRemaining { get; }

        public float HealthRatio => MaxHealth <= 0f ? 0f : Health / MaxHealth;

        public float ArmorRatio => MaxArmor <= 0f ? 0f : Armor / MaxArmor;

        public bool IsWarningActive => (Flags & MonsterOverlayFlags.Warning) != 0;

        public bool IsExecutable => (Flags & MonsterOverlayFlags.ExecuteWindow) != 0;

        public bool IsDormant => (Flags & MonsterOverlayFlags.Dormant) != 0;

        public bool HasSuperArmor => (Flags & MonsterOverlayFlags.SuperArmor) != 0;

        /// <summary>没有目标时 HUD 应该隐藏怪物血条。</summary>
        public static MonsterPresentationState Empty => default;

        public static MonsterPresentationState FromCore(
            MonsterCore core,
            AttackColorTag warningColorTag) =>
            new MonsterPresentationState(
                core.Tuning.MonsterId,
                core.Tuning.DisplayName,
                !core.IsDead,
                core.Health,
                core.MaxHealth,
                core.Armor,
                core.MaxArmor,
                core.Phase,
                core.Action,
                core.Intent,
                BuildFlags(core),
                warningColorTag,
                core.ExecuteWindowRemaining);

        private static MonsterOverlayFlags BuildFlags(MonsterCore core)
        {
            var flags = MonsterOverlayFlags.None;
            if (core.IsDormant)
            {
                flags |= MonsterOverlayFlags.Dormant;
            }

            if (core.IsExecutable)
            {
                flags |= MonsterOverlayFlags.ExecuteWindow;
            }

            return flags;
        }
    }
}
