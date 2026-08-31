using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Naraka.Server.Application.Accounts;
using Naraka.Server.Application.Networking;
using Naraka.Server.Application.Sessions;
using Naraka.Server.LegacyNetworkV1.Messages;
using Naraka.Server.LegacyNetworkV1.Protocol;

namespace Naraka.Server.LegacyNetworkV1;

/// <summary>
/// Runtime boundary for the frozen AES/handshake/protobuf/framing/heartbeat/Socket.Select transport.
/// Business authentication is delegated to Application services; this type only adapts the V1 wire.
/// </summary>
public sealed class LegacyNetworkTransport(
    AccountService accounts,
    LoginSessionService loginSessions) : ILegacyNetworkTransport
{
    public const string PublicHandshakeKey = "abc123";
    public const int LegacyClientHeartbeatIntervalSeconds = 300;
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(360);

    private int _isRunning;
    private int _isIntegrated;
    private int _listeningPort;
    private int _connectedClientCount;

    public bool IsIntegrated => Volatile.Read(ref _isIntegrated) == 1;

    public int ListeningPort => Volatile.Read(ref _listeningPort);

    public int ConnectedClientCount => Volatile.Read(ref _connectedClientCount);

    public string CompatibilityContract => IsIntegrated
        ? $"LegacyNetworkV1 listening on port {ListeningPort}; frozen V1 wire and authenticated sessions active"
        : "LegacyNetworkV1 socket listener is not active";

    public Task RunAsync(IPAddress listenAddress, int listenPort, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listenAddress);
        if (listenPort is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(listenPort));
        }

        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            throw new InvalidOperationException("The legacy network transport is already running.");
        }

        return Task.Run(() => RunLoop(listenAddress, listenPort, cancellationToken), CancellationToken.None);
    }

    private void RunLoop(IPAddress listenAddress, int listenPort, CancellationToken cancellationToken)
    {
        Socket? listener = null;
        var clients = new List<LegacyClientConnection>();

        try
        {
            listener = new Socket(listenAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listener.ExclusiveAddressUse = true;
            listener.Bind(new IPEndPoint(listenAddress, listenPort));
            listener.Listen(100);

            Volatile.Write(ref _listeningPort, ((IPEndPoint)listener.LocalEndPoint!).Port);
            Volatile.Write(ref _isIntegrated, 1);

            while (!cancellationToken.IsCancellationRequested)
            {
                var readable = new List<Socket>(clients.Count + 1) { listener };
                readable.AddRange(clients.Select(client => client.Socket));
                Socket.Select(readable, null, null, 1_000_000);

                if (readable.Contains(listener))
                {
                    AcceptClient(listener, clients);
                    Volatile.Write(ref _connectedClientCount, clients.Count);
                }

                foreach (var client in clients.ToArray())
                {
                    if (readable.Contains(client.Socket) && !ReceiveAndDispatch(client, cancellationToken))
                    {
                        CloseClient(client, clients);
                    }
                }

                var now = DateTime.UtcNow;
                foreach (var client in clients.Where(client => now - client.LastHeartbeatUtc > HeartbeatTimeout).ToArray())
                {
                    CloseClient(client, clients);
                }
            }
        }
        finally
        {
            foreach (var client in clients.ToArray())
            {
                CloseClient(client, clients);
            }

            CloseSocket(listener);
            Volatile.Write(ref _connectedClientCount, 0);
            Volatile.Write(ref _listeningPort, 0);
            Volatile.Write(ref _isIntegrated, 0);
            Volatile.Write(ref _isRunning, 0);
        }
    }

    private static void AcceptClient(Socket listener, ICollection<LegacyClientConnection> clients)
    {
        var socket = listener.Accept();
        socket.NoDelay = true;
        clients.Add(new LegacyClientConnection(socket));
    }

    private bool ReceiveAndDispatch(LegacyClientConnection client, CancellationToken cancellationToken)
    {
        try
        {
            var received = client.Receive();
            if (received == 0)
            {
                return false;
            }

            while (client.TryTakeFrame(out var frame))
            {
                Dispatch(client, frame, cancellationToken);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is SocketException or IOException or CryptographicException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private void Dispatch(LegacyClientConnection client, LegacyFrame frame, CancellationToken cancellationToken)
    {
        var key = frame.ProtocolName == "MsgSecret"
            ? PublicHandshakeKey
            : client.SessionKey ?? throw new InvalidDataException("Handshake required before business messages.");

        var plaintext = LegacyAesCodec.Decrypt(frame.EncryptedBody, key);
        var message = LegacyProtobufCodec.DeserializeIncoming(frame.ProtocolName, plaintext);
        ValidateEmbeddedProtocol(frame.ProtocolName, message.ProtocolType);

        switch (message)
        {
            case LegacyMsgSecret:
                loginSessions.Disconnect(client.ConnectionId);
                client.SessionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                Send(client, new LegacyMsgSecret { Secret = client.SessionKey }, PublicHandshakeKey);
                break;

            case LegacyMsgPing:
                client.LastHeartbeatUtc = DateTime.UtcNow;
                Send(client, new LegacyMsgPing(), key);
                break;

            case LegacyMsgRegister register:
                HandleRegister(client, register, key, cancellationToken);
                break;

            case LegacyMsgLogin login:
                HandleLogin(client, login, key, cancellationToken);
                break;

            default:
                throw new InvalidDataException($"Unsupported legacy message type: {message.GetType().Name}.");
        }
    }

    private void HandleRegister(
        LegacyClientConnection client,
        LegacyMsgRegister request,
        string key,
        CancellationToken cancellationToken)
    {
        var result = accounts.RegisterAsync(
                request.Account ?? string.Empty,
                request.Password ?? string.Empty,
                cancellationToken)
            .GetAwaiter()
            .GetResult();

        var legacyResult = result.Status switch
        {
            AccountRegistrationStatus.Success => LegacyRegisterResult.Success,
            AccountRegistrationStatus.AlreadyExists => LegacyRegisterResult.AlreadyExist,
            AccountRegistrationStatus.InvalidUsername or AccountRegistrationStatus.WeakPassword => LegacyRegisterResult.Failed,
            _ => LegacyRegisterResult.Failed
        };

        Send(client, new LegacyMsgRegister { Result = legacyResult }, key);
    }

    private void HandleLogin(
        LegacyClientConnection client,
        LegacyMsgLogin request,
        string key,
        CancellationToken cancellationToken)
    {
        var result = loginSessions.LoginAsync(
                client.ConnectionId,
                request.Account ?? string.Empty,
                request.Password ?? string.Empty,
                cancellationToken)
            .GetAwaiter()
            .GetResult();

        var legacyResult = result.Status switch
        {
            AccountAuthenticationStatus.Success => LegacyLoginResult.Success,
            AccountAuthenticationStatus.UserNotFound => LegacyLoginResult.UserNotExist,
            AccountAuthenticationStatus.WrongPassword => LegacyLoginResult.WrongPwd,
            AccountAuthenticationStatus.Forbidden => LegacyLoginResult.Failed,
            _ => LegacyLoginResult.Failed
        };

        var accountId = result.Session?.AccountId ?? 0;
        if (accountId > int.MaxValue)
        {
            loginSessions.Disconnect(client.ConnectionId);
            legacyResult = LegacyLoginResult.Failed;
            accountId = 0;
        }

        Send(client, new LegacyMsgLogin
        {
            Result = legacyResult,
            AccountId = checked((int)accountId)
        }, key);
    }

    private static void ValidateEmbeddedProtocol(string protocolName, LegacyProtocolValue protocolType)
    {
        if (!LegacyProtocolCatalog.Client.TryGetValue(protocolName, out var expected) || expected != (int)protocolType)
        {
            throw new InvalidDataException(
                $"Legacy protocol envelope mismatch: name={protocolName}, value={(int)protocolType}.");
        }
    }

    private static void Send(LegacyClientConnection client, LegacyMessage message, string key)
    {
        var messageTypeName = message.GetType().Name.Replace("Legacy", string.Empty, StringComparison.Ordinal);
        var outbound = LegacyOutboundProtocolResolver.Resolve(messageTypeName);
        message.ProtocolType = (LegacyProtocolValue)outbound.EmbeddedProtocolValue;
        var protobuf = LegacyProtobufCodec.Serialize(message);
        var ciphertext = LegacyAesCodec.Encrypt(protobuf, key);
        client.Send(LegacyFrameCodec.Encode(outbound.WireProtocolName, ciphertext));
    }

    private void CloseClient(LegacyClientConnection client, ICollection<LegacyClientConnection> clients)
    {
        loginSessions.Disconnect(client.ConnectionId);
        clients.Remove(client);
        CloseSocket(client.Socket);
        Volatile.Write(ref _connectedClientCount, clients.Count);
    }

    private static void CloseSocket(Socket? socket)
    {
        if (socket is null)
        {
            return;
        }

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

    private sealed class LegacyClientConnection(Socket socket)
    {
        private const int ReceiveChunkSize = 8 * 1024;
        private readonly byte[] _receiveChunk = new byte[ReceiveChunkSize];
        private byte[] _buffer = new byte[ReceiveChunkSize];
        private int _bufferedCount;

        public Socket Socket { get; } = socket;
        public ConnectionId ConnectionId { get; } = ConnectionId.New();
        public string? SessionKey { get; set; }
        public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;

        public int Receive()
        {
            var received = Socket.Receive(_receiveChunk);
            if (received == 0)
            {
                return 0;
            }

            var required = checked(_bufferedCount + received);
            if (required > LegacyFrameCodec.HeaderLength + LegacyFrameCodec.MaximumPayloadLength)
            {
                throw new InvalidDataException("Legacy receive buffer exceeded the maximum frame size.");
            }

            if (required > _buffer.Length)
            {
                Array.Resize(ref _buffer, Math.Min(
                    Math.Max(required, _buffer.Length * 2),
                    LegacyFrameCodec.HeaderLength + LegacyFrameCodec.MaximumPayloadLength));
            }

            _receiveChunk.AsSpan(0, received).CopyTo(_buffer.AsSpan(_bufferedCount));
            _bufferedCount = required;
            return received;
        }

        public bool TryTakeFrame(out LegacyFrame frame)
        {
            if (!LegacyFrameCodec.TryDecode(_buffer.AsSpan(0, _bufferedCount), out var decoded, out var consumed))
            {
                frame = null!;
                return false;
            }

            frame = decoded!;
            _buffer.AsSpan(consumed, _bufferedCount - consumed).CopyTo(_buffer);
            _bufferedCount -= consumed;
            return true;
        }

        public void Send(byte[] packet)
        {
            var sent = 0;
            while (sent < packet.Length)
            {
                var current = Socket.Send(packet, sent, packet.Length - sent, SocketFlags.None);
                if (current == 0)
                {
                    throw new IOException("Legacy socket closed during send.");
                }

                sent += current;
            }
        }
    }
}
