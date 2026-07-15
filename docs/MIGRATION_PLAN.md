# Migration plan

## Completed through Database v1 / settings read phase

- Independent Git branch and worktree.
- Upstream source baseline and GPL attribution retained.
- Tauri/React/MUI shell with dark theme, navigation, routing, loading, empty/error feedback, and toast feedback.
- Real legacy schema/sample analysis and data dictionary.
- Independent, checksummed Database v1 schema migration and full migration tool.
- Legacy ID mapping, cleaning warnings, backups, reports, integrity/foreign-key checks, and sample verification.
- 2,500 movies plus user state, people, tags, relationships, history, and images migrated into the independent database.
- Bridge switched to Database v1 with pagination, search, sorting, details, covers, and player launch.
- Seven-category read-only settings DTO and shared Material UI settings pages, including light/dark preview.
- Debug, Release, NSIS package, and independent installed smoke deployment.

## Next phase: validated writes and core management

1. Add authenticated settings writes to a separate `settings.json` with validation, atomic replacement, backup, reread comparison, and rollback.
2. Complete movie details/editing, libraries, tags, actors, favorites, and history through versioned Bridge DTOs.
3. Introduce the single-writer task service and validated write transactions.
4. Add database backup/restore UI and regression tests before considering WPF replacement.

## Later phases

- Media browsing refinement: card/list density, filters, and cached details.
- Media management: libraries, tags, actors, favorites, ratings, and history.
- Details/editing: metadata, ratings, people, images, path, player, previous/next prefetch.
- Advanced: scraping, synchronization, plugins, server resources, database tools, and logs.
- AI begins only after the database, writes, settings, details, task system, and rollback are stable; no speculative AI tables or page code are part of this phase.
- Replacement decision only after full regression testing; the WPF baseline stays available until then.
