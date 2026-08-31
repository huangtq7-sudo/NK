# Repository tools

This directory contains deterministic repository automation and must never emit credentials.

- `CI/Invoke-ServerTests.ps1`: restores, builds, audits, and tests the .NET 10 server.
- `CI/Invoke-UnityTests.ps1`: validates Unity `2021.3.45f2c1` and runs EditMode/PlayMode tests.

CI usage and self-hosted Unity Runner requirements are documented in `Docs/CI.md`.
