using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Social;

/// <summary>
/// 玩家资料。昵称在这张表上唯一，因此好友搜索是一次等值查找而不是全表扫描。
/// </summary>
[SugarTable("player_profiles")]
internal sealed class PlayerProfileRow
{
    [SugarColumn(ColumnName = "player_id", IsPrimaryKey = true, IsIdentity = true)]
    public long PlayerId { get; set; }

    [SugarColumn(ColumnName = "account_id")]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "display_name", Length = 64)]
    public string DisplayName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// 好友关系。每段关系写两行（A→B 与 B→A），因此"我的好友"永远是一次等值查询。
/// </summary>
[SugarTable("account_friends")]
internal sealed class AccountFriendRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "friend_account_id", IsPrimaryKey = true)]
    public long FriendAccountId { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }
}

/// <summary>待处理的好友申请。接受或拒绝都会删除这一行。</summary>
[SugarTable("account_friend_requests")]
internal sealed class AccountFriendRequestRow
{
    [SugarColumn(ColumnName = "requester_account_id", IsPrimaryKey = true)]
    public long RequesterAccountId { get; set; }

    [SugarColumn(ColumnName = "target_account_id", IsPrimaryKey = true)]
    public long TargetAccountId { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }
}

/// <summary>屏蔽名单。屏蔽是单向记录，但判定时任一方存在即生效。</summary>
[SugarTable("account_blocks")]
internal sealed class AccountBlockRow
{
    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "blocked_account_id", IsPrimaryKey = true)]
    public long BlockedAccountId { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }
}

/// <summary>一对一会话。账号对按低-高规范化，因此谁先开都只会有一行。</summary>
[SugarTable("chat_conversations")]
internal sealed class ChatConversationRow
{
    [SugarColumn(ColumnName = "conversation_id", IsPrimaryKey = true, IsIdentity = true)]
    public long ConversationId { get; set; }

    [SugarColumn(ColumnName = "low_account_id")]
    public long LowAccountId { get; set; }

    [SugarColumn(ColumnName = "high_account_id")]
    public long HighAccountId { get; set; }

    [SugarColumn(ColumnName = "last_message_utc", IsNullable = true)]
    public DateTime? LastMessageUtc { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }
}

[SugarTable("chat_messages")]
internal sealed class ChatMessageRow
{
    [SugarColumn(ColumnName = "message_id", IsPrimaryKey = true, IsIdentity = true)]
    public long MessageId { get; set; }

    [SugarColumn(ColumnName = "conversation_id")]
    public long ConversationId { get; set; }

    [SugarColumn(ColumnName = "sender_account_id")]
    public long SenderAccountId { get; set; }

    [SugarColumn(ColumnName = "body", Length = 512)]
    public string Body { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "sent_utc")]
    public DateTime SentUtc { get; set; }
}

/// <summary>每个账号在每段会话里的已读位置。两侧各存一行，互不影响。</summary>
[SugarTable("chat_read_positions")]
internal sealed class ChatReadPositionRow
{
    [SugarColumn(ColumnName = "conversation_id", IsPrimaryKey = true)]
    public long ConversationId { get; set; }

    [SugarColumn(ColumnName = "account_id", IsPrimaryKey = true)]
    public long AccountId { get; set; }

    [SugarColumn(ColumnName = "last_read_message_id")]
    public long LastReadMessageId { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}
