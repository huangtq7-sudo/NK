using System;

namespace Naraka.Core.Application.Presentation
{
    /// <summary>
    /// R3 integration seam for continuous read-only presentation state.
    /// </summary>
    public interface IReadOnlyState<out T> : IObservable<T>
    {
        T Current { get; }
    }
}
