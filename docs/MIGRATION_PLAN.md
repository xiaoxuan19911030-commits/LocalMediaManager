# Migration plan

## Completed in phase 0/1 prototype

- Independent Git branch and worktree.
- Upstream source baseline and GPL attribution retained.
- Tauri/React/MUI shell with dark theme, navigation, routing, loading, empty/error feedback, and toast feedback.
- .NET Backend Bridge with read-only SQLite access.
- Real library count and bounded media list.
- Existing cover lookup and player-launch endpoint.
- Debug and Windows package scripts.

## Next phase: settings

1. Define versioned Bridge DTOs for all seven settings categories.
2. Read legacy configuration without changing field names.
3. Add authenticated write operations with validation, atomic file replacement, backup, and rollback.
4. Implement General, Library, Playback, Metadata, Appearance, Shortcuts, and Advanced pages with shared Material UI setting rows.
5. Verify each setting against the legacy behavior before marking it migrated.

## Later phases

- Media browsing: card/list views, search, filters, sorting, pagination.
- Media management: libraries, tags, actors, favorites, and history.
- Details/editing: metadata, ratings, people, images, path, player, previous/next prefetch.
- Advanced: scraping, synchronization, tasks, plugins, server resources, database tools, logs.
- Replacement decision only after full regression testing; the WPF baseline stays available until then.

