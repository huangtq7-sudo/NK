using System;
using System.Collections.Generic;
using Naraka.Core.Application.MVC;

namespace Naraka.Features.Social.Controller
{
    /// <summary>界面分页。好友与聊天是同一个模块的两个视图。</summary>
    public enum SocialTab
    {
        Friends = 0,
        Requests = 1,
        Chat = 2,
        Blocked = 3
    }

    /// <summary>一名玩家的公开资料。</summary>
    public readonly struct SocialPlayerSnapshot
    {
        public SocialPlayerSnapshot(long accountId, string displayName, string avatarId, bool isOnline)
        {
            AccountId = accountId;
            DisplayName = displayName ?? string.Empty;
            AvatarId = avatarId ?? string.Empty;
            IsOnline = isOnline;
        }

        public long AccountId { get; }

        public string DisplayName { get; }

        public string AvatarId { get; }

        /// <summary>
        /// 在线状态。
        ///
        /// 这不是红点：绿点/灰点表达的是"对方现在在不在"，而红点表达的是"有你还没处理的东西"。
        /// 因此它留在社交模块自己的展示状态里，不进红点树。
        /// </summary>
        public bool IsOnline { get; }

        public bool IsValid => AccountId > 0;
    }

    /// <summary>一段一对一会话的列表项。</summary>
    public readonly struct ChatConversationSnapshot
    {
        public ChatConversationSnapshot(
            long conversationId,
            SocialPlayerSnapshot peer,
            long lastMessageId,
            long lastReadMessageId)
        {
            ConversationId = conversationId;
            Peer = peer;
            LastMessageId = lastMessageId;
            LastReadMessageId = lastReadMessageId;
        }

        public long ConversationId { get; }

        public SocialPlayerSnapshot Peer { get; }

        public long LastMessageId { get; }

        public long LastReadMessageId { get; }

        /// <summary>存在未读消息。会话红点只看这一项。</summary>
        public bool HasUnread => LastMessageId > LastReadMessageId;
    }

    /// <summary>一条私聊消息。</summary>
    public readonly struct ChatMessageSnapshot
    {
        public ChatMessageSnapshot(
            long messageId,
            long conversationId,
            long senderAccountId,
            string body,
            long sentUnixSeconds)
        {
            MessageId = messageId;
            ConversationId = conversationId;
            SenderAccountId = senderAccountId;
            Body = body ?? string.Empty;
            SentUnixSeconds = sentUnixSeconds;
        }

        public long MessageId { get; }

        public long ConversationId { get; }

        public long SenderAccountId { get; }

        public string Body { get; }

        public long SentUnixSeconds { get; }
    }

    /// <summary>
    /// 好友与聊天界面的只读展示状态。
    ///
    /// 每一次服务端响应都带回完整的社交视图，因此这里永远是整体替换而不是局部合并：
    /// 合并出错会留下一个既发不了消息也删不掉的好友。
    /// </summary>
    public readonly struct SocialPresentationState : IPresentationState
    {
        public SocialPresentationState(
            bool isOpen,
            bool isLoading,
            bool isBusy,
            bool hasServerState,
            SocialTab tab,
            long selfAccountId,
            IReadOnlyList<SocialPlayerSnapshot> friends,
            IReadOnlyList<SocialPlayerSnapshot> incomingRequests,
            IReadOnlyList<SocialPlayerSnapshot> blocked,
            IReadOnlyList<ChatConversationSnapshot> conversations,
            long activeConversationId,
            long activePeerAccountId,
            IReadOnlyList<ChatMessageSnapshot> messages,
            SocialPlayerSnapshot searchResult,
            bool hasSearched,
            string statusMessage)
        {
            IsOpen = isOpen;
            IsLoading = isLoading;
            IsBusy = isBusy;
            HasServerState = hasServerState;
            Tab = tab;
            SelfAccountId = selfAccountId;
            Friends = friends ?? Array.Empty<SocialPlayerSnapshot>();
            IncomingRequests = incomingRequests ?? Array.Empty<SocialPlayerSnapshot>();
            Blocked = blocked ?? Array.Empty<SocialPlayerSnapshot>();
            Conversations = conversations ?? Array.Empty<ChatConversationSnapshot>();
            ActiveConversationId = activeConversationId;
            ActivePeerAccountId = activePeerAccountId;
            Messages = messages ?? Array.Empty<ChatMessageSnapshot>();
            SearchResult = searchResult;
            HasSearched = hasSearched;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public static SocialPresentationState Initial => new SocialPresentationState(
            false, false, false, false, SocialTab.Friends, 0,
            null, null, null, null, 0, 0, null, default, false, string.Empty);

        public bool IsOpen { get; }

        public bool IsLoading { get; }

        /// <summary>请求在途。期间禁用全部动作按钮，避免重复点击产生两条申请。</summary>
        public bool IsBusy { get; }

        public bool HasServerState { get; }

        public SocialTab Tab { get; }

        /// <summary>自己的账号号。用于把聊天气泡分成左右两侧。</summary>
        public long SelfAccountId { get; }

        public IReadOnlyList<SocialPlayerSnapshot> Friends { get; }

        public IReadOnlyList<SocialPlayerSnapshot> IncomingRequests { get; }

        public IReadOnlyList<SocialPlayerSnapshot> Blocked { get; }

        public IReadOnlyList<ChatConversationSnapshot> Conversations { get; }

        public long ActiveConversationId { get; }

        public long ActivePeerAccountId { get; }

        public IReadOnlyList<ChatMessageSnapshot> Messages { get; }

        /// <summary>最近一次搜索命中的玩家。未命中时 <see cref="SocialPlayerSnapshot.IsValid"/> 为 false。</summary>
        public SocialPlayerSnapshot SearchResult { get; }

        /// <summary>是否已经搜索过。用来区分"还没搜"与"搜了但没找到"。</summary>
        public bool HasSearched { get; }

        public string StatusMessage { get; }

        public bool HasIncomingRequests => IncomingRequests.Count > 0;

        /// <summary>存在任意未读会话。</summary>
        public bool HasUnreadChat
        {
            get
            {
                foreach (var conversation in Conversations)
                {
                    if (conversation.HasUnread)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool IsFriend(long accountId)
        {
            foreach (var friend in Friends)
            {
                if (friend.AccountId == accountId)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsBlocked(long accountId)
        {
            foreach (var blocked in Blocked)
            {
                if (blocked.AccountId == accountId)
                {
                    return true;
                }
            }

            return false;
        }

        public SocialPresentationState WithOpen(bool isOpen) => Copy(isOpen: isOpen);

        public SocialPresentationState WithLoading(bool isLoading) => Copy(isLoading: isLoading);

        public SocialPresentationState WithBusy(bool isBusy) => Copy(isBusy: isBusy);

        public SocialPresentationState WithTab(SocialTab tab) => Copy(tab: tab);

        public SocialPresentationState WithSelfAccountId(long selfAccountId) =>
            Copy(selfAccountId: selfAccountId);

        public SocialPresentationState WithStatusMessage(string statusMessage) =>
            Copy(statusMessage: statusMessage);

        public SocialPresentationState WithServerState(
            IReadOnlyList<SocialPlayerSnapshot> friends,
            IReadOnlyList<SocialPlayerSnapshot> incomingRequests,
            IReadOnlyList<SocialPlayerSnapshot> blocked,
            IReadOnlyList<ChatConversationSnapshot> conversations) =>
            new SocialPresentationState(
                IsOpen, false, false, true, Tab, SelfAccountId,
                friends, incomingRequests, blocked, conversations,
                ActiveConversationId, ActivePeerAccountId, Messages,
                SearchResult, HasSearched, StatusMessage);

        public SocialPresentationState WithConversation(
            long conversationId,
            long peerAccountId,
            IReadOnlyList<ChatMessageSnapshot> messages) =>
            new SocialPresentationState(
                IsOpen, IsLoading, IsBusy, HasServerState, Tab, SelfAccountId,
                Friends, IncomingRequests, Blocked, Conversations,
                conversationId, peerAccountId, messages,
                SearchResult, HasSearched, StatusMessage);

        public SocialPresentationState WithSearchResult(SocialPlayerSnapshot player, bool hasSearched) =>
            new SocialPresentationState(
                IsOpen, IsLoading, IsBusy, HasServerState, Tab, SelfAccountId,
                Friends, IncomingRequests, Blocked, Conversations,
                ActiveConversationId, ActivePeerAccountId, Messages,
                player, hasSearched, StatusMessage);

        private SocialPresentationState Copy(
            bool? isOpen = null,
            bool? isLoading = null,
            bool? isBusy = null,
            SocialTab? tab = null,
            long? selfAccountId = null,
            string statusMessage = null) =>
            new SocialPresentationState(
                isOpen ?? IsOpen,
                isLoading ?? IsLoading,
                isBusy ?? IsBusy,
                HasServerState,
                tab ?? Tab,
                selfAccountId ?? SelfAccountId,
                Friends,
                IncomingRequests,
                Blocked,
                Conversations,
                ActiveConversationId,
                ActivePeerAccountId,
                Messages,
                SearchResult,
                HasSearched,
                statusMessage ?? StatusMessage);
    }
}
