# Local Media Manager Next

This is an isolated migration prototype for replacing the WPF presentation layer with Tauri 2, React, TypeScript, Material UI, and Emotion while preserving the existing Local Media Manager data and C# behavior as the compatibility baseline.

The prototype is intentionally read-only with respect to the existing SQLite database. It proves four boundaries before broad migration begins:

1. The Tauri desktop shell starts independently of the WPF application.
2. A .NET 8 Backend Bridge opens the existing database with SQLite `ReadOnly` mode.
3. The React frontend displays the real video count and a minimal real video list.
4. The Bridge can hand a selected existing video path to the configured or system player without updating database history during this prototype phase.

## Development

```powershell
pnpm install
dotnet build backend/LocalMediaManager.Bridge/LocalMediaManager.Bridge.csproj
pnpm bridge:publish
pnpm tauri:dev
```

Environment overrides:

- `LMM_LEGACY_ROOT`: legacy installed root, default `D:\Jvedio\Jvedio5.0`.
- `LMM_DATABASE_PATH`: exact existing SQLite database path.
- `LMM_IMAGE_ROOT`: image directory containing `CardCovers` and `SmallPic`.
- `LMM_PLAYER_PATH`: optional external player executable.
- `LMM_BRIDGE_PATH`: optional Bridge executable used by the Tauri host.

The stable WPF project remains in the repository and is not replaced by this prototype.

