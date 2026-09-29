namespace Naraka.Features.Character.Model
{
    /// <summary>
    /// 一帧状态机输出。View 只消费它：位移交给角色运动适配层，
    /// 动画交给 Animator 投影，命中窗交给灰盒命中层。
    /// </summary>
    public readonly struct PlayerFrameOutput
    {
        public PlayerFrameOutput(
            LocomotionState locomotion,
            ActionState action,
            ReactionState reaction,
            PlayerOverlayFlags flags,
            PlayerAnimation animation,
            bool animationRestarted,
            float forwardSpeed,
            float turnDegrees,
            bool hitWindowOpen,
            int attackId,
            float attackDamage,
            float attackRadius,
            float animationSpeed,
            ActionRejection rejection)
        {
            Locomotion = locomotion;
            Action = action;
            Reaction = reaction;
            Flags = flags;
            Animation = animation;
            AnimationRestarted = animationRestarted;
            ForwardSpeed = forwardSpeed;
            TurnDegrees = turnDegrees;
            HitWindowOpen = hitWindowOpen;
            AttackId = attackId;
            AttackDamage = attackDamage;
            AttackRadius = attackRadius;
            AnimationSpeed = animationSpeed;
            Rejection = rejection;
        }

        public LocomotionState Locomotion { get; }

        public ActionState Action { get; }

        public ReactionState Reaction { get; }

        public PlayerOverlayFlags Flags { get; }

        /// <summary>Animator 应当呈现的动画。Animator 不回写这个值。</summary>
        public PlayerAnimation Animation { get; }

        /// <summary>本帧是否要求重新进入该动画（切换或强制重播）。</summary>
        public bool AnimationRestarted { get; }

        /// <summary>沿当前朝向的速度（米/秒），负值表示后退。</summary>
        public float ForwardSpeed { get; }

        /// <summary>本帧的朝向变化量（度，顺时针为正）。</summary>
        public float TurnDegrees { get; }

        public bool HitWindowOpen { get; }

        /// <summary>
        /// 本次攻击的唯一标识，0 表示当前没有攻击。
        /// 命中层用它保证同一次攻击对同一目标只结算一次。
        /// </summary>
        public int AttackId { get; }

        public float AttackDamage { get; }

        /// <summary>灰盒命中半径（米）。0 表示使用命中盒自己的尺寸。</summary>
        public float AttackRadius { get; }

        /// <summary>
        /// Animator 本帧应当使用的播放倍率。后摇段会比主体段快，
        /// 因此这个值在一个动作内部也会变化。它是业务状态的投影，Animator 不回写它。
        /// </summary>
        public float AnimationSpeed { get; }

        /// <summary>本帧被拒绝的操作原因，没有拒绝时为 None。</summary>
        public ActionRejection Rejection { get; }

        public bool IsInputLocked => (Flags & PlayerOverlayFlags.InputLocked) != 0;

        public bool HasSuperArmor => (Flags & PlayerOverlayFlags.SuperArmor) != 0;

        public bool HasSpawnProtection => (Flags & PlayerOverlayFlags.SpawnProtection) != 0;
    }
}
