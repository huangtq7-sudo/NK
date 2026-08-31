using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Networking;
using Naraka.Core.Domain;
using Naraka.Features.Account.Controller;

namespace Naraka.Infrastructure.Network
{
    /// <summary>
    /// Client adapter for the frozen LegacyNetworkV1 wire. It never accesses Unity objects.
    /// </summary>
    public sealed class LegacyNetworkAdapter : INetworkFacade, IAccountGateway, IDisposable
    {
        public const string PublicHandshakeKey = "abc123";
        public const int HeartbeatIntervalSeconds = 300;

        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);
        private readonly string _host;
        private readonly int _port;
        private readonly object _socketLock = new object();
        private readonly object _sendLock = new object();
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<LegacyProtocolValue, TaskCompletionSource<LegacyMessage>> _pending =
            new ConcurrentDictionary<LegacyProtocolValue, TaskCompletionSource<LegacyMessage>>();
        private readonly ManualResetEventSlim _stop = new ManualResetEventSlim(false);
        private Socket _socket;
        private Thread _receiveThread;
        private Thread _heartbeatThread;
        private TaskCompletionSource<LegacyMsgSecret> _handshake;
        private byte[] _receiveBuffer = new byte[8 * 1024];
        private int _receiveCount;
        private string _sessionKey = string.Empty;
        private DateTime _lastPingUtc;
        private bool _disposed;

        public LegacyNetworkAdapter(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Legacy server host cannot be empty.", nameof(host));
            }

            if (port <= 0 || port > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            _host = host;
            _port = port;
        }

        public async UniTask<AccountRegistrationResult> RegisterAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            var response = await SendRequestAsync<LegacyMsgRegister>(
                new LegacyMsgRegister { Account = username, Password = password },
                DefaultRequestTimeout,
                cancellationToken);

            var status = response.Result switch
            {
                LegacyRegisterResult.Success => AccountRegistrationStatus.Success,
                LegacyRegisterResult.AlreadyExist => AccountRegistrationStatus.AlreadyExists,
                LegacyRegisterResult.Forbidden => AccountRegistrationStatus.Forbidden,
                _ => AccountRegistrationStatus.Failed
            };
            return new AccountRegistrationResult(status);
        }

        public async UniTask<AccountLoginResult> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            var response = await SendRequestAsync<LegacyMsgLogin>(
                new LegacyMsgLogin { Account = username, Password = password },
                DefaultRequestTimeout,
                cancellationToken);

            var status = response.Result switch
            {
                LegacyLoginResult.Success => AccountLoginStatus.Success,
                LegacyLoginResult.WrongPwd => AccountLoginStatus.WrongPassword,
                LegacyLoginResult.UserNotExist => AccountLoginStatus.UserNotFound,
                LegacyLoginResult.TimeoutToken => AccountLoginStatus.Timeout,
                _ => AccountLoginStatus.Failed
            };
            return new AccountLoginResult(status, response.AccountId);
        }

        public UniTask SendAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException(
                "No LegacyNetworkV1 mapping is registered for " + typeof(TMessage).FullName + ".");
        }

        public UniTask<TResponse> RequestAsync<TRequest, TResponse>(
            TRequest request,
            RequestId requestId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException(
                "Use a feature gateway so legacy DTOs do not leak into business controllers.");
        }

        public IObservable<TEvent> Events<TEvent>() => EmptyObservable<TEvent>.Instance;

        public UniTask SendCriticalAsync<TMessage>(TMessage message, CancellationToken cancellationToken) =>
            SendAsync(message, cancellationToken);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Close(new ObjectDisposedException(nameof(LegacyNetworkAdapter)));
            StopWorkers();
            _connectGate.Dispose();
            _stop.Dispose();
        }

        private async UniTask<TMessage> SendRequestAsync<TMessage>(
            TMessage request,
            TimeSpan timeout,
            CancellationToken cancellationToken)
            where TMessage : LegacyMessage
        {
            ThrowIfDisposed();
            await EnsureConnectedAsync(cancellationToken);

            var completion = new TaskCompletionSource<LegacyMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(request.ProtocolType, completion))
            {
                throw new InvalidOperationException(
                    "A legacy request of type " + request.ProtocolType + " is already pending.");
            }

            try
            {
                Send(request, _sessionKey);
                var response = await AwaitWithTimeoutAsync(completion.Task, timeout, cancellationToken);
                return (TMessage)response;
            }
            finally
            {
                _pending.TryRemove(request.ProtocolType, out _);
            }
        }

        private async UniTask EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (IsReady())
            {
                return;
            }

            await _connectGate.WaitAsync(cancellationToken);
            try
            {
                if (IsReady())
                {
                    return;
                }

                Close(new IOException("Legacy connection is being replaced."));
                StopWorkers();
                _stop.Reset();
                _receiveCount = 0;
                _sessionKey = string.Empty;
                _handshake = new TaskCompletionSource<LegacyMsgSecret>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true
                };
                lock (_socketLock)
                {
                    _socket = socket;
                }

                try
                {
                    await Task.Run(() => socket.Connect(_host, _port), cancellationToken);
                    StartWorkers();
                    Send(new LegacyMsgSecret(), PublicHandshakeKey);
                    var secret = await AwaitWithTimeoutAsync(
                        _handshake.Task,
                        DefaultRequestTimeout,
                        cancellationToken);
                    if (string.IsNullOrWhiteSpace(secret.Secret))
                    {
                        throw new InvalidDataException("Legacy handshake returned an empty session key.");
                    }
                }
                catch
                {
                    Close(new IOException("Legacy handshake failed."));
                    throw;
                }
            }
            finally
            {
                _connectGate.Release();
            }
        }

        private void StartWorkers()
        {
            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "NARAKA Legacy Receive"
            };
            _receiveThread.Start();

            _lastPingUtc = DateTime.UtcNow;
            _heartbeatThread = new Thread(HeartbeatLoop)
            {
                IsBackground = true,
                Name = "NARAKA Legacy Heartbeat"
            };
            _heartbeatThread.Start();
        }

        private void ReceiveLoop()
        {
            var chunk = new byte[8 * 1024];
            try
            {
                while (!_stop.IsSet)
                {
                    var socket = GetSocket();
                    if (socket == null)
                    {
                        return;
                    }

                    var received = socket.Receive(chunk);
                    if (received <= 0)
                    {
                        throw new IOException("Legacy server closed the connection.");
                    }

                    AppendReceived(chunk, received);
                    DrainFrames();
                }
            }
            catch (Exception exception)
            {
                if (!_stop.IsSet)
                {
                    Close(exception);
                }
            }
        }

        private void HeartbeatLoop()
        {
            while (!_stop.Wait(TimeSpan.FromSeconds(1)))
            {
                if (string.IsNullOrEmpty(_sessionKey) ||
                    DateTime.UtcNow - _lastPingUtc < TimeSpan.FromSeconds(HeartbeatIntervalSeconds))
                {
                    continue;
                }

                try
                {
                    Send(new LegacyMsgPing(), _sessionKey);
                    _lastPingUtc = DateTime.UtcNow;
                }
                catch (Exception exception)
                {
                    Close(exception);
                    return;
                }
            }
        }

        private void AppendReceived(byte[] source, int count)
        {
            var required = checked(_receiveCount + count);
            if (required > LegacyWireCodec.HeaderLength + LegacyWireCodec.MaximumPayloadLength)
            {
                throw new InvalidDataException("Legacy receive buffer exceeded the safety limit.");
            }

            if (required > _receiveBuffer.Length)
            {
                var next = Math.Min(
                    Math.Max(required, _receiveBuffer.Length * 2),
                    LegacyWireCodec.HeaderLength + LegacyWireCodec.MaximumPayloadLength);
                Array.Resize(ref _receiveBuffer, next);
            }

            Buffer.BlockCopy(source, 0, _receiveBuffer, _receiveCount, count);
            _receiveCount = required;
        }

        private void DrainFrames()
        {
            while (true)
            {
                var key = string.IsNullOrEmpty(_sessionKey) ? PublicHandshakeKey : _sessionKey;
                if (!LegacyWireCodec.TryDecode(
                        _receiveBuffer,
                        _receiveCount,
                        key,
                        out var message,
                        out var consumed))
                {
                    return;
                }

                Buffer.BlockCopy(_receiveBuffer, consumed, _receiveBuffer, 0, _receiveCount - consumed);
                _receiveCount -= consumed;
                Dispatch(message);
            }
        }

        private void Dispatch(LegacyMessage message)
        {
            if (message is LegacyMsgSecret secret)
            {
                _sessionKey = secret.Secret ?? string.Empty;
                _handshake.TrySetResult(secret);
                return;
            }

            if (message is LegacyMsgPing)
            {
                return;
            }

            if (_pending.TryRemove(message.ProtocolType, out var completion))
            {
                completion.TrySetResult(message);
            }
        }

        private void Send(LegacyMessage message, string passphrase)
        {
            var packet = LegacyWireCodec.Encode(message, passphrase);
            lock (_sendLock)
            {
                var socket = GetSocket();
                if (socket == null || !socket.Connected)
                {
                    throw new IOException("Legacy socket is not connected.");
                }

                var offset = 0;
                while (offset < packet.Length)
                {
                    var sent = socket.Send(packet, offset, packet.Length - offset, SocketFlags.None);
                    if (sent <= 0)
                    {
                        throw new IOException("Legacy socket closed during send.");
                    }

                    offset += sent;
                }
            }
        }

        private bool IsReady()
        {
            var socket = GetSocket();
            return socket != null && socket.Connected && !string.IsNullOrEmpty(_sessionKey) && !_stop.IsSet;
        }

        private Socket GetSocket()
        {
            lock (_socketLock)
            {
                return _socket;
            }
        }

        private void Close(Exception reason)
        {
            _stop.Set();
            Socket socket;
            lock (_socketLock)
            {
                socket = _socket;
                _socket = null;
            }

            if (socket != null)
            {
                try
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
                catch (SocketException)
                {
                }
                finally
                {
                    socket.Dispose();
                }
            }

            _sessionKey = string.Empty;
            _handshake?.TrySetException(reason);
            foreach (var pair in _pending.ToArray())
            {
                if (_pending.TryRemove(pair.Key, out var completion))
                {
                    completion.TrySetException(reason);
                }
            }
        }

        private void StopWorkers()
        {
            if (_receiveThread != null && _receiveThread != Thread.CurrentThread)
            {
                _receiveThread.Join(TimeSpan.FromSeconds(1));
            }

            if (_heartbeatThread != null && _heartbeatThread != Thread.CurrentThread)
            {
                _heartbeatThread.Join(TimeSpan.FromSeconds(1));
            }

            _receiveThread = null;
            _heartbeatThread = null;
        }

        private static async Task<T> AwaitWithTimeoutAsync<T>(
            Task<T> task,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var delay = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(task, delay);
            if (completed == task)
            {
                return await task;
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Legacy network request timed out.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LegacyNetworkAdapter));
            }
        }

        private sealed class EmptyObservable<T> : IObservable<T>
        {
            public static readonly EmptyObservable<T> Instance = new EmptyObservable<T>();

            public IDisposable Subscribe(IObserver<T> observer)
            {
                observer?.OnCompleted();
                return EmptyDisposable.Instance;
            }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();

            public void Dispose()
            {
            }
        }
    }
}
