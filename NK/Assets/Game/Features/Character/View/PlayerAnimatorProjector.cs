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
        }

        [Tooltip("状态之间的固定交叉淡入时长（秒）。")]
        [SerializeField] private float crossFadeSeconds = 0.1f;

        [Tooltip("出场、受击与死亡这类一次性动作使用的更短淡入时长（秒）。")]
        [SerializeField] private float snapFadeSeconds = 0.02f;

        private Animator _animator;
        private int[] _hashes;
        private PlayerAnimation _currentAnimation = PlayerAnimation.None;

        public PlayerAnimation CurrentAnimation => _currentAnimation;

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
            var wanted = speed <= 0f ? 1f : speed;
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
                return;
            }

            _currentAnimation = animation;
            _animator.CrossFadeInFixedTime(hash, FadeFor(animation), 0, 0f);
        }

        private float FadeFor(PlayerAnimation animation) => animation switch
        {
            PlayerAnimation.HitStun => snapFadeSeconds,
            PlayerAnimation.Death => snapFadeSeconds,
            PlayerAnimation.SpawnBurstLobbyToMap01 => snapFadeSeconds,
            PlayerAnimation.SpawnBurstMap01ToMap02 => snapFadeSeconds,
            _ => crossFadeSeconds
        };

        /// <summary>
        /// 缓存全部 State Hash。运行期不做任何按名字的字符串查找，
        /// 因此 Update 里没有字符串分配也没有哈希计算。
        /// </summary>
        private void CacheHashes()
        {
            _hashes = new int[(int)PlayerAnimation.SpawnBurstMap01ToMap02 + 1];
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
