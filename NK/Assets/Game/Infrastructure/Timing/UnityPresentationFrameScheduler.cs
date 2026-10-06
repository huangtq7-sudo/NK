using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Presentation;

namespace Naraka.Infrastructure.Timing
{
    /// <summary>
    /// 用 Unity 的 PlayerLoop 实现"等一帧"。
    ///
    /// <c>UniTask.NextFrame</c> 在下一帧的 Update 注入点恢复，因此当前帧已经走完了
    /// LateUpdate 与渲染。对加载界面来说这就是"上一次写进 PresentationState 的值
    /// 已经有机会被画出来了"。
    ///
    /// 刻意**不用** <c>UniTask.Delay</c> 或 <c>Task.Delay</c>：计时器在主线程卡住时
    /// 照样会到期，于是又变成按时间推进进度，这正是要修的缺陷。
    /// 也刻意不用 <c>Thread.Sleep</c>，它会把主线程本身堵住。
    /// </summary>
    public sealed class UnityPresentationFrameScheduler : IPresentationFrameScheduler
    {
        public UniTask NextFrameAsync(CancellationToken cancellationToken) =>
            UniTask.NextFrame(PlayerLoopTiming.Update, cancellationToken);
    }
}
