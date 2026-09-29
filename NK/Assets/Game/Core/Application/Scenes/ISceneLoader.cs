using System.Threading;
using Cysharp.Threading.Tasks;

namespace Naraka.Core.Application.Scenes
{
    /// <summary>
    /// 一次进行中的场景加载。抽象出来是为了让"真实进度"与"何时允许激活"
    /// 成为可测试的业务概念，而不是 View 直接去读 Unity 的 AsyncOperation。
    /// </summary>
    public interface ISceneLoadOperation
    {
        /// <summary>
        /// 已经归一化到 0–1 的真实加载进度。
        /// Unity 在允许激活之前最高只会给到 0.9，实现方必须先把它换算成 0–1。
        /// </summary>
        float Progress { get; }

        /// <summary>资源已经就绪，只等一声令下就切换场景。</summary>
        bool IsReadyToActivate { get; }

        /// <summary>允许激活。只有真实加载完成且满足最短显示时间之后才应该调用。</summary>
        void Activate();

        /// <summary>等待场景真正切换完成。</summary>
        UniTask WaitForCompletionAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 场景加载能力。当前实现基于 <c>SceneManager.LoadSceneAsync</c>；
    /// 这层抽象同时是后续迁移到 Addressables 时的边界，业务层不必改动。
    /// </summary>
    public interface ISceneLoader
    {
        /// <summary>
        /// 开始加载并立刻返回句柄。实现必须把 <c>allowSceneActivation</c> 设为 false，
        /// 由调用方决定激活时机。
        /// </summary>
        ISceneLoadOperation BeginLoad(string sceneName);
    }
}
