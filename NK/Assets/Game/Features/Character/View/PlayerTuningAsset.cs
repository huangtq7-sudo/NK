using Naraka.Features.Character.Model;
using UnityEngine;

namespace Naraka.Features.Character.View
{
    /// <summary>
    /// 玩家数值的编辑器载体。它只负责把 Inspector 上的数值转成
    /// <see cref="PlayerTuning"/> 纯数据快照传给 Model —— Model 永远不引用 ScriptableObject。
    ///
    /// 这些是客户端手感与灰盒验证数值。经济与账号资产仍由服务端权威判定，
    /// 不通过这里配置。
    ///
    /// 片段长度由 <c>NARAKA/Setup/Rebuild Player Animator</c> 从实际 AnimationClip 同步，
    /// 手改会被下一次同步覆盖；速度、窗口与位移是设计值，工具不会动它们。
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerTuning", menuName = "NARAKA/Player Tuning")]
    public sealed class PlayerTuningAsset : ScriptableObject
    {
        /// <summary>
        /// 一个有时间轴的动作。窗口全部是**片段时间**，因此调播放速度不会让窗口失效。
        /// </summary>
        [System.Serializable]
        public sealed class ActionEntry
        {
            [Tooltip("动画片段长度（秒）。由 Rebuild Player Animator 从 AnimationClip 同步，别手改。")]
            public float clipSeconds = 1f;

            [Tooltip("主体播放速度倍率。调大让动作更快更跟手。")]
            public float mainSpeed = 1f;

            [Tooltip("后摇播放速度倍率。调大让收招更快。")]
            public float recoverySpeed = 1f;

            [Tooltip("后摇起点（片段时间）。小于 0 时使用命中窗结束时间；没有命中窗则整段算主体。")]
            public float recoveryStartSeconds = -1f;

            [Tooltip("命中窗开始（片段时间）。此前为前摇。")]
            public float hitWindowStart;

            [Tooltip("命中窗结束（片段时间）。此后为后摇。")]
            public float hitWindowEnd;

            [Tooltip("允许消费缓存输入推进下一段的最早时间（片段时间）。")]
            public float comboWindowStart;

            [Tooltip("允许消费缓存输入推进下一段的最晚时间（片段时间）。")]
            public float comboWindowEnd;

            [Tooltip("沿当前朝向的总位移（米）。攻击类动作应为 0，冲刺与位移技能才给值。")]
            public float forwardDisplacement;

            [Tooltip("位移在片段时间的前多少秒内完成。0 表示铺满整段；给小值就是瞬间冲刺。")]
            public float displacementClipSeconds;

            [Tooltip("灰盒基础伤害。")]
            public float damage;

            public TimedActionTuning ToTuning()
            {
                // 后摇起点没填时用命中窗结束时间；连命中窗都没有就整段算主体。
                var recoveryStart = recoveryStartSeconds >= 0f
                    ? recoveryStartSeconds
                    : hitWindowEnd > 0f
                        ? hitWindowEnd
                        : clipSeconds;
                return new TimedActionTuning(
                    new ActionPlayback(clipSeconds, mainSpeed, recoverySpeed, recoveryStart),
                    hitWindowStart,
                    hitWindowEnd,
                    comboWindowStart,
                    comboWindowEnd,
                    forwardDisplacement,
                    displacementClipSeconds,
                    damage);
            }

            public static ActionEntry From(TimedActionTuning tuning) => new ActionEntry
            {
                clipSeconds = tuning.ClipSeconds,
                mainSpeed = tuning.Playback.MainSpeed,
                recoverySpeed = tuning.Playback.RecoverySpeed,
                recoveryStartSeconds = tuning.Playback.RecoveryStartSeconds,
                hitWindowStart = tuning.HitWindowStart,
                hitWindowEnd = tuning.HitWindowEnd,
                comboWindowStart = tuning.ComboWindowStart,
                comboWindowEnd = tuning.ComboWindowEnd,
                forwardDisplacement = tuning.ForwardDisplacement,
                displacementClipSeconds = tuning.DisplacementClipSeconds,
                damage = tuning.Damage
            };
        }

        private static readonly PlayerTuning Baseline = PlayerTuning.CreateBaseline();

        [Header("移动（相机相对，四个方向同速，没有后退）")]
        [Tooltip("行走速度（米/秒）。")]
        public float walkSpeed = 5.0f;

        [Tooltip("奔跑速度（米/秒）。")]
        public float runSpeed = 7.5f;

        [Tooltip("转向角速度（度/秒）。角色朝输入方向转，转到位之前沿当前朝向前进。调大更跟手。")]
        public float turnDegreesPerSecond = 720f;

        public float gravity = 25f;
        public float groundSnapSpeed = 2f;

        [Tooltip("停止走路动画时长（秒）。由 Rebuild Player Animator 从动画片段同步。")]
        public float stopWalkSeconds = 1.5f;

        [Tooltip("停止走路动画烘焙的前移距离（米）。减速曲线按它收敛，避免脚打滑。")]
        public float stopWalkDistance = 1.439f;

        [Tooltip("停止奔跑动画时长（秒）。由 Rebuild Player Animator 从动画片段同步。")]
        public float stopRunSeconds = 1.333f;

        [Tooltip("停止奔跑动画烘焙的前移距离（米）。")]
        public float stopRunDistance = 1.417f;

        [Tooltip("奔跑反向动画时长（秒）。由 Rebuild Player Animator 从动画片段同步。")]
        public float runTurnbackSeconds = 1.667f;

        [Tooltip("奔跑反向的允许时间窗（秒）。超过它就是普通的重新起步，不播反向动作。")]
        public float runTurnbackInputWindowSeconds = 0.25f;

        [Tooltip("判定反向的角度阈值（度）。输入方向与上一次的夹角超过它才算反转。")]
        public float runTurnbackAngleThresholdDegrees = 135f;

        [Tooltip("移动输入死区。摇杆量小于它视为没有移动输入。")]
        public float moveInputDeadzone = 0.2f;

        [Header("体力")]
        public float staminaMax = 20f;
        public float dashStaminaCost = 10f;
        public float staminaRegenPerSecond = 5f;

        [Tooltip("Move_F 结束后多久开始恢复。冲刺进行中同样不恢复。")]
        public float staminaRegenDelaySeconds = 0.75f;

        public float staminaHitPauseSeconds = 0.5f;

        [Header("Shift 与冲刺")]
        [Tooltip("按下 Shift 不足这个时长释放算点按（Move_F），达到则算长按（Run）。")]
        public float sprintTapMaxSeconds = 0.18f;

        [Tooltip("Move_F 快速冲刺。位移窗口很短，因此是瞬间冲刺而不是缓慢滑行。")]
        public ActionEntry dash = ActionEntry.From(Baseline.Dash);

        [Header("待机")]
        [Tooltip("连续无角色操作多久后播放一次待机动作。")]
        public float idleVariationDelaySeconds = 5f;

        [Tooltip("待机动作时长（秒）。由 Rebuild Player Animator 从动画片段同步。")]
        public float idleVariationDurationSeconds = 15.067f;

        [Header("连招")]
        public float comboInputBufferSeconds = 0.18f;
        public float comboResetSeconds = 0.8f;
        public ActionEntry combo1 = ActionEntry.From(Baseline.Combo.Step1);
        public ActionEntry combo2 = ActionEntry.From(Baseline.Combo.Step2);
        public ActionEntry combo3 = ActionEntry.From(Baseline.Combo.Step3);

        [Header("蓄力")]
        [Tooltip("鼠标左键按住达到这个时长自动释放蓄力。")]
        public float chargeHoldSeconds = 0.75f;

        public ActionEntry charge = ActionEntry.From(Baseline.Charge.Action);

        [Tooltip("预留的视觉分档阈值。本阶段只实现单档满蓄力，这两个值不产生任何行为差异。")]
        public float reservedChargeTier1Seconds = 0.6f;

        [Tooltip("见上。")]
        public float reservedChargeTier2Seconds = 1.2f;

        [Header("技能")]
        public float skillFCooldownSeconds = 15f;
        public ActionEntry skillF = ActionEntry.From(Baseline.SkillF.Action);
        public float skillFRadius = 4f;
        public float skillVCooldownSeconds = 40f;
        public ActionEntry skillV = ActionEntry.From(Baseline.SkillV.Action);
        public float skillVRadius = 6f;

        [Header("受击、死亡与出场")]
        public float hitStunSeconds = 2.033f;
        public float deathSeconds = 2.5f;

        [Tooltip("重生保护时长（秒）。")]
        public float spawnProtectionSeconds = 3f;

        [Tooltip("Burst02：大厅进入地图一的出场动画。")]
        public ActionEntry spawnLobbyToMap01 = ActionEntry.From(Baseline.Reaction.SpawnLobbyToMap01);

        [Tooltip("Burst01：地图一进入地图二的出场动画。")]
        public ActionEntry spawnMap01ToMap02 = ActionEntry.From(Baseline.Reaction.SpawnMap01ToMap02);

        [Header("生命与护甲")]
        public float maxHealth = 1000f;
        public float maxArmor = 500f;

        [Tooltip("重生时护甲恢复比例。")]
        [Range(0f, 1f)]
        public float reviveArmorRatio = 0.5f;

        public PlayerTuning ToTuning() => new PlayerTuning(
            new LocomotionTuning(
                walkSpeed,
                runSpeed,
                turnDegreesPerSecond,
                gravity,
                groundSnapSpeed,
                stopWalkSeconds,
                stopWalkDistance,
                stopRunSeconds,
                stopRunDistance,
                runTurnbackSeconds,
                runTurnbackInputWindowSeconds,
                runTurnbackAngleThresholdDegrees,
                moveInputDeadzone),
            new StaminaTuning(
                staminaMax,
                dashStaminaCost,
                staminaRegenPerSecond,
                staminaRegenDelaySeconds,
                staminaHitPauseSeconds),
            dash.ToTuning(),
            new ComboTuning(
                comboInputBufferSeconds,
                comboResetSeconds,
                combo1.ToTuning(),
                combo2.ToTuning(),
                combo3.ToTuning()),
            new ChargeTuning(
                chargeHoldSeconds,
                charge.ToTuning(),
                reservedChargeTier1Seconds,
                reservedChargeTier2Seconds),
            new SkillTuning(skillFCooldownSeconds, skillF.ToTuning(), skillFRadius),
            new SkillTuning(skillVCooldownSeconds, skillV.ToTuning(), skillVRadius),
            new IdleTuning(idleVariationDelaySeconds, idleVariationDurationSeconds),
            new ReactionTuning(
                hitStunSeconds,
                deathSeconds,
                spawnProtectionSeconds,
                spawnLobbyToMap01.ToTuning(),
                spawnMap01ToMap02.ToTuning()),
            new VitalsTuning(maxHealth, maxArmor, reviveArmorRatio),
            sprintTapMaxSeconds);
    }
}
