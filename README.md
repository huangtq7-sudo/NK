# NARAKA

Formal development workspace for the Unity 2021.3 LTS + URP game client and the .NET LTS server.

## Layout

- `NK/`: Unity client (`2021.3.45f2c1`).
- `Server/`: modular-monolith server and tests.
- `Shared/`: generated protocol/config contracts shared by build tooling.
- `Tools/`: repository automation.
- `Docs/Deployment/`: secret-free deployment and operations records.
- `Docs/AI/`: multi-model collaboration rules and reusable prompts.
- Root `NARAKA_*.md` files: current project design and engineering baselines.

The client business layer must remain modular MVC. The legacy network transport protocol and runtime behavior are frozen behind adapter interfaces.

Secrets must be supplied through local environment variables or deployment secret storage and must never be committed.

## Continuous integration

- `Server CI` runs the .NET 10 Release build, vulnerability gate, and server tests on a GitHub-hosted Windows runner.
- `Unity Client CI` runs EditMode and PlayMode tests on a trusted Windows runner with Unity `2021.3.45f2c1`.
- Local and runner setup instructions are in `Docs/CI.md`.
