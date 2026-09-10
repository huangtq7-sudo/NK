using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Timing;

namespace Naraka.Infrastructure.Timing
{
    /// <summary>
    /// 基于Unity真实时间的时钟。使用Realtime而非缩放时间，
    /// 因此加载界面不会受Time.timeScale影响。
    /// </summary>
    public sealed class UnityGameClock : IGameClock
    {
        public double NowSeconds => UnityEngine.Time.realtimeSinceStartupAsDouble;

        public UniTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
            UniTask.Delay(duration, DelayType.Realtime, PlayerLoopTiming.Update, cancellationToken);
    }
}
