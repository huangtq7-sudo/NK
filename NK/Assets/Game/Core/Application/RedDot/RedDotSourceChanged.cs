namespace Naraka.Core.Application.RedDot
{
    /// <summary>
    /// 一个红点来源的状态变化。
    ///
    /// 业务模块只声明"我这个节点现在有没有东西"，不关心它挂在树的哪一层，也不关心
    /// 祖先要不要一起亮。红点模块订阅这个事件并独自完成聚合，因此业务模块与红点模块
    /// 之间没有任何直接引用。
    /// </summary>
    public readonly struct RedDotSourceChanged
    {
        public RedDotSourceChanged(string path, bool hasContent)
        {
            Path = path ?? string.Empty;
            HasContent = hasContent;
        }

        /// <summary>红点节点路径。取值来自 <see cref="RedDotPath"/>。</summary>
        public string Path { get; }

        /// <summary>该节点是否还有可领取、未读或新增的内容。</summary>
        public bool HasContent { get; }
    }

    /// <summary>
    /// 玩家已经看过某个节点。
    ///
    /// 与 <see cref="RedDotSourceChanged"/> 分开，是因为"看过"与"有没有内容"是两件事：
    /// 玩家看过一次之后，业务侧再产生新内容仍然应该重新亮起。
    /// </summary>
    public readonly struct RedDotSeen
    {
        public RedDotSeen(string path)
        {
            Path = path ?? string.Empty;
        }

        public string Path { get; }
    }

    /// <summary>一个动态节点被移除，例如一段被删除的会话。</summary>
    public readonly struct RedDotSourceRemoved
    {
        public RedDotSourceRemoved(string path)
        {
            Path = path ?? string.Empty;
        }

        public string Path { get; }
    }
}
