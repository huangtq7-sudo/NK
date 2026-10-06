namespace Naraka.Features.Monster.Model
{
    /// <summary>怪物这一帧该往哪走。几何由 View 负责，Model 只说"去哪一类地方"。</summary>
    public enum MonsterMoveTarget
    {
        None = 0,

        /// <summary>朝玩家移动。</summary>
        Player = 1,

        /// <summary>朝巡逻点移动。具体巡逻点由 View 在巡逻半径内挑选。</summary>
        PatrolPoint = 2,

        /// <summary>回到出生点。</summary>
        Home = 3
    }

    /// <summary>
    /// 怪物这一帧感知到的世界。它是纯数据：Model 不知道 Transform、NavMesh
    /// 或 Physics 的存在，因此 EditMode 里可以直接构造任意距离与角度来断言规则。
    /// </summary>
    public readonly struct MonsterSenses
    {
        public MonsterSenses(
            bool hasTarget,
            bool targetIsAlive,
            float distanceToTarget,
            float angleToTargetDegrees,
            float distanceFromHome,
            bool isWithinEngageRange = false)
        {
            HasTarget = hasTarget;
            TargetIsAlive = targetIsAlive;
            DistanceToTarget = distanceToTarget;
            AngleToTargetDegrees = angleToTargetDegrees;
            DistanceFromHome = distanceFromHome;
            IsWithinEngageRange = isWithinEngageRange;
        }

        /// <summary>场上有没有一个玩家目标。</summary>
        public bool HasTarget { get; }

        /// <summary>目标是否还活着。死掉的玩家不该继续被追。</summary>
        public bool TargetIsAlive { get; }

        public float DistanceToTarget { get; }

        /// <summary>目标相对怪物正前方的带符号夹角（度）。</summary>
        public float AngleToTargetDegrees { get; }

        public float DistanceFromHome { get; }

        /// <summary>
        /// 已经走到可以出手的位置，不需要再往前挪了。
        ///
        /// 这是一个**几何结论**，由 View 算出来再交给 Model —— 和
        /// <see cref="DistanceFromHome"/> 一样。原因是"多近才够出手"取决于命中盒的
        /// 偏移与半径，那是表现层的东西：配置里的攻击距离是 3.2，而命中盒的实际触达
        /// 只到 2.92，所以 Model 自己按攻击距离判断会停在打不到的地方。
        ///
        /// Model 用它决定"追击意图该不该真的产生位移"：已经到位时站住面向目标等冷却，
        /// 而不是把动作层设成 Move 然后原地跑步。
        /// </summary>
        public bool IsWithinEngageRange { get; }

        /// <summary>有一个可以交战的目标。</summary>
        public bool HasLivingTarget => HasTarget && TargetIsAlive;

        /// <summary>没有目标时的空感知。</summary>
        public static MonsterSenses None => default;

        public static MonsterSenses ToTarget(
            float distance,
            float angleDegrees = 0f,
            float fromHome = 0f,
            bool withinEngageRange = false) =>
            new MonsterSenses(true, true, distance, angleDegrees, fromHome, withinEngageRange);
    }

    /// <summary>
    /// 怪物一帧的状态机输出。View 只消费它：移动交给运动适配层，
    /// 动画交给 Animator 投影，命中窗交给命中盒。
    /// </summary>
    public readonly struct MonsterFrameOutput
    {
        public MonsterFrameOutput(
            MonsterIntent intent,
            MonsterActionState action,
            MonsterPhase phase,
            MonsterOverlayFlags flags,
            MonsterAnimation animation,
            bool animationRestarted,
            MonsterMoveTarget moveTarget,
            float moveSpeed,
            bool faceTarget,
            int attackId,
            bool hitWindowOpen,
            float rawDamage,
            float attackRadius,
            float attackConeDegrees,
            Naraka.Features.Combat.Model.AttackColorTag colorTag,
            bool counterable)
        {
            Intent = intent;
            Action = action;
            Phase = phase;
            Flags = flags;
            Animation = animation;
            AnimationRestarted = animationRestarted;
            MoveTarget = moveTarget;
            MoveSpeed = moveSpeed;
            FaceTarget = faceTarget;
            AttackId = attackId;
            HitWindowOpen = hitWindowOpen;
            RawDamage = rawDamage;
            AttackRadius = attackRadius;
            AttackConeDegrees = attackConeDegrees;
            ColorTag = colorTag;
            Counterable = counterable;
        }

        public MonsterIntent Intent { get; }

        public MonsterActionState Action { get; }

        public MonsterPhase Phase { get; }

        public MonsterOverlayFlags Flags { get; }

        public MonsterAnimation Animation { get; }

        public bool AnimationRestarted { get; }

        public MonsterMoveTarget MoveTarget { get; }

        /// <summary>本帧的移动速度（世界单位/秒）。0 表示原地不动。</summary>
        public float MoveSpeed { get; }

        /// <summary>本帧是否应该转向玩家。</summary>
        public bool FaceTarget { get; }

        /// <summary>本次攻击的唯一标识，0 表示当前没有攻击。</summary>
        public int AttackId { get; }

        public bool HitWindowOpen { get; }

        /// <summary>扣防御之前的伤害。</summary>
        public float RawDamage { get; }

        public float AttackRadius { get; }

        /// <summary>锥形角度，0 表示不是锥形。</summary>
        public float AttackConeDegrees { get; }

        public Naraka.Features.Combat.Model.AttackColorTag ColorTag { get; }

        public bool Counterable { get; }

        public bool HasSuperArmor => (Flags & MonsterOverlayFlags.SuperArmor) != 0;

        public bool IsDormant => (Flags & MonsterOverlayFlags.Dormant) != 0;

        /// <summary>技能预警显示中。预警一定排在命中窗之前。</summary>
        public bool IsWarningActive => (Flags & MonsterOverlayFlags.Warning) != 0;

        public bool IsInExecuteWindow => (Flags & MonsterOverlayFlags.ExecuteWindow) != 0;
    }
}
