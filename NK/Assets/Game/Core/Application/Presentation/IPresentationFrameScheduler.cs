using System.Threading;
using Cysharp.Threading.Tasks;

namespace Naraka.Core.Application.Presentation
{
    /// <summary>
    /// 等待表现层真正走过一帧。
    ///
    /// 为什么需要它：写入 PresentationState 只是改了内存里的值，**不等于屏幕上已经显示**。
    /// 加载界面必须先把 0% 显示出来，再去做会卡住主线程的重活；
    /// 100% 也必须至少存在一帧，否则"写 100%"和"隐藏界面"落在同一帧里，
    /// 玩家根本看不到满进度。
    ///
    /// 为什么不用时钟：主线程卡住时墙钟照走，而屏幕一帧都没刷。
    /// 恢复之后按墙钟算出来的进度会一口气跳到一半，看起来就像"进度条从 50% 开始"。
    /// 按"已经真正走过几帧"推进，卡顿期间进度条跟着停住 —— 那是可以接受的；
    /// 跳一半不可以。
    ///
    /// 这个抽象让 Controller 不必知道 PlayerLoop、MonoBehaviour 或 UIDocument 的存在，
    /// 同时让"等了几帧"在测试里可以被精确控制与断言。
    /// </summary>
    public interface IPresentationFrameScheduler
    {
        /// <summary>
        /// 等到表现层有机会呈现下一帧为止。
        ///
        /// 实现必须响应取消，并且不得用线程睡眠或计时器冒充帧边界 ——
        /// 那两种做法在主线程卡住时照样"完成"，于是又回到按时间跳进度的老问题。
        /// </summary>
        UniTask NextFrameAsync(CancellationToken cancellationToken);
    }
}
