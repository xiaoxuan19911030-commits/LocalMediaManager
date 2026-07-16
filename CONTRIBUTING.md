# Contributing to Local Media Manager

Thank you for helping improve LMM. Please discuss large product or schema changes in an issue before implementation.

## Architecture requirements

1. React pages are presentation-only and must not access SQLite, the filesystem or system commands.
2. Business operations go through typed Bridge DTOs and services.
3. Database changes use ordered checksummed migrations.
4. Long-running operations use the Tasks system.
5. Dangerous writes require preview, confirmation, audit/backup and a rollback strategy.
6. New UI reuses the shared Material UI theme and supports light and dark themes.

Read `AI_RULES.md`, `AGENTS.md`, `PROJECT_CONTEXT.md`, `docs/ARCHITECTURE.md`, `docs/UI_DESIGN_SPEC.md` and `docs/TEST_PLAN.md` before making product changes. These rules apply equally to human contributors, AI agents, accounts, models and API providers.

## Local verification

```powershell
pnpm install --frozen-lockfile
pnpm build:web
dotnet test backend/LocalMediaManager.Bridge.Tests/LocalMediaManager.Bridge.Tests.csproj -c Release
cargo check --manifest-path src-tauri/Cargo.toml
```

Pull requests should explain the user-facing outcome, Bridge/Migration impact, data risk, tests performed and rollback path.

## Stable main policy

Development work stays on a dedicated local Sprint branch until the complete lifecycle has passed: `Planning → Design → Develop → Self Test → Smoke Test → Freeze → Release`. Only a buildable, runnable and publishable release candidate with verification and rollback evidence may be pushed to GitHub `main`.

