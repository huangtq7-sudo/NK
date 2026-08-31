using Naraka.Server.Application.Accounts;
using Naraka.Server.Application.Health;
using Naraka.Server.Application.Modules;
using Naraka.Server.Application.Networking;
using Naraka.Server.Application.Sessions;
using Naraka.Server.Infrastructure.Health;
using Naraka.Server.Infrastructure.Persistence;
using Naraka.Server.Infrastructure.Persistence.Accounts;
using Naraka.Server.Infrastructure.Security;
using Naraka.Server.Host;
using Naraka.Server.LegacyNetworkV1;
using Naraka.Server.LegacyNetworkV1.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IMySqlConnectionStringSource, EnvironmentMySqlConnectionStringSource>();
builder.Services.AddSingleton<SqlSugarClientFactory>();
builder.Services.AddSingleton<IAccountRepository, SqlSugarAccountRepository>();
builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<IAuthenticatedSessionRegistry, InMemoryAuthenticatedSessionRegistry>();
builder.Services.AddSingleton<LoginSessionService>();
builder.Services.AddSingleton<LegacySessionAccountResolver>();
builder.Services.AddSingleton<IDatabaseHealthProbe, MySqlDatabaseHealthProbe>();
builder.Services.AddSingleton<LegacyNetworkTransport>();
builder.Services.AddSingleton<ILegacyNetworkTransport>(services =>
    services.GetRequiredService<LegacyNetworkTransport>());
builder.Services.AddHostedService<LegacyNetworkHostedService>();

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "live",
    service = "Naraka.Server",
}));

app.MapGet(
    "/health/ready",
    async (IDatabaseHealthProbe database, ILegacyNetworkTransport transport, CancellationToken cancellationToken) =>
    {
        var databaseStatus = await database.CheckAsync(cancellationToken);
        var ready = databaseStatus.IsReady && transport.IsIntegrated;

        return Results.Json(
            new
            {
                status = ready ? "ready" : "not-ready",
                database = databaseStatus.Status,
                legacyNetwork = transport.CompatibilityContract,
            },
            statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    });

app.MapGet("/modules", () => Results.Ok(ModuleCatalog.Names));

app.Run();

public partial class Program;
