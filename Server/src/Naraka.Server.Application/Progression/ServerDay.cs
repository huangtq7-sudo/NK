namespace Naraka.Server.Application.Progression;

/// <summary>
/// 服务器"游戏日"的换算。
///
/// 玩法基线规定七日签到以<b>服务器每日 05:00</b> 作为日期边界。这里把它实现为一个纯函数：
/// 给定 UTC 时间，返回一个单调递增的整数天号。用天号而不是日期，是因为"相差几天"
/// 这类判断在整数上是显然正确的，而在日期上很容易在跨月和跨年时出错。
///
/// 时区固定为 UTC+8：本作面向中文玩家，服务器日的语义必须与玩家的日常作息一致，
/// 而不是跟随部署机房所在时区漂移。运维把机器搬到别的时区不会改变游戏日边界。
/// </summary>
public static class ServerDay
{
    /// <summary>服务器时区相对 UTC 的偏移。</summary>
    public static readonly TimeSpan TimeZoneOffset = TimeSpan.FromHours(8);

    /// <summary>每日重置时刻。05:00 之前仍算前一天。</summary>
    public static readonly TimeSpan ResetTimeOfDay = TimeSpan.FromHours(5);

    /// <summary>天号的零点。取一个固定的过去日期，保证天号恒为正且跨版本稳定。</summary>
    private static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>把 UTC 时间换算成服务器游戏日天号。</summary>
    public static long FromUtc(DateTime utcNow)
    {
        var serverLocal = utcNow + TimeZoneOffset - ResetTimeOfDay;
        return (long)Math.Floor((serverLocal - Epoch).TotalDays);
    }

    /// <summary>该天号对应的服务器日起始 UTC 时刻。用于日志与展示，不参与判定。</summary>
    public static DateTime ToUtcStart(long serverDay) =>
        Epoch.AddDays(serverDay) + ResetTimeOfDay - TimeZoneOffset;
}
