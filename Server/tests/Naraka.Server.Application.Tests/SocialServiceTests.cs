using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Sessions;
using Naraka.Server.Application.Social;

namespace Naraka.Server.Application.Tests;

/// <summary>
/// 好友与一对一私聊。
///
/// 这里的测试全部围绕三条不变量：账号只能来自已认证会话；任一方拉黑就断绝一切往来；
/// 好友关系永远成对存在，不会出现单向好友。
/// </summary>
public sealed class SocialServiceTests
{
    private const long Alice = 1;
    private const long Bob = 2;
    private const long Carol = 3;

    private sealed class Fixture
    {
        public FakeSocialRepository Social { get; } = new();

        public FakeChatRepository Chat { get; } = new();

        public InMemoryAuthenticatedSessionRegistry Sessions { get; } = new();

        public FakeTimeProvider Time { get; } = new(new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));

        public SocialService Service => new(Social, Chat, Sessions, Time);

        public Fixture WithPlayers(params long[] accountIds)
        {
            foreach (var accountId in accountIds)
            {
                Social.Players[accountId] = new SocialPlayer(
                    accountId, "player" + accountId, "avatar_default");
            }

            return this;
        }

        public Fixture WithFriends(long first, long second)
        {
            Social.Friends.Add((first, second));
            Social.Friends.Add((second, first));
            return this;
        }

        public void GoOnline(long accountId) =>
            Sessions.Bind(new ConnectionId(Guid.NewGuid()), accountId);
    }

    [Fact]
    public async Task AnUnauthenticatedAccountIsRejectedEverywhere()
    {
        var service = new Fixture().Service;

        Assert.Equal(LobbyOperationStatus.InvalidRequest,
            (await service.GetAsync(0, CancellationToken.None)).Status);
        Assert.Equal(LobbyOperationStatus.InvalidRequest,
            (await service.SendRequestAsync(0, Bob, CancellationToken.None)).Status);
        Assert.Equal(LobbyOperationStatus.InvalidRequest,
            (await service.SendMessageAsync(0, Bob, "hi", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task SearchingByExactNameFindsThePlayer()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);

        var result = await fixture.Service.SearchAsync(Alice, "player2", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Equal(Bob, result.SearchResult?.AccountId);
    }

    [Fact]
    public async Task SearchingForYourselfFindsNothing()
    {
        var fixture = new Fixture().WithPlayers(Alice);

        var result = await fixture.Service.SearchAsync(Alice, "player1", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Null(result.SearchResult);
    }

    [Fact]
    public async Task SearchingForABlockedPlayerFindsNothing()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);
        fixture.Social.Blocks.Add((Alice, Bob));

        var result = await fixture.Service.SearchAsync(Alice, "player2", CancellationToken.None);

        Assert.Null(result.SearchResult);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AnEmptySearchIsRejected(string? name)
    {
        var fixture = new Fixture().WithPlayers(Alice);

        var result = await fixture.Service.SearchAsync(Alice, name, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task ASearchNameLongerThanTheColumnIsRejectedRatherThanTruncated()
    {
        var fixture = new Fixture().WithPlayers(Alice);
        var tooLong = new string('a', SocialService.MaxDisplayNameLength + 1);

        var result = await fixture.Service.SearchAsync(Alice, tooLong, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task AFriendRequestBecomesAPendingRequestOnTheOtherSide()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);

        var result = await fixture.Service.SendRequestAsync(Alice, Bob, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Contains((Alice, Bob), fixture.Social.Requests);
    }

    [Fact]
    public async Task YouCannotFriendYourself()
    {
        var fixture = new Fixture().WithPlayers(Alice);

        var result = await fixture.Service.SendRequestAsync(Alice, Alice, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task AFriendRequestToAnUnknownAccountIsNotFound()
    {
        var fixture = new Fixture().WithPlayers(Alice);

        var result = await fixture.Service.SendRequestAsync(Alice, 999, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task SendingTheSameRequestTwiceIsRejected()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);
        await fixture.Service.SendRequestAsync(Alice, Bob, CancellationToken.None);

        var second = await fixture.Service.SendRequestAsync(Alice, Bob, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.AlreadyClaimed, second.Status);
        Assert.Single(fixture.Social.Requests);
    }

    [Fact]
    public async Task RequestingSomeoneWhoAlreadyRequestedYouMakesYouFriendsImmediately()
    {
        // 否则两条互相等待的申请会一直挂着，双方都以为对方没有回应。
        var fixture = new Fixture().WithPlayers(Alice, Bob);
        await fixture.Service.SendRequestAsync(Bob, Alice, CancellationToken.None);

        var result = await fixture.Service.SendRequestAsync(Alice, Bob, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Empty(fixture.Social.Requests);
        Assert.Contains((Alice, Bob), fixture.Social.Friends);
        Assert.Contains((Bob, Alice), fixture.Social.Friends);
    }

    [Fact]
    public async Task ARequestToAnAlreadyFriendIsRejected()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);

        var result = await fixture.Service.SendRequestAsync(Alice, Bob, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.AlreadyClaimed, result.Status);
    }

    [Fact]
    public async Task ARequestToSomeoneWhoBlockedYouIsRejected()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);
        fixture.Social.Blocks.Add((Bob, Alice));

        var result = await fixture.Service.SendRequestAsync(Alice, Bob, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotAvailable, result.Status);
        Assert.Empty(fixture.Social.Requests);
    }

    [Fact]
    public async Task AcceptingARequestCreatesTheFriendshipOnBothSides()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);
        await fixture.Service.SendRequestAsync(Alice, Bob, CancellationToken.None);

        var result = await fixture.Service.AcceptRequestAsync(Bob, Alice, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Contains((Alice, Bob), fixture.Social.Friends);
        Assert.Contains((Bob, Alice), fixture.Social.Friends);
        Assert.Empty(fixture.Social.Requests);
    }

    [Fact]
    public async Task AcceptingARequestThatDoesNotExistIsNotFound()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);

        var result = await fixture.Service.AcceptRequestAsync(Bob, Alice, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task AcceptingTheSameRequestTwiceFailsTheSecondTime()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);
        await fixture.Service.SendRequestAsync(Alice, Bob, CancellationToken.None);
        await fixture.Service.AcceptRequestAsync(Bob, Alice, CancellationToken.None);

        var second = await fixture.Service.AcceptRequestAsync(Bob, Alice, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotFound, second.Status);
        Assert.Equal(2, fixture.Social.Friends.Count);
    }

    [Fact]
    public async Task RejectingARequestRemovesItWithoutCreatingAFriendship()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);
        await fixture.Service.SendRequestAsync(Alice, Bob, CancellationToken.None);

        var result = await fixture.Service.RejectRequestAsync(Bob, Alice, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Empty(fixture.Social.Requests);
        Assert.Empty(fixture.Social.Friends);
    }

    [Fact]
    public async Task RemovingAFriendRemovesBothDirections()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);

        var result = await fixture.Service.RemoveFriendAsync(Alice, Bob, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Empty(fixture.Social.Friends);
    }

    [Fact]
    public async Task BlockingAlsoDropsTheFriendshipAndAnyPendingRequests()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);
        fixture.Social.Requests.Add((Bob, Alice));

        var result = await fixture.Service.BlockAsync(Alice, Bob, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Empty(fixture.Social.Friends);
        Assert.Empty(fixture.Social.Requests);
        Assert.Contains((Alice, Bob), fixture.Social.Blocks);
    }

    [Fact]
    public async Task UnblockingRemovesTheBlockButNotTheFriendship()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);
        await fixture.Service.BlockAsync(Alice, Bob, CancellationToken.None);

        var result = await fixture.Service.UnblockAsync(Alice, Bob, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        Assert.Empty(fixture.Social.Blocks);
        Assert.Empty(fixture.Social.Friends);
    }

    [Fact]
    public async Task OnlineStatusComesFromLiveConnections()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob, Carol)
            .WithFriends(Alice, Bob)
            .WithFriends(Alice, Carol);
        fixture.GoOnline(Bob);

        var result = await fixture.Service.GetAsync(Alice, CancellationToken.None);

        var friends = result.View!.Friends.ToDictionary(entry => entry.Player.AccountId);
        Assert.True(friends[Bob].IsOnline);
        Assert.False(friends[Carol].IsOnline);
    }

    [Fact]
    public async Task OnlyFriendsCanOpenAConversation()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);

        var result = await fixture.Service.OpenConversationAsync(Alice, Bob, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotAvailable, result.Status);
    }

    [Fact]
    public async Task FriendsCanOpenAConversation()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);

        var result = await fixture.Service.OpenConversationAsync(Alice, Bob, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
    }

    [Fact]
    public async Task OnlyFriendsCanSendMessages()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);

        var result = await fixture.Service.SendMessageAsync(Alice, Bob, "hello", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotAvailable, result.Status);
        Assert.Empty(fixture.Chat.Messages);
    }

    [Fact]
    public async Task ABlockedPlayerCannotBeMessaged()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);
        fixture.Social.Blocks.Add((Bob, Alice));

        var result = await fixture.Service.SendMessageAsync(Alice, Bob, "hello", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotAvailable, result.Status);
        Assert.Empty(fixture.Chat.Messages);
    }

    [Fact]
    public async Task AMessageIsStoredAndReturned()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);

        var result = await fixture.Service.SendMessageAsync(Alice, Bob, "hello", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, result.Status);
        var message = Assert.Single(result.Messages);
        Assert.Equal("hello", message.Body);
        Assert.Equal(Alice, message.SenderAccountId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AnEmptyMessageIsRejected(string? body)
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);

        var result = await fixture.Service.SendMessageAsync(Alice, Bob, body, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task AMessageLongerThanTheColumnIsRejectedRatherThanTruncated()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);
        var tooLong = new string('a', SocialService.MaxMessageLength + 1);

        var result = await fixture.Service.SendMessageAsync(Alice, Bob, tooLong, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(fixture.Chat.Messages);
    }

    [Fact]
    public async Task ControlCharactersAreStrippedSoTheStoredTextMatchesWhatIsShown()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);

        var result = await fixture.Service.SendMessageAsync(
            Alice, Bob, "he\r\nllo\t", CancellationToken.None);

        Assert.Equal("hello", Assert.Single(result.Messages).Body);
    }

    [Fact]
    public async Task AMessageOfOnlyControlCharactersIsRejected()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);

        var result = await fixture.Service.SendMessageAsync(Alice, Bob, "\r\n\t", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.InvalidRequest, result.Status);
    }

    [Fact]
    public async Task SendingTooManyMessagesTooFastIsRateLimited()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);
        for (var i = 0; i < SocialService.RateLimitMessages; i++)
        {
            var sent = await fixture.Service.SendMessageAsync(
                Alice, Bob, "message " + i, CancellationToken.None);
            Assert.Equal(LobbyOperationStatus.Success, sent.Status);
        }

        var limited = await fixture.Service.SendMessageAsync(
            Alice, Bob, "one too many", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.RateLimited, limited.Status);
    }

    [Fact]
    public async Task TheRateLimitReleasesOnceTheWindowPasses()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);
        for (var i = 0; i < SocialService.RateLimitMessages; i++)
        {
            await fixture.Service.SendMessageAsync(Alice, Bob, "message " + i, CancellationToken.None);
        }

        fixture.Time.Advance(SocialService.RateLimitWindow + TimeSpan.FromSeconds(1));
        var afterWindow = await fixture.Service.SendMessageAsync(
            Alice, Bob, "later", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, afterWindow.Status);
    }

    [Fact]
    public async Task ARejectedMessageDoesNotConsumeRateLimitBudget()
    {
        // 限流放在权限校验之后，因此一串被拒绝的请求不会把正常发言的额度吃掉。
        var fixture = new Fixture().WithPlayers(Alice, Bob);
        for (var i = 0; i < SocialService.RateLimitMessages * 2; i++)
        {
            await fixture.Service.SendMessageAsync(Alice, Bob, "blocked", CancellationToken.None);
        }

        fixture.Social.Friends.Add((Alice, Bob));
        fixture.Social.Friends.Add((Bob, Alice));
        var allowed = await fixture.Service.SendMessageAsync(Alice, Bob, "now ok", CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.Success, allowed.Status);
    }

    [Fact]
    public async Task MarkingAConversationYouAreNotInIsRefused()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob, Carol).WithFriends(Bob, Carol);
        var conversationId = await fixture.Chat.EnsureConversationAsync(
            Bob, Carol, fixture.Time.GetUtcNow().UtcDateTime, CancellationToken.None);

        var result = await fixture.Service.MarkReadAsync(
            Alice, conversationId, 1, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotAvailable, result.Status);
    }

    [Fact]
    public async Task MarkingAnUnknownConversationIsNotFound()
    {
        var fixture = new Fixture().WithPlayers(Alice);

        var result = await fixture.Service.MarkReadAsync(Alice, 999, 1, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task ReadingYourOwnSideDoesNotClearThePeerUnread()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob).WithFriends(Alice, Bob);
        await fixture.Service.SendMessageAsync(Bob, Alice, "hello", CancellationToken.None);

        await fixture.Service.OpenConversationAsync(Alice, Bob, CancellationToken.None);

        var conversationId = fixture.Chat.Conversations.Keys.Single();
        Assert.Equal(1, fixture.Chat.ReadPositions[(conversationId, Alice)]);
        Assert.Equal(1, fixture.Chat.ReadPositions[(conversationId, Bob)]);
    }

    [Fact]
    public async Task StorageFaultsSurfaceAsDatabaseUnavailableRatherThanCrashing()
    {
        var fixture = new Fixture().WithPlayers(Alice, Bob);
        fixture.Social.ThrowOnRead = true;

        var result = await fixture.Service.GetAsync(Alice, CancellationToken.None);

        Assert.Equal(LobbyOperationStatus.DatabaseUnavailable, result.Status);
    }

    private sealed class FakeTimeProvider(DateTime startUtc) : TimeProvider
    {
        private DateTimeOffset _now = new(startUtc, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private sealed class FakeSocialRepository : ISocialRepository
    {
        public Dictionary<long, SocialPlayer> Players { get; } = new();

        public HashSet<(long, long)> Friends { get; } = new();

        public HashSet<(long, long)> Requests { get; } = new();

        public HashSet<(long, long)> Blocks { get; } = new();

        public bool ThrowOnRead { get; set; }

        public Task<SocialPlayer?> FindByAccountIdAsync(
            long accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Players.TryGetValue(accountId, out var player) ? player : null);

        public Task<SocialPlayer?> FindByDisplayNameAsync(
            string displayName,
            CancellationToken cancellationToken) =>
            Task.FromResult(Players.Values
                .FirstOrDefault(player => string.Equals(
                    player.DisplayName, displayName, StringComparison.Ordinal)));

        public Task<IReadOnlyList<SocialPlayer>> ListFriendsAsync(
            long accountId,
            CancellationToken cancellationToken)
        {
            if (ThrowOnRead)
            {
                throw new ProgressionStorageException("boom");
            }

            return Task.FromResult<IReadOnlyList<SocialPlayer>>(Friends
                .Where(pair => pair.Item1 == accountId)
                .Select(pair => Players[pair.Item2])
                .ToArray());
        }

        public Task<IReadOnlyList<FriendRequestEntry>> ListIncomingRequestsAsync(
            long accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FriendRequestEntry>>(Requests
                .Where(pair => pair.Item2 == accountId)
                .Select(pair => new FriendRequestEntry(Players[pair.Item1], DateTime.UnixEpoch))
                .ToArray());

        public Task<IReadOnlyList<long>> ListOutgoingRequestTargetsAsync(
            long accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<long>>(Requests
                .Where(pair => pair.Item1 == accountId)
                .Select(pair => pair.Item2)
                .ToArray());

        public Task<IReadOnlyList<SocialPlayer>> ListBlockedAsync(
            long accountId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SocialPlayer>>(Blocks
                .Where(pair => pair.Item1 == accountId)
                .Select(pair => Players[pair.Item2])
                .ToArray());

        public Task<bool> IsFriendAsync(
            long accountId,
            long otherAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Friends.Contains((accountId, otherAccountId)));

        public Task<bool> IsBlockedEitherWayAsync(
            long accountId,
            long otherAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Blocks.Contains((accountId, otherAccountId)) ||
                Blocks.Contains((otherAccountId, accountId)));

        public Task<FriendRequestOutcome> TrySendRequestAsync(
            long requesterAccountId,
            long targetAccountId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            if (Blocks.Contains((requesterAccountId, targetAccountId)) ||
                Blocks.Contains((targetAccountId, requesterAccountId)))
            {
                return Task.FromResult(FriendRequestOutcome.Blocked);
            }

            if (Friends.Contains((requesterAccountId, targetAccountId)))
            {
                return Task.FromResult(FriendRequestOutcome.AlreadyFriends);
            }

            if (Requests.Remove((targetAccountId, requesterAccountId)))
            {
                Friends.Add((requesterAccountId, targetAccountId));
                Friends.Add((targetAccountId, requesterAccountId));
                return Task.FromResult(FriendRequestOutcome.AutoAccepted);
            }

            return Task.FromResult(Requests.Add((requesterAccountId, targetAccountId))
                ? FriendRequestOutcome.Applied
                : FriendRequestOutcome.AlreadyRequested);
        }

        public Task<bool> TryAcceptRequestAsync(
            long targetAccountId,
            long requesterAccountId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            if (!Requests.Remove((requesterAccountId, targetAccountId)))
            {
                return Task.FromResult(false);
            }

            Friends.Add((requesterAccountId, targetAccountId));
            Friends.Add((targetAccountId, requesterAccountId));
            return Task.FromResult(true);
        }

        public Task<bool> TryRejectRequestAsync(
            long targetAccountId,
            long requesterAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Requests.Remove((requesterAccountId, targetAccountId)));

        public Task<bool> TryRemoveFriendAsync(
            long accountId,
            long friendAccountId,
            CancellationToken cancellationToken)
        {
            var first = Friends.Remove((accountId, friendAccountId));
            var second = Friends.Remove((friendAccountId, accountId));
            return Task.FromResult(first || second);
        }

        public Task TryBlockAsync(
            long accountId,
            long blockedAccountId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            Blocks.Add((accountId, blockedAccountId));
            Friends.Remove((accountId, blockedAccountId));
            Friends.Remove((blockedAccountId, accountId));
            Requests.Remove((accountId, blockedAccountId));
            Requests.Remove((blockedAccountId, accountId));
            return Task.CompletedTask;
        }

        public Task<bool> TryUnblockAsync(
            long accountId,
            long blockedAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Blocks.Remove((accountId, blockedAccountId)));
    }

    private sealed class FakeChatRepository : IChatRepository
    {
        private long _nextConversationId = 1;
        private long _nextMessageId = 1;

        public Dictionary<long, (long Low, long High)> Conversations { get; } = new();

        public List<ChatMessage> Messages { get; } = new();

        public Dictionary<(long Conversation, long Account), long> ReadPositions { get; } = new();

        public Task<long> EnsureConversationAsync(
            long accountId,
            long peerAccountId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var low = Math.Min(accountId, peerAccountId);
            var high = Math.Max(accountId, peerAccountId);
            foreach (var pair in Conversations)
            {
                if (pair.Value == (low, high))
                {
                    return Task.FromResult(pair.Key);
                }
            }

            var id = _nextConversationId++;
            Conversations[id] = (low, high);
            return Task.FromResult(id);
        }

        public Task<IReadOnlyList<ChatConversation>> ListConversationsAsync(
            long accountId,
            CancellationToken cancellationToken)
        {
            var rows = new List<ChatConversation>();
            foreach (var pair in Conversations)
            {
                if (pair.Value.Low != accountId && pair.Value.High != accountId)
                {
                    continue;
                }

                var peer = pair.Value.Low == accountId ? pair.Value.High : pair.Value.Low;
                var last = Messages
                    .Where(message => message.ConversationId == pair.Key)
                    .Select(message => message.MessageId)
                    .DefaultIfEmpty(0)
                    .Max();

                rows.Add(new ChatConversation(
                    pair.Key,
                    new SocialPlayer(peer, "player" + peer, string.Empty),
                    false,
                    last,
                    ReadPositions.TryGetValue((pair.Key, accountId), out var read) ? read : 0,
                    null));
            }

            return Task.FromResult<IReadOnlyList<ChatConversation>>(rows);
        }

        public Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(
            long conversationId,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(Messages
                .Where(message => message.ConversationId == conversationId)
                .TakeLast(limit)
                .ToArray());

        public Task<ChatMessage> AppendMessageAsync(
            long conversationId,
            long senderAccountId,
            string body,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var message = new ChatMessage(
                _nextMessageId++, conversationId, senderAccountId, body, nowUtc);
            Messages.Add(message);
            return Task.FromResult(message);
        }

        public Task MarkReadAsync(
            long conversationId,
            long accountId,
            long lastReadMessageId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var key = (conversationId, accountId);
            if (!ReadPositions.TryGetValue(key, out var existing) || existing < lastReadMessageId)
            {
                ReadPositions[key] = lastReadMessageId;
            }

            return Task.CompletedTask;
        }

        public Task<(long Low, long High)?> FindParticipantsAsync(
            long conversationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Conversations.TryGetValue(conversationId, out var pair)
                ? pair
                : ((long, long)?)null);

        public Task<int> CountRecentMessagesAsync(
            long accountId,
            DateTime sinceUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult(Messages.Count(message =>
                message.SenderAccountId == accountId && message.SentUtc > sinceUtc));
    }
}
