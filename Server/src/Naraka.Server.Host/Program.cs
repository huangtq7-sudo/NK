using Naraka.Server.Application.Accounts;
using Naraka.Config;
using Naraka.Server.Application.Bootstrap;
using Naraka.Server.Application.Config;
using Naraka.Server.Application.Forge;
using Naraka.Server.Application.Gacha;
using Naraka.Server.Application.Health;
using Naraka.Server.Application.Lobby;
using Naraka.Server.Application.Inventory;
using Naraka.Server.Application.Modules;
using Naraka.Server.Application.Progression;
using Naraka.Server.Application.Networking;
using Naraka.Server.Application.Sessions;
using Naraka.Server.Application.Shop;
using Naraka.Server.Application.Social;
using Naraka.Server.Infrastructure.Config;
using Naraka.Server.Infrastructure.Health;
using Naraka.Server.Infrastructure.Persistence;
using Naraka.Server.Infrastructure.Persistence.Accounts;
using Naraka.Server.Infrastructure.Persistence.Forge;
using Naraka.Server.Infrastructure.Persistence.Gacha;
using Naraka.Server.Infrastructure.Persistence.Inventory;
using Naraka.Server.Infrastructure.Persistence.Lobby;
using Naraka.Server.Infrastructure.Persistence.Progression;
using Naraka.Server.Infrastructure.Persistence.Shop;
using Naraka.Server.Infrastructure.Persistence.Social;
using Naraka.Server.Infrastructure.Security;
using Naraka.Server.Host;
using Naraka.Server.LegacyNetworkV1;
using Naraka.Server.LegacyNetworkV1.Authentication;

var builder = WebApplication.CreateBuilder(args);

// 权威配置在建立任何服务之前加载。经济、锻造、抽奖与签到判定全部依赖它，
// 缺失或损坏时必须直接拒绝启动，而不是带着半份配置对外服务。
var gameConfigResult = new FileGameConfigSource().Load(GameConfig.RequiredSchemaVersion);
if (!gameConfigResult.IsSuccess)
{
    throw new InvalidOperationException(
        $"Game configuration could not be loaded ({gameConfigResult.Status}): {gameConfigResult.Message}");
}

var gameConfig = gameConfigResult.Config!;

var bootstrapSection = builder.Configuration.GetSection("Naraka:Bootstrap");
var configVersionManifest = ConfigVersionManifest.Create(
    bootstrapSection["ConfigVersion"],
    bootstrapSection["MinimumClientVersion"],
    bootstrapSection["MaximumClientVersion"],
    bootstrapSection["ProtocolVersion"],
    // 只声明本 Host 真正实现的能力，而不是整份登记表：声明一个未实现的能力
    // 会让客户端发送传输层不认识的协议。版本门禁匹配但 Host 不返回该字段时，
    // 客户端才会退回 P1.1-A 兼容模式；完整 P1 与旧 p0-config-1 由预检直接隔离。
    NarakaServerCapabilities.Implemented);

builder.Services.AddSingleton(configVersionManifest);
builder.Services.AddSingleton(gameConfig);
builder.Services.AddSingleton<IMySqlConnectionStringSource, EnvironmentMySqlConnectionStringSource>();
builder.Services.AddSingleton<SqlSugarClientFactory>();
builder.Services.AddSingleton<IAccountRepository, SqlSugarAccountRepository>();
builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<IAuthenticatedSessionRegistry, InMemoryAuthenticatedSessionRegistry>();
builder.Services.AddSingleton<LoginSessionService>();
builder.Services.AddSingleton<LegacySessionAccountResolver>();
builder.Services.AddSingleton<ILobbyAccountRepository, SqlSugarLobbyAccountRepository>();
builder.Services.AddSingleton<LobbyAccountService>();
builder.Services.AddSingleton<IAccountProfileRepository, SqlSugarAccountProfileRepository>();
builder.Services.AddSingleton<AccountProfileService>();
builder.Services.AddSingleton<IInventoryRepository, SqlSugarInventoryRepository>();
builder.Services.AddSingleton<ICurrencyLedgerRepository, SqlSugarCurrencyLedgerRepository>();
builder.Services.AddSingleton<InventoryService>();
builder.Services.AddSingleton<IShopRepository, SqlSugarShopRepository>();
builder.Services.AddSingleton<ShopService>();
builder.Services.AddSingleton<IWeaponRepository, SqlSugarWeaponRepository>();
builder.Services.AddSingleton<ForgeService>();
builder.Services.AddSingleton<IGachaRepository, SqlSugarGachaRepository>();
// 抽奖随机源始终是密码学安全随机数；客户端不参与任何一次抽取。
builder.Services.AddSingleton<IGachaRandom, CryptoGachaRandom>();
builder.Services.AddSingleton<GachaService>();
builder.Services.AddSingleton<SqlSugarProgressionRepository>();
builder.Services.AddSingleton<ISignInRepository>(services =>
    services.GetRequiredService<SqlSugarProgressionRepository>());
builder.Services.AddSingleton<IAchievementRepository>(services =>
    services.GetRequiredService<SqlSugarProgressionRepository>());
builder.Services.AddSingleton<SignInService>();
builder.Services.AddSingleton<AchievementService>();
builder.Services.AddSingleton<IRedDotRepository, SqlSugarRedDotRepository>();
builder.Services.AddSingleton<RedDotService>();
builder.Services.AddSingleton<ISocialRepository, SqlSugarSocialRepository>();
builder.Services.AddSingleton<IChatRepository, SqlSugarChatRepository>();
builder.Services.AddSingleton<SocialService>();
builder.Services.AddSingleton<IDatabaseHealthProbe, MySqlDatabaseHealthProbe>();
builder.Services.AddSingleton<ILegacyTransportDiagnostics, LoggingLegacyTransportDiagnostics>();
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

// 无敏感信息的配置自检端点，用于确认 Host 实际加载的是哪一份生成配置。
app.MapGet(
    "/config/version",
    (GameConfig config) => Results.Ok(new
    {
        configVersion = config.ConfigVersion,
        schemaVersion = config.SchemaVersion,
        catalogSha256 = config.CatalogSha256,
    }));

app.MapGet(
    "/bootstrap/config-version",
    (ConfigVersionManifest manifest) => Results.Ok(manifest));

app.Run();

public partial class Program;
