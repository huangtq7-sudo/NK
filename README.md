# NARAKA

Formal development workspace for the Unity 2021.3 LTS + URP game client and the .NET LTS server.

## Layout

- `NK/`: Unity client (`2021.3.45f2c1`).
- `Server/`: modular-monolith server and tests.
- `Shared/`: generated protocol/config contracts shared by build tooling.
- `Tools/`: repository automation.
- Root `NARAKA_*.md` files: current project design and engineering baselines.

The client business layer must remain modular MVC. The legacy network transport protocol and runtime behavior are frozen behind adapter interfaces.

Secrets must be supplied through local environment variables or deployment secret storage and must never be committed.
