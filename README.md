<p align="center">
  <img src="assets/brand/lmm-logo-light.svg" width="560" alt="Local Media Manager">
</p>

<p align="center">
  A modern, local-first desktop media manager for Windows.
</p>

<p align="center">
  <a href="https://github.com/xiaoxuan19911030-commits/LocalMediaManager/releases"><img alt="Release" src="https://img.shields.io/github/v/release/xiaoxuan19911030-commits/LocalMediaManager?include_prereleases&style=flat-square"></a>
  <a href="https://github.com/xiaoxuan19911030-commits/LocalMediaManager/actions/workflows/ci.yml"><img alt="CI" src="https://img.shields.io/github/actions/workflow/status/xiaoxuan19911030-commits/LocalMediaManager/ci.yml?branch=main&style=flat-square&label=build"></a>
  <img alt="Windows" src="https://img.shields.io/badge/platform-Windows-2563eb?style=flat-square">
  <a href="LICENSE"><img alt="GPL-3.0-only" src="https://img.shields.io/badge/license-GPL--3.0--only-7c3aed?style=flat-square"></a>
</p>

## About

Local Media Manager (LMM) is an independent desktop product for organizing and exploring local media libraries. It combines a Tauri 2 desktop shell, React and Material UI presentation, a typed .NET 8 Bridge, and a checksummed SQLite migration system.

The product is local-first: media files, metadata, ratings, favorites, tags and playback history stay under the user's control. React never accesses SQLite or the filesystem directly; all business operations pass through authenticated Bridge APIs.

> Current release: **0.4.1 — Legacy Feature Migration Part 1**
> Development status: Sprint 0.4.2 is paused while the public repository is prepared.

## Highlights

- Modern movie wall, dashboard, global search and information-focused detail pages.
- Favorites, ratings, custom tags, actor relationships and playback history with restart persistence.
- Independent Database v1 with checksummed migrations, integrity validation and backups.
- Authenticated loopback Bridge boundary with typed DTOs and consistent errors.
- Shared Material UI design system with light/dark themes and responsive desktop layouts.
- Unified Tasks foundation for scanning, metadata, images and future long-running work.
- Provider-neutral AI architecture reserved for a later release; AI is not part of the current runtime.

## Architecture

```text
Tauri 2 / React / Material UI
              │
              │ typed authenticated Bridge API
              ▼
LocalMediaManager.Bridge (.NET 8)
              │
              │ migrations + transactional services
              ▼
       SQLite Database v1
```

The legacy WPF project is a compatibility and business-rule reference only. Its UI is not copied into LMM, and the stable legacy installation is never overwritten by Next builds.

## Documentation

- [Product vision](docs/PRODUCT_VISION.md)
- [Roadmap](docs/ROADMAP.md)
- [Architecture](docs/ARCHITECTURE.md)
- [UI design specification](docs/UI_DESIGN_SPEC.md)
- [Feature parity matrix](docs/migration/FEATURE_PARITY_MATRIX.md)
- [Test plan](docs/TEST_PLAN.md)
- [Changelog](docs/CHANGELOG.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)

## Development

### Requirements

- Windows 10/11 x64
- Node.js and pnpm 11
- .NET 8 SDK
- Rust stable toolchain
- WebView2 Runtime

### Start locally

```powershell
pnpm install --frozen-lockfile
pnpm bridge:publish
pnpm migration:publish
pnpm tauri:dev
```

### Validate

```powershell
pnpm build:web
dotnet test backend/LocalMediaManager.Bridge.Tests/LocalMediaManager.Bridge.Tests.csproj -c Release
cargo check --manifest-path src-tauri/Cargo.toml
```

### Build the Windows installer

```powershell
pnpm bridge:publish
pnpm migration:publish
pnpm tauri:build
```

The NSIS installer is generated under `src-tauri/target/release/bundle/nsis/`.

## Data safety

- Database schema changes use ordered, checksummed Migration files.
- Dangerous operations require impact preview and confirmation.
- User-authored ratings, favorites, tags, notes and selected images take priority over automated sources.
- Source media is never deleted by metadata or UI operations.
- Cloud AI and external providers are disabled unless deliberately implemented and enabled in a future release.

## License and attribution

Local Media Manager is licensed under [GPL-3.0-only](LICENSE). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for upstream architecture attribution and dependency notices.
