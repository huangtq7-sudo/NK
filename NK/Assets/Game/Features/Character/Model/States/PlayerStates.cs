using System;

namespace Naraka.Features.Character.Model
{
    /// <summary>
    /// 分层状态机的移动层。
    ///
    /// 移动是相机相对的：WASD 四个方向都会移动，角色先转向目标方向再前进，
    /// 因此只有"走"和"跑"两种行进状态 —— 没有后退，也没有横向滑步。
    /// </summary>
    public enum LocomotionState
    {
        Idle = 0,
        IdleVariation = 1,

        /// <summary>行走。方向由摄像机与输入决定，动画始终是前进。</summary>
        Walk = 2,

        /// <summary>奔跑。同上。</summary>
        Run = 3,

        /// <summary>奔跑中输入方向发生大角度反转时的转身动作。</summary>
        RunTurnback = 4,

        StopWalk = 5,
        StopRun = 6
    }

    /// <summary>
    /// 分层状态机的动作层。出场状态属于强制状态，优先级在普通 Action 之上。
    /// </summary>
    public enum ActionState
    {
        None = 0,
        SpawnLobbyToMap01 = 1,
        SpawnMap01ToMap02 = 2,
        Dash = 3,
        AttackCombo1 = 4,
        AttackCombo2 = 5,
        AttackCombo3 = 6,
        Charge = 7,
        SkillF = 8,
        SkillV = 9
    }

    /// <summary>
    /// 分层状态机的反应层。Death 一旦进入不可被普通状态打断。
    /// </summary>
    public enum ReactionState
    {
        None = 0,
        HitStun = 1,
        Death = 2
    }

    /// <summary>
    /// 覆盖标签。它们与主状态并行存在，不互相顶掉，也不构成第二份状态真相。
    /// </summary>
    [Flags]
    public enum PlayerOverlayFlags
    {
        None = 0,

        /// <summary>加载、出场、死亡或设置界面期间禁用角色输入。</summary>
        InputLocked = 1 << 0,

        /// <summary>霸体：只阻止普通受击硬直，不阻止伤害和死亡。</summary>
        SuperArmor = 1 << 1,

        /// <summary>重生保护：本阶段只标记，伤害免除留给命中层校验。</summary>
        SpawnProtection = 1 << 2,

        /// <summary>场景加载中。</summary>
        Loading = 1 << 3,

        /// <summary>地面检测结果。</summary>
        Grounded = 1 << 4
    }

    /// <summary>
    /// 操作被拒绝的原因。进入 PresentationState 供 HUD 与音效使用，不写死在 View。
    /// </summary>
    public enum ActionRejection
    {
        None = 0,
        InsufficientStamina = 1,
        SkillFOnCooldown = 2,
        SkillVOnCooldown = 3,
        InputLocked = 4,
        Busy = 5
    }

    /// <summary>
    /// 状态机要求 Animator 呈现的表现。它是业务状态的投影，本身不是状态真相：
    /// Animator 不回写任何一项，View 也不读取 Animator 的 state 名或 normalizedTime。
    /// </summary>
    public enum PlayerAnimation
    {
        None = 0,
        Idle = 1,
        IdleVariation = 2,
        Walk = 3,
        Run = 4,
        RunTurnback = 5,
        StopWalk = 6,
        StopRun = 7,
        Dash = 8,
        AttackCombo1 = 9,
        AttackCombo2 = 10,
        AttackCombo3 = 11,
        Charge = 12,
        SkillF = 13,
        SkillV = 14,
        HitStun = 15,
        Death = 16,
        SpawnBurstLobbyToMap01 = 17,
        SpawnBurstMap01ToMap02 = 18
    }
}
