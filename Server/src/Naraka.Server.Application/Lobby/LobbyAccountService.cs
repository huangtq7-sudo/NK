namespace Naraka.Server.Application.Lobby;

/// <summary>
/// Reads the lobby summary for an already authenticated account. The caller supplies the account id
/// obtained from the connection session; ADR-0007 forbids trusting a client supplied account id here.
/// </summary>
public sealed class LobbyAccountService(ILobbyAccountRepository repository)
{
    private readonly ILobbyAccountRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<LobbyAccountSummaryResult> GetSummaryAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        if (accountId <= 0)
        {
            return LobbyAccountSummaryResult.Failed(LobbyAccountSummaryStatus.InvalidRequest);
        }

        try
        {
            var record = await _repository.FindProgressionAsync(accountId, cancellationToken);
            if (record is null)
            {
                return LobbyAccountSummaryResult.Failed(LobbyAccountSummaryStatus.NotFound);
            }

            // A row that violates the invariants is a data fault, not a client error: never pass it on.
            return LobbyAccountSummary.TryCreate(
                record.AccountLevel,
                record.Copper,
                record.Silk,
                record.Gold,
                out var summary) && summary is not null
                ? LobbyAccountSummaryResult.Success(summary)
                : LobbyAccountSummaryResult.Failed(LobbyAccountSummaryStatus.InternalError);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LobbyAccountStorageException)
        {
            return LobbyAccountSummaryResult.Failed(LobbyAccountSummaryStatus.DatabaseUnavailable);
        }
        catch (Exception)
        {
            return LobbyAccountSummaryResult.Failed(LobbyAccountSummaryStatus.InternalError);
        }
    }
}
