using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Social;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Social;

/// <summary>
/// 一对一私聊的持久化。
///
/// 会话按 (低账号, 高账号) 规范化，因此无论谁先开都只会有一行；未读位置每个账号各存一行，
/// 因此一方读完不会把另一方的未读也清掉。
/// </summary>
public sealed class SqlSugarChatRepository(SqlSugarClientFactory factory) : IChatRepository
{
    public async Task<long> EnsureConversationAsync(
        long accountId,
        long peerAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (low, high) = Canonical(accountId, peerAccountId);

        using var database = factory.Create();
        try
        {
            var existing = await database.Queryable<ChatConversationRow>()
                .Where(row => row.LowAccountId == low && row.HighAccountId == high)
                .SingleAsync();
            if (existing is not null)
            {
                return existing.ConversationId;
            }

            await database.Insertable(new ChatConversationRow
            {
                LowAccountId = low,
                HighAccountId = high,
                LastMessageUtc = null,
                CreatedUtc = nowUtc
            }).ExecuteCommandAsync();

            // 并发下两个连接可能同时插入，唯一键会让其中一个失败；重新查询即可拿到胜出的那一行。
            var created = await database.Queryable<ChatConversationRow>()
                .Where(row => row.LowAccountId == low && row.HighAccountId == high)
                .SingleAsync();

            return created?.ConversationId
                   ?? throw new InvalidOperationException("Conversation row disappeared after insert.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Failed to open a conversation.", exception);
        }
    }

    public async Task<IReadOnlyList<ChatConversation>> ListConversationsAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<ChatConversation>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var database = factory.Create();
        try
        {
            var rows = await database.Queryable<ChatConversationRow>()
                .Where(row => row.LowAccountId == accountId || row.HighAccountId == accountId)
                .ToListAsync();
            if (rows.Count == 0)
            {
                return Array.Empty<ChatConversation>();
            }

            var conversationIds = rows.Select(row => row.ConversationId).ToList();
            var peerIds = rows
                .Select(row => row.LowAccountId == accountId ? row.HighAccountId : row.LowAccountId)
                .Distinct()
                .ToList();

            var profiles = (await database.Queryable<PlayerProfileRow>()
                    .Where(row => peerIds.Contains(row.AccountId))
                    .ToListAsync())
                .ToDictionary(row => row.AccountId);

            var lastMessages = (await database.Queryable<ChatMessageRow>()
                    .Where(row => conversationIds.Contains(row.ConversationId))
                    .GroupBy(row => row.ConversationId)
                    .Select(row => new { row.ConversationId, LastId = SqlFunc.AggregateMax(row.MessageId) })
                    .ToListAsync())
                .ToDictionary(entry => entry.ConversationId, entry => entry.LastId);

            var readPositions = (await database.Queryable<ChatReadPositionRow>()
                    .Where(row => conversationIds.Contains(row.ConversationId) &&
                                  row.AccountId == accountId)
                    .ToListAsync())
                .ToDictionary(row => row.ConversationId, row => row.LastReadMessageId);

            var conversations = new List<ChatConversation>(rows.Count);
            foreach (var row in rows)
            {
                var peerId = row.LowAccountId == accountId ? row.HighAccountId : row.LowAccountId;
                if (!profiles.TryGetValue(peerId, out var profile))
                {
                    continue;
                }

                var avatar = await database.Queryable<Progression.AccountProfileRow>()
                    .Where(inner => inner.AccountId == peerId)
                    .Select(inner => inner.AvatarId)
                    .FirstAsync();

                conversations.Add(new ChatConversation(
                    row.ConversationId,
                    new SocialPlayer(peerId, profile.DisplayName, avatar ?? string.Empty),
                    // 在线状态由应用层按实时连接填充，仓储不知道谁在线。
                    false,
                    lastMessages.TryGetValue(row.ConversationId, out var last) ? last : 0,
                    readPositions.TryGetValue(row.ConversationId, out var read) ? read : 0,
                    row.LastMessageUtc));
            }

            return conversations
                .OrderByDescending(conversation => conversation.LastMessageUtc ?? DateTime.MinValue)
                .ThenBy(conversation => conversation.ConversationId)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Failed to list conversations.", exception);
        }
    }

    public async Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(
        long conversationId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (conversationId <= 0 || limit <= 0)
        {
            return Array.Empty<ChatMessage>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var database = factory.Create();
        try
        {
            // 取最近的一页，再按时间正序返回，界面就不需要自己翻转。
            var rows = await database.Queryable<ChatMessageRow>()
                .Where(row => row.ConversationId == conversationId)
                .OrderBy(row => row.MessageId, OrderByType.Desc)
                .Take(limit)
                .ToListAsync();

            return rows
                .OrderBy(row => row.MessageId)
                .Select(row => new ChatMessage(
                    row.MessageId, row.ConversationId, row.SenderAccountId, row.Body, row.SentUtc))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Failed to read messages.", exception);
        }
    }

    public async Task<ChatMessage> AppendMessageAsync(
        long conversationId,
        long senderAccountId,
        string body,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var messageId = await database.Insertable(new ChatMessageRow
            {
                ConversationId = conversationId,
                SenderAccountId = senderAccountId,
                Body = body,
                SentUtc = nowUtc
            }).ExecuteReturnBigIdentityAsync();

            await database.Updateable<ChatConversationRow>()
                .SetColumns(row => new ChatConversationRow { LastMessageUtc = nowUtc })
                .Where(row => row.ConversationId == conversationId)
                .ExecuteCommandAsync();

            database.Ado.CommitTran();
            return new ChatMessage(messageId, conversationId, senderAccountId, body, nowUtc);
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ProgressionStorageException("Failed to append a message.", exception);
        }
    }

    public async Task MarkReadAsync(
        long conversationId,
        long accountId,
        long lastReadMessageId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var database = factory.Create();
        try
        {
            var existing = await database.Queryable<ChatReadPositionRow>()
                .Where(row => row.ConversationId == conversationId && row.AccountId == accountId)
                .SingleAsync();

            if (existing is null)
            {
                await database.Insertable(new ChatReadPositionRow
                {
                    ConversationId = conversationId,
                    AccountId = accountId,
                    LastReadMessageId = lastReadMessageId,
                    UpdatedUtc = nowUtc
                }).ExecuteCommandAsync();
                return;
            }

            // 只前进不后退：迟到的旧请求不该把已读位置往回拉。
            if (existing.LastReadMessageId >= lastReadMessageId)
            {
                return;
            }

            await database.Updateable<ChatReadPositionRow>()
                .SetColumns(row => new ChatReadPositionRow
                {
                    LastReadMessageId = lastReadMessageId,
                    UpdatedUtc = nowUtc
                })
                .Where(row => row.ConversationId == conversationId && row.AccountId == accountId)
                .ExecuteCommandAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Failed to update the read position.", exception);
        }
    }

    public async Task<(long Low, long High)?> FindParticipantsAsync(
        long conversationId,
        CancellationToken cancellationToken)
    {
        if (conversationId <= 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var database = factory.Create();
        try
        {
            var row = await database.Queryable<ChatConversationRow>()
                .Where(entry => entry.ConversationId == conversationId)
                .SingleAsync();

            return row is null ? null : (row.LowAccountId, row.HighAccountId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Failed to read conversation participants.", exception);
        }
    }

    public async Task<int> CountRecentMessagesAsync(
        long accountId,
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return 0;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var database = factory.Create();
        try
        {
            return await database.Queryable<ChatMessageRow>()
                .Where(row => row.SenderAccountId == accountId && row.SentUtc > sinceUtc)
                .CountAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Failed to count recent messages.", exception);
        }
    }

    private static (long Low, long High) Canonical(long first, long second) =>
        first <= second ? (first, second) : (second, first);

    private static void SafeRollback(ISqlSugarClient database)
    {
        try
        {
            database.Ado.RollbackTran();
        }
        catch (Exception)
        {
            // 回滚失败时连接已经不可用，交给上层按存储故障处理即可。
        }
    }
}
