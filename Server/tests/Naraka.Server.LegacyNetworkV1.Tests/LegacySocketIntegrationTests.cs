using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Naraka.Server.Application.Accounts;
using Naraka.Server.Application.Sessions;
using Naraka.Server.LegacyNetworkV1.Messages;
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
        var transport = new LegacyNetworkTransport(accounts, new LoginSessionService(accounts, sessions));
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
        return LegacyProtobufCodec.DeserializeIncoming(frame.ProtocolName, responsePlaintext);
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

        public Task<AccountCredentialRecord?> FindByUsernameAsync(
            string username,
            CancellationToken cancellationToken)
        {
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
