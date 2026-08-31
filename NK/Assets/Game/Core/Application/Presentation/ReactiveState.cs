using System;
using R3;

namespace Naraka.Core.Application.Presentation
{
    /// <summary>
    /// Keeps the mutable R3 source private while exposing the existing read-only state seam.
    /// </summary>
    public sealed class ReactiveState<T> : IReadOnlyState<T>, IDisposable
    {
        private readonly ReactiveProperty<T> _source;

        public ReactiveState(T initialValue)
        {
            _source = new ReactiveProperty<T>(initialValue);
        }

        public T Current => _source.Value;

        public void Set(T value) => _source.Value = value;

        public IDisposable Subscribe(IObserver<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            return _source.AsSystemObservable().Subscribe(observer);
        }

        public void Dispose() => _source.Dispose();
    }
}
