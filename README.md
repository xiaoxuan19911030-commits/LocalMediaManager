# Local Media Manager Next

This is the isolated Next-generation desktop application built with Tauri 2, React, TypeScript, Material UI, Emotion, and a .NET 8 Bridge. The stable WPF application remains independently installed and continues to use its own legacy databases.

Version 0.2 establishes these boundaries:

1. The Tauri desktop shell starts independently of the WPF application.
2. The independent migration tool reads the legacy databases in SQLite `ReadOnly` mode, creates backups, builds Database v1 in a temporary file, validates it, and only then performs an atomic switch.
3. Next reads only `D:\Local Media Manager Next Data\data\LocalMediaManager.db`; it does not use the WPF business database at runtime.
4. The React frontend displays real paged/searchable media data and a seven-category read-only settings migration view through Bridge DTOs.
5. Cover streaming, details DTOs, and player launch operate through the Bridge rather than direct frontend database access.

## Development

```powershell
pnpm install
dotnet build backend/LocalMediaManager.Bridge/LocalMediaManager.Bridge.csproj
pnpm bridge:publish
pnpm migration:publish
pnpm tauri:dev
```

Environment overrides:

- `LMM_LEGACY_ROOT`: legacy installed root, default `D:\Jvedio\Jvedio5.0`.
- `LMM_DATABASE_PATH`: Next Database v1 path; default `D:\Local Media Manager Next Data\data\LocalMediaManager.db` in the Bridge.
- `LMM_CONFIG_DATABASE_PATH`: legacy settings source opened read-only during the settings migration phase.
- `LMM_NEXT_DATA_ROOT`: independent migration data root, default `D:\Local Media Manager Next Data`.
- `LMM_LEGACY_DATABASE_PATH` / `LMM_LEGACY_CONFIG_DATABASE_PATH`: optional source overrides used only by the independent migration tool.
- `LMM_IMAGE_ROOT`: image directory containing `CardCovers` and `SmallPic`.
- `LMM_PLAYER_PATH`: optional external player executable.
- `LMM_BRIDGE_PATH`: optional Bridge executable used by the Tauri host.

The stable WPF project and installation remain available and are not overwritten by Next. Application binaries are deployed to `D:\Local Media Manager Next`; data, backups, and migration reports stay outside that installation directory.
