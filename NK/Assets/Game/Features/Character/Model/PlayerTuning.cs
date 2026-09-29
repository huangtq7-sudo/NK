namespace Naraka.Features.Character.Model
{
    /// <summary>
    /// 一个动作的播放时间轴。
    ///
    /// 动画片段的长度是固定的，但播放速度不是。把两者分开之后：
    /// - 片段时间（clip time）用来定命中窗、连段窗、位移窗，它们跟着动画帧走；
    /// - 真实时间（real time）是状态机实际经过的秒数，受播放速度影响。
    ///
    /// 动作分两段：主体与后摇。主体是"打出去"的部分，速度要看得清；
    /// 后摇是收招，可以放快。<see cref="RecoveryStartSeconds"/> 之后就算后摇。
    /// </summary>
    public readonly struct ActionPlayback
    {
        public ActionPlayback(
            float clipSeconds,
            float mainSpeed = 1f,
            float recoverySpeed = 0f,
            float recoveryStartSeconds = -1f)
        {
            ClipSeconds = clipSeconds < 0f ? 0f : clipSeconds;
            MainSpeed = mainSpeed <= 0f ? 1f : mainSpeed;
            RecoverySpeed = recoverySpeed <= 0f ? (mainSpeed <= 0f ? 1f : mainSpeed) : recoverySpeed;
            RecoveryStartSeconds = recoveryStartSeconds < 0f || recoveryStartSeconds > ClipSeconds
                ? ClipSeconds
                : recoveryStartSeconds;
        }

        /// <summary>动画片段长度（秒）。由 Editor 工具从实际 AnimationClip 同步。</summary>
        public float ClipSeconds { get; }

        /// <summary>主体播放速度倍率。</summary>
        public float MainSpeed { get; }

        /// <summary>后摇播放速度倍率。</summary>
        public float RecoverySpeed { get; }

        /// <summary>后摇起点（片段时间）。等于 ClipSeconds 表示整段都是主体。</summary>
        public float RecoveryStartSeconds { get; }

        /// <summary>主体部分占用的真实秒数。</summary>
        public float MainRealSeconds => RecoveryStartSeconds / MainSpeed;

        /// <summary>整个动作占用的真实秒数。状态机用它判断动作何时结束。</summary>
        public float RealDurationSeconds =>
            MainRealSeconds + ((ClipSeconds - RecoveryStartSeconds) / RecoverySpeed);

        /// <summary>把真实经过时间换算成片段时间。</summary>
        public float ClipTimeAt(float realElapsed)
        {
            if (realElapsed <= 0f)
            {
                return 0f;
            }

            var mainReal = MainRealSeconds;
            if (realElapsed <= mainReal)
            {
                return realElapsed * MainSpeed;
            }

            return RecoveryStartSeconds + ((realElapsed - mainReal) * RecoverySpeed);
        }

        /// <summary>某个片段时间点上的播放速度。Animator 投影用它设置播放倍率。</summary>
        public float SpeedAt(float clipTime) =>
            clipTime < RecoveryStartSeconds ? MainSpeed : RecoverySpeed;
    }

    /// <summary>
    /// 一个有时间轴的动作。攻击、技能、冲刺与出场动画都用它。
    ///
    /// 所有窗口都是**片段时间**，因此改播放速度不会让窗口跑到动作之外。
    /// 前摇是 <see cref="HitWindowStart"/>，后摇是 <c>ClipSeconds - HitWindowEnd</c>，
    /// 两者都由这里的数值决定，状态类不写死任何一项。
    /// </summary>
    public readonly struct TimedActionTuning
    {
        public TimedActionTuning(
            ActionPlayback playback,
            float hitWindowStart,
            float hitWindowEnd,
            float comboWindowStart,
            float comboWindowEnd,
            float forwardDisplacement,
            float displacementClipSeconds,
            float damage)
        {
            Playback = playback;
            HitWindowStart = hitWindowStart;
            HitWindowEnd = hitWindowEnd;
            ComboWindowStart = comboWindowStart;
            ComboWindowEnd = comboWindowEnd;
            ForwardDisplacement = forwardDisplacement;
            DisplacementClipSeconds = displacementClipSeconds;
            Damage = damage;
        }

        public ActionPlayback Playback { get; }

        /// <summary>动画片段长度（秒）。</summary>
        public float ClipSeconds => Playback.ClipSeconds;

        /// <summary>动作实际占用的真实秒数，受播放速度影响。</summary>
        public float RealDurationSeconds => Playback.RealDurationSeconds;

        /// <summary>命中窗开始（片段时间）。此前为前摇。</summary>
        public float HitWindowStart { get; }

        /// <summary>命中窗结束（片段时间）。此后为后摇。</summary>
        public float HitWindowEnd { get; }

        /// <summary>允许消费缓存输入推进下一段的最早时间（片段时间）。</summary>
        public float ComboWindowStart { get; }

        /// <summary>允许消费缓存输入推进下一段的最晚时间（片段时间）。</summary>
        public float ComboWindowEnd { get; }

        /// <summary>沿当前朝向的总位移（米）。攻击类动作为 0，冲刺与位移技能才有值。</summary>
        public float ForwardDisplacement { get; }

        /// <summary>
        /// 位移在片段时间的前多少秒内完成。
        /// 小于等于 0 表示铺满整个动作；给一个很小的值就是"瞬间冲刺"。
        /// </summary>
        public float DisplacementClipSeconds { get; }

        /// <summary>灰盒基础伤害。正式数值由服务端权威判定。</summary>
        public float Damage { get; }

        public float ClipTimeAt(float realElapsed) => Playback.ClipTimeAt(realElapsed);

        public float SpeedAt(float clipTime) => Playback.SpeedAt(clipTime);

        /// <summary>命中窗此刻是否开启。参数是片段时间。</summary>
        public bool IsHitWindowOpen(float clipTime) =>
            HitWindowEnd > HitWindowStart && clipTime >= HitWindowStart && clipTime <= HitWindowEnd;

        /// <summary>连段窗此刻是否开启。参数是片段时间。</summary>
        public bool IsComboWindowOpen(float clipTime) =>
            clipTime >= ComboWindowStart && clipTime <= ComboWindowEnd;

        /// <summary>
        /// 本帧沿朝向的速度（米/秒，真实时间）。
        ///
        /// 位移在片段时间的 <see cref="DisplacementClipSeconds"/> 内均匀完成，
        /// 换算到真实时间时乘上当前播放速度，因此不论怎么调速度，
        /// 总位移都精确等于 <see cref="ForwardDisplacement"/>。
        /// </summary>
        public float ForwardSpeedAt(float realElapsed)
        {
            if (ForwardDisplacement == 0f)
            {
                return 0f;
            }

            var window = DisplacementClipSeconds <= 0f || DisplacementClipSeconds > ClipSeconds
                ? ClipSeconds
                : DisplacementClipSeconds;
            if (window <= 0f)
            {
                return 0f;
            }

            var clipTime = ClipTimeAt(realElapsed);
            if (clipTime > window)
            {
                return 0f;
            }

            return (ForwardDisplacement / window) * SpeedAt(clipTime);
        }

        /// <summary>只播一段动画、没有命中窗与位移的动作（出场动画这类）。</summary>
        public static TimedActionTuning Simple(ActionPlayback playback) => new TimedActionTuning(
            playback, 0f, 0f, playback.ClipSeconds, playback.ClipSeconds, 0f, 0f, 0f);
    }

    /// <summary>体力规则数值。名称统一为"体力"，不使用"耐力"等别名。</summary>
    public readonly struct StaminaTuning
    {
        public StaminaTuning(
            float max,
            float dashCost,
            float regenPerSecond,
            float regenDelaySeconds,
            float hitRegenPauseSeconds)
        {
            Max = max;
            DashCost = dashCost;
            RegenPerSecond = regenPerSecond;
            RegenDelaySeconds = regenDelaySeconds;
            HitRegenPauseSeconds = hitRegenPauseSeconds;
        }

        public float Max { get; }

        /// <summary>Move_F 快速冲刺的消耗。</summary>
        public float DashCost { get; }

        public float RegenPerSecond { get; }

        /// <summary>Move_F 结束后多久开始恢复。冲刺进行中同样不恢复。</summary>
        public float RegenDelaySeconds { get; }

        /// <summary>受击后恢复额外暂停的时长。</summary>
        public float HitRegenPauseSeconds { get; }
    }

    public readonly struct LocomotionTuning
    {
        public LocomotionTuning(
            float walkSpeed,
            float runSpeed,
            float turnDegreesPerSecond,
            float gravity,
            float groundSnapSpeed,
            float stopWalkSeconds,
            float stopWalkDistance,
            float stopRunSeconds,
            float stopRunDistance,
            float runTurnbackSeconds,
            float runTurnbackInputWindowSeconds,
            float runTurnbackAngleThresholdDegrees,
            float moveInputDeadzone)
        {
            WalkSpeed = walkSpeed;
            RunSpeed = runSpeed;
            TurnDegreesPerSecond = turnDegreesPerSecond;
            Gravity = gravity;
            GroundSnapSpeed = groundSnapSpeed;
            StopWalkSeconds = stopWalkSeconds;
            StopWalkDistance = stopWalkDistance;
            StopRunSeconds = stopRunSeconds;
            StopRunDistance = stopRunDistance;
            RunTurnbackSeconds = runTurnbackSeconds;
            RunTurnbackInputWindowSeconds = runTurnbackInputWindowSeconds;
            RunTurnbackAngleThresholdDegrees = runTurnbackAngleThresholdDegrees;
            MoveInputDeadzone = moveInputDeadzone;
        }

        /// <summary>行走速度。移动是相机相对的，四个方向同速，没有后退速度。</summary>
        public float WalkSpeed { get; }

        /// <summary>奔跑速度。</summary>
        public float RunSpeed { get; }

        /// <summary>转向角速度（度/秒）。角色朝着输入方向转，转到位之前沿当前朝向前进。</summary>
        public float TurnDegreesPerSecond { get; }

        /// <summary>重力加速度，正值表示向下的大小。</summary>
        public float Gravity { get; }

        /// <summary>着地时向下贴合的速度，避免下坡时脱离地面。</summary>
        public float GroundSnapSpeed { get; }

        public float StopWalkSeconds { get; }

        /// <summary>停止走路动画烘焙的前移距离（米）。减速曲线按它收敛，避免脚打滑。</summary>
        public float StopWalkDistance { get; }

        public float StopRunSeconds { get; }

        /// <summary>停止奔跑动画烘焙的前移距离（米）。</summary>
        public float StopRunDistance { get; }

        public float RunTurnbackSeconds { get; }

        /// <summary>
        /// 奔跑反向的允许时间窗：方向反转必须在这个时间内完成才算"突然反向"。
        /// 松开方向键停一会儿再按反方向属于重新起步，不播反向动作。
        /// </summary>
        public float RunTurnbackInputWindowSeconds { get; }

        /// <summary>
        /// 判定"反向"的角度阈值（度）。输入方向与上一次的夹角超过它才算反转。
        /// 用角度阈值而不是单帧浮点相等判断。
        /// </summary>
        public float RunTurnbackAngleThresholdDegrees { get; }

        /// <summary>移动输入的死区。摇杆量小于它视为没有移动输入。</summary>
        public float MoveInputDeadzone { get; }
    }

    public readonly struct ComboTuning
    {
        public ComboTuning(
            float inputBufferSeconds,
            float resetSeconds,
            TimedActionTuning step1,
            TimedActionTuning step2,
            TimedActionTuning step3)
        {
            InputBufferSeconds = inputBufferSeconds;
            ResetSeconds = resetSeconds;
            Step1 = step1;
            Step2 = step2;
            Step3 = step3;
        }

        /// <summary>输入缓存时长。玩家可以提前把下一次点击存进来。</summary>
        public float InputBufferSeconds { get; }

        /// <summary>连招重置时长。这么久没有继续攻击就回到第一段。</summary>
        public float ResetSeconds { get; }

        public TimedActionTuning Step1 { get; }

        public TimedActionTuning Step2 { get; }

        public TimedActionTuning Step3 { get; }

        public TimedActionTuning Step(int step) => step switch
        {
            1 => Step1,
            2 => Step2,
            _ => Step3
        };
    }

    public readonly struct ChargeTuning
    {
        public ChargeTuning(
            float holdSeconds,
            TimedActionTuning action,
            float reservedTier1Seconds,
            float reservedTier2Seconds)
        {
            HoldSeconds = holdSeconds;
            Action = action;
            ReservedTier1Seconds = reservedTier1Seconds;
            ReservedTier2Seconds = reservedTier2Seconds;
        }

        /// <summary>按住达到这个时长自动进入并释放蓄力。</summary>
        public float HoldSeconds { get; }

        public TimedActionTuning Action { get; }

        /// <summary>
        /// 预留档位阈值。本阶段只实现单档满蓄力，这两个值
        /// 不产生任何行为差异，也不产生伤害分档，仅为后续视觉分档保留配置位。
        /// </summary>
        public float ReservedTier1Seconds { get; }

        /// <summary>见 <see cref="ReservedTier1Seconds"/>。</summary>
        public float ReservedTier2Seconds { get; }
    }

    public readonly struct SkillTuning
    {
        public SkillTuning(float cooldownSeconds, TimedActionTuning action, float radius)
        {
            CooldownSeconds = cooldownSeconds;
            Action = action;
            Radius = radius;
        }

        public float CooldownSeconds { get; }

        public TimedActionTuning Action { get; }

        /// <summary>灰盒作用半径（米）。</summary>
        public float Radius { get; }
    }

    public readonly struct IdleTuning
    {
        public IdleTuning(float variationDelaySeconds, float variationDurationSeconds)
        {
            VariationDelaySeconds = variationDelaySeconds;
            VariationDurationSeconds = variationDurationSeconds;
        }

        /// <summary>连续无角色操作多久后播放一次待机动作。</summary>
        public float VariationDelaySeconds { get; }

        public float VariationDurationSeconds { get; }
    }

    public readonly struct ReactionTuning
    {
        public ReactionTuning(
            float hitStunSeconds,
            float deathSeconds,
            float spawnProtectionSeconds,
            TimedActionTuning spawnLobbyToMap01,
            TimedActionTuning spawnMap01ToMap02)
        {
            HitStunSeconds = hitStunSeconds;
            DeathSeconds = deathSeconds;
            SpawnProtectionSeconds = spawnProtectionSeconds;
            SpawnLobbyToMap01 = spawnLobbyToMap01;
            SpawnMap01ToMap02 = spawnMap01ToMap02;
        }

        public float HitStunSeconds { get; }

        public float DeathSeconds { get; }

        public float SpawnProtectionSeconds { get; }

        /// <summary>Burst02：大厅进入地图一的出场动画。</summary>
        public TimedActionTuning SpawnLobbyToMap01 { get; }

        /// <summary>Burst01：地图一进入地图二的出场动画。</summary>
        public TimedActionTuning SpawnMap01ToMap02 { get; }
    }

    public readonly struct VitalsTuning
    {
        public VitalsTuning(float maxHealth, float maxArmor, float reviveArmorRatio)
        {
            MaxHealth = maxHealth;
            MaxArmor = maxArmor;
            ReviveArmorRatio = reviveArmorRatio;
        }

        public float MaxHealth { get; }

        public float MaxArmor { get; }

        /// <summary>重生时护甲恢复比例。</summary>
        public float ReviveArmorRatio { get; }
    }

    /// <summary>
    /// 玩家配置快照。它是纯数据：Model 不引用 ScriptableObject，
    /// View 侧的 <c>PlayerTuningAsset</c> 负责把编辑器数值转成这个结构再传进来。
    /// </summary>
    public readonly struct PlayerTuning
    {
        public PlayerTuning(
            LocomotionTuning locomotion,
            StaminaTuning stamina,
            TimedActionTuning dash,
            ComboTuning combo,
            ChargeTuning charge,
            SkillTuning skillF,
            SkillTuning skillV,
            IdleTuning idle,
            ReactionTuning reaction,
            VitalsTuning vitals,
            float sprintTapMaxSeconds)
        {
            Locomotion = locomotion;
            Stamina = stamina;
            Dash = dash;
            Combo = combo;
            Charge = charge;
            SkillF = skillF;
            SkillV = skillV;
            Idle = idle;
            Reaction = reaction;
            Vitals = vitals;
            SprintTapMaxSeconds = sprintTapMaxSeconds;
        }

        public LocomotionTuning Locomotion { get; }

        public StaminaTuning Stamina { get; }

        /// <summary>Move_F 快速冲刺。位移窗口很短，因此是瞬间冲刺而不是缓慢滑行。</summary>
        public TimedActionTuning Dash { get; }

        public ComboTuning Combo { get; }

        public ChargeTuning Charge { get; }

        public SkillTuning SkillF { get; }

        public SkillTuning SkillV { get; }

        public IdleTuning Idle { get; }

        public ReactionTuning Reaction { get; }

        public VitalsTuning Vitals { get; }

        /// <summary>Shift 按下后不足这个时长释放算点按，达到则算长按。</summary>
        public float SprintTapMaxSeconds { get; }

        /// <summary>
        /// 本轮确认的玩法基线，同时是 EditMode 测试的期望值与没有绑定
        /// <c>PlayerTuningAsset</c> 时的兜底值。
        ///
        /// **片段长度**取自实测 AnimationClip，不是估计值；
        /// **播放速度与位移窗口**是纯手感参数，没有数据来源，这里给的是起始值，需要实机调。
        /// 运行期真正生效的是 <c>PlayerTuning.asset</c>，片段长度由
        /// <c>NARAKA/Setup/Rebuild Player Animator</c> 从片段同步，
        /// 并由 EditMode 契约测试守住"配置片段长度必须等于实际片段长度"。
        /// </summary>
        public static PlayerTuning CreateBaseline() => new PlayerTuning(
            locomotion: new LocomotionTuning(
                walkSpeed: 5.0f,
                runSpeed: 7.5f,
                turnDegreesPerSecond: 720f,
                gravity: 25f,
                groundSnapSpeed: 2f,
                stopWalkSeconds: 1.5f,
                stopWalkDistance: 1.439f,
                stopRunSeconds: 1.333f,
                stopRunDistance: 1.417f,
                runTurnbackSeconds: 1.667f,
                runTurnbackInputWindowSeconds: 0.25f,
                runTurnbackAngleThresholdDegrees: 135f,
                moveInputDeadzone: 0.2f),
            stamina: new StaminaTuning(
                max: 20f,
                dashCost: 10f,
                regenPerSecond: 5f,
                regenDelaySeconds: 0.75f,
                hitRegenPauseSeconds: 0.5f),
            // Move_F：位移在片段前 0.45 秒内完成，因此是一次瞬间冲刺而不是缓慢滑行。
            dash: new TimedActionTuning(
                new ActionPlayback(1.333f, 1.6f),
                0f, 0f, 1.333f, 1.333f, 12.276f, 0.45f, 0f),
            combo: new ComboTuning(
                inputBufferSeconds: 0.18f,
                resetSeconds: 0.8f,
                // Attack01：向下斜斩。攻击不改变坐标，因此位移为 0。
                step1: new TimedActionTuning(
                    new ActionPlayback(3.033f, 1.6f, 2.4f, 1.356f),
                    0.785f, 1.356f, 1.070f, 3.033f, 0f, 0f, 100f),
                // AM_Summon：横扫。
                step2: new TimedActionTuning(
                    new ActionPlayback(1.833f, 1.4f, 2.2f, 0.880f),
                    0.550f, 0.880f, 0.697f, 1.833f, 0f, 0f, 120f),
                // Attack02：转身斜向上攻击再转身，第三段带蓝色霸体语义。
                step3: new TimedActionTuning(
                    new ActionPlayback(4.167f, 1.6f, 2.4f, 1.987f),
                    1.282f, 1.987f, 4.167f, 4.167f, 0f, 0f, 160f)),
            charge: new ChargeTuning(
                holdSeconds: 0.75f,
                action: new TimedActionTuning(
                    new ActionPlayback(2.2f, 1.3f, 2.0f, 0.974f),
                    0.550f, 0.974f, 2.2f, 2.2f, 0f, 0f, 300f),
                reservedTier1Seconds: 0.6f,
                reservedTier2Seconds: 1.2f),
            skillF: new SkillTuning(
                cooldownSeconds: 15f,
                action: new TimedActionTuning(
                    new ActionPlayback(4.6f, 1.5f, 2.4f, 1.840f),
                    1.073f, 1.840f, 4.6f, 4.6f, 0f, 0f, 200f),
                radius: 4f),
            skillV: new SkillTuning(
                cooldownSeconds: 40f,
                // V 技能是位移技能：位移在片段前 0.9 秒内完成。
                action: new TimedActionTuning(
                    new ActionPlayback(5.567f, 1.5f, 2.4f, 2.404f),
                    1.518f, 2.404f, 5.567f, 5.567f, 17.838f, 0.9f, 400f),
                radius: 6f),
            idle: new IdleTuning(variationDelaySeconds: 5f, variationDurationSeconds: 15.067f),
            reaction: new ReactionTuning(
                hitStunSeconds: 2.033f,
                deathSeconds: 2.5f,
                spawnProtectionSeconds: 3f,
                // Burst02：前 2 秒是落地亮相，之后是收招，收招放快。
                spawnLobbyToMap01: TimedActionTuning.Simple(
                    new ActionPlayback(5.5f, 1.0f, 2.5f, 2.0f)),
                // Burst01：从天而降，整体放快，收招再快一档。
                spawnMap01ToMap02: TimedActionTuning.Simple(
                    new ActionPlayback(7.233f, 1.5f, 3.0f, 2.5f))),
            vitals: new VitalsTuning(maxHealth: 1000f, maxArmor: 500f, reviveArmorRatio: 0.5f),
            sprintTapMaxSeconds: 0.18f);
    }
}
