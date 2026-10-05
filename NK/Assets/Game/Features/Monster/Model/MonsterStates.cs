using System;

namespace Naraka.Features.Monster.Model
{
    /// <summary>
    /// 怪物 HFSM 的动作层。**这是"当前动作"的唯一真相**：
    /// 行为树只选意图，不保存动作状态，因此不存在两份互相矛盾的"它正在干什么"。
    /// </summary>
    public enum MonsterActionState
    {
        Idle = 0,
        Move = 1,
        Attack = 2,
        Skill = 3,
        HitStun = 4,

        /// <summary>击倒。本阶段暮影妖狼不会进入，留给后续精英/首领。</summary>
        Knockdown = 5,
        Death = 6
    }

    /// <summary>
    /// 行为树选出的意图。它只表达"接下来想做什么"，
    /// 真正做到哪一步由 <see cref="MonsterActionState"/> 说了算。
    /// </summary>
    public enum MonsterIntent
    {
        /// <summary>玩家太远，停止高频决策。</summary>
        Dormant = 0,

        /// <summary>在出生点附近巡逻。</summary>
        Patrol = 1,

        /// <summary>刚发现玩家，转向并进入战斗。</summary>
        Perceive = 2,

        /// <summary>追击玩家。</summary>
        Chase = 3,

        /// <summary>普通攻击。不可反击。</summary>
        NormalAttack = 4,

        /// <summary>释放一个技能。</summary>
        SelectSkill = 5,

        /// <summary>脱战回复：玩家跑远了，停手回到巡逻。</summary>
        Recover = 6,

        /// <summary>已经死亡，不再产生任何行动。</summary>
        Dead = 7
    }

    /// <summary>怪物阶段。生命降到阈值以下进入第二阶段并解锁阶段技能。</summary>
    public enum MonsterPhase
    {
        Normal = 0,

        /// <summary>生命低于或等于阈值比例。</summary>
        Enraged = 1
    }

    /// <summary>怪物的覆盖标签。与动作层并行，不构成第二份动作真相。</summary>
    [Flags]
    public enum MonsterOverlayFlags
    {
        None = 0,

        /// <summary>霸体：只免疫普通硬直，不免疫伤害、护甲扣减与死亡。</summary>
        SuperArmor = 1 << 0,

        /// <summary>休眠：远离玩家，决策频率降低。</summary>
        Dormant = 1 << 1,

        /// <summary>处于被处决窗口内。</summary>
        ExecuteWindow = 1 << 2,

        /// <summary>技能预警显示中。</summary>
        Warning = 1 << 3
    }

    /// <summary>怪物状态切换的原因。每次切换都必须给出原因。</summary>
    public enum MonsterStateChangeReason
    {
        Initial = 0,
        IntentSelected = 1,
        ActionCompleted = 2,
        Damaged = 3,
        Died = 4,
        TargetLost = 5,
        Respawned = 6
    }

    /// <summary>
    /// 怪物要求 Animator 呈现的表现。与玩家一样，它是业务状态的单向投影，
    /// Animator 不回写任何一项。
    /// </summary>
    public enum MonsterAnimation
    {
        None = 0,
        Idle = 1,
        Walk = 2,
        Run = 3,
        Attack = 4,
        Skill = 5,
        HitStun = 6,
        Death = 7
    }
}
