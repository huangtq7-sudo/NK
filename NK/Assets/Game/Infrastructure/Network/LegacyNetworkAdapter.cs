using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Networking;
using Naraka.Core.Domain;

namespace Naraka.Infrastructure.Network
{
    /// <summary>
    /// Boundary for the frozen AES/handshake/Protobuf/Socket transport.
    /// Legacy source is integrated behind this type after protocol golden tests are captured.
    /// </summary>
    public sealed class LegacyNetworkAdapter : INetworkFacade
    {
        public UniTask SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken) =>
            throw NotIntegrated();

        public UniTask<TResponse> RequestAsync<TRequest, TResponse>(
            TRequest request,
            RequestId requestId,
            TimeSpan timeout,
            CancellationToken cancellationToken) => throw NotIntegrated();

        public IObservable<TEvent> Events<TEvent>() => throw NotIntegrated();

        public UniTask SendCriticalAsync<TMessage>(TMessage message, CancellationToken cancellationToken) =>
            throw NotIntegrated();

        private static InvalidOperationException NotIntegrated() =>
            new InvalidOperationException("LegacyNetworkV1 has not been integrated. Use MockNetworkFacade during P0.");
    }
}
