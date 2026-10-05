using System;
using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using UnityEngine;

namespace Naraka.Features.Combat.View
{
    /// <summary>
    /// 灰盒近战命中盒。玩家与怪物共用同一个实现。
    ///
    /// 它只回答"这一帧扫到了谁"，能不能算、算多少、算几次全部交给 <see cref="IHitResolver"/>。
    /// Animator Event 可以用来对齐表现时间点，但命中窗的真相来自状态机配置，不是动画。
    ///
    /// 性能：使用 NonAlloc 查询与预分配缓冲，稳定态不做每帧分配，也不用 LINQ。
    /// </summary>
    public sealed class MeleeHitbox : MonoBehaviour
    {
        private const int MaxOverlaps = 32;

        [SerializeField] private Faction owner = Faction.Player;

        [Tooltip("命中判定的中心相对本对象的偏移。")]
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 1f, 1.2f);

        [Tooltip("默认命中半径（米）。技能可以用自己的配置半径覆盖它。")]
        [SerializeField] private float radius = 1.6f;

        [Tooltip("参与命中检测的层。")]
        [SerializeField] private LayerMask targetLayers = ~0;

        [Tooltip("勾选后在 Scene 视图画出命中范围。")]
        [SerializeField] private bool drawGizmo = true;

        private readonly Collider[] _overlaps = new Collider[MaxOverlaps];
        private IHitResolver _resolver;
        private HitId _activeHit;
        private bool _windowOpen;
        private float _rawDamage;
        private float _activeRadius;
        private float _activeConeDegrees;
        private AttackColorTag _colorTag;
        private bool _counterable;

        /// <summary>
        /// 这一刀被目标反击掉了。由命中结算同步返回，因此攻击者不需要去问任何全局表
        /// "刚才是不是被反击了"，也不会因为查询时机差一帧而漏掉。
        /// </summary>
        public event Action<int> Countered;

        public bool IsWindowOpen => _windowOpen;

        public HitId ActiveHitId => _activeHit;

        public void Bind(IHitResolver resolver) => _resolver = resolver;

        /// <summary>
        /// 由状态机投影驱动。<paramref name="attackId"/> 为 0 表示命中窗关闭。
        /// 攻击切换时自动释放上一刀的去重记录。
        ///
        /// <paramref name="rawDamage"/> 是扣防御之前的伤害；防御由受击方自己扣。
        /// </summary>
        public void SetWindow(
            int attackId,
            float rawDamage,
            float overrideRadius,
            AttackColorTag colorTag = AttackColorTag.None,
            bool counterable = false,
            float coneAngleDegrees = 0f)
        {
            var open = attackId > 0 && rawDamage > 0f;
            if (!open)
            {
                CloseWindow();
                return;
            }

            var hitId = new HitId(gameObject.GetInstanceID(), attackId);
            if (_windowOpen && !hitId.Equals(_activeHit))
            {
                _resolver?.ReleaseAttack(_activeHit);
            }

            _activeHit = hitId;
            _windowOpen = true;
            _rawDamage = rawDamage;
            _activeRadius = overrideRadius > 0f ? overrideRadius : radius;
            _activeConeDegrees = coneAngleDegrees;
            _colorTag = colorTag;
            _counterable = counterable;
        }

        public void CloseWindow()
        {
            if (!_windowOpen)
            {
                return;
            }

            _windowOpen = false;
            _resolver?.ReleaseAttack(_activeHit);
            _activeHit = default;
            _activeConeDegrees = 0f;
            _colorTag = AttackColorTag.None;
            _counterable = false;
        }

        private void FixedUpdate()
        {
            if (!_windowOpen || _resolver == null)
            {
                return;
            }

            var center = transform.TransformPoint(localOffset);
            var count = Physics.OverlapSphereNonAlloc(
                center, _activeRadius, _overlaps, targetLayers, QueryTriggerInteraction.Collide);
            for (var i = 0; i < count; i++)
            {
                var candidate = _overlaps[i];
                if (candidate == null)
                {
                    continue;
                }

                var receiver = candidate.GetComponentInParent<IDamageTaker>();
                if (receiver == null || !receiver.IsAlive)
                {
                    continue;
                }

                if (!IsInsideCone(candidate.transform.position))
                {
                    continue;
                }

                var request = new HitRequest(
                    _activeHit,
                    owner,
                    receiver.TargetId,
                    receiver.Faction,
                    _rawDamage,
                    _colorTag,
                    _counterable);
                var resolution = _resolver.Resolve(in request, receiver.TakeDamage);
                if (resolution.Rejection == HitRejection.Countered)
                {
                    Countered?.Invoke(receiver.TargetId);
                }
            }
        }

        /// <summary>
        /// 锥形过滤。角度为 0 表示不是锥形攻击，整个球体都算。
        /// 用点积而不是 <c>Vector3.Angle</c>：后者每次都要一次反三角函数。
        /// </summary>
        private bool IsInsideCone(Vector3 targetPosition)
        {
            if (_activeConeDegrees <= 0f || _activeConeDegrees >= 360f)
            {
                return true;
            }

            var toTarget = targetPosition - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            var forward = transform.forward;
            forward.y = 0f;
            var cosine = Vector3.Dot(forward.normalized, toTarget.normalized);
            return cosine >= Mathf.Cos(_activeConeDegrees * 0.5f * Mathf.Deg2Rad);
        }

        private void OnDisable() => CloseWindow();

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmo)
            {
                return;
            }

            Gizmos.color = _windowOpen ? new Color(1f, 0.3f, 0.2f, 0.5f) : new Color(1f, 1f, 1f, 0.15f);
            Gizmos.DrawWireSphere(
                transform.TransformPoint(localOffset), _activeRadius > 0f ? _activeRadius : radius);
        }
#endif
    }
}
