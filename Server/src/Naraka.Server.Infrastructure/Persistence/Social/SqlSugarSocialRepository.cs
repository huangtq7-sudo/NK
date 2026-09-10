using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Social;
using Naraka.Server.Infrastructure.Persistence.Progression;
using SqlSugar;

namespace Naraka.Server.Infrastructure.Persistence.Social;

/// <summary>
/// 好友、申请与屏蔽的持久化。
///
/// 每一个写操作都在一个事务里完成全部相关行：接受申请要同时删除申请并写两行好友关系，
/// 屏蔽要同时写屏蔽行、删除好友关系与两个方向的申请。分步写会留下"只删了一半"的中间状态，
/// 而那种状态在界面上表现为一个删不掉也加不回来的好友。
/// </summary>
public sealed class SqlSugarSocialRepository(SqlSugarClientFactory factory) : ISocialRepository
{
    public async Task<SocialPlayer?> FindByAccountIdAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await ReadAsync(async database =>
        {
            var profile = await database.Queryable<PlayerProfileRow>()
                .Where(row => row.AccountId == accountId)
                .SingleAsync();

            return profile is null ? null : await ToPlayerAsync(database, profile);
        });
    }

    public async Task<SocialPlayer?> FindByDisplayNameAsync(
        string displayName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(displayName))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await ReadAsync(async database =>
        {
            var profile = await database.Queryable<PlayerProfileRow>()
                .Where(row => row.DisplayName == displayName)
                .FirstAsync();

            return profile is null ? null : await ToPlayerAsync(database, profile);
        });
    }

    public async Task<IReadOnlyList<SocialPlayer>> ListFriendsAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<SocialPlayer>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await ReadAsync(async database =>
        {
            var ids = await database.Queryable<AccountFriendRow>()
                .Where(row => row.AccountId == accountId)
                .Select(row => row.FriendAccountId)
                .ToListAsync();

            return await ResolvePlayersAsync(database, ids);
        });
    }

    public async Task<IReadOnlyList<FriendRequestEntry>> ListIncomingRequestsAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<FriendRequestEntry>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await ReadAsync(async database =>
        {
            var rows = await database.Queryable<AccountFriendRequestRow>()
                .Where(row => row.TargetAccountId == accountId)
                .ToListAsync();

            var players = await ResolvePlayersAsync(
                database, rows.Select(row => row.RequesterAccountId).ToList());
            var byId = players.ToDictionary(player => player.AccountId);

            var entries = new List<FriendRequestEntry>(rows.Count);
            foreach (var row in rows.OrderBy(row => row.CreatedUtc))
            {
                if (byId.TryGetValue(row.RequesterAccountId, out var player))
                {
                    entries.Add(new FriendRequestEntry(player, row.CreatedUtc));
                }
            }

            return (IReadOnlyList<FriendRequestEntry>)entries;
        });
    }

    public async Task<IReadOnlyList<long>> ListOutgoingRequestTargetsAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<long>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await ReadAsync(async database =>
            (IReadOnlyList<long>)(await database.Queryable<AccountFriendRequestRow>()
                .Where(row => row.RequesterAccountId == accountId)
                .Select(row => row.TargetAccountId)
                .ToListAsync()).ToArray());
    }

    public async Task<IReadOnlyList<SocialPlayer>> ListBlockedAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return Array.Empty<SocialPlayer>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await ReadAsync(async database =>
        {
            var ids = await database.Queryable<AccountBlockRow>()
                .Where(row => row.AccountId == accountId)
                .Select(row => row.BlockedAccountId)
                .ToListAsync();

            return await ResolvePlayersAsync(database, ids);
        });
    }

    public async Task<bool> IsFriendAsync(
        long accountId,
        long otherAccountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || otherAccountId <= 0)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await ReadAsync(async database => await database.Queryable<AccountFriendRow>()
            .Where(row => row.AccountId == accountId && row.FriendAccountId == otherAccountId)
            .AnyAsync());
    }

    public async Task<bool> IsBlockedEitherWayAsync(
        long accountId,
        long otherAccountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0 || otherAccountId <= 0)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await ReadAsync(async database => await database.Queryable<AccountBlockRow>()
            .Where(row =>
                (row.AccountId == accountId && row.BlockedAccountId == otherAccountId) ||
                (row.AccountId == otherAccountId && row.BlockedAccountId == accountId))
            .AnyAsync());
    }

    public async Task<FriendRequestOutcome> TrySendRequestAsync(
        long requesterAccountId,
        long targetAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var blocked = await database.Queryable<AccountBlockRow>()
                .Where(row =>
                    (row.AccountId == requesterAccountId && row.BlockedAccountId == targetAccountId) ||
                    (row.AccountId == targetAccountId && row.BlockedAccountId == requesterAccountId))
                .AnyAsync();
            if (blocked)
            {
                database.Ado.RollbackTran();
                return FriendRequestOutcome.Blocked;
            }

            var alreadyFriends = await database.Queryable<AccountFriendRow>()
                .Where(row => row.AccountId == requesterAccountId &&
                              row.FriendAccountId == targetAccountId)
                .AnyAsync();
            if (alreadyFriends)
            {
                database.Ado.RollbackTran();
                return FriendRequestOutcome.AlreadyFriends;
            }

            // 对方已经申请过自己：直接成为好友，而不是让两条申请互相等待。
            var reciprocal = await database.Queryable<AccountFriendRequestRow>()
                .Where(row => row.RequesterAccountId == targetAccountId &&
                              row.TargetAccountId == requesterAccountId)
                .AnyAsync();
            if (reciprocal)
            {
                await database.Deleteable<AccountFriendRequestRow>()
                    .Where(row => row.RequesterAccountId == targetAccountId &&
                                  row.TargetAccountId == requesterAccountId)
                    .ExecuteCommandAsync();
                await InsertFriendPairAsync(database, requesterAccountId, targetAccountId, nowUtc);
                database.Ado.CommitTran();
                return FriendRequestOutcome.AutoAccepted;
            }

            var alreadyRequested = await database.Queryable<AccountFriendRequestRow>()
                .Where(row => row.RequesterAccountId == requesterAccountId &&
                              row.TargetAccountId == targetAccountId)
                .AnyAsync();
            if (alreadyRequested)
            {
                database.Ado.RollbackTran();
                return FriendRequestOutcome.AlreadyRequested;
            }

            await database.Insertable(new AccountFriendRequestRow
            {
                RequesterAccountId = requesterAccountId,
                TargetAccountId = targetAccountId,
                CreatedUtc = nowUtc
            }).ExecuteCommandAsync();

            database.Ado.CommitTran();
            return FriendRequestOutcome.Applied;
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ProgressionStorageException("Failed to send a friend request.", exception);
        }
    }

    public async Task<bool> TryAcceptRequestAsync(
        long targetAccountId,
        long requesterAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var removed = await database.Deleteable<AccountFriendRequestRow>()
                .Where(row => row.RequesterAccountId == requesterAccountId &&
                              row.TargetAccountId == targetAccountId)
                .ExecuteCommandAsync();
            if (removed == 0)
            {
                database.Ado.RollbackTran();
                return false;
            }

            await InsertFriendPairAsync(database, targetAccountId, requesterAccountId, nowUtc);
            database.Ado.CommitTran();
            return true;
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ProgressionStorageException("Failed to accept a friend request.", exception);
        }
    }

    public async Task<bool> TryRejectRequestAsync(
        long targetAccountId,
        long requesterAccountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await WriteAsync(async database => await database.Deleteable<AccountFriendRequestRow>()
            .Where(row => row.RequesterAccountId == requesterAccountId &&
                          row.TargetAccountId == targetAccountId)
            .ExecuteCommandAsync() > 0);
    }

    public async Task<bool> TryRemoveFriendAsync(
        long accountId,
        long friendAccountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            // 成对删除：只删一边会留下一条只有对方看得见的好友。
            var removed = await database.Deleteable<AccountFriendRow>()
                .Where(row =>
                    (row.AccountId == accountId && row.FriendAccountId == friendAccountId) ||
                    (row.AccountId == friendAccountId && row.FriendAccountId == accountId))
                .ExecuteCommandAsync();

            database.Ado.CommitTran();
            return removed > 0;
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ProgressionStorageException("Failed to remove a friend.", exception);
        }
    }

    public async Task TryBlockAsync(
        long accountId,
        long blockedAccountId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var database = factory.Create();
        try
        {
            database.Ado.BeginTran();

            var exists = await database.Queryable<AccountBlockRow>()
                .Where(row => row.AccountId == accountId && row.BlockedAccountId == blockedAccountId)
                .AnyAsync();
            if (!exists)
            {
                await database.Insertable(new AccountBlockRow
                {
                    AccountId = accountId,
                    BlockedAccountId = blockedAccountId,
                    CreatedUtc = nowUtc
                }).ExecuteCommandAsync();
            }

            // 屏蔽的同时清掉既有关系与两个方向的申请：否则被屏蔽的人仍然留在好友列表里。
            await database.Deleteable<AccountFriendRow>()
                .Where(row =>
                    (row.AccountId == accountId && row.FriendAccountId == blockedAccountId) ||
                    (row.AccountId == blockedAccountId && row.FriendAccountId == accountId))
                .ExecuteCommandAsync();

            await database.Deleteable<AccountFriendRequestRow>()
                .Where(row =>
                    (row.RequesterAccountId == accountId && row.TargetAccountId == blockedAccountId) ||
                    (row.RequesterAccountId == blockedAccountId && row.TargetAccountId == accountId))
                .ExecuteCommandAsync();

            database.Ado.CommitTran();
        }
        catch (OperationCanceledException)
        {
            SafeRollback(database);
            throw;
        }
        catch (Exception exception)
        {
            SafeRollback(database);
            throw new ProgressionStorageException("Failed to block a player.", exception);
        }
    }

    public async Task<bool> TryUnblockAsync(
        long accountId,
        long blockedAccountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await WriteAsync(async database => await database.Deleteable<AccountBlockRow>()
            .Where(row => row.AccountId == accountId && row.BlockedAccountId == blockedAccountId)
            .ExecuteCommandAsync() > 0);
    }

    private static async Task InsertFriendPairAsync(
        ISqlSugarClient database,
        long accountId,
        long friendAccountId,
        DateTime nowUtc)
    {
        await database.Insertable(new List<AccountFriendRow>
        {
            new()
            {
                AccountId = accountId, FriendAccountId = friendAccountId, CreatedUtc = nowUtc
            },
            new()
            {
                AccountId = friendAccountId, FriendAccountId = accountId, CreatedUtc = nowUtc
            }
        }).ExecuteCommandAsync();
    }

    private static async Task<SocialPlayer> ToPlayerAsync(
        ISqlSugarClient database,
        PlayerProfileRow profile)
    {
        var avatar = await database.Queryable<AccountProfileRow>()
            .Where(row => row.AccountId == profile.AccountId)
            .Select(row => row.AvatarId)
            .FirstAsync();

        return new SocialPlayer(profile.AccountId, profile.DisplayName, avatar ?? string.Empty);
    }

    private static async Task<IReadOnlyList<SocialPlayer>> ResolvePlayersAsync(
        ISqlSugarClient database,
        List<long> accountIds)
    {
        if (accountIds.Count == 0)
        {
            return Array.Empty<SocialPlayer>();
        }

        var profiles = await database.Queryable<PlayerProfileRow>()
            .Where(row => accountIds.Contains(row.AccountId))
            .ToListAsync();

        var avatars = (await database.Queryable<AccountProfileRow>()
                .Where(row => accountIds.Contains(row.AccountId))
                .ToListAsync())
            .ToDictionary(row => row.AccountId, row => row.AvatarId);

        return profiles
            .Select(profile => new SocialPlayer(
                profile.AccountId,
                profile.DisplayName,
                avatars.TryGetValue(profile.AccountId, out var avatar) ? avatar : string.Empty))
            .OrderBy(player => player.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<T> ReadAsync<T>(Func<ISqlSugarClient, Task<T>> read)
    {
        using var database = factory.Create();
        try
        {
            return await read(database);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Failed to read social state.", exception);
        }
    }

    private async Task<T> WriteAsync<T>(Func<ISqlSugarClient, Task<T>> write)
    {
        using var database = factory.Create();
        try
        {
            return await write(database);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProgressionStorageException("Failed to write social state.", exception);
        }
    }

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
