# NARAKA server

The first release is a .NET 10 modular monolith. The solution preserves the legacy transport protocol and runtime behavior behind `Naraka.Server.LegacyNetworkV1`; domain and application projects never reference socket, AES, Protobuf, SqlSugar, or concrete storage code.

## Projects

- `Naraka.Server.Domain`: pure domain values and rules.
- `Naraka.Server.Application`: use-case contracts and module boundaries.
- `Naraka.Server.Infrastructure`: storage, time, telemetry, and external-service implementations.
- `Naraka.Server.LegacyNetworkV1`: adapter boundary for the frozen transport.
- `Naraka.Server.Host`: composition root and health endpoints.
- `Naraka.Server.ArchitectureTests`: dependency-direction tests.

The legacy source remains read-only at `D:\培训项目\7.21net\SimpleServer`. The Host runs the byte-compatible listener at `127.0.0.1:8011`; connection strings are supplied only through `NARAKA_MYSQL_CONNECTION_STRING`.

## P0 bootstrap version endpoint

`GET /bootstrap/config-version` exposes the compatibility metadata required before account registration or login. Values are configured under `Naraka:Bootstrap` and the Host refuses to start if a value is empty, longer than 64 characters, or if the client-version range is invalid.

The P0 baseline used `ConfigVersion=p0-config-1`. The complete P1 release uses `ConfigVersion=p1-config-1`, `MinimumClientVersion=0.1`, `MaximumClientVersion=0.1`, and `ProtocolVersion=LegacyNetworkV1`. This endpoint does not change the frozen socket transport. Direct remote clients must use HTTPS; the single-developer cloud environment instead forwards the loopback endpoint through an encrypted SSH tunnel as defined by ADR-0006.

## Development cloud deployment

The validated development topology runs the self-contained Windows Host and MySQL 5.7.26 on one Windows Server 2016 Datacenter host. MySQL, bootstrap HTTP, and `LegacyNetworkV1` remain bound to loopback; the Unity client reaches ports 5222 and 8011 through a manually started, key-only SSH local forward. The cloud Host and database do not depend on the local tunnel and continue running when the developer PC is offline. This topology is not a production deployment.

The secret-free operations and acceptance record is in `Docs/Deployment/aliyun-windows-development.md`.

## P3 expedition foundation

P3 starts with a pure domain aggregate under `Naraka.Server.Domain.Expeditions`. It owns temporary
monster-drop assets, death cleanup, normal/connection-loss settlement, and replay of the first
successful settlement summary. `ExpeditionService` is the authenticated application boundary; it
creates stable expedition/map identifiers, accepts drops only from trusted server modules, and drives
an atomic `IExpeditionRepository` port without referencing SqlSugar or the frozen transport. The
transactional MySQL implementation stays in Infrastructure; see ADR-0021.

The P3 application-message adapter is implemented above the frozen transport with the append-only
pair `MsgExpeditionRequest=65` and `MsgExpeditionResponse=66`. The authenticated connection is the
only account-identity source, and the request contract has no account, asset, quantity, or drop-list
claim. Client disconnects settle through the same idempotent application service before session
removal, while Host shutdown preserves the last active snapshot for recovery. The matching Unity
MVC integration now keeps the authenticated connection, minimal account session, capability set,
and expedition snapshot in the persistent application root. A matching Host advertises the
`expedition` Bootstrap capability; clients connected to an older Host block expedition entry and do
not send protocols 65/66.

The MySQL implementation now lives in `SqlSugarExpeditionRepository`, backed by additive migration
`0010_p3_expeditions.sql`. A nullable unique active-account key enforces one active expedition per
account. Settlement grants currency with an immutable ledger entry, fills available inventory space,
stores overflow in account mail, persists the first settlement summary, and closes the expedition in
one transaction. `Naraka.Server.ExpeditionSmoke` creates and removes its own test account to verify
that path against a reachable development database. Dry-run and smoke commands are:

```powershell
dotnet run --project Server/tools/Naraka.Server.DatabaseMigrator -- --dry-run
dotnet run --project Server/tools/Naraka.Server.DatabaseMigrator
dotnet run --project Server/tools/Naraka.Server.ExpeditionSmoke
```

The connection string remains environment-only. Do not pass it on the command line or write it into
repository files. On 2026-10-08, a loopback-only MySQL 5.7.26 development instance applied and
verified migrations 0001-0010 twice and the self-cleaning expedition smoke passed death cleanup,
atomic settlement, inventory overflow, currency ledger, and replay. That run exposed and fixed
database replay ordering and `DATETIME(6)` precision normalization. Local start/stop and acceptance
commands are documented in `Docs/Deployment/local-mysql57-development.md`.

The adapter regression suite uses real encrypted loopback sockets and covers authentication, start,
active lookup, death, return, replay, disconnect settlement, and Host-shutdown preservation. On
2026-10-08 the targeted LegacyNetworkV1 suite passed 64/64 and the complete server CI passed 395/395
with a zero-warning Release build. A separate Host composition smoke returned HTTP 200 for live and
Bootstrap and opened the isolated legacy listener. Readiness was intentionally not recorded as passed
because port 3306 was occupied by an unrelated phpStudy MySQL instance; that process was left untouched.
