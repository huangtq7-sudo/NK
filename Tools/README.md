# Repository tools

This directory contains deterministic repository automation and must never emit credentials.

- `CI/Invoke-ServerTests.ps1`: restores, builds, audits, and tests the .NET 10 server.
- `CI/Invoke-UnityTests.ps1`: validates Unity `2021.3.45f2c1` and runs EditMode/PlayMode tests.
- `Deployment/New-NarakaServerRelease.ps1`: runs release gates and creates a versioned, hashed Windows Host and migrator bundle.
- `Deployment/Cloud/Deploy-NarakaServerRelease.ps1`: validates a clean release manifest, migrates sequentially, swaps one Host process, and rolls back the binary directory on failure.
- `Deployment/Cloud/Start-NarakaServerSupervised.ps1`: low-memory delayed-restart launcher template for the existing cloud scheduled task; it is not installed merely by being committed.

CI usage and self-hosted Unity Runner requirements are documented in `Docs/CI.md`. The milestone-batched cloud process is documented in `Docs/Deployment/server-release-process.md`.
