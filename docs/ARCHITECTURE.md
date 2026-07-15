# Architecture validation

## Upstream baseline

The inspected Clash Verge Rev development baseline uses React 19, Material UI 9, Emotion, React Router, Vite 8, Tauri 2.11, and Rust. Its reusable infrastructure is the desktop window lifecycle, theme model, routed layout, side navigation, Material UI component conventions, loading/error feedback, responsive composition, and internationalization boundary.

Proxy-specific frontend pages, Mihomo APIs, proxy profiles, rules, connections, traffic, subscription state, DNS, TUN, system proxy integration, updater behavior tied to the original product, and their Rust crates are excluded rather than renamed.

## Selected Bridge boundary

LMM uses an independent .NET 8 loopback HTTP process.

```text
Tauri 2 / React / Material UI
          |
          | http://127.0.0.1:47831
          v
LocalMediaManager.Bridge (.NET 8)
          |
          | SQLite Mode=ReadOnly
          v
LocalMediaManager.db + existing media/image paths
```

HTTP provides a debuggable, typed JSON boundary that can be exercised independently from Tauri and does not require duplicating a pipe client in Rust and TypeScript. It binds only to loopback. Authentication and per-session tokens are required before any write endpoint is introduced.

The Bridge exposes health, dashboard, global search, libraries, tasks, paged/searchable/sortable media DTOs, movie details, cover streaming, player launch, and settings DTOs. React never accesses SQLite directly. Runtime media access uses only Database v1.

## Product phase boundary

Version 0.3.0 adds product-facing routes without changing the foundation: Dashboard, movie wall, movie details, global search, media libraries, and task center. Pages contain presentation and interaction only; SQL remains in the Bridge product reader. Database corrections and upgrades remain checksummed migrations.

Version 0.4.0 treats Bridge, Database, Migration, Material UI, and Tauri as stable infrastructure. Experience work is implemented through shared React presentation components and existing DTOs; it does not introduce page-level persistence or alternate business paths.

The official brand is Local Media Manager (LMM). Brand SVG sources live under `assets/brand`, while generated PNG, ICO, application, and NSIS assets are derived from those sources.

## Existing C# reuse assessment

- Reuse as compatibility authority: SQLite schema/mappers, configuration paths, scanning, imports, metadata/NFO, scraper integration, image/cache rules, tags, actors, favorites, ratings, history, player selection, plugins, and server resources.
- Extract after the boundary stabilizes: pure data contracts, query services, path resolution, image resolution, and player service.
- Keep in WPF until later: controls, dialogs, `BitmapSource` handling, routed events, and other presentation-bound code.
- Do not rewrite in Rust during phase 1: scanning, scraping, database writes, media processing, or configuration persistence.

## Safety

- Legacy databases are opened with `SqliteOpenMode.ReadOnly`; their hashes are checked before and after migration.
- The stable WPF installation is never overwritten by the Next build.
- The deployment root remains `D:\Local Media Manager Next` for upgrade compatibility; Database v1 and its reports/backups remain in `D:\Local Media Manager Next Data`.
- Schema changes are formal, checksummed migrations. Full import uses a new temporary database, integrity and foreign-key checks, sampling, a report, and an explicit confirmed switch.
- AI remains an architecture-only future module; see `AI_ARCHITECTURE.md`.
