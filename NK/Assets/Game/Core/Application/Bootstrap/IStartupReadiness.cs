namespace Naraka.Core.Application.Bootstrap
{
    public interface IStartupReadiness
    {
        bool IsReady { get; }

        string BlockingReason { get; }
    }
}
