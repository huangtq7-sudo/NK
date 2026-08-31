using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Domain;

namespace Naraka.Core.Application.Networking
{
    public interface INetworkFacade
    {
        UniTask SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken);

        UniTask<TResponse> RequestAsync<TRequest, TResponse>(
            TRequest request,
            RequestId requestId,
            TimeSpan timeout,
            CancellationToken cancellationToken);

        IObservable<TEvent> Events<TEvent>();

        UniTask SendCriticalAsync<TMessage>(TMessage message, CancellationToken cancellationToken);
    }
}
