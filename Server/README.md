# NARAKA server

The first release is a .NET 10 modular monolith. The solution preserves the legacy transport protocol and runtime behavior behind `Naraka.Server.LegacyNetworkV1`; domain and application projects never reference socket, AES, Protobuf, SqlSugar, or concrete storage code.

## Projects

- `Naraka.Server.Domain`: pure domain values and rules.
- `Naraka.Server.Application`: use-case contracts and module boundaries.
- `Naraka.Server.Infrastructure`: storage, time, telemetry, and external-service implementations.
- `Naraka.Server.LegacyNetworkV1`: adapter boundary for the frozen transport.
- `Naraka.Server.Host`: composition root and health endpoints.
- `Naraka.Server.ArchitectureTests`: dependency-direction tests.

The legacy source remains read-only at `D:\培训项目\7.21net\SimpleServer`. The Host runs the byte-compatible listener at `127.0.0.1:8011` for local development; connection strings are supplied only through `NARAKA_MYSQL_CONNECTION_STRING`.
