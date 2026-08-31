namespace Naraka.Server.Infrastructure.Persistence;

public interface IMySqlConnectionStringSource
{
    string? GetConnectionString();
}
