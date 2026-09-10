using System;
using System.Collections.Generic;
using Naraka.Core.Application.RedDot;

namespace Naraka.Features.RedDot.Controller
{
    /// <summary>一个红点节点的版本对。</summary>
    public readonly struct RedDotNode
    {
        public RedDotNode(long version, long seenVersion)
        {
            Version = version;
            SeenVersion = seenVersion;
        }

        /// <summary>业务侧最后一次"有新东西"的版本号。单调递增。</summary>
        public long Version { get; }

        /// <summary>玩家最后一次看过的版本号。</summary>
        public long SeenVersion { get; }

        /// <summary>亮红点：还有没看过的版本。</summary>
        public bool IsActive => Version > SeenVersion;
    }

    /// <summary>
    /// 红点前缀树。
    ///
    /// 用版本号而不是 bool，是因为 bool 无法回答"清掉之后又来了一个新的"这种情况：
    /// 玩家点开界面时把 SeenVersion 推到当前 Version，之后业务再推进 Version 红点就会自动重新亮起，
    /// 不需要任何"重新置位"的额外调用，也不会因为顺序问题永久熄灭或永久常亮。
    ///
    /// 叶子变脏时只把它自身与祖先的版本号往上推，兄弟节点不受影响。
    /// </summary>
    public sealed class RedDotTree
    {
        private readonly Dictionary<string, long> _versions = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _seen = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>当前树的整体版本。任何一次变更都会 +1，供展示状态判断是否需要重建。</summary>
        public long Revision { get; private set; }

        /// <summary>已经记录过版本的节点。</summary>
        public IEnumerable<string> Paths => _versions.Keys;

        public RedDotNode Get(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return default;
            }

            _versions.TryGetValue(path, out var version);
            _seen.TryGetValue(path, out var seen);
            return new RedDotNode(version, seen);
        }

        public bool IsActive(string path) => Get(path).IsActive;

        /// <summary>
        /// 设置一个叶子节点是否有内容。
        ///
        /// <paramref name="hasContent"/> 为 true 时把版本推到"未读"；为 false 时把版本拉回已读，
        /// 这样"东西被领完了"就会立刻熄灭，而不是等玩家再点一次。
        /// </summary>
        public bool SetLeaf(string path, bool hasContent)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var node = Get(path);
            if (hasContent == node.IsActive)
            {
                return false;
            }

            if (hasContent)
            {
                Bump(path);
            }
            else
            {
                Clear(path);
            }

            return true;
        }

        /// <summary>把一个叶子推进一个新版本，并让全部祖先跟着变脏。</summary>
        public void Bump(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Revision++;
            foreach (var node in RedDotPath.SelfAndAncestors(path))
            {
                _versions.TryGetValue(node, out var version);
                _versions[node] = version + 1;
            }
        }

        /// <summary>
        /// 标记一个节点已读。
        ///
        /// 只把该节点与其子树推到已读；祖先则重新按"是否还有其它未读子节点"计算，
        /// 因此读完一段会话不会顺手把整个社交入口的红点抹掉。
        /// </summary>
        public void MarkSeen(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Revision++;
            ClearSubtree(path);
            RecomputeAncestors(path);
        }

        /// <summary>清空一个叶子（含子树）并重算祖先。</summary>
        public void Clear(string path) => MarkSeen(path);

        /// <summary>移除一个动态节点，例如一段被删除的会话。</summary>
        public void Remove(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Revision++;
            var removed = new List<string>();
            foreach (var node in _versions.Keys)
            {
                if (string.Equals(node, path, StringComparison.Ordinal) ||
                    RedDotPath.IsDescendantOf(node, path))
                {
                    removed.Add(node);
                }
            }

            foreach (var node in removed)
            {
                _versions.Remove(node);
                _seen.Remove(node);
            }

            RecomputeAncestors(path);
        }

        /// <summary>还原服务端持久化的版本对。重新登录时用它恢复"看过没看过"。</summary>
        public void Restore(string path, long version, long seenVersion)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Revision++;
            _versions[path] = version;
            _seen[path] = seenVersion;
            RecomputeAncestors(path);
        }

        public void Reset()
        {
            Revision++;
            _versions.Clear();
            _seen.Clear();
        }

        private void ClearSubtree(string path)
        {
            _versions.TryGetValue(path, out var version);
            _seen[path] = version;

            foreach (var node in _versions.Keys)
            {
                if (RedDotPath.IsDescendantOf(node, path))
                {
                    _seen[node] = _versions[node];
                }
            }
        }

        /// <summary>
        /// 重算祖先。
        ///
        /// 祖先的红点是"子树里还有没有未读"的聚合结果，因此不能简单地跟着子节点清零：
        /// 这里逐层把祖先的 SeenVersion 对齐到 Version（熄灭）或留在旧值（保持亮起），
        /// 取决于它下面是否还存在未读叶子。
        /// </summary>
        private void RecomputeAncestors(string path)
        {
            var chain = RedDotPath.SelfAndAncestors(path);
            for (var i = 1; i < chain.Count; i++)
            {
                var ancestor = chain[i];
                if (HasActiveDescendant(ancestor))
                {
                    _versions.TryGetValue(ancestor, out var version);
                    _seen.TryGetValue(ancestor, out var seen);
                    if (version <= seen)
                    {
                        _versions[ancestor] = seen + 1;
                    }
                }
                else
                {
                    _versions.TryGetValue(ancestor, out var version);
                    _seen[ancestor] = version;
                }
            }
        }

        private bool HasActiveDescendant(string ancestor)
        {
            foreach (var pair in _versions)
            {
                if (!RedDotPath.IsDescendantOf(pair.Key, ancestor))
                {
                    continue;
                }

                _seen.TryGetValue(pair.Key, out var seen);
                if (pair.Value > seen)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
