using Naraka.Features.Character.Model;
using UnityEngine;

namespace Naraka.Features.Character.View
{
    /// <summary>
    /// Animator 投影。
    ///
    /// 这是单向的：状态机说播什么就播什么，Animator 的 state 名、Trigger 与
    /// normalizedTime 永远不会被读回业务层。控制器里没有任何 Animator 状态转换条件，
    /// 每个动画各占一个独立 State，切换由这里用缓存好的 Hash 直接 CrossFade。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class PlayerAnimatorProjector : MonoBehaviour
    {
        /// <summary>Animator Controller 里的 State 名。Editor 装配工具用同一份常量建状态。</summary>
        public static class StateNames
        {
            public const string Idle = "Idle";
            public const string IdleVariation = "IdleVariation";
            public const string Walk = "Walk";
            public const string Run = "Run";
            public const string RunTurnback = "RunTurnback";
            public const string StopWalk = "StopWalk";
            public const string StopRun = "StopRun";
            public const string Dash = "Dash";
            public const string AttackCombo1 = "AttackCombo1";
            public const string AttackCombo2 = "AttackCombo2";
            public const string AttackCombo3 = "AttackCombo3";
            public const string Charge = "Charge";
            public const string SkillF = "SkillF";
            public const string SkillV = "SkillV";
            public const string HitStun = "HitStun";
            public const string Death = "Death";
            public const string SpawnBurstLobbyToMap01 = "SpawnBurstLobbyToMap01";
            public const string SpawnBurstMap01ToMap02 = "SpawnBurstMap01ToMap02";

            // Counter 与 Execute 刻意没有 State：长离这套动画里没有经过确认的
            // 反击/处决动作，按"不随意复用其他动画"的约束，宁可暂时没有表现，
            // 也不拿一段不相干的动画顶替。见 NARAKA_待确认问题.md 的 Q-023。
        }

        [Tooltip("状态之间的固定交叉淡入时长（秒）。")]
        [SerializeField] private float crossFadeSeconds = 0.1f;

        [Tooltip("出场、受击与死亡这类一次性动作使用的更短淡入时长（秒）。")]
        [SerializeField] private float snapFadeSeconds = 0.02f;

        // 动作结束交还 Idle / 走 / 跑时的淡入。测量（Pre-P4 Idle Transition Scan 与运行期记录）表明
        // 回 Idle 的"卡一下"不是性能问题，而是收招末姿态与 Idle 首帧差了约 127°（右前臂）与
        // 约 100°（头发）：0.1 秒要甩过这么多，折合约 1270°/s。拉长到 0.25 秒后降到约 500°/s，
        // 不比动作自身收招更快。只用于"动作 → 移动层"，其余切换保持原值。
        // 这只能减轻，不能根治：根治要让收招末姿态与 Idle 首帧衔接（美术端）。
        // Prefab 上没有这个字段时取这里的初始值，因此不必改写 Prefab。
        [Tooltip("动作结束交还 Idle / 走 / 跑时的淡入时长（秒）。收招末姿态与 Idle 首帧差得很远，" +
                 "淡入太短会看成卡一下。")]
        [SerializeField] private float returnFadeSeconds = 0.25f;

        private Animator _animator;
        private int[] _hashes;
        private PlayerAnimation _currentAnimation = PlayerAnimation.None;

        public PlayerAnimation CurrentAnimation => _currentAnimation;

        /// <summary>动作结束交还移动层时使用的淡入时长。回归测试据此核对过渡速度。</summary>
        public float ReturnFadeSeconds => returnFadeSeconds;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            CacheHashes();
        }

        /// <summary>
        /// 施加一帧投影。<paramref name="restart"/> 为真时强制重新进入，
        /// 用于同一动画需要重播的场合（例如连续两次第一段）。
        /// </summary>
        public void Apply(PlayerAnimation animation, bool restart, float speed = 1f)
        {
            if (_animator == null || animation == PlayerAnimation.None)
            {
                return;
            }

            // 播放倍率由业务状态决定（主体一档、后摇一档）。整条 Animator 只播一个
            // State，因此直接写 Animator.speed 就够了，不必为此引入 Animator 参数。
            var wanted = (speed <= 0f ? 1f : speed) * BusinessStepRatio(Time.deltaTime);
            if (_animator.speed != wanted)
            {
                _animator.speed = wanted;
            }

            if (_hashes == null)
            {
                CacheHashes();
            }

            if (!restart && animation == _currentAnimation)
            {
                return;
            }

            var index = (int)animation;
            if (index < 0 || index >= _hashes.Length)
            {
                return;
            }

            var hash = _hashes[index];
            if (hash == 0 || !_animator.HasState(0, hash))
            {
                // 没有对应 State 的动画（当前是 Counter 与 Execute）保持上一个姿态。
                // 这是有意的缺位，不是失败路径，因此不报错也不回退到 Idle。
                return;
            }

            var previous = _currentAnimation;
            _currentAnimation = animation;
            _animator.CrossFadeInFixedTime(
                hash, FadeFor(previous, animation, crossFadeSeconds, snapFadeSeconds, returnFadeSeconds), 0, 0f);
        }

        /// <summary>
        /// 卡顿帧里 Animator 只能走业务走过的那么远。
        ///
        /// 状态机单帧最多推进 <see cref="PlayerCore.MaxDeltaSeconds"/>，而 Animator 按 Time.deltaTime
        /// 推进（最多 0.333 秒）。不对齐的话，几次卡顿就让动画跑到业务前面：
        /// 进地图后头几帧一卡，出场动作在几帧里就播完了，玩家看到的是"角色突然出现"，
        /// 然后定格在末帧等业务结束。按比例压低这一帧的播放倍率，两边推进的片段时间就一致。
        /// </summary>
        public static float BusinessStepRatio(float frameSeconds) =>
            frameSeconds > PlayerCore.MaxDeltaSeconds ? PlayerCore.MaxDeltaSeconds / frameSeconds : 1f;

        /// <summary>
        /// 这一次切换用多长的淡入。
        ///
        /// - 进入受击、死亡、出场：快切（一次性动作要立刻生效）；
        /// - 动作结束交还 Idle / 走 / 跑：回 Idle 淡入（收招末姿态离 Idle 很远，太短会看成卡一下）；
        /// - 其余（移动层内部切换、进入攻击与技能、Idle → 待机动作）：常规淡入，保持原值。
        /// </summary>
        public static float FadeFor(
            PlayerAnimation from, PlayerAnimation to, float crossFade, float snapFade, float returnFade)
        {
            switch (to)
            {
                case PlayerAnimation.HitStun:
                case PlayerAnimation.Death:
                case PlayerAnimation.SpawnBurstLobbyToMap01:
                case PlayerAnimation.SpawnBurstMap01ToMap02:
                    return snapFade;
                case PlayerAnimation.Idle:
                case PlayerAnimation.Walk:
                case PlayerAnimation.Run:
                    return IsLocomotion(from) ? crossFade : returnFade;
                default:
                    return crossFade;
            }
        }

        private static bool IsLocomotion(PlayerAnimation animation) =>
            animation == PlayerAnimation.None ||
            animation == PlayerAnimation.Idle ||
            animation == PlayerAnimation.Walk ||
            animation == PlayerAnimation.Run ||
            animation == PlayerAnimation.RunTurnback;

        /// <summary>
        /// 缓存全部 State Hash。运行期不做任何按名字的字符串查找，
        /// 因此 Update 里没有字符串分配也没有哈希计算。
        /// </summary>
        private void CacheHashes()
        {
            // 数组按枚举最大值开尺寸，没有片段的动画保持 0，Apply 会据此跳过。
            _hashes = new int[(int)PlayerAnimation.Execute + 1];
            _hashes[(int)PlayerAnimation.Idle] = Animator.StringToHash(StateNames.Idle);
            _hashes[(int)PlayerAnimation.IdleVariation] = Animator.StringToHash(StateNames.IdleVariation);
            _hashes[(int)PlayerAnimation.Walk] = Animator.StringToHash(StateNames.Walk);
            _hashes[(int)PlayerAnimation.Run] = Animator.StringToHash(StateNames.Run);
            _hashes[(int)PlayerAnimation.RunTurnback] = Animator.StringToHash(StateNames.RunTurnback);
            _hashes[(int)PlayerAnimation.StopWalk] = Animator.StringToHash(StateNames.StopWalk);
            _hashes[(int)PlayerAnimation.StopRun] = Animator.StringToHash(StateNames.StopRun);
            _hashes[(int)PlayerAnimation.Dash] = Animator.StringToHash(StateNames.Dash);
            _hashes[(int)PlayerAnimation.AttackCombo1] = Animator.StringToHash(StateNames.AttackCombo1);
            _hashes[(int)PlayerAnimation.AttackCombo2] = Animator.StringToHash(StateNames.AttackCombo2);
            _hashes[(int)PlayerAnimation.AttackCombo3] = Animator.StringToHash(StateNames.AttackCombo3);
            _hashes[(int)PlayerAnimation.Charge] = Animator.StringToHash(StateNames.Charge);
            _hashes[(int)PlayerAnimation.SkillF] = Animator.StringToHash(StateNames.SkillF);
            _hashes[(int)PlayerAnimation.SkillV] = Animator.StringToHash(StateNames.SkillV);
            _hashes[(int)PlayerAnimation.HitStun] = Animator.StringToHash(StateNames.HitStun);
            _hashes[(int)PlayerAnimation.Death] = Animator.StringToHash(StateNames.Death);
            _hashes[(int)PlayerAnimation.SpawnBurstLobbyToMap01] =
                Animator.StringToHash(StateNames.SpawnBurstLobbyToMap01);
            _hashes[(int)PlayerAnimation.SpawnBurstMap01ToMap02] =
                Animator.StringToHash(StateNames.SpawnBurstMap01ToMap02);
        }
    }
}
