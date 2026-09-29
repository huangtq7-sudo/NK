using Naraka.Features.Combat.Controller;
using Naraka.Features.Combat.Model;
using UnityEngine;

namespace Naraka.Features.Combat.View
{
    /// <summary>
    /// 灰盒近战命中盒。
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
        private float _damage;
        private float _activeRadius;

        public bool IsWindowOpen => _windowOpen;

        public HitId ActiveHitId => _activeHit;

        public void Bind(IHitResolver resolver) => _resolver = resolver;

        /// <summary>
        /// 由状态机投影驱动。<paramref name="attackId"/> 为 0 表示命中窗关闭。
        /// 攻击切换时自动释放上一刀的去重记录。
        /// </summary>
        public void SetWindow(int attackId, float damage, float overrideRadius)
        {
            var open = attackId > 0 && damage > 0f;
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
            _damage = damage;
            _activeRadius = overrideRadius > 0f ? overrideRadius : radius;
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

                var receiver = candidate.GetComponentInParent<DamageReceiver>();
                if (receiver == null || !receiver.IsAlive)
                {
                    continue;
                }

                var request = new HitRequest(
                    _activeHit, owner, receiver.TargetId, receiver.Faction, _damage);
                _resolver.Resolve(request, receiver.Apply);
            }
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
