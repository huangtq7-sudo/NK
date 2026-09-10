using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Naraka.Core.Application.Timing
{
    /// <summary>
    /// 可注入的时间来源。业务层用它表达"等待"与"经过了多久"，
    /// 以便最短加载时长这类规则可以在EditMode中确定性测试，不依赖真实时间。
    /// </summary>
    public interface IGameClock
    {
        double NowSeconds { get; }

        UniTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
    }
}
