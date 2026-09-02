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

The local P0 defaults are `ConfigVersion=p0-config-1`, `MinimumClientVersion=0.1`, `MaximumClientVersion=0.1`, and `ProtocolVersion=LegacyNetworkV1`. This endpoint does not change the frozen socket transport. Direct remote clients must use HTTPS; the single-developer cloud environment instead forwards the loopback endpoint through an encrypted SSH tunnel as defined by ADR-0006.

## Development cloud deployment

The validated development topology runs the self-contained Windows Host and MySQL 5.7.26 on one Windows Server 2016 Datacenter host. MySQL, bootstrap HTTP, and `LegacyNetworkV1` remain bound to loopback; the Unity client reaches ports 5222 and 8011 through a manually started, key-only SSH local forward. The cloud Host and database do not depend on the local tunnel and continue running when the developer PC is offline. This topology is not a production deployment.

The secret-free operations and acceptance record is in `Docs/Deployment/aliyun-windows-development.md`.
