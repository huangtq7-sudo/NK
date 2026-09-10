using System.Text;
using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Sessions;

namespace Naraka.Server.Application.Social;

/// <summary>一名玩家的公开资料。搜索结果与好友列表都只暴露这些字段。</summary>
public sealed record SocialPlayer(long AccountId, string DisplayName, string AvatarId);

/// <summary>一条好友关系。<see cref="IsOnline"/> 由实时连接推导，不落库。</summary>
public sealed record FriendEntry(SocialPlayer Player, bool IsOnline);

/// <summary>一条待处理的好友申请。</summary>
public sealed record FriendRequestEntry(SocialPlayer Player, DateTime CreatedUtc);

/// <summary>一条一对一私聊消息。</summary>
public sealed record ChatMessage(
    long MessageId,
    long ConversationId,
    long SenderAccountId,
    string Body,
    DateTime SentUtc);

/// <summary>一段会话在列表里的摘要。</summary>
public sealed record ChatConversation(
    long ConversationId,
    SocialPlayer Peer,
    bool IsOnline,
    long LastMessageId,
    long LastReadMessageId,
    DateTime? LastMessageUtc)
{
    /// <summary>未读条数只按消息 ID 之差估算不可靠，因此未读与否由仓储返回的精确计数决定。</summary>
    public bool HasUnread => LastMessageId > LastReadMessageId;
}

/// <summary>好友申请的处理结果。</summary>
public enum FriendRequestOutcome
{
    Applied = 0,
    AlreadyFriends = 1,
    AlreadyRequested = 2,
    Blocked = 3,
    NotFound = 4,

    /// <summary>对方已经向自己发过申请，这一次直接成为好友。</summary>
    AutoAccepted = 5
}

public interface ISocialRepository
{
    Task<SocialPlayer?> FindByAccountIdAsync(long accountId, CancellationToken cancellationToken);

    /// <summary>按显示名精确查找。昵称唯一，因此不做模糊匹配，避免把搜索变成全表扫描。</summary>
    Task<SocialPlayer?> FindByDisplayNameAsync(string displayName, CancellationToken cancellationToken);

    Task<IReadOnlyList<SocialPlayer>> ListFriendsAsync(long accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FriendRequestEntry>> ListIncomingRequestsAsync(
        long accountId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<long>> ListOutgoingRequestTargetsAsync(
        long accountId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SocialPlayer>> ListBlockedAsync(long accountId, CancellationToken cancellationToken);

    Task<bool> IsFriendAsync(long accountId, long otherAccountId, CancellationToken cancellationToken);

    /// <summary>任一方拉黑都算拉黑。发起申请与发消息都必须先过这一关。</summary>
    Task<bool> IsBlockedEitherWayAsync(
        long accountId,
        long otherAccountId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 在<b>一个</b>事务内写入好友申请。
    ///
    /// 若对方已经申请过自己，则直接成对写入好友关系并删除那条申请，返回
    /// <see cref="FriendRequestOutcome.AutoAccepted"/>：这避免了两条互相等待的申请永远挂着。
    /// </summary>
    Task<FriendRequestOutcome> TrySendRequestAsync(
        long requesterAccountId,
        long targetAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>在一个事务内删除申请并成对写入好友关系。申请不存在时返回 false。</summary>
    Task<bool> TryAcceptRequestAsync(
        long targetAccountId,
        long requesterAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<bool> TryRejectRequestAsync(
        long targetAccountId,
        long requesterAccountId,
        CancellationToken cancellationToken);

    /// <summary>成对删除好友关系。单向删除会留下一条只有对方看得见的好友。</summary>
    Task<bool> TryRemoveFriendAsync(
        long accountId,
        long friendAccountId,
        CancellationToken cancellationToken);

    /// <summary>拉黑。同时清除既有好友关系与两个方向的申请。</summary>
    Task TryBlockAsync(
        long accountId,
        long blockedAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<bool> TryUnblockAsync(long accountId, long blockedAccountId, CancellationToken cancellationToken);
}

public interface IChatRepository
{
    /// <summary>取得或创建一对一会话。会话按账号对唯一，谁先开都一样。</summary>
    Task<long> EnsureConversationAsync(
        long accountId,
        long peerAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatConversation>> ListConversationsAsync(
        long accountId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(
        long conversationId,
        int limit,
        CancellationToken cancellationToken);

    Task<ChatMessage> AppendMessageAsync(
        long conversationId,
        long senderAccountId,
        string body,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>推进自己的已读位置。只前进不后退。</summary>
    Task MarkReadAsync(
        long conversationId,
        long accountId,
        long lastReadMessageId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>会话的参与者。用于校验发送者确实属于这段会话。</summary>
    Task<(long Low, long High)?> FindParticipantsAsync(
        long conversationId,
        CancellationToken cancellationToken);

    /// <summary>该账号在给定时间点之后发送的消息条数。用于限流。</summary>
    Task<int> CountRecentMessagesAsync(
        long accountId,
        DateTime sinceUtc,
        CancellationToken cancellationToken);
}

public sealed record SocialView(
    IReadOnlyList<FriendEntry> Friends,
    IReadOnlyList<FriendRequestEntry> IncomingRequests,
    IReadOnlyList<SocialPlayer> Blocked,
    IReadOnlyList<ChatConversation> Conversations);

public readonly struct SocialResult
{
    private SocialResult(
        LobbyOperationStatus status,
        SocialView? view,
        SocialPlayer? searchResult,
        IReadOnlyList<ChatMessage>? messages)
    {
        Status = status;
        View = view;
        SearchResult = searchResult;
        Messages = messages ?? Array.Empty<ChatMessage>();
    }

    public LobbyOperationStatus Status { get; }

    public SocialView? View { get; }

    /// <summary>搜索命中的玩家。未搜索或没有命中时为 null。</summary>
    public SocialPlayer? SearchResult { get; }

    public IReadOnlyList<ChatMessage> Messages { get; }

    public static SocialResult Success(SocialView view) =>
        new(LobbyOperationStatus.Success, view, null, null);

    public static SocialResult Found(SocialView view, SocialPlayer? player) =>
        new(LobbyOperationStatus.Success, view, player, null);

    public static SocialResult WithMessages(SocialView view, IReadOnlyList<ChatMessage> messages) =>
        new(LobbyOperationStatus.Success, view, null, messages);

    public static SocialResult Failed(LobbyOperationStatus status) => new(status, null, null, null);
}

/// <summary>
/// 好友与一对一私聊。
///
/// 三条规则贯穿全部写操作：账号只能来自已认证会话（这里的 accountId 由传输层提供，不接受客户端指定）；
/// 任一方拉黑就不能加好友、不能发消息；每一次写入都是一个事务，好友关系成对写入，
/// 因此不会出现"我这边有你、你那边没有我"。
///
/// P1 只做一对一文字聊天：世界频道、群聊、语音、文件与玩家交易都不在范围内。
/// </summary>
public sealed class SocialService(
    ISocialRepository social,
    IChatRepository chat,
    IAuthenticatedSessionRegistry sessions,
    TimeProvider? timeProvider = null)
{
    /// <summary>单条消息的最大字符数。与 chat_messages.body 的列宽一致。</summary>
    public const int MaxMessageLength = 512;

    /// <summary>昵称的最大字符数。与 player_profiles.display_name 一致。</summary>
    public const int MaxDisplayNameLength = 64;

    /// <summary>限流窗口。</summary>
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(10);

    /// <summary>限流窗口内允许发送的消息条数。</summary>
    public const int RateLimitMessages = 10;

    /// <summary>一次拉取的最大历史消息条数。</summary>
    public const int MessagePageSize = 50;

    private readonly ISocialRepository _social = social ?? throw new ArgumentNullException(nameof(social));
    private readonly IChatRepository _chat = chat ?? throw new ArgumentNullException(nameof(chat));

    private readonly IAuthenticatedSessionRegistry _sessions =
        sessions ?? throw new ArgumentNullException(nameof(sessions));

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public Task<SocialResult> GetAsync(long accountId, CancellationToken cancellationToken) =>
        GuardAsync(accountId, async () => SocialResult.Success(
            await ReadViewAsync(accountId, cancellationToken)));

    /// <summary>
    /// 按昵称精确搜索。
    ///
    /// 搜到自己、搜到已拉黑的对象都返回"未找到"：让玩家看见一个自己无论如何都加不了的结果
    /// 只会制造困惑。
    /// </summary>
    public Task<SocialResult> SearchAsync(
        long accountId,
        string? displayName,
        CancellationToken cancellationToken) =>
        GuardAsync(accountId, async () =>
        {
            var normalized = NormalizeName(displayName);
            if (normalized is null)
            {
                return SocialResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            var found = await _social.FindByDisplayNameAsync(normalized, cancellationToken);
            if (found is not null &&
                (found.AccountId == accountId ||
                 await _social.IsBlockedEitherWayAsync(accountId, found.AccountId, cancellationToken)))
            {
                found = null;
            }

            return SocialResult.Found(await ReadViewAsync(accountId, cancellationToken), found);
        });

    public Task<SocialResult> SendRequestAsync(
        long accountId,
        long targetAccountId,
        CancellationToken cancellationToken) =>
        GuardAsync(accountId, async () =>
        {
            if (targetAccountId <= 0 || targetAccountId == accountId)
            {
                return SocialResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            if (await _social.FindByAccountIdAsync(targetAccountId, cancellationToken) is null)
            {
                return SocialResult.Failed(LobbyOperationStatus.NotFound);
            }

            if (await _social.IsBlockedEitherWayAsync(accountId, targetAccountId, cancellationToken))
            {
                return SocialResult.Failed(LobbyOperationStatus.NotAvailable);
            }

            var outcome = await _social.TrySendRequestAsync(
                accountId, targetAccountId, _time.GetUtcNow().UtcDateTime, cancellationToken);

            return outcome switch
            {
                FriendRequestOutcome.Applied or FriendRequestOutcome.AutoAccepted =>
                    SocialResult.Success(await ReadViewAsync(accountId, cancellationToken)),
                FriendRequestOutcome.AlreadyFriends or FriendRequestOutcome.AlreadyRequested =>
                    SocialResult.Failed(LobbyOperationStatus.AlreadyClaimed),
                FriendRequestOutcome.Blocked => SocialResult.Failed(LobbyOperationStatus.NotAvailable),
                FriendRequestOutcome.NotFound => SocialResult.Failed(LobbyOperationStatus.NotFound),
                _ => SocialResult.Failed(LobbyOperationStatus.InternalError)
            };
        });

    public Task<SocialResult> AcceptRequestAsync(
        long accountId,
        long requesterAccountId,
        CancellationToken cancellationToken) =>
        GuardAsync(accountId, async () =>
        {
            if (requesterAccountId <= 0 || requesterAccountId == accountId)
            {
                return SocialResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            var accepted = await _social.TryAcceptRequestAsync(
                accountId, requesterAccountId, _time.GetUtcNow().UtcDateTime, cancellationToken);

            return accepted
                ? SocialResult.Success(await ReadViewAsync(accountId, cancellationToken))
                : SocialResult.Failed(LobbyOperationStatus.NotFound);
        });

    public Task<SocialResult> RejectRequestAsync(
        long accountId,
        long requesterAccountId,
        CancellationToken cancellationToken) =>
        GuardAsync(accountId, async () =>
        {
            if (requesterAccountId <= 0 || requesterAccountId == accountId)
            {
                return SocialResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            var rejected = await _social.TryRejectRequestAsync(
                accountId, requesterAccountId, cancellationToken);

            return rejected
                ? SocialResult.Success(await ReadViewAsync(accountId, cancellationToken))
                : SocialResult.Failed(LobbyOperationStatus.NotFound);
        });

    public Task<SocialResult> RemoveFriendAsync(
        long accountId,
        long friendAccountId,
        CancellationToken cancellationToken) =>
        GuardAsync(accountId, async () =>
        {
            if (friendAccountId <= 0 || friendAccountId == accountId)
            {
                return SocialResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            var removed = await _social.TryRemoveFriendAsync(accountId, friendAccountId, cancellationToken);
            return removed
                ? SocialResult.Success(await ReadViewAsync(accountId, cancellationToken))
                : SocialResult.Failed(LobbyOperationStatus.NotFound);
        });

    public Task<SocialResult> BlockAsync(
        long accountId,
        long blockedAccountId,
        CancellationToken cancellationToken) =>
        GuardAsync(accountId, async () =>
        {
            if (blockedAccountId <= 0 || blockedAccountId == accountId)
            {
                return SocialResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            if (await _social.FindByAccountIdAsync(blockedAccountId, cancellationToken) is null)
            {
                return SocialResult.Failed(LobbyOperationStatus.NotFound);
            }

            await _social.TryBlockAsync(
                accountId, blockedAccountId, _time.GetUtcNow().UtcDateTime, cancellationToken);
            return SocialResult.Success(await ReadViewAsync(accountId, cancellationToken));
        });

    public Task<SocialResult> UnblockAsync(
        long accountId,
        long blockedAccountId,
        CancellationToken cancellationToken) =>
        GuardAsync(accountId, async () =>
        {
            if (blockedAccountId <= 0 || blockedAccountId == accountId)
            {
                return SocialResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            var removed = await _social.TryUnblockAsync(accountId, blockedAccountId, cancellationToken);
            return removed
                ? SocialResult.Success(await ReadViewAsync(accountId, cancellationToken))
                : SocialResult.Failed(LobbyOperationStatus.NotFound);
        });

    /// <summary>打开一段会话并取回最近的消息。只有好友之间才能开会话。</summary>
    public Task<SocialResult> OpenConversationAsync(
        long accountId,
        long peerAccountId,
        CancellationToken cancellationToken) =>
        GuardAsync(accountId, async () =>
        {
            if (peerAccountId <= 0 || peerAccountId == accountId)
            {
                return SocialResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            if (!await _social.IsFriendAsync(accountId, peerAccountId, cancellationToken))
            {
                return SocialResult.Failed(LobbyOperationStatus.NotAvailable);
            }

            if (await _social.IsBlockedEitherWayAsync(accountId, peerAccountId, cancellationToken))
            {
                return SocialResult.Failed(LobbyOperationStatus.NotAvailable);
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var conversationId = await _chat.EnsureConversationAsync(
                accountId, peerAccountId, now, cancellationToken);
            var messages = await _chat.ListMessagesAsync(
                conversationId, MessagePageSize, cancellationToken);

            if (messages.Count > 0)
            {
                await _chat.MarkReadAsync(
                    conversationId, accountId, messages[^1].MessageId, now, cancellationToken);
            }

            return SocialResult.WithMessages(
                await ReadViewAsync(accountId, cancellationToken), messages);
        });

    /// <summary>
    /// 发送一条私聊消息。
    ///
    /// 校验顺序刻意是：内容合法 → 是好友 → 未被拉黑 → 未超限流。前三条是权限，最后一条是节流；
    /// 把限流放在最后，可以保证一个没有权限的请求不会占用限流额度。
    /// </summary>
    public Task<SocialResult> SendMessageAsync(
        long accountId,
        long peerAccountId,
        string? body,
        CancellationToken cancellationToken) =>
        GuardAsync(accountId, async () =>
        {
            var normalized = NormalizeMessage(body);
            if (normalized is null || peerAccountId <= 0 || peerAccountId == accountId)
            {
                return SocialResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            if (!await _social.IsFriendAsync(accountId, peerAccountId, cancellationToken))
            {
                return SocialResult.Failed(LobbyOperationStatus.NotAvailable);
            }

            if (await _social.IsBlockedEitherWayAsync(accountId, peerAccountId, cancellationToken))
            {
                return SocialResult.Failed(LobbyOperationStatus.NotAvailable);
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var recent = await _chat.CountRecentMessagesAsync(
                accountId, now - RateLimitWindow, cancellationToken);
            if (recent >= RateLimitMessages)
            {
                return SocialResult.Failed(LobbyOperationStatus.RateLimited);
            }

            var conversationId = await _chat.EnsureConversationAsync(
                accountId, peerAccountId, now, cancellationToken);
            var message = await _chat.AppendMessageAsync(
                conversationId, accountId, normalized, now, cancellationToken);

            // 自己发的消息对自己来说当然是已读的。
            await _chat.MarkReadAsync(conversationId, accountId, message.MessageId, now, cancellationToken);

            var messages = await _chat.ListMessagesAsync(
                conversationId, MessagePageSize, cancellationToken);
            return SocialResult.WithMessages(
                await ReadViewAsync(accountId, cancellationToken), messages);
        });

    /// <summary>把一段会话标记为已读到某条消息。</summary>
    public Task<SocialResult> MarkReadAsync(
        long accountId,
        long conversationId,
        long lastReadMessageId,
        CancellationToken cancellationToken) =>
        GuardAsync(accountId, async () =>
        {
            if (conversationId <= 0 || lastReadMessageId < 0)
            {
                return SocialResult.Failed(LobbyOperationStatus.InvalidRequest);
            }

            var participants = await _chat.FindParticipantsAsync(conversationId, cancellationToken);
            if (participants is null)
            {
                return SocialResult.Failed(LobbyOperationStatus.NotFound);
            }

            // 只能标记自己参与的会话：账号来自已认证会话，客户端指定的会话号仍然要验一遍。
            if (participants.Value.Low != accountId && participants.Value.High != accountId)
            {
                return SocialResult.Failed(LobbyOperationStatus.NotAvailable);
            }

            await _chat.MarkReadAsync(
                conversationId, accountId, lastReadMessageId,
                _time.GetUtcNow().UtcDateTime, cancellationToken);
            return SocialResult.Success(await ReadViewAsync(accountId, cancellationToken));
        });

    /// <summary>去除首尾空白并校验长度。不合法时返回 null。</summary>
    internal static string? NormalizeName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var trimmed = displayName.Trim();
        return trimmed.Length is 0 or > MaxDisplayNameLength ? null : trimmed;
    }

    /// <summary>
    /// 规范化一条消息。
    ///
    /// 控制字符会被剔除：它们在 UI Toolkit 里可能被解释成换行或不可见字符，
    /// 让一条消息看起来和存下来的不一样。表情通过预置文本标记传输，因此这里不需要放行任何控制字符。
    /// </summary>
    internal static string? NormalizeMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var builder = new StringBuilder(body.Length);
        foreach (var character in body)
        {
            if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        var trimmed = builder.ToString().Trim();
        return trimmed.Length is 0 or > MaxMessageLength ? null : trimmed;
    }

    private async Task<SocialView> ReadViewAsync(long accountId, CancellationToken cancellationToken)
    {
        var friends = await _social.ListFriendsAsync(accountId, cancellationToken);
        var entries = new List<FriendEntry>(friends.Count);
        foreach (var friend in friends)
        {
            // 在线状态来自实时连接注册表，绝不落库：存下来的标记在崩溃后会永远停在"在线"。
            entries.Add(new FriendEntry(friend, _sessions.IsOnline(friend.AccountId)));
        }

        // 仓储不知道谁在线，因此会话的在线标记在这里补齐，与好友列表用同一个来源。
        var conversations = await _chat.ListConversationsAsync(accountId, cancellationToken);
        var withPresence = new List<ChatConversation>(conversations.Count);
        foreach (var conversation in conversations)
        {
            withPresence.Add(conversation with
            {
                IsOnline = _sessions.IsOnline(conversation.Peer.AccountId)
            });
        }

        return new SocialView(
            entries,
            await _social.ListIncomingRequestsAsync(accountId, cancellationToken),
            await _social.ListBlockedAsync(accountId, cancellationToken),
            withPresence);
    }

    private async Task<SocialResult> GuardAsync(long accountId, Func<Task<SocialResult>> action)
    {
        if (accountId <= 0)
        {
            return SocialResult.Failed(LobbyOperationStatus.InvalidRequest);
        }

        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ProgressionStorageException)
        {
            return SocialResult.Failed(LobbyOperationStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return SocialResult.Failed(LobbyOperationStatus.InternalError);
        }
    }
}
