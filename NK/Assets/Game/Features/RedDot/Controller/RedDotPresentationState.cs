using System;
using System.Collections.Generic;
using Naraka.Core.Application.MVC;

namespace Naraka.Features.RedDot.Controller
{
    /// <summary>
    /// 红点的只读展示状态。
    ///
    /// 界面<b>只</b>订阅这一个状态，不去问任何业务模块"你有没有可领取"。
    /// 状态里只有"哪些节点是亮的"，没有数量、没有业务语义，因此任何一个功能新增红点
    /// 都不需要改动界面代码。
    ///
    /// 在线好友的绿点/灰点不属于红点系统：它表达的是状态而不是"有未处理内容"，
    /// 由社交模块自己的展示状态承载。
    /// </summary>
    public readonly struct RedDotPresentationState : IPresentationState
    {
        private readonly HashSet<string> _active;

        public RedDotPresentationState(long revision, HashSet<string> active)
        {
            Revision = revision;
            _active = active;
        }

        public static RedDotPresentationState Initial =>
            new RedDotPresentationState(0, new HashSet<string>(StringComparer.Ordinal));

        /// <summary>树的整体版本。用于判断状态是否真的变过。</summary>
        public long Revision { get; }

        /// <summary>当前亮起的全部节点路径。</summary>
        public IReadOnlyCollection<string> ActivePaths =>
            (IReadOnlyCollection<string>)_active ?? Array.Empty<string>();

        /// <summary>某个节点是否亮红点。未知路径一律为 false——没有内容就没有红点。</summary>
        public bool IsActive(string path) =>
            _active != null && path != null && _active.Contains(path);

        /// <summary>该节点下是否有任意亮起的后代。用于给一个入口按钮做聚合显示。</summary>
        public bool HasActiveDescendant(string ancestor)
        {
            if (_active == null || string.IsNullOrEmpty(ancestor))
            {
                return false;
            }

            foreach (var path in _active)
            {
                if (Naraka.Core.Application.RedDot.RedDotPath.IsDescendantOf(path, ancestor))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
