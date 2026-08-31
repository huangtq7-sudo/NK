using Naraka.Server.Application.Health;
using Naraka.Server.Application.Modules;
using Naraka.Server.Application.Networking;
using Naraka.Server.Infrastructure.Health;
using Naraka.Server.LegacyNetworkV1;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IDatabaseHealthProbe, EnvironmentDatabaseHealthProbe>();
builder.Services.AddSingleton<ILegacyNetworkTransport, LegacyNetworkTransport>();

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
