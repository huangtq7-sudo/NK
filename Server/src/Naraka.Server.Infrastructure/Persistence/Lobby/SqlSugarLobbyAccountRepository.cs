using Naraka.Server.Application.Lobby;

namespace Naraka.Server.Infrastructure.Persistence.Lobby;

public sealed class SqlSugarLobbyAccountRepository(SqlSugarClientFactory factory) : ILobbyAccountRepository
{
    public async Task<LobbyAccountProgressionRecord?> FindProgressionAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var database = factory.Create();
            var row = await database.Queryable<AccountProgressionRow>()
                .Where(progression => progression.AccountId == accountId)
                .SingleAsync();
            cancellationToken.ThrowIfCancellationRequested();

            return row is null
                ? null
                : new LobbyAccountProgressionRecord(row.AccountLevel, row.Copper, row.Silk, row.Gold);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The Application layer maps this to a stable DatabaseUnavailable code without leaking
            // the connection string or the provider exception text to the client.
            throw new LobbyAccountStorageException("Account progression read failed.", exception);
        }
    }
}
