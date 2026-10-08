using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Naraka.Server.Application.Accounts;
using Naraka.Server.Application.Expeditions;
using Naraka.Server.Application.Lobby;
using Naraka.Server.Application.Sessions;
using Naraka.Server.Domain.Expeditions;
using Naraka.Server.LegacyNetworkV1.Authentication;
using Naraka.Server.LegacyNetworkV1.Messages;
using Naraka.Server.LegacyNetworkV1.Protocol;

namespace Naraka.Server.LegacyNetworkV1.Tests;

/// <summary>
/// P3 application adapter tests use a real encrypted socket and an in-memory authoritative
/// expedition repository. They prove authentication, routing, replay, and connection lifecycle
/// without modifying the frozen frame/AES transport or requiring MySQL.
/// </summary>
public sealed class ExpeditionSocketTests
{
    private sealed record Fixture(
        LegacyNetworkTransport Transport,
        MemoryExpeditionRepository Expeditions,
        InMemoryAuthenticatedSessionRegistry Sessions,
        CancellationTokenSource Cancellation,
        Task Server);

    [Fact]
    public async Task AuthenticatedSocketCompletesTheExpeditionCommandLifecycle()
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

            var started = Assert.IsType<LegacyMsgExpeditionResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgExpeditionRequest
                {
                    RequestId = "start-001",
                    Operation = LegacyExpeditionOperation.Start
                },
                key));
            Assert.Equal(LegacyLobbyOperationStatus.Success, started.Status);
            Assert.False(started.IsReplay);
            Assert.Equal("exp-socket-001", started.Snapshot?.ExpeditionId);
            Assert.Equal(ExpeditionMapIds.Map01, started.Snapshot?.EntryMapId);
            Assert.Equal(LegacyExpeditionState.Active, started.Snapshot?.State);

            var active = Assert.IsType<LegacyMsgExpeditionResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgExpeditionRequest
                {
                    RequestId = "get-001",
                    Operation = LegacyExpeditionOperation.GetActive
                },
                key));
            Assert.Equal(LegacyLobbyOperationStatus.Success, active.Status);
            Assert.Equal(started.Snapshot?.ExpeditionId, active.Snapshot?.ExpeditionId);

            var death = Assert.IsType<LegacyMsgExpeditionResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgExpeditionRequest
                {
                    RequestId = "death-001",
                    Operation = LegacyExpeditionOperation.RecordDeath,
                    ExpeditionId = started.Snapshot!.ExpeditionId
                },
                key));
            Assert.Equal(LegacyLobbyOperationStatus.Success, death.Status);
            Assert.Equal(1, death.Death?.DeathCount);
            Assert.Equal(1, death.Snapshot?.DeathCount);

            var settled = Assert.IsType<LegacyMsgExpeditionResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgExpeditionRequest
                {
                    RequestId = "return-001",
                    Operation = LegacyExpeditionOperation.ReturnToLobby,
                    ExpeditionId = started.Snapshot.ExpeditionId
                },
                key));
            Assert.Equal(LegacyLobbyOperationStatus.Success, settled.Status);
            Assert.False(settled.IsReplay);
            Assert.Equal(LegacyExpeditionSettlementReason.ReturnedToLobby, settled.Settlement?.Reason);

            var replay = Assert.IsType<LegacyMsgExpeditionResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgExpeditionRequest
                {
                    RequestId = "return-retry-001",
                    Operation = LegacyExpeditionOperation.ReturnToLobby,
                    ExpeditionId = started.Snapshot.ExpeditionId
                },
                key));
            Assert.Equal(LegacyLobbyOperationStatus.Success, replay.Status);
            Assert.True(replay.IsReplay);
            Assert.Equal("return-001", replay.Settlement?.RequestId);
            Assert.Null(fixture.Expeditions.Active);
        }
        finally
        {
            await StopClientThenServerAsync(client, fixture);
        }
    }

    [Fact]
    public async Task ExpeditionCommandRequiresAuthenticationAndAValidShape()
    {
        var fixture = StartServer();
        await WaitUntilAsync(() => fixture.Transport.IsIntegrated);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, fixture.Transport.ListeningPort);
            var stream = client.GetStream();
            var key = await HandshakeAsync(stream);

            var unauthenticated = Assert.IsType<LegacyMsgExpeditionResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgExpeditionRequest
                {
                    RequestId = "start-unauthenticated",
                    Operation = LegacyExpeditionOperation.Start
                },
                key));
            Assert.Equal(LegacyLobbyOperationStatus.Unauthenticated, unauthenticated.Status);

            await RegisterAndLoginAsync(stream, key);
            var forgedStartId = Assert.IsType<LegacyMsgExpeditionResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgExpeditionRequest
                {
                    RequestId = "start-with-client-id",
                    Operation = LegacyExpeditionOperation.Start,
                    ExpeditionId = "client-chosen-id"
                },
                key));
            Assert.Equal(LegacyLobbyOperationStatus.InvalidRequest, forgedStartId.Status);
            Assert.Null(fixture.Expeditions.Active);

            var missingDeathId = Assert.IsType<LegacyMsgExpeditionResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgExpeditionRequest
                {
                    RequestId = "death-without-expedition",
                    Operation = LegacyExpeditionOperation.RecordDeath
                },
                key));
            Assert.Equal(LegacyLobbyOperationStatus.InvalidRequest, missingDeathId.Status);
        }
        finally
        {
            await StopClientThenServerAsync(client, fixture);
        }
    }

    [Fact]
    public async Task ClientDisconnectSettlesTheActiveExpeditionBeforeRemovingTheSession()
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
            var started = Assert.IsType<LegacyMsgExpeditionResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgExpeditionRequest
                {
                    RequestId = "start-before-disconnect",
                    Operation = LegacyExpeditionOperation.Start
                },
                key));
            Assert.Equal(LegacyLobbyOperationStatus.Success, started.Status);

            client.Close();
            await WaitUntilAsync(() => fixture.Expeditions.Settlement is not null);

            Assert.Equal(
                ExpeditionSettlementReason.ConnectionLost,
                fixture.Expeditions.Settlement?.Reason);
            Assert.Equal("server-connection-lost", fixture.Expeditions.Settlement?.RequestId.Value);
            Assert.False(fixture.Sessions.IsOnline(1));
        }
        finally
        {
            fixture.Cancellation.Cancel();
            await fixture.Server.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Cancellation.Dispose();
        }
    }

    [Fact]
    public async Task HostShutdownPreservesAnActiveExpeditionForRecovery()
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
            var started = Assert.IsType<LegacyMsgExpeditionResponse>(await RoundTripAsync(
                stream,
                new LegacyMsgExpeditionRequest
                {
                    RequestId = "start-before-host-stop",
                    Operation = LegacyExpeditionOperation.Start
                },
                key));
            Assert.Equal(LegacyLobbyOperationStatus.Success, started.Status);

            fixture.Cancellation.Cancel();
            await fixture.Server.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotNull(fixture.Expeditions.Active);
            Assert.Null(fixture.Expeditions.Settlement);
        }
        finally
        {
            fixture.Cancellation.Dispose();
        }
    }

    private static Fixture StartServer()
    {
        var accounts = new AccountService(new MemoryAccountRepository(), new PlainTextHasher());
        var sessions = new InMemoryAuthenticatedSessionRegistry();
        var profiles = new MemoryAccountProfileRepository();
        var expeditionRepository = new MemoryExpeditionRepository();
        var expeditionService = new ExpeditionService(
            expeditionRepository,
            new FixedExpeditionIdGenerator());
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
            TestAccountProfiles.Socials(sessions),
            expeditions: expeditionService);

        var cancellation = new CancellationTokenSource();
        var server = transport.RunAsync(IPAddress.Loopback, 0, cancellation.Token);
        return new Fixture(transport, expeditionRepository, sessions, cancellation, server);
    }

    private static async Task StopClientThenServerAsync(TcpClient client, Fixture fixture)
    {
        client.Close();
        fixture.Cancellation.Cancel();
        await fixture.Server.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Cancellation.Dispose();
    }

    private static async Task<string> HandshakeAsync(NetworkStream stream)
    {
        var secret = Assert.IsType<LegacyMsgSecret>(await RoundTripAsync(
            stream,
            new LegacyMsgSecret(),
            LegacyNetworkTransport.PublicHandshakeKey));
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

    private sealed class FixedExpeditionIdGenerator : IExpeditionIdGenerator
    {
        public ExpeditionId NewId() => new("exp-socket-001");
    }

    private sealed class MemoryExpeditionRepository : IExpeditionRepository
    {
        private readonly object _sync = new();
        private string? _startRequestId;
        private ExpeditionSnapshot? _active;
        private ExpeditionSettlementSummary? _settlement;

        public ExpeditionSnapshot? Active
        {
            get
            {
                lock (_sync)
                {
                    return _active;
                }
            }
        }

        public ExpeditionSettlementSummary? Settlement
        {
            get
            {
                lock (_sync)
                {
                    return _settlement;
                }
            }
        }

        public Task<ExpeditionSnapshot?> FindActiveAsync(
            long accountId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return Task.FromResult(
                    _active?.AccountId == accountId ? _active : null);
            }
        }

        public Task<ExpeditionWriteResult> TryStartAsync(
            StartExpeditionCommand command,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_startRequestId == command.RequestId && _active is not null)
                {
                    return Result(ExpeditionWriteOutcome.AlreadyApplied, snapshot: _active);
                }

                if (_active is not null)
                {
                    return Result(ExpeditionWriteOutcome.Conflict);
                }

                _startRequestId = command.RequestId;
                _active = new ExpeditionSnapshot(
                    command.ExpeditionId,
                    command.AccountId,
                    command.EntryMapId,
                    command.StartedAt,
                    ExpeditionStatus.Active,
                    Array.Empty<ExpeditionAsset>(),
                    0);
                return Result(ExpeditionWriteOutcome.Applied, snapshot: _active);
            }
        }

        public Task<ExpeditionWriteResult> TryAppendDropsAsync(
            AppendExpeditionDropsCommand command,
            CancellationToken cancellationToken) =>
            Result(ExpeditionWriteOutcome.Conflict);

        public Task<ExpeditionWriteResult> TryRecordDeathAsync(
            RecordExpeditionDeathCommand command,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_active is null ||
                    _active.AccountId != command.AccountId ||
                    _active.ExpeditionId != command.ExpeditionId)
                {
                    return Result(ExpeditionWriteOutcome.NotFound);
                }

                var death = new ExpeditionDeathResult(
                    command.ExpeditionId,
                    command.OccurredAt,
                    _active.TemporaryAssets,
                    _active.DeathCount + 1);
                _active = _active with
                {
                    TemporaryAssets = Array.Empty<ExpeditionAsset>(),
                    DeathCount = death.DeathCount
                };
                return Result(
                    ExpeditionWriteOutcome.Applied,
                    snapshot: _active,
                    death: death);
            }
        }

        public Task<ExpeditionWriteResult> TrySettleAsync(
            SettleExpeditionCommand command,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_settlement?.ExpeditionId == command.ExpeditionId &&
                    _settlement.AccountId == command.AccountId)
                {
                    return Result(
                        ExpeditionWriteOutcome.AlreadyApplied,
                        settlement: _settlement);
                }

                if (_active is null ||
                    _active.AccountId != command.AccountId ||
                    _active.ExpeditionId != command.ExpeditionId)
                {
                    return Result(ExpeditionWriteOutcome.NotFound);
                }

                _settlement = new ExpeditionSettlementSummary(
                    _active.ExpeditionId,
                    _active.AccountId,
                    command.RequestId,
                    command.Reason,
                    command.SettledAt,
                    _active.TemporaryAssets,
                    _active.DeathCount);
                _active = null;
                return Result(
                    ExpeditionWriteOutcome.Applied,
                    settlement: _settlement);
            }
        }

        private static Task<ExpeditionWriteResult> Result(
            ExpeditionWriteOutcome outcome,
            ExpeditionSnapshot? snapshot = null,
            ExpeditionDeathResult? death = null,
            ExpeditionSettlementSummary? settlement = null) =>
            Task.FromResult(new ExpeditionWriteResult(outcome, snapshot, death, settlement));
    }

    private sealed class MemoryAccountRepository : IAccountRepository
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

    private sealed class PlainTextHasher : IPasswordHasher
    {
        public ValueTask<PasswordHashResult> HashAsync(
            string password,
            CancellationToken cancellationToken) =>
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
            ValueTask.FromResult(
                expectedHash.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(password)));
    }

    private sealed class EmptyLobbyAccountRepository : ILobbyAccountRepository
    {
        public Task<LobbyAccountProgressionRecord?> FindProgressionAsync(
            long accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult<LobbyAccountProgressionRecord?>(null);
    }
}
