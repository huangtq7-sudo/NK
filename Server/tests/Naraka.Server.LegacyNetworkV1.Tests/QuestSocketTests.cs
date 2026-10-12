using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Naraka.Server.Application.Accounts;
using Naraka.Server.Application.Lobby;
using Naraka.Server.Application.Quests;
using Naraka.Server.Application.Sessions;
using Naraka.Server.LegacyNetworkV1.Authentication;
using Naraka.Server.LegacyNetworkV1.Messages;
using Naraka.Server.LegacyNetworkV1.Protocol;
using Naraka.Server.Tests.Shared;

namespace Naraka.Server.LegacyNetworkV1.Tests;

/// <summary>
/// P4.1 任务消息 67/68 的契约与真实加密 Socket 端到端测试。
/// 存储用内存实现，配置用仓库中真实生成的那一份；不修改冻结的帧、AES 与握手。
/// </summary>
public sealed class QuestProtocolTests
{
    [Fact]
    public void QuestPairAppendsAfterTheExpeditionPair()
    {
        Assert.Equal(67, (int)LegacyProtocolValue.MsgQuestRequest);
        Assert.Equal(68, (int)LegacyProtocolValue.MsgQuestResponse);
        Assert.Equal(67, ApplicationProtocolCatalog.Inbound[ApplicationProtocolCatalog.QuestRequest]);
        Assert.Equal(68, ApplicationProtocolCatalog.Outbound[ApplicationProtocolCatalog.QuestResponse]);
        Assert.False(LegacyProtocolCatalog.Client.ContainsKey(ApplicationProtocolCatalog.QuestRequest));
        Assert.False(LegacyProtocolCatalog.Server.ContainsKey(ApplicationProtocolCatalog.QuestResponse));
    }

    [Fact]
    public void QuestRequestCannotCarryAccountRewardOrExperienceClaims()
    {
        var properties = typeof(LegacyMsgQuestRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            new[] { "ProtocolType", "RequestId", "Operation", "QuestId", "ObjectiveId", "DialogueId" },
            properties);
    }

    [Fact]
    public void QuestRequestAndResponseSurviveSeparateCodecRoundTrips()
    {
        var request = new LegacyMsgQuestRequest
        {
            RequestId = "quest-talk-001",
            Operation = LegacyQuestOperation.CompleteDialogue,
            QuestId = "main_c1_01",
            ObjectiveId = "main_c1_01_o1",
            DialogueId = "dlg_c1_01_yunsheng"
        };
        var decodedRequest = Assert.IsType<LegacyMsgQuestRequest>(LegacyProtobufCodec.DeserializeIncoming(
            ApplicationProtocolCatalog.QuestRequest, LegacyProtobufCodec.Serialize(request)));
        Assert.Equal(LegacyQuestOperation.CompleteDialogue, decodedRequest.Operation);
        Assert.Equal("main_c1_01_o1", decodedRequest.ObjectiveId);
        Assert.Equal("dlg_c1_01_yunsheng", decodedRequest.DialogueId);

        var response = new LegacyMsgQuestResponse
        {
            RequestId = request.RequestId,
            Operation = request.Operation,
            Status = LegacyLobbyOperationStatus.Success,
            Journal = new LegacyQuestJournal { AccountXp = 100, AccountLevel = 2 },
            Claim = new LegacyQuestClaim { QuestId = "main_c1_01", AccountXpAfter = 100 }
        };
        var entry = new LegacyQuestEntry { QuestId = "main_c1_01", Status = LegacyQuestStatus.ReadyToSubmit };
        entry.Objectives.Add(new LegacyQuestObjective
        {
            ObjectiveId = "main_c1_01_o1", Progress = 1, TargetCount = 1, IsCompleted = true
        });
        response.Journal.Quests.Add(entry);
        response.Claim.Rewards.Add(new LegacyQuestReward { Kind = "Currency", RewardId = "Copper", Amount = 300 });

        var decoded = Assert.IsType<LegacyMsgQuestResponse>(LegacyProtobufCodec.DeserializeOutgoing(
            ApplicationProtocolCatalog.QuestResponse, LegacyProtobufCodec.Serialize(response)));
        Assert.Equal(LegacyQuestStatus.ReadyToSubmit, Assert.Single(decoded.Journal!.Quests).Status);
        Assert.True(Assert.Single(decoded.Journal.Quests[0].Objectives).IsCompleted);
        Assert.Equal(300, Assert.Single(decoded.Claim!.Rewards).Amount);
        Assert.Equal(2, decoded.Journal.AccountLevel);
    }

    [Fact]
    public void QuestResponseIsNeverAcceptedAsAnInboundCommand()
    {
        Assert.Throws<InvalidDataException>(() => LegacyProtobufCodec.DeserializeIncoming(
            ApplicationProtocolCatalog.QuestResponse,
            LegacyProtobufCodec.Serialize(new LegacyMsgQuestResponse())));
    }
}

public sealed class QuestSocketTests
{
    private const string Quest = "main_c1_01";

    private sealed record Fixture(
        LegacyNetworkTransport Transport,
        MemoryQuestRepository Quests,
        ManualTimeProvider Time,
        CancellationTokenSource Cancellation,
        Task Server);

    [Fact]
    public async Task AuthenticatedSocketCompletesTheMainQuestLoopAndRestoresItAfterRelogin()
    {
        var fixture = StartServer();
        await WaitUntilAsync(() => fixture.Transport.IsIntegrated);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, fixture.Transport.ListeningPort);
            var stream = client.GetStream();
            var key = await HandshakeAsync(stream);
            await RegisterAndLoginAsync(stream, key, register: true);

            var journal = await QuestAsync(stream, key, "journal-1", LegacyQuestOperation.GetJournal);
            Assert.Equal(LegacyLobbyOperationStatus.Success, journal.Status);
            Assert.Contains(Quest, journal.Journal!.AvailableQuestIds);

            fixture.Time.Advance(TimeSpan.FromSeconds(5));
            var accepted = await QuestAsync(
                stream, key, "accept-1", LegacyQuestOperation.Accept, Quest, dialogueId: "dlg_c1_01_offer");
            Assert.Equal(LegacyLobbyOperationStatus.Success, accepted.Status);
            Assert.Equal(LegacyQuestStatus.Accepted, Assert.Single(accepted.Journal!.Quests).Status);

            fixture.Time.Advance(TimeSpan.FromSeconds(5));
            var talked = await QuestAsync(
                stream, key, "talk-1", LegacyQuestOperation.CompleteDialogue, Quest,
                objectiveId: "main_c1_01_o1", dialogueId: "dlg_c1_01_yunsheng");
            Assert.Equal(LegacyQuestStatus.ReadyToSubmit, Assert.Single(talked.Journal!.Quests).Status);

            fixture.Time.Advance(TimeSpan.FromSeconds(5));
            var submitted = await QuestAsync(
                stream, key, "submit-1", LegacyQuestOperation.Submit, Quest, dialogueId: "dlg_c1_01_submit");
            Assert.Equal(LegacyQuestStatus.RewardPending, Assert.Single(submitted.Journal!.Quests).Status);

            fixture.Time.Advance(TimeSpan.FromSeconds(5));
            var claimed = await QuestAsync(stream, key, "claim-1", LegacyQuestOperation.Claim, Quest);
            Assert.Equal(LegacyLobbyOperationStatus.Success, claimed.Status);
            Assert.Equal(100, claimed.Claim!.AccountXpAfter);
            Assert.Equal(2, claimed.Journal!.AccountLevel);

            fixture.Time.Advance(TimeSpan.FromSeconds(5));
            var replay = await QuestAsync(stream, key, "claim-1", LegacyQuestOperation.Claim, Quest);
            Assert.True(replay.IsReplay);
            fixture.Time.Advance(TimeSpan.FromSeconds(5));
            var duplicate = await QuestAsync(stream, key, "claim-2", LegacyQuestOperation.Claim, Quest);
            Assert.Equal(LegacyLobbyOperationStatus.AlreadyClaimed, duplicate.Status);
            Assert.Single(fixture.Quests.Grants);
        }
        finally
        {
            client.Close();
        }

        using var relogin = new TcpClient();
        try
        {
            await relogin.ConnectAsync(IPAddress.Loopback, fixture.Transport.ListeningPort);
            var stream = relogin.GetStream();
            var key = await HandshakeAsync(stream);
            await RegisterAndLoginAsync(stream, key, register: false);
            fixture.Time.Advance(TimeSpan.FromSeconds(5));

            var restored = await QuestAsync(stream, key, "journal-2", LegacyQuestOperation.GetJournal);
            Assert.Equal(LegacyQuestStatus.Claimed, Assert.Single(restored.Journal!.Quests).Status);
            Assert.Equal(100, restored.Journal.AccountXp);
        }
        finally
        {
            await StopClientThenServerAsync(relogin, fixture);
        }
    }

    [Fact]
    public async Task QuestCommandRequiresAuthenticationAndARequestId()
    {
        var fixture = StartServer();
        await WaitUntilAsync(() => fixture.Transport.IsIntegrated);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, fixture.Transport.ListeningPort);
            var stream = client.GetStream();
            var key = await HandshakeAsync(stream);

            var unauthenticated = await QuestAsync(stream, key, "journal-1", LegacyQuestOperation.GetJournal);
            Assert.Equal(LegacyLobbyOperationStatus.Unauthenticated, unauthenticated.Status);

            await RegisterAndLoginAsync(stream, key, register: true);
            var missingId = await QuestAsync(stream, key, string.Empty, LegacyQuestOperation.GetJournal);
            Assert.Equal(LegacyLobbyOperationStatus.InvalidRequest, missingId.Status);

            var unknownOperation = await QuestAsync(stream, key, "none-1", LegacyQuestOperation.None);
            Assert.Equal(LegacyLobbyOperationStatus.InvalidRequest, unknownOperation.Status);
            Assert.Equal(0, fixture.Quests.Writes);
        }
        finally
        {
            await StopClientThenServerAsync(client, fixture);
        }
    }

    private static Fixture StartServer()
    {
        var accounts = new AccountService(new MemoryAccountRepository(), new PlainTextHasher());
        var sessions = new InMemoryAuthenticatedSessionRegistry();
        var profiles = new MemoryAccountProfileRepository();
        var quests = new MemoryQuestRepository { MaximumAccountXp = TestAccountProfiles.Config.MaximumAccountXp };
        quests.AddAccount(1);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero));
        var questService = new QuestService(quests, new FixedExpeditionProbe(), TestAccountProfiles.Config, time);
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
            quests: questService);

        var cancellation = new CancellationTokenSource();
        var server = transport.RunAsync(IPAddress.Loopback, 0, cancellation.Token);
        return new Fixture(transport, quests, time, cancellation, server);
    }

    private static async Task<LegacyMsgQuestResponse> QuestAsync(
        NetworkStream stream,
        string key,
        string requestId,
        LegacyQuestOperation operation,
        string? questId = null,
        string? objectiveId = null,
        string? dialogueId = null) =>
        Assert.IsType<LegacyMsgQuestResponse>(await RoundTripAsync(
            stream,
            new LegacyMsgQuestRequest
            {
                RequestId = requestId,
                Operation = operation,
                QuestId = questId,
                ObjectiveId = objectiveId,
                DialogueId = dialogueId
            },
            key));

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

    private static async Task RegisterAndLoginAsync(NetworkStream stream, string key, bool register)
    {
        if (register)
        {
            var registration = Assert.IsType<LegacyMsgRegister>(await RoundTripAsync(
                stream,
                new LegacyMsgRegister { Account = "quest-user", Password = "quest-password" },
                key));
            Assert.Equal(LegacyRegisterResult.Success, registration.Result);
        }

        var login = Assert.IsType<LegacyMsgLogin>(await RoundTripAsync(
            stream,
            new LegacyMsgLogin { Account = "quest-user", Password = "quest-password" },
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
