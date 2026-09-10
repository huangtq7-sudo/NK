namespace Naraka.Core.Application.Config
{
    /// <summary>
    /// 配置目录的注入入口。Controller 只依赖这个接口，
    /// 因此测试可以直接构造内存目录，而不需要 StreamingAssets 或 Unity 运行时。
    /// </summary>
    public interface IGameConfigProvider
    {
        /// <summary>配置是否已经成功加载。为 false 时界面必须显示配置错误，而不是显示空列表。</summary>
        bool IsLoaded { get; }

        /// <summary>加载失败时的中文提示，成功时为空字符串。不包含文件系统路径。</summary>
        string LoadError { get; }

        /// <summary>已加载的只读目录。<see cref="IsLoaded"/> 为 false 时为 null。</summary>
        GameConfigCatalog Catalog { get; }
    }
}
