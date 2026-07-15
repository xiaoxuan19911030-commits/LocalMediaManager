<p align="center"><img src="assets/brand/lmm-logo-light.svg" width="560" alt="Local Media Manager"></p>

# Local Media Manager

Local Media Manager (LMM) is a modern, local-first desktop media manager built with Tauri 2, React, TypeScript, Material UI, Emotion, and a .NET 8 Bridge. The stable legacy WPF application remains independently installed and continues to use its own databases during the migration period.

Version 0.4.0 focuses on the modern media experience while preserving the established architecture boundaries:

1. The Tauri desktop shell starts independently of the WPF application.
2. The independent migration tool reads the legacy databases in SQLite `ReadOnly` mode, creates backups, builds Database v1 in a temporary file, validates it, and only then performs an atomic switch.
3. LMM reads only `D:\Local Media Manager Next Data\data\LocalMediaManager.db`; it does not use the WPF business database at runtime. The existing directory name is retained for upgrade compatibility.
4. Dashboard, the modern movie wall, movie details, global search, media libraries, and the task center all consume typed Bridge DTOs.
5. Cover streaming, details DTOs, and player launch operate through the Bridge rather than direct frontend database access.
6. The LMM brand source is maintained as SVG, generated in standard PNG/ICO sizes, and reused by the application and NSIS installer.

Version 0.4.0 introduces an information-first movie details layout, a shared animated movie card, Dashboard health and shortcuts, Search 2.0, Library, Actors, Tags, Metadata, Diagnostics, Tasks, Plugin foundations, and the AI Provider architecture placeholder. Motion respects the operating system reduced-motion preference and all colors come from the shared light/dark Material UI theme.

## Product documentation

- [Product vision](docs/PRODUCT_VISION.md)
- [Roadmap](docs/ROADMAP.md)
- [Changelog](docs/CHANGELOG.md)
- [TODO](docs/TODO.md)
- [UI design specification](docs/UI_DESIGN_SPEC.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Feature parity matrix](docs/migration/FEATURE_PARITY_MATRIX.md)

The Roadmap is the formal version-scope authority. Unfinished work belongs in TODO, released behavior belongs in Changelog, and legacy migration status belongs in the feature parity matrix.

## Development

```powershell
pnpm install
dotnet build backend/LocalMediaManager.Bridge/LocalMediaManager.Bridge.csproj
pnpm bridge:publish
pnpm migration:publish
pnpm tauri:dev
```

Create the complete Windows brand asset set with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\generate-brand-assets.ps1
```

Environment overrides:

- `LMM_LEGACY_ROOT`: legacy installed root, default `D:\Jvedio\Jvedio5.0`.
- `LMM_DATABASE_PATH`: LMM Database v1 path; default `D:\Local Media Manager Next Data\data\LocalMediaManager.db` in the Bridge.
- `LMM_CONFIG_DATABASE_PATH`: legacy settings source opened read-only during the settings migration phase.
- `LMM_NEXT_DATA_ROOT`: independent migration data root, default `D:\Local Media Manager Next Data`.
- `LMM_LEGACY_DATABASE_PATH` / `LMM_LEGACY_CONFIG_DATABASE_PATH`: optional source overrides used only by the independent migration tool.
- `LMM_IMAGE_ROOT`: image directory containing `CardCovers` and `SmallPic`.
- `LMM_PLAYER_PATH`: optional external player executable.
- `LMM_BRIDGE_PATH`: optional Bridge executable used by the Tauri host.

The stable WPF project and installation remain available and are not overwritten by LMM. Application binaries remain deployed to `D:\Local Media Manager Next` for upgrade compatibility; data, backups, and migration reports stay outside that installation directory.
