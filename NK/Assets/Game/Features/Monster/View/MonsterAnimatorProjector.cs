using Naraka.Features.Monster.Model;
using UnityEngine;

namespace Naraka.Features.Monster.View
{
    /// <summary>
    /// 把怪物 HFSM 的当前动作**单向**投影到 Animator，形态与玩家的
    /// <c>PlayerAnimatorProjector</c> 完全一致：
    ///
    /// - 每个 <see cref="MonsterAnimation"/> 各占一个独立 State；
    /// - Animator Controller 有 **0 个 Parameter、0 条 Transition**；
    /// - 切换由这里用缓存好的 Hash 直接 <c>CrossFadeInFixedTime</c>；
    /// - 业务层从不读取 Animator 的 State 名、Trigger 或 <c>normalizedTime</c>。
    ///
    /// 为什么必须是单向：Animator 一旦参与决定"现在是什么动作"，
    /// 就出现了第二份当前动作真相，而 <see cref="MonsterCore"/> 的动作层已经是唯一真相
    /// （见 ADR-0018）。Parameter 与 Transition 是 Animator 自己做决定的入口，
    /// 因此它们的数量必须是 0，而不是"我们约定不用"。
    ///
    /// Root Motion 在 <c>Awake</c> 里强制关闭：移动由
    /// <see cref="DuskshadowWolfView"/> 经 NavMeshAgent 驱动，
    /// 动画再推一次世界坐标会变成双倍位移。导入的也是 `WO Root`（without root motion）
    /// 那一套动画，这里关闭只是第二道保险 —— 美术换一版带 Root 的动画进来时
    /// 不应该表现为"狼突然漂移"。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterAnimatorProjector : MonoBehaviour
    {
        /// <summary>
        /// Animator State 名。装配工具按这些名字建 State，投影层按同样的名字取 Hash，
        /// 因此两边不可能各写一份字符串。
        /// </summary>
        public static class StateNames
        {
            public const string Idle = "Idle";
            public const string Walk = "Walk";
            public const string Run = "Run";
            public const string Attack = "Attack";
            public const string Skill = "Skill";
            public const string HitStun = "HitStun";
            public const string Death = "Death";
        }

        [Tooltip("目标 Animator。留空时自动在子物体里查找（正式模型的 Animator 在视觉子物体上）。")]
        [SerializeField] private Animator animator;

        [Tooltip("常规动作之间的交叉淡入时长（秒）。")]
        [SerializeField] private float crossFadeSeconds = 0.08f;

        [Tooltip("受击与死亡的淡入时长。这两种必须立刻可读，因此比常规短得多。")]
        [SerializeField] private float snapFadeSeconds = 0.02f;

        private int[] _hashes;
        private MonsterAnimation _currentAnimation = MonsterAnimation.None;

        /// <summary>当前投影到 Animator 的动画。测试断言它，而不是去读 Animator 内部状态。</summary>
        public MonsterAnimation CurrentAnimation => _currentAnimation;

        public Animator Animator => animator;

        /// <summary>投影器有没有可用的 Animator。缺少 Animator 时整个投影是空操作。</summary>
        public bool IsReady => animator != null && _hashes != null;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (animator == null)
            {
                Debug.LogError(
                    "MonsterAnimatorProjector 找不到 Animator，怪物不会播放任何动画。" +
                    "请把正式模型作为视觉子物体挂在怪物 Prefab 下。",
                    this);
                return;
            }

            // 移动由 NavMeshAgent 驱动，动画不得再推世界坐标。
            animator.applyRootMotion = false;
            CacheHashes();
        }

        /// <summary>
        /// 投影一次。<paramref name="restart"/> 为真时即使动画没变也从头播，
        /// 用于"连续两次普攻"这种同一动画需要重新开始的情况。
        /// </summary>
        public void Apply(MonsterAnimation animation, bool restart)
        {
            if (!IsReady || animation == MonsterAnimation.None)
            {
                return;
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
            if (hash == 0 || !animator.HasState(0, hash))
            {
                // 没有对应 State 的动画保持上一个姿态。这与玩家投影层的处理一致：
                // 表现缺位是有意的，不报错也不回退到 Idle。
                return;
            }

            _currentAnimation = animation;
            animator.CrossFadeInFixedTime(hash, FadeFor(animation), 0, 0f);
        }

        private float FadeFor(MonsterAnimation animation) => animation switch
        {
            MonsterAnimation.HitStun => snapFadeSeconds,
            MonsterAnimation.Death => snapFadeSeconds,
            _ => crossFadeSeconds
        };

        /// <summary>
        /// 缓存全部 State Hash。运行期不做任何按名字的字符串查找，
        /// 因此 Update 里没有字符串分配也没有哈希计算。
        /// </summary>
        private void CacheHashes()
        {
            _hashes = new int[(int)MonsterAnimation.Death + 1];
            _hashes[(int)MonsterAnimation.Idle] = Animator.StringToHash(StateNames.Idle);
            _hashes[(int)MonsterAnimation.Walk] = Animator.StringToHash(StateNames.Walk);
            _hashes[(int)MonsterAnimation.Run] = Animator.StringToHash(StateNames.Run);
            _hashes[(int)MonsterAnimation.Attack] = Animator.StringToHash(StateNames.Attack);
            _hashes[(int)MonsterAnimation.Skill] = Animator.StringToHash(StateNames.Skill);
            _hashes[(int)MonsterAnimation.HitStun] = Animator.StringToHash(StateNames.HitStun);
            _hashes[(int)MonsterAnimation.Death] = Animator.StringToHash(StateNames.Death);
        }

        /// <summary>State 名与 <see cref="MonsterAnimation"/> 的对应表。装配工具与测试共用。</summary>
        public static string StateNameFor(MonsterAnimation animation) => animation switch
        {
            MonsterAnimation.Idle => StateNames.Idle,
            MonsterAnimation.Walk => StateNames.Walk,
            MonsterAnimation.Run => StateNames.Run,
            MonsterAnimation.Attack => StateNames.Attack,
            MonsterAnimation.Skill => StateNames.Skill,
            MonsterAnimation.HitStun => StateNames.HitStun,
            MonsterAnimation.Death => StateNames.Death,
            _ => null
        };
    }
}
