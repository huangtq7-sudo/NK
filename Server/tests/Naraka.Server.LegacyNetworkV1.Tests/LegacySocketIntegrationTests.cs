using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Naraka.Server.Application.Accounts;
using Naraka.Server.Application.Lobby;
using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Sessions;
using Naraka.Server.LegacyNetworkV1.Messages;
using Naraka.Server.LegacyNetworkV1.Authentication;
using Naraka.Server.LegacyNetworkV1.Protocol;

namespace Naraka.Server.LegacyNetworkV1.Tests;

public sealed class LegacySocketIntegrationTests
{
    [Fact]
    public async Task LiveSocketCompletesHandshakeRegistrationLoginAndHeartbeat()
    {
        var repository = new MemoryAccountRepository();
        var accounts = new AccountService(repository, new PlainFixturePasswordHasher());
        var sessions = new InMemoryAuthenticatedSessionRegistry();
        var lobbyAccounts = new MemoryLobbyAccountRepository
        {
            Progression = new LobbyAccountProgressionRecord(7, 1234, 56, 7)
        };
        var transport = new LegacyNetworkTransport(
            accounts,
            new LoginSessionService(accounts, sessions),
            new LegacySessionAccountResolver(sessions),
            new LobbyAccountService(lobbyAccounts),
            TestAccountProfiles.Service(),
            TestAccountProfiles.Inventory(),
            TestAccountProfiles.Shop(),
            TestAccountProfiles.Forge(),
            TestAccountProfiles.Gacha(),
            TestAccountProfiles.SignIn(),
            TestAccountProfiles.Achievements(),
            TestAccountProfiles.RedDots(),
            TestAccountProfiles.Socials());
        using var cancellation = new CancellationTokenSource();
        var server = transport.RunAsync(IPAddress.Loopback, 0, cancellation.Token);

        await WaitUntilAsync(() => transport.IsIntegrated);
        Assert.True(transport.ListeningPort > 0);
        Assert.True(
            LegacyNetworkTransport.HeartbeatTimeout.TotalSeconds
            > LegacyNetworkTransport.LegacyClientHeartbeatIntervalSeconds);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, transport.ListeningPort);
            var stream = client.GetStream();

            var secret = Assert.IsType<LegacyMsgSecret>(await RoundTripAsync(
                stream,
                new LegacyMsgSecret(),
                LegacyNetworkTransport.PublicHandshakeKey,
                fragmented: true));
            Assert.False(string.IsNullOrWhiteSpace(secret.Secret));
            Assert.NotEqual(LegacyNetworkTransport.PublicHandshakeKey, secret.Secret);

            var registration = Assert.IsType<LegacyMsgRegister>(await RoundTripAsync(
                stream,
                new LegacyMsgRegister
                {
                    Account = "fixture-user",
                    Password = "fixture-password"
                },
                secret.Secret!));
            Assert.Equal(LegacyRegisterResult.Success, registration.Result);
            Assert.Null(registration.Password);

            var login = Assert.IsType<LegacyMsgLogin>(await RoundTripAsync(
                stream,
                new LegacyMsgLogin
                {
                    Account = "fixture-user",
                    Password = "fixture-password"
                },
                secret.Secret!));
            Assert.Equal(LegacyLoginResult.Success, login.Result);
            Assert.Equal(42, login.AccountId);
            Assert.Null(login.Password);

            var ping = await RoundTripAsync(stream, new LegacyMsgPing(), secret.Secret!);
            Assert.IsType<LegacyMsgPing>(ping);

            // P1.1-A end to end over the frozen wire: the request carries no account id at all.
            var summary = Assert.IsType<LegacyMsgLobbyAccountSummaryResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgLobbyAccountSummaryRequest { RequestId = "fixture-request-1" },
                secret.Secret!));
            Assert.Equal(LegacyLobbyAccountSummaryStatus.Success, summary.Status);
            Assert.Equal("fixture-request-1", summary.RequestId);
            Assert.Equal(7, summary.AccountLevel);
            Assert.Equal(1234, summary.Copper);
            Assert.Equal(56, summary.Silk);
            Assert.Equal(7, summary.Gold);
            Assert.Equal(42, lobbyAccounts.LastRequestedAccountId);
        }
        finally
        {
            client.Close();
            cancellation.Cancel();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.False(transport.IsIntegrated);
        Assert.Equal(0, transport.ConnectedClientCount);
    }

    [Fact]
    public async Task LobbyAccountSummaryIsRejectedWithoutLogin()
    {
        var repository = new MemoryAccountRepository();
        var accounts = new AccountService(repository, new PlainFixturePasswordHasher());
        var sessions = new InMemoryAuthenticatedSessionRegistry();
        var lobbyAccounts = new MemoryLobbyAccountRepository
        {
            Progression = new LobbyAccountProgressionRecord(7, 1234, 56, 7)
        };
        var transport = new LegacyNetworkTransport(
            accounts,
            new LoginSessionService(accounts, sessions),
            new LegacySessionAccountResolver(sessions),
            new LobbyAccountService(lobbyAccounts),
            TestAccountProfiles.Service(),
            TestAccountProfiles.Inventory(),
            TestAccountProfiles.Shop(),
            TestAccountProfiles.Forge(),
            TestAccountProfiles.Gacha(),
            TestAccountProfiles.SignIn(),
            TestAccountProfiles.Achievements(),
            TestAccountProfiles.RedDots(),
            TestAccountProfiles.Socials());
        using var cancellation = new CancellationTokenSource();
        var server = transport.RunAsync(IPAddress.Loopback, 0, cancellation.Token);

        await WaitUntilAsync(() => transport.IsIntegrated);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, transport.ListeningPort);
            var stream = client.GetStream();
            var secret = Assert.IsType<LegacyMsgSecret>(await RoundTripAsync(
                stream,
                new LegacyMsgSecret(),
                LegacyNetworkTransport.PublicHandshakeKey));

            var summary = Assert.IsType<LegacyMsgLobbyAccountSummaryResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgLobbyAccountSummaryRequest { RequestId = "fixture-request-2" },
                secret.Secret!));

            Assert.Equal(LegacyLobbyAccountSummaryStatus.Unauthenticated, summary.Status);
            Assert.Equal(0, summary.AccountLevel);
            Assert.Equal(0, summary.Copper);
            Assert.Null(lobbyAccounts.LastRequestedAccountId);
        }
        finally
        {
            client.Close();
            cancellation.Cancel();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// Regression for the outage where one successful login was followed by "cannot reach the server":
    /// a client sent a protocol name the running build did not register, the resulting InvalidDataException
    /// escaped the receive loop, the accept loop ended and the whole host stopped. An unsupported frame
    /// may only cost the sender its connection; the listener has to keep serving everyone else.
    /// </summary>
    [Fact]
    public async Task UnsupportedProtocolClosesOnlyTheOffendingConnection()
    {
        var repository = new MemoryAccountRepository();
        var accounts = new AccountService(repository, new PlainFixturePasswordHasher());
        var sessions = new InMemoryAuthenticatedSessionRegistry();
        var diagnostics = new RecordingTransportDiagnostics();
        var transport = new LegacyNetworkTransport(
            accounts,
            new LoginSessionService(accounts, sessions),
            new LegacySessionAccountResolver(sessions),
            new LobbyAccountService(new MemoryLobbyAccountRepository()),
            TestAccountProfiles.Service(),
            TestAccountProfiles.Inventory(),
            TestAccountProfiles.Shop(),
            TestAccountProfiles.Forge(),
            TestAccountProfiles.Gacha(),
            TestAccountProfiles.SignIn(),
            TestAccountProfiles.Achievements(),
            TestAccountProfiles.RedDots(),
            TestAccountProfiles.Socials(),
            diagnostics);
        using var cancellation = new CancellationTokenSource();
        var server = transport.RunAsync(IPAddress.Loopback, 0, cancellation.Token);

        await WaitUntilAsync(() => transport.IsIntegrated);
        var port = transport.ListeningPort;

        try
        {
            using (var offender = new TcpClient())
            {
                await offender.ConnectAsync(IPAddress.Loopback, port);
                var offenderStream = offender.GetStream();
                var offenderSecret = Assert.IsType<LegacyMsgSecret>(await RoundTripAsync(
                    offenderStream,
                    new LegacyMsgSecret(),
                    LegacyNetworkTransport.PublicHandshakeKey));

                // Exactly what a newer client sends to an older build: a protocol name it cannot resolve.
                await offenderStream.WriteAsync(
                    EncodeUnknownProtocolFrame("MsgUnregisteredFutureRequest", offenderSecret.Secret!));
                Assert.Equal(0, await ReadWithTimeoutAsync(offenderStream));
            }

            await WaitUntilAsync(() => diagnostics.ConnectionDrops == 1);
            Assert.Contains("frame handling failed", diagnostics.Reasons);
            Assert.True(transport.IsIntegrated);
            Assert.True(transport.ListeningPort > 0);

            // The listening port survived: a fresh client still completes handshake, register and login.
            using var survivor = new TcpClient();
            await survivor.ConnectAsync(IPAddress.Loopback, port);
            var stream = survivor.GetStream();
            var secret = Assert.IsType<LegacyMsgSecret>(await RoundTripAsync(
                stream,
                new LegacyMsgSecret(),
                LegacyNetworkTransport.PublicHandshakeKey));

            var registration = Assert.IsType<LegacyMsgRegister>(await RoundTripAsync(
                stream,
                new LegacyMsgRegister { Account = "fixture-user", Password = "fixture-password" },
                secret.Secret!));
            Assert.Equal(LegacyRegisterResult.Success, registration.Result);

            var login = Assert.IsType<LegacyMsgLogin>(await RoundTripAsync(
                stream,
                new LegacyMsgLogin { Account = "fixture-user", Password = "fixture-password" },
                secret.Secret!));
            Assert.Equal(LegacyLoginResult.Success, login.Result);
            Assert.Equal(42, login.AccountId);
        }
        finally
        {
            cancellation.Cancel();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// A storage fault inside a handler is the same class of failure: it must cost one connection,
    /// not the listening port. Database exceptions are not part of the frozen wire contract, so the
    /// receive loop cannot enumerate them and has to treat every handler fault as connection scoped.
    /// </summary>
    [Fact]
    public async Task StorageFaultDuringLoginClosesOnlyTheOffendingConnection()
    {
        var repository = new MemoryAccountRepository { FailingUsername = "outage-user" };
        var accounts = new AccountService(repository, new PlainFixturePasswordHasher());
        var sessions = new InMemoryAuthenticatedSessionRegistry();
        var diagnostics = new RecordingTransportDiagnostics();
        var transport = new LegacyNetworkTransport(
            accounts,
            new LoginSessionService(accounts, sessions),
            new LegacySessionAccountResolver(sessions),
            new LobbyAccountService(new MemoryLobbyAccountRepository()),
            TestAccountProfiles.Service(),
            TestAccountProfiles.Inventory(),
            TestAccountProfiles.Shop(),
            TestAccountProfiles.Forge(),
            TestAccountProfiles.Gacha(),
            TestAccountProfiles.SignIn(),
            TestAccountProfiles.Achievements(),
            TestAccountProfiles.RedDots(),
            TestAccountProfiles.Socials(),
            diagnostics);
        using var cancellation = new CancellationTokenSource();
        var server = transport.RunAsync(IPAddress.Loopback, 0, cancellation.Token);

        await WaitUntilAsync(() => transport.IsIntegrated);
        var port = transport.ListeningPort;

        try
        {
            using (var offender = new TcpClient())
            {
                await offender.ConnectAsync(IPAddress.Loopback, port);
                var offenderStream = offender.GetStream();
                var offenderSecret = Assert.IsType<LegacyMsgSecret>(await RoundTripAsync(
                    offenderStream,
                    new LegacyMsgSecret(),
                    LegacyNetworkTransport.PublicHandshakeKey));

                var plaintext = LegacyProtobufCodec.Serialize(new LegacyMsgLogin
                {
                    Account = "outage-user",
                    Password = "fixture-password"
                });
                await offenderStream.WriteAsync(LegacyFrameCodec.Encode(
                    "MsgLogin",
                    LegacyAesCodec.Encrypt(plaintext, offenderSecret.Secret!)));
                Assert.Equal(0, await ReadWithTimeoutAsync(offenderStream));
            }

            await WaitUntilAsync(() => diagnostics.ConnectionDrops == 1);
            Assert.True(transport.IsIntegrated);

            using var survivor = new TcpClient();
            await survivor.ConnectAsync(IPAddress.Loopback, port);
            var stream = survivor.GetStream();
            var secret = Assert.IsType<LegacyMsgSecret>(await RoundTripAsync(
                stream,
                new LegacyMsgSecret(),
                LegacyNetworkTransport.PublicHandshakeKey));

            var registration = Assert.IsType<LegacyMsgRegister>(await RoundTripAsync(
                stream,
                new LegacyMsgRegister { Account = "fixture-user", Password = "fixture-password" },
                secret.Secret!));
            Assert.Equal(LegacyRegisterResult.Success, registration.Result);

            var login = Assert.IsType<LegacyMsgLogin>(await RoundTripAsync(
                stream,
                new LegacyMsgLogin { Account = "fixture-user", Password = "fixture-password" },
                secret.Secret!));
            Assert.Equal(LegacyLoginResult.Success, login.Result);
        }
        finally
        {
            cancellation.Cancel();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static byte[] EncodeUnknownProtocolFrame(string protocolName, string key)
    {
        var plaintext = LegacyProtobufCodec.Serialize(new LegacyMsgPing());
        return LegacyFrameCodec.Encode(protocolName, LegacyAesCodec.Encrypt(plaintext, key));
    }

    /// <summary>Reads one byte, or reports the server closed side. Never blocks the test forever.</summary>
    private static async Task<int> ReadWithTimeoutAsync(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await stream.ReadAsync(new byte[1], timeout.Token);
    }

    private sealed class RecordingTransportDiagnostics : ILegacyTransportDiagnostics
    {
        private readonly ConcurrentQueue<string> _reasons = new();

        public int ConnectionDrops => _reasons.Count;

        public IEnumerable<string> Reasons => _reasons.ToArray();

        public void ConnectionDropped(ConnectionId connectionId, string reason, Exception? exception) =>
            _reasons.Enqueue(reason);

        public void AcceptFailed(Exception exception) => _reasons.Enqueue("accept failed");

        public void BusinessFaulted(ConnectionId connectionId, string operation, Exception? exception) =>
            _reasons.Enqueue("business faulted: " + operation);
    }

    private sealed class MemoryLobbyAccountRepository : ILobbyAccountRepository
    {
        public LobbyAccountProgressionRecord? Progression { get; set; }

        public long? LastRequestedAccountId { get; private set; }

        public Task<LobbyAccountProgressionRecord?> FindProgressionAsync(
            long accountId,
            CancellationToken cancellationToken)
        {
            LastRequestedAccountId = accountId;
            return Task.FromResult(Progression);
        }
    }

    private static async Task<LegacyMessage> RoundTripAsync(
        NetworkStream stream,
        LegacyMessage request,
        string key,
        bool fragmented = false)
    {
        var protocolName = request.ProtocolType.ToString();
        var plaintext = LegacyProtobufCodec.Serialize(request);
        var packet = LegacyFrameCodec.Encode(protocolName, LegacyAesCodec.Encrypt(plaintext, key));
        if (fragmented)
        {
            await stream.WriteAsync(packet.AsMemory(0, 3));
            await stream.WriteAsync(packet.AsMemory(3));
        }
        else
        {
            await stream.WriteAsync(packet);
        }

        var header = new byte[LegacyFrameCodec.HeaderLength];
        await stream.ReadExactlyAsync(header);
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        var payload = new byte[payloadLength];
        await stream.ReadExactlyAsync(payload);

        var responsePacket = new byte[header.Length + payload.Length];
        header.CopyTo(responsePacket, 0);
        payload.CopyTo(responsePacket, header.Length);
        Assert.True(LegacyFrameCodec.TryDecode(responsePacket, out var frame, out var consumed));
        Assert.NotNull(frame);
        Assert.Equal(responsePacket.Length, consumed);

        var responsePlaintext = LegacyAesCodec.Decrypt(frame.EncryptedBody, key);
        return LegacyProtobufCodec.DeserializeOutgoing(frame.ProtocolName, responsePlaintext);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class MemoryAccountRepository : IAccountRepository
    {
        private readonly ConcurrentDictionary<string, AccountCredentialRecord> _accounts =
            new(StringComparer.Ordinal);

        /// <summary>Reads for this account fail the way an unreachable database would.</summary>
        public string? FailingUsername { get; init; }

        public Task<AccountCredentialRecord?> FindByUsernameAsync(
            string username,
            CancellationToken cancellationToken)
        {
            if (FailingUsername is not null && string.Equals(username, FailingUsername, StringComparison.Ordinal))
            {
                throw new TimeoutException("Fixture storage is unavailable.");
            }

            _accounts.TryGetValue(username, out var account);
            return Task.FromResult(account);
        }

        public Task<long> CreateAsync(CreateAccountRecord account, CancellationToken cancellationToken)
        {
            const long accountId = 42;
            var credential = new AccountCredentialRecord(
                accountId,
                account.Username,
                account.PasswordHash,
                account.PasswordSalt,
                account.PasswordParameters,
                Status: 0);
            if (!_accounts.TryAdd(account.Username, credential))
            {
                throw new InvalidOperationException("Duplicate fixture account.");
            }

            return Task.FromResult(accountId);
        }
    }

    private sealed class PlainFixturePasswordHasher : IPasswordHasher
    {
        public ValueTask<PasswordHashResult> HashAsync(string password, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PasswordHashResult(
                System.Text.Encoding.UTF8.GetBytes(password),
                [1],
                "fixture"));

        public ValueTask<bool> VerifyAsync(
            string password,
            byte[] expectedHash,
            byte[] salt,
            string parameters,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(expectedHash.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(password)));
    }
}
