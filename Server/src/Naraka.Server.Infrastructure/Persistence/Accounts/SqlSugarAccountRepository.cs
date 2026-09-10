using Naraka.Server.Application.Accounts;
using Naraka.Server.Infrastructure.Persistence.Lobby;

namespace Naraka.Server.Infrastructure.Persistence.Accounts;

public sealed class SqlSugarAccountRepository(SqlSugarClientFactory factory) : IAccountRepository
{
    public async Task<AccountCredentialRecord?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        cancellationToken.ThrowIfCancellationRequested();

        using var database = factory.Create();
        var row = await database.Queryable<AccountRow>()
            .Where(account => account.Username == username)
            .SingleAsync();
        cancellationToken.ThrowIfCancellationRequested();

        return row is null
            ? null
            : new AccountCredentialRecord(
                row.AccountId,
                row.Username,
                row.PasswordHash,
                row.PasswordSalt,
                row.PasswordParameters,
                row.Status);
    }

    public async Task<long> CreateAsync(CreateAccountRecord account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        cancellationToken.ThrowIfCancellationRequested();

        var row = new AccountRow
        {
            Username = account.Username,
            PasswordHash = account.PasswordHash,
            PasswordSalt = account.PasswordSalt,
            PasswordParameters = account.PasswordParameters,
            Status = 0,
            CreatedUtc = DateTime.UtcNow
        };

        using var database = factory.Create();
        try
        {
            // A new account must start with its P1 progression row, otherwise the first lobby
            // read would report NotFound. Both inserts commit together.
            database.Ado.BeginTran();
            var accountId = await database.Insertable(row).ExecuteReturnBigIdentityAsync();
            var now = DateTime.UtcNow;
            await database.Insertable(new AccountProgressionRow
            {
                AccountId = accountId,
                AccountLevel = 1,
                Copper = 0,
                Silk = 0,
                Gold = 0,
                CreatedUtc = now,
                UpdatedUtc = now
            }).ExecuteCommandAsync();
            database.Ado.CommitTran();
            cancellationToken.ThrowIfCancellationRequested();
            return accountId;
        }
        catch
        {
            database.Ado.RollbackTran();
            throw;
        }
    }
}
