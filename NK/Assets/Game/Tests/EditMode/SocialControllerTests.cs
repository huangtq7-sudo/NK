using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.RedDot;
using Naraka.Features.RedDot.Controller;
using Naraka.Features.Lobby.Controller;
using Naraka.Features.Lobby.Model;
using Naraka.Features.Social.Controller;
using NUnit.Framework;

namespace Naraka.P0.Tests
{
    internal sealed class FakeSocialGateway : ISocialGateway
    {
        public int LoadCount { get; private set; }

        public List<string> Calls { get; } = new List<string>();

        public SocialResult LoadResult { get; set; }

        public SocialResult ActionResult { get; set; }

        public Exception ThrowOnAction { get; set; }

        public FakeSocialGateway()
        {
            LoadResult = Snapshot();
            ActionResult = Snapshot();
        }

        public static SocialPlayerSnapshot Player(long id, bool online = false) =>
            new SocialPlayerSnapshot(id, "player" + id, "avatar_default", online);

        public static SocialResult Snapshot(
            IReadOnlyList<SocialPlayerSnapshot> friends = null,
            IReadOnlyList<SocialPlayerSnapshot> requests = null,
            IReadOnlyList<SocialPlayerSnapshot> blocked = null,
            IReadOnlyList<ChatConversationSnapshot> conversations = null,
            SocialPlayerSnapshot searchResult = default,
            long conversationId = 0,
            IReadOnlyList<ChatMessageSnapshot> messages = null) =>
            SocialResult.Success(
                friends ?? Array.Empty<SocialPlayerSnapshot>(),
                requests ?? Array.Empty<SocialPlayerSnapshot>(),
                blocked ?? Array.Empty<SocialPlayerSnapshot>(),
                conversations ?? Array.Empty<ChatConversationSnapshot>(),
                searchResult,
                conversationId,
                messages);

        public UniTask<SocialResult> RequestSocialAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return UniTask.FromResult(LoadResult);
        }

        public UniTask<SocialResult> SearchAsync(string displayName, CancellationToken cancellationToken)
        {
            Calls.Add("search:" + displayName);
            return Result();
        }

        public UniTask<SocialResult> ActAsync(
            SocialAction action,
            long targetAccountId,
            CancellationToken cancellationToken)
        {
            Calls.Add(action + ":" + targetAccountId);
            return Result();
        }

        public UniTask<SocialResult> OpenConversationAsync(
            long peerAccountId,
            CancellationToken cancellationToken)
        {
            Calls.Add("open:" + peerAccountId);
            return Result();
        }

        public UniTask<SocialResult> SendMessageAsync(
            long peerAccountId,
            string body,
            CancellationToken cancellationToken)
        {
            Calls.Add("send:" + peerAccountId + ":" + body);
            return Result();
        }

        public UniTask<SocialResult> MarkReadAsync(
            long conversationId,
            long lastReadMessageId,
            CancellationToken cancellationToken)
        {
            Calls.Add("read:" + conversationId + ":" + lastReadMessageId);
            return Result();
        }

        private UniTask<SocialResult> Result() =>
            ThrowOnAction != null
                ? UniTask.FromException<SocialResult>(ThrowOnAction)
                : UniTask.FromResult(ActionResult);
    }

    /// <summary>
    /// 好友与一对一聊天客户端。
    ///
    /// 关注三件事：兼容模式下一个社交协议都不能发；界面显示的永远是服务端返回的整份视图；
    /// 未读与申请翻译成红点来源事件时，每段会话是独立叶子。
    /// </summary>
    public sealed class SocialControllerTests
    {
        private const long Self = 42;
        private const long Bob = 2;
        private const long Carol = 3;

        private static (SocialController Social, FakeSocialGateway Gateway, TestEventBus Bus, LobbyController Lobby)
            Create(IServerCapabilities capabilities = null, SocialResult? load = null)
        {
            LobbyAccountSnapshot.TryCreate(1, 1000, 1000, 1000, out var summary);
            var caps = capabilities ?? LobbyTestCapabilities.Full();
            var lobby = new LobbyController(
                new LobbyModel(),
                new FakeLobbySceneGateway(),
                "Map1",
                new FakeLobbyAccountGateway { Result = LobbyAccountSummaryResult.Success(summary) },
                new FakeLobbyProfileGateway(),
                caps);

            var gateway = new FakeSocialGateway();
            if (load.HasValue)
            {
                gateway.LoadResult = load.Value;
                gateway.ActionResult = load.Value;
            }

            var bus = new TestEventBus();
            var social = new SocialController(gateway, lobby, caps, bus);

            lobby.Enter("player-one", Self);
            lobby.RequestFeature(LobbyFeature.Friends);
            return (social, gateway, bus, lobby);
        }

        [Test]
        public void OpeningTheFriendsEntryLoadsTheSocialView()
        {
            var (social, gateway, _, _) = Create();

            Assert.That(social.Current.IsOpen, Is.True);
            Assert.That(gateway.LoadCount, Is.EqualTo(1));
            Assert.That(social.Current.HasServerState, Is.True);
            Assert.That(social.Current.Tab, Is.EqualTo(SocialTab.Friends));
        }

        [Test]
        public void OpeningTheChatEntryStartsOnTheChatTab()
        {
            var (social, _, _, lobby) = Create();
            lobby.CloseFeature();

            lobby.RequestFeature(LobbyFeature.Chat);

            Assert.That(social.Current.IsOpen, Is.True);
            Assert.That(social.Current.Tab, Is.EqualTo(SocialTab.Chat));
        }

        [Test]
        public void CompatibilityModeNeverSendsASocialRequest()
        {
            var (social, gateway, _, _) = Create(LobbyTestCapabilities.LegacyCloud());

            social.Search("player2");
            social.SendRequest(Bob);
            social.OpenConversation(Bob);

            Assert.That(gateway.LoadCount, Is.Zero);
            Assert.That(gateway.Calls, Is.Empty);
            Assert.That(social.Current.StatusMessage, Does.Contain("服务器功能尚未升级"));
        }

        [Test]
        public void TheSelfAccountIdComesFromTheLobbySession()
        {
            var (social, _, _, _) = Create();

            Assert.That(social.Current.SelfAccountId, Is.EqualTo(Self));
        }

        [Test]
        public void SearchingSendsTheTrimmedName()
        {
            var (social, gateway, _, _) = Create();

            social.Search("  player2  ");

            Assert.That(gateway.Calls, Is.EqualTo(new[] { "search:player2" }));
        }

        [Test]
        public void AnEmptySearchIsRejectedLocallyWithoutSendingAnything()
        {
            var (social, gateway, _, _) = Create();

            social.Search("   ");

            Assert.That(gateway.Calls, Is.Empty);
            Assert.That(social.Current.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void AnOverlongSearchNameIsRejectedLocally()
        {
            var (social, gateway, _, _) = Create();

            social.Search(new string('a', SocialController.MaxDisplayNameLength + 1));

            Assert.That(gateway.Calls, Is.Empty);
            Assert.That(social.Current.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void AFoundPlayerIsKeptSeparatelyFromTheFriendList()
        {
            var (social, gateway, _, _) = Create();
            gateway.ActionResult = FakeSocialGateway.Snapshot(
                searchResult: FakeSocialGateway.Player(Bob));

            social.Search("player2");

            Assert.That(social.Current.HasSearched, Is.True);
            Assert.That(social.Current.SearchResult.AccountId, Is.EqualTo(Bob));
            Assert.That(social.Current.Friends, Is.Empty);
        }

        [Test]
        public void ANotFoundSearchStillCountsAsHavingSearched()
        {
            var (social, _, _, _) = Create();

            social.Search("nobody");

            Assert.That(social.Current.HasSearched, Is.True);
            Assert.That(social.Current.SearchResult.IsValid, Is.False);
        }

        [Test]
        public void EachFriendActionSendsItsOwnAction()
        {
            var (social, gateway, _, _) = Create();

            social.SendRequest(Bob);
            social.AcceptRequest(Bob);
            social.RejectRequest(Bob);
            social.RemoveFriend(Bob);
            social.Block(Bob);
            social.Unblock(Bob);

            Assert.That(gateway.Calls, Is.EqualTo(new[]
            {
                "SendRequest:2", "AcceptRequest:2", "RejectRequest:2",
                "RemoveFriend:2", "Block:2", "Unblock:2"
            }));
        }

        [Test]
        public void AnInvalidAccountIdIsIgnoredWithoutSendingAnything()
        {
            var (social, gateway, _, _) = Create();

            social.SendRequest(0);
            social.AcceptRequest(-1);

            Assert.That(gateway.Calls, Is.Empty);
        }

        [Test]
        public void TheServerViewReplacesTheLocalOneWholesale()
        {
            var (social, gateway, _, _) = Create();
            gateway.ActionResult = FakeSocialGateway.Snapshot(
                friends: new[] { FakeSocialGateway.Player(Bob, online: true) },
                requests: new[] { FakeSocialGateway.Player(Carol) });

            social.AcceptRequest(Bob);

            Assert.That(social.Current.Friends.Count, Is.EqualTo(1));
            Assert.That(social.Current.Friends[0].IsOnline, Is.True);
            Assert.That(social.Current.IncomingRequests.Count, Is.EqualTo(1));
        }

        [Test]
        public void OpeningAConversationCarriesItsMessages()
        {
            var (social, gateway, _, _) = Create();
            gateway.ActionResult = FakeSocialGateway.Snapshot(
                friends: new[] { FakeSocialGateway.Player(Bob) },
                conversations: new[] { new ChatConversationSnapshot(7, FakeSocialGateway.Player(Bob), 3, 3) },
                conversationId: 7,
                messages: new[]
                {
                    new ChatMessageSnapshot(1, 7, Bob, "hello", 0),
                    new ChatMessageSnapshot(2, 7, Self, "hi", 0)
                });

            social.OpenConversation(Bob);

            Assert.That(social.Current.ActiveConversationId, Is.EqualTo(7));
            Assert.That(social.Current.ActivePeerAccountId, Is.EqualTo(Bob));
            Assert.That(social.Current.Messages.Count, Is.EqualTo(2));
        }

        [Test]
        public void SendingAMessageRequiresAnOpenConversation()
        {
            var (social, gateway, _, _) = Create();

            social.SendMessage("hello");

            Assert.That(gateway.Calls, Is.Empty);
        }

        [Test]
        public void SendingAMessageUsesTheActivePeer()
        {
            var (social, gateway, _, _) = Create();
            gateway.ActionResult = FakeSocialGateway.Snapshot(conversationId: 7);
            social.OpenConversation(Bob);
            gateway.Calls.Clear();

            social.SendMessage("  hello  ");

            Assert.That(gateway.Calls, Is.EqualTo(new[] { "send:2:hello" }));
        }

        [Test]
        public void AnEmptyMessageIsNeverSent()
        {
            var (social, gateway, _, _) = Create();
            gateway.ActionResult = FakeSocialGateway.Snapshot(conversationId: 7);
            social.OpenConversation(Bob);
            gateway.Calls.Clear();

            social.SendMessage("   ");

            Assert.That(gateway.Calls, Is.Empty);
        }

        [Test]
        public void AnOverlongMessageIsRejectedLocally()
        {
            var (social, gateway, _, _) = Create();
            gateway.ActionResult = FakeSocialGateway.Snapshot(conversationId: 7);
            social.OpenConversation(Bob);
            gateway.Calls.Clear();

            social.SendMessage(new string('a', SocialController.MaxMessageLength + 1));

            Assert.That(gateway.Calls, Is.Empty);
            Assert.That(social.Current.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void AFailedActionReportsTheReasonAndKeepsTheView()
        {
            var (social, gateway, _, _) = Create(load: FakeSocialGateway.Snapshot(
                friends: new[] { FakeSocialGateway.Player(Bob) }));
            gateway.ActionResult = SocialResult.Failed(LobbyOperationStatus.RateLimited);

            social.SendRequest(Carol);

            Assert.That(social.Current.StatusMessage, Is.Not.Empty);
            Assert.That(social.Current.Friends.Count, Is.EqualTo(1));
        }

        [Test]
        public void ATransportFailureIsReportedRatherThanSilentlySwallowed()
        {
            var (social, gateway, _, _) = Create();
            gateway.ThrowOnAction = new InvalidOperationException("offline");

            social.SendRequest(Bob);

            Assert.That(social.Current.IsBusy, Is.False);
            Assert.That(social.Current.StatusMessage, Is.Not.Empty);
        }

        [Test]
        public void ClosedPanelIgnoresEveryAction()
        {
            var (social, gateway, _, _) = Create();
            social.Close();
            gateway.Calls.Clear();

            social.SendRequest(Bob);
            social.Search("player2");
            social.OpenConversation(Bob);

            Assert.That(gateway.Calls, Is.Empty);
        }

        [Test]
        public void AnIncomingRequestLightsTheFriendRequestRedDot()
        {
            var (_, _, bus, _) = Create(load: FakeSocialGateway.Snapshot(
                requests: new[] { FakeSocialGateway.Player(Carol) }));

            Assert.That(bus.LastStateOf(RedDotPath.SocialFriendRequest), Is.True);
        }

        [Test]
        public void NoRequestsMeansNoFriendRequestRedDot()
        {
            var (_, _, bus, _) = Create();

            Assert.That(bus.LastStateOf(RedDotPath.SocialFriendRequest), Is.False);
        }

        [Test]
        public void EachConversationGetsItsOwnRedDotLeaf()
        {
            var (_, _, bus, _) = Create(load: FakeSocialGateway.Snapshot(
                conversations: new[]
                {
                    new ChatConversationSnapshot(7, FakeSocialGateway.Player(Bob), 5, 3),
                    new ChatConversationSnapshot(8, FakeSocialGateway.Player(Carol), 2, 2)
                }));

            Assert.That(bus.LastStateOf(RedDotPath.UnreadChat("7")), Is.True);
            Assert.That(bus.LastStateOf(RedDotPath.UnreadChat("8")), Is.False);
        }

        [Test]
        public void AConversationThatDisappearsHasItsRedDotNodeRemoved()
        {
            var (social, gateway, bus, _) = Create(load: FakeSocialGateway.Snapshot(
                conversations: new[]
                {
                    new ChatConversationSnapshot(7, FakeSocialGateway.Player(Bob), 5, 3)
                }));

            gateway.ActionResult = FakeSocialGateway.Snapshot();
            social.RemoveFriend(Bob);

            // 会话消失后必须显式移除节点，否则它的红点会永远留在树上。
            var redDot = new RedDotController(bus, new FakeRedDotGateway(), LobbyTestCapabilities.Full());
            Assert.That(redDot.Current.IsActive(RedDotPath.UnreadChat("7")), Is.False);
        }

        [Test]
        public void AggregatedThroughTheRedDotModuleTheSocialEntryLightsUp()
        {
            var bus = new TestEventBus();
            var redDot = new RedDotController(bus, new FakeRedDotGateway(), LobbyTestCapabilities.Full());

            bus.Publish(new RedDotSourceChanged(RedDotPath.UnreadChat("7"), true));

            Assert.That(redDot.Current.HasActiveDescendant(RedDotPath.Social), Is.True);
            Assert.That(redDot.Current.HasActiveDescendant(RedDotPath.SocialUnreadChat), Is.True);
        }

        [Test]
        public void SwitchingTabsDoesNotReload()
        {
            var (social, gateway, _, _) = Create();
            var before = gateway.LoadCount;

            social.SelectTab(SocialTab.Blocked);

            Assert.That(social.Current.Tab, Is.EqualTo(SocialTab.Blocked));
            Assert.That(gateway.LoadCount, Is.EqualTo(before));
        }
    }
}
