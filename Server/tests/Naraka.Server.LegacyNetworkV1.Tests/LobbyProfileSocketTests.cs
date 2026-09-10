using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Naraka.Server.Application.Accounts;
using Naraka.Server.Application.Lobby;
using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Sessions;
using Naraka.Server.LegacyNetworkV1.Authentication;
using Naraka.Server.LegacyNetworkV1.Messages;
using Naraka.Server.LegacyNetworkV1.Protocol;

namespace Naraka.Server.LegacyNetworkV1.Tests;

/// <summary>
/// P1.1-B 的真实 Socket 端到端行为：登录时开通账号、读取资料、修改头像，
/// 以及未认证、缺少 RequestId 与非法头像 ID 的拒绝路径。
/// </summary>
public sealed class LobbyProfileSocketTests
{
    private sealed record Fixture(
        LegacyNetworkTransport Transport,
        MemoryAccountProfileRepository Profiles,
        CancellationTokenSource Cancellation,
        Task Server);

    private static Fixture StartServer()
    {
        var accounts = new AccountService(new SimpleAccountRepository(), new PlainTextHasher());
        var sessions = new InMemoryAuthenticatedSessionRegistry();
        var profiles = new MemoryAccountProfileRepository();
        var transport = new LegacyNetworkTransport(
            accounts,
            new LoginSessionService(accounts, sessions),
            new LegacySessionAccountResolver(sessions),
            new LobbyAccountService(new EmptyLobbyAccountRepository()),
            TestAccountProfiles.Service(profiles),
            TestAccountProfiles.Inventory(profiles),
            TestAccountProfiles.Shop(profiles),
            TestAccountProfiles.Forge(profiles),
            TestAccountProfiles.Gacha(profiles),
            TestAccountProfiles.SignIn(profiles),
            TestAccountProfiles.Achievements(profiles),
            TestAccountProfiles.RedDots(),
            TestAccountProfiles.Socials());

        var cancellation = new CancellationTokenSource();
        var server = transport.RunAsync(IPAddress.Loopback, 0, cancellation.Token);
        return new Fixture(transport, profiles, cancellation, server);
    }

    [Fact]
    public async Task LoginProvisionsTheAccountAndTheProfileReadsBackOneThousandOfEachCurrency()
    {
        var fixture = StartServer();
        await WaitUntilAsync(() => fixture.Transport.IsIntegrated);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, fixture.Transport.ListeningPort);
            var stream = client.GetStream();
            var key = await HandshakeAsync(stream);
            await RegisterAndLoginAsync(stream, key);

            var profile = Assert.IsType<LegacyMsgLobbyProfileResponse>(await RoundTripAsync(
                stream, new LegacyMsgLobbyProfileRequest { RequestId = "req-1" }, key));

            Assert.Equal(LegacyLobbyOperationStatus.Success, profile.Status);
            Assert.Equal("req-1", profile.RequestId);
            Assert.Equal(1000, profile.Copper);
            Assert.Equal(1000, profile.Silk);
            Assert.Equal(1000, profile.Gold);
            Assert.Equal(TestAccountProfiles.Config.DefaultHeroId, profile.SelectedHeroId);
            Assert.Equal(TestAccountProfiles.Config.DefaultAvatarId, profile.AvatarId);
            Assert.Equal(1, profile.AccountLevel);
            Assert.Equal(0, profile.AccountXp);

            // 初始赠送必须伴随不可变流水：每种货币一条。
            Assert.Equal(3, fixture.Profiles.Ledger.Count);
            Assert.All(
                fixture.Profiles.Ledger,
                entry => Assert.Equal(CurrencyLedgerReason.StarterGrant, entry.Reason));
        }
        finally
        {
            await StopAsync(client, fixture);
        }
    }

    [Fact]
    public async Task ProfileRequestWithoutLoginIsRejected()
    {
        var fixture = StartServer();
        await WaitUntilAsync(() => fixture.Transport.IsIntegrated);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, fixture.Transport.ListeningPort);
            var stream = client.GetStream();
            var key = await HandshakeAsync(stream);

            var profile = Assert.IsType<LegacyMsgLobbyProfileResponse>(await RoundTripAsync(
                stream, new LegacyMsgLobbyProfileRequest { RequestId = "req-1" }, key));

            Assert.Equal(LegacyLobbyOperationStatus.Unauthenticated, profile.Status);
            Assert.Null(profile.AvatarId);
            Assert.Equal(0, profile.Copper);
        }
        finally
        {
            await StopAsync(client, fixture);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProfileRequestWithoutRequestIdIsRejectedBeforeAuthentication(string? requestId)
    {
        var fixture = StartServer();
        await WaitUntilAsync(() => fixture.Transport.IsIntegrated);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, fixture.Transport.ListeningPort);
            var stream = client.GetStream();
            var key = await HandshakeAsync(stream);
            await RegisterAndLoginAsync(stream, key);

            var profile = Assert.IsType<LegacyMsgLobbyProfileResponse>(await RoundTripAsync(
                stream, new LegacyMsgLobbyProfileRequest { RequestId = requestId }, key));

            Assert.Equal(LegacyLobbyOperationStatus.InvalidRequest, profile.Status);
        }
        finally
        {
            await StopAsync(client, fixture);
        }
    }

    [Fact]
    public async Task AppearanceChangePersistsAndIsReadBack()
    {
        var fixture = StartServer();
        await WaitUntilAsync(() => fixture.Transport.IsIntegrated);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, fixture.Transport.ListeningPort);
            var stream = client.GetStream();
            var key = await HandshakeAsync(stream);
            await RegisterAndLoginAsync(stream, key);

            var avatar = TestAccountProfiles.Config.AvatarsInDisplayOrder[5].AvatarId;
            var frame = TestAccountProfiles.Config.AvatarFramesInDisplayOrder[3].AvatarFrameId;

            var applied = Assert.IsType<LegacyMsgLobbySetAppearanceResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgLobbySetAppearanceRequest
                {
                    RequestId = "req-set",
                    AvatarId = avatar,
                    AvatarFrameId = frame
                },
                key));

            Assert.Equal(LegacyLobbyOperationStatus.Success, applied.Status);
            Assert.Equal(avatar, applied.AvatarId);

            var profile = Assert.IsType<LegacyMsgLobbyProfileResponse>(await RoundTripAsync(
                stream, new LegacyMsgLobbyProfileRequest { RequestId = "req-2" }, key));

            Assert.Equal(avatar, profile.AvatarId);
            Assert.Equal(frame, profile.AvatarFrameId);
        }
        finally
        {
            await StopAsync(client, fixture);
        }
    }

    [Fact]
    public async Task AppearanceChangeWithUnknownIdIsRejectedAndChangesNothing()
    {
        var fixture = StartServer();
        await WaitUntilAsync(() => fixture.Transport.IsIntegrated);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, fixture.Transport.ListeningPort);
            var stream = client.GetStream();
            var key = await HandshakeAsync(stream);
            await RegisterAndLoginAsync(stream, key);

            var rejected = Assert.IsType<LegacyMsgLobbySetAppearanceResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgLobbySetAppearanceRequest
                {
                    RequestId = "req-bad",
                    AvatarId = "avatar_injected_by_client",
                    AvatarFrameId = "frame_white"
                },
                key));

            Assert.Equal(LegacyLobbyOperationStatus.InvalidRequest, rejected.Status);

            var profile = Assert.IsType<LegacyMsgLobbyProfileResponse>(await RoundTripAsync(
                stream, new LegacyMsgLobbyProfileRequest { RequestId = "req-3" }, key));

            Assert.Equal(TestAccountProfiles.Config.DefaultAvatarId, profile.AvatarId);
        }
        finally
        {
            await StopAsync(client, fixture);
        }
    }

    [Fact]
    public async Task RepeatedLoginGrantsTheStarterBundleOnlyOnce()
    {
        var fixture = StartServer();
        await WaitUntilAsync(() => fixture.Transport.IsIntegrated);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, fixture.Transport.ListeningPort);
            var stream = client.GetStream();
            var key = await HandshakeAsync(stream);
            await RegisterAndLoginAsync(stream, key);

            // 同一连接上重复登录，模拟断线重连后再次登录。
            await RoundTripAsync(
                stream,
                new LegacyMsgLogin { Account = "fixture-user", Password = "fixture-password" },
                key);

            var profile = Assert.IsType<LegacyMsgLobbyProfileResponse>(await RoundTripAsync(
                stream, new LegacyMsgLobbyProfileRequest { RequestId = "req-4" }, key));

            Assert.Equal(1000, profile.Copper);
            Assert.Equal(3, fixture.Profiles.Ledger.Count);
        }
        finally
        {
            await StopAsync(client, fixture);
        }
    }

    private static async Task StopAsync(TcpClient client, Fixture fixture)
    {
        client.Close();
        fixture.Cancellation.Cancel();
        await fixture.Server.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Cancellation.Dispose();
    }

    private static async Task<string> HandshakeAsync(NetworkStream stream)
    {
        var secret = Assert.IsType<LegacyMsgSecret>(await RoundTripAsync(
            stream, new LegacyMsgSecret(), LegacyNetworkTransport.PublicHandshakeKey));
        return secret.Secret!;
    }

    private static async Task RegisterAndLoginAsync(NetworkStream stream, string key)
    {
        var registration = Assert.IsType<LegacyMsgRegister>(await RoundTripAsync(
            stream,
            new LegacyMsgRegister { Account = "fixture-user", Password = "fixture-password" },
            key));
        Assert.Equal(LegacyRegisterResult.Success, registration.Result);

        var login = Assert.IsType<LegacyMsgLogin>(await RoundTripAsync(
            stream,
            new LegacyMsgLogin { Account = "fixture-user", Password = "fixture-password" },
            key));
        Assert.Equal(LegacyLoginResult.Success, login.Result);
    }

    private static async Task<LegacyMessage> RoundTripAsync(
        NetworkStream stream,
        LegacyMessage request,
        string key)
    {
        var packet = LegacyFrameCodec.Encode(
            request.ProtocolType.ToString(),
            LegacyAesCodec.Encrypt(LegacyProtobufCodec.Serialize(request), key));
        await stream.WriteAsync(packet);

        var header = new byte[LegacyFrameCodec.HeaderLength];
        await stream.ReadExactlyAsync(header);
        var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
        await stream.ReadExactlyAsync(payload);

        var responsePacket = new byte[header.Length + payload.Length];
        header.CopyTo(responsePacket, 0);
        payload.CopyTo(responsePacket, header.Length);
        Assert.True(LegacyFrameCodec.TryDecode(responsePacket, out var frame, out _));

        return LegacyProtobufCodec.DeserializeOutgoing(
            frame!.ProtocolName,
            LegacyAesCodec.Decrypt(frame.EncryptedBody, key));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class SimpleAccountRepository : IAccountRepository
    {
        private readonly ConcurrentDictionary<string, AccountCredentialRecord> _accounts =
            new(StringComparer.Ordinal);

        private long _nextId;

        public Task<AccountCredentialRecord?> FindByUsernameAsync(
            string username,
            CancellationToken cancellationToken)
        {
            _accounts.TryGetValue(username, out var account);
            return Task.FromResult(account);
        }

        public Task<long> CreateAsync(CreateAccountRecord account, CancellationToken cancellationToken)
        {
            var accountId = Interlocked.Increment(ref _nextId);
            _accounts[account.Username] = new AccountCredentialRecord(
                accountId,
                account.Username,
                account.PasswordHash,
                account.PasswordSalt,
                account.PasswordParameters,
                0);
            return Task.FromResult(accountId);
        }
    }

    /// <summary>密码哈希不是本测试的对象，只需要可复现的确定性实现。</summary>
    private sealed class PlainTextHasher : IPasswordHasher
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

    private sealed class EmptyLobbyAccountRepository : ILobbyAccountRepository
    {
        public Task<LobbyAccountProgressionRecord?> FindProgressionAsync(
            long accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LobbyAccountProgressionRecord?>(null);
    }
}
