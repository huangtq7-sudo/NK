using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Networking;
using Naraka.Core.Domain;

namespace Naraka.Infrastructure.Network
{
    /// <summary>
    /// Test-only transport substitute. The runtime composition root uses LegacyNetworkAdapter.
    /// </summary>
    public sealed class MockNetworkFacade : INetworkFacade
    {
        public UniTask SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }

        public UniTask<TResponse> RequestAsync<TRequest, TResponse>(
            TRequest request,
            RequestId requestId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("No mock response has been registered for this request.");
        }

        public IObservable<TEvent> Events<TEvent>() => EmptyObservable<TEvent>.Instance;

        public UniTask SendCriticalAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }

        private sealed class EmptyObservable<T> : IObservable<T>
        {
            public static readonly EmptyObservable<T> Instance = new EmptyObservable<T>();

            private EmptyObservable()
            {
            }

            public IDisposable Subscribe(IObserver<T> observer)
            {
                observer.OnCompleted();
                return EmptyDisposable.Instance;
            }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();

            private EmptyDisposable()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
