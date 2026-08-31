namespace Naraka.Server.Application.Health;

public interface IDatabaseHealthProbe
{
    ValueTask<DatabaseHealthSnapshot> CheckAsync(CancellationToken cancellationToken);
}
