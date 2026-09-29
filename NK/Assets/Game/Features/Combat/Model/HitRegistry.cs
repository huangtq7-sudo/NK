using System.Collections.Generic;

namespace Naraka.Features.Combat.Model
{
    /// <summary>
    /// 攻击去重表。规则：同一次攻击（同一个 <see cref="HitId"/>）对同一个目标最多结算一次。
    ///
    /// 实现上按 HitId 保存已命中集合，攻击结束时整条丢弃。
    /// 不用"每帧新建集合"的写法，避免稳定态每帧分配。
    /// </summary>
    public sealed class HitRegistry
    {
        private readonly Dictionary<HitId, HashSet<int>> _hits = new Dictionary<HitId, HashSet<int>>();
        private readonly Stack<HashSet<int>> _pool = new Stack<HashSet<int>>();

        public bool HasHit(HitId hitId, int targetId) =>
            _hits.TryGetValue(hitId, out var targets) && targets.Contains(targetId);

        /// <summary>登记一次命中。返回 false 表示这一刀已经打过这个目标。</summary>
        public bool TryRegister(HitId hitId, int targetId)
        {
            if (!_hits.TryGetValue(hitId, out var targets))
            {
                targets = _pool.Count > 0 ? _pool.Pop() : new HashSet<int>();
                _hits[hitId] = targets;
            }

            return targets.Add(targetId);
        }

        /// <summary>攻击结束，回收这一刀的记录。</summary>
        public void Release(HitId hitId)
        {
            if (!_hits.TryGetValue(hitId, out var targets))
            {
                return;
            }

            _hits.Remove(hitId);
            targets.Clear();
            _pool.Push(targets);
        }

        public void Clear()
        {
            foreach (var pair in _hits)
            {
                pair.Value.Clear();
                _pool.Push(pair.Value);
            }

            _hits.Clear();
        }

        public int TrackedAttackCount => _hits.Count;
    }
}
