using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Naraka.Config;
using Naraka.Core.Application.Bootstrap;
using Naraka.Core.Application.Messaging;
using Naraka.Core.Application.MVC;
using Naraka.Core.Application.Presentation;
using Naraka.Core.Application.RedDot;
using Naraka.Features.Lobby.Controller;

namespace Naraka.Features.Social.Controller
{
    /// <summary>好友动作。数值与服务端 LegacySocialAction 一致。</summary>
    public enum SocialAction
    {
        SendRequest = 1,
        AcceptRequest = 2,
        RejectRequest = 3,
        RemoveFriend = 4,
        Block = 5,
        Unblock = 6
    }

    /// <summary>社交请求的结果。每次都带回完整视图，客户端因此不需要做局部合并。</summary>
    public readonly struct SocialResult
    {
        private SocialResult(
            LobbyOperationStatus status,
            bool hasView,
            IReadOnlyList<SocialPlayerSnapshot> friends,
            IReadOnlyList<SocialPlayerSnapshot> incomingRequests,
            IReadOnlyList<SocialPlayerSnapshot> blocked,
            IReadOnlyList<ChatConversationSnapshot> conversations,
            SocialPlayerSnapshot searchResult,
            long conversationId,
            IReadOnlyList<ChatMessageSnapshot> messages)
        {
            Status = status;
            HasView = hasView;
            Friends = friends ?? Array.Empty<SocialPlayerSnapshot>();
            IncomingRequests = incomingRequests ?? Array.Empty<SocialPlayerSnapshot>();
            Blocked = blocked ?? Array.Empty<SocialPlayerSnapshot>();
            Conversations = conversations ?? Array.Empty<ChatConversationSnapshot>();
            SearchResult = searchResult;
            ConversationId = conversationId;
            Messages = messages ?? Array.Empty<ChatMessageSnapshot>();
        }

        public LobbyOperationStatus Status { get; }

        public bool HasView { get; }

        public IReadOnlyList<SocialPlayerSnapshot> Friends { get; }

        public IReadOnlyList<SocialPlayerSnapshot> IncomingRequests { get; }

        public IReadOnlyList<SocialPlayerSnapshot> Blocked { get; }

        public IReadOnlyList<ChatConversationSnapshot> Conversations { get; }

        public SocialPlayerSnapshot SearchResult { get; }

        public long ConversationId { get; }

        public IReadOnlyList<ChatMessageSnapshot> Messages { get; }

        public bool IsSuccess => Status == LobbyOperationStatus.Success;

        public static SocialResult Success(
            IReadOnlyList<SocialPlayerSnapshot> friends,
            IReadOnlyList<SocialPlayerSnapshot> incomingRequests,
            IReadOnlyList<SocialPlayerSnapshot> blocked,
            IReadOnlyList<ChatConversationSnapshot> conversations,
            SocialPlayerSnapshot searchResult = default,
            long conversationId = 0,
            IReadOnlyList<ChatMessageSnapshot> messages = null) =>
            new SocialResult(
                LobbyOperationStatus.Success, true, friends, incomingRequests, blocked,
                conversations, searchResult, conversationId, messages);

        public static SocialResult Failed(LobbyOperationStatus status) =>
            new SocialResult(status, false, null, null, null, null, default, 0, null);
    }

    /// <summary>社交网关。发起方账号永远来自已认证会话，客户端只能指定对方。</summary>
    public interface ISocialGateway
    {
        UniTask<SocialResult> RequestSocialAsync(CancellationToken cancellationToken);

        UniTask<SocialResult> SearchAsync(string displayName, CancellationToken cancellationToken);

        UniTask<SocialResult> ActAsync(
            SocialAction action,
            long targetAccountId,
            CancellationToken cancellationToken);

        UniTask<SocialResult> OpenConversationAsync(
            long peerAccountId,
            CancellationToken cancellationToken);

        UniTask<SocialResult> SendMessageAsync(
            long peerAccountId,
            string body,
            CancellationToken cancellationToken);

        UniTask<SocialResult> MarkReadAsync(
            long conversationId,
            long lastReadMessageId,
            CancellationToken cancellationToken);
    }

    public interface ISocialController : IReadOnlyState<SocialPresentationState>
    {
        void Close();

        void SelectTab(SocialTab tab);

        void Search(string displayName);

        void SendRequest(long accountId);

        void AcceptRequest(long accountId);

        void RejectRequest(long accountId);

        void RemoveFriend(long accountId);

        void Block(long accountId);

        void Unblock(long accountId);

        void OpenConversation(long peerAccountId);

        void SendMessage(string body);

        UniTask ReloadAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 好友与一对一聊天的编排。
    ///
    /// 客户端只做三件事：显示服务端返回的视图、发送用户意图、把未读与申请翻译成红点来源事件。
    /// 是否能加好友、能不能发消息、有没有超限流，全部由服务端判定；这里连"对方是不是好友"
    /// 都只用来决定按钮的可用状态，不用来放行请求。
    /// </summary>
    public sealed class SocialController : IController, ISocialController, IDisposable
    {
        /// <summary>输入框允许的最大字符数。与服务端 SocialService.MaxMessageLength 一致。</summary>
        public const int MaxMessageLength = 512;

        /// <summary>昵称最大字符数。与服务端一致。</summary>
        public const int MaxDisplayNameLength = 64;

        private readonly ISocialGateway _gateway;
        private readonly ILobbyController _lobby;
        private readonly IServerCapabilities _capabilities;
        private readonly IDomainEventBus _bus;
        private readonly ReactiveState<SocialPresentationState> _state;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly IDisposable _lobbySubscription;
        private readonly HashSet<string> _publishedConversationNodes =
            new HashSet<string>(StringComparer.Ordinal);

        private bool _isLoading;
        private bool _isBusy;
        private bool _wasOpen;

        public SocialController(
            ISocialGateway gateway,
            ILobbyController lobby,
            IServerCapabilities capabilities,
            IDomainEventBus bus)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));

            _state = new ReactiveState<SocialPresentationState>(SocialPresentationState.Initial);
            _lobbySubscription = _lobby.Subscribe(new LobbyStateObserver(this));
            ApplyLobbyState(_lobby.Current);
        }

        public SocialPresentationState Current => _state.Current;

        public void Close() => _lobby.CloseFeature();

        public void SelectTab(SocialTab tab)
        {
            if (Current.Tab == tab)
            {
                return;
            }

            _state.Set(Current.WithTab(tab).WithStatusMessage(string.Empty));
        }

        public void Search(string displayName)
        {
            var trimmed = (displayName ?? string.Empty).Trim();
            if (trimmed.Length == 0 || trimmed.Length > MaxDisplayNameLength)
            {
                _state.Set(Current.WithStatusMessage("请输入 1-64 个字符的玩家名。"));
                return;
            }

            RunAsync(token => _gateway.SearchAsync(trimmed, token), searched: true).Forget();
        }

        public void SendRequest(long accountId) => Act(SocialAction.SendRequest, accountId);

        public void AcceptRequest(long accountId) => Act(SocialAction.AcceptRequest, accountId);

        public void RejectRequest(long accountId) => Act(SocialAction.RejectRequest, accountId);

        public void RemoveFriend(long accountId) => Act(SocialAction.RemoveFriend, accountId);

        public void Block(long accountId) => Act(SocialAction.Block, accountId);

        public void Unblock(long accountId) => Act(SocialAction.Unblock, accountId);

        public void OpenConversation(long peerAccountId)
        {
            if (peerAccountId <= 0)
            {
                return;
            }

            RunAsync(token => _gateway.OpenConversationAsync(peerAccountId, token),
                peerAccountId: peerAccountId).Forget();
        }

        public void SendMessage(string body)
        {
            var peer = Current.ActivePeerAccountId;
            if (peer <= 0)
            {
                return;
            }

            var trimmed = (body ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return;
            }

            if (trimmed.Length > MaxMessageLength)
            {
                _state.Set(Current.WithStatusMessage("消息最多 512 个字符。"));
                return;
            }

            RunAsync(token => _gateway.SendMessageAsync(peer, trimmed, token),
                peerAccountId: peer).Forget();
        }

        public async UniTask ReloadAsync(CancellationToken cancellationToken)
        {
            if (_isLoading)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Social))
            {
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing)));
                return;
            }

            _isLoading = true;
            _state.Set(Current.WithLoading(true).WithStatusMessage(string.Empty));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, _lifetime.Token))
                {
                    Apply(await _gateway.RequestSocialAsync(linked.Token), false, 0, false);
                }
            }
            catch (OperationCanceledException)
            {
                _state.Set(Current.WithLoading(false));
            }
            catch (Exception)
            {
                _state.Set(Current
                    .WithLoading(false)
                    .WithStatusMessage(LobbyOperationMessages.Describe(LobbyOperationStatus.TransportFailure)));
            }
            finally
            {
                _isLoading = false;
            }
        }

        public IDisposable Subscribe(IObserver<SocialPresentationState> observer) => _state.Subscribe(observer);

        public void Dispose()
        {
            _lobbySubscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _state.Dispose();
        }

        private void Act(SocialAction action, long accountId)
        {
            if (accountId <= 0)
            {
                return;
            }

            RunAsync(token => _gateway.ActAsync(action, accountId, token)).Forget();
        }

        private async UniTaskVoid RunAsync(
            Func<CancellationToken, UniTask<SocialResult>> call,
            bool searched = false,
            long peerAccountId = 0)
        {
            if (_isBusy || !Current.IsOpen)
            {
                return;
            }

            if (!_capabilities.Has(NarakaServerCapabilities.Social))
            {
                _state.Set(Current.WithStatusMessage(
                    LobbyOperationMessages.Describe(LobbyOperationStatus.ServerCapabilityMissing)));
                return;
            }

            _isBusy = true;
            _state.Set(Current.WithBusy(true).WithStatusMessage(string.Empty));
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           CancellationToken.None, _lifetime.Token))
                {
                    Apply(await call(linked.Token), searched, peerAccountId, true);
                }
            }
            catch (OperationCanceledException)
            {
                _state.Set(Current.WithBusy(false));
            }
            catch (Exception)
            {
                _state.Set(Current
                    .WithBusy(false)
                    .WithStatusMessage(LobbyOperationMessages.Describe(LobbyOperationStatus.TransportFailure)));
            }
            finally
            {
                _isBusy = false;
            }
        }

        private void Apply(SocialResult result, bool searched, long peerAccountId, bool fromAction)
        {
            if (!result.IsSuccess)
            {
                _state.Set(Current
                    .WithLoading(false)
                    .WithBusy(false)
                    .WithStatusMessage(LobbyOperationMessages.Describe(result.Status)));
                return;
            }

            var next = Current
                .WithServerState(
                    result.Friends, result.IncomingRequests, result.Blocked, result.Conversations)
                .WithStatusMessage(string.Empty);

            if (searched)
            {
                next = next.WithSearchResult(result.SearchResult, true);
            }

            if (peerAccountId > 0)
            {
                next = next.WithConversation(result.ConversationId, peerAccountId, result.Messages);
            }

            _state.Set(next);
            PublishRedDots();
        }

        /// <summary>
        /// 声明社交侧的红点来源。
        ///
        /// 每段会话是一个独立叶子，因此读完一个人的消息不会把其他人的未读一起熄灭。
        /// 已经消失的会话要显式声明为"无内容"，否则它的红点会永远留在树上。
        /// </summary>
        private void PublishRedDots()
        {
            var state = Current;
            _bus.Publish(new RedDotSourceChanged(
                RedDotPath.SocialFriendRequest, state.HasIncomingRequests));

            var live = new HashSet<string>(StringComparer.Ordinal);
            foreach (var conversation in state.Conversations)
            {
                var path = RedDotPath.UnreadChat(
                    conversation.ConversationId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                live.Add(path);
                _bus.Publish(new RedDotSourceChanged(path, conversation.HasUnread));
            }

            foreach (var path in _publishedConversationNodes)
            {
                if (!live.Contains(path))
                {
                    _bus.Publish(new RedDotSourceRemoved(path));
                }
            }

            _publishedConversationNodes.Clear();
            foreach (var path in live)
            {
                _publishedConversationNodes.Add(path);
            }
        }

        private void ApplyLobbyState(LobbyPresentationState lobby)
        {
            // 好友与聊天共用一个模块：两个大厅入口都打开它，只是默认分页不同。
            var isFriends = lobby.IsFeatureOpen && lobby.OpenFeature == LobbyFeature.Friends;
            var isChat = lobby.IsFeatureOpen && lobby.OpenFeature == LobbyFeature.Chat;
            var isOpen = isFriends || isChat;

            _state.Set(Current.WithSelfAccountId(lobby.AccountId));

            if (isOpen == _wasOpen)
            {
                return;
            }

            _wasOpen = isOpen;
            var next = Current.WithOpen(isOpen);
            if (isOpen)
            {
                next = next.WithTab(isChat ? SocialTab.Chat : SocialTab.Friends);
            }

            _state.Set(next);
            if (isOpen)
            {
                ReloadAsync(CancellationToken.None).Forget();
            }
        }

        private sealed class LobbyStateObserver : IObserver<LobbyPresentationState>
        {
            private readonly SocialController _owner;

            public LobbyStateObserver(SocialController owner) => _owner = owner;

            public void OnNext(LobbyPresentationState value) => _owner.ApplyLobbyState(value);

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }
    }
}
