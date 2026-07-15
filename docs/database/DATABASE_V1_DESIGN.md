# Local Media Manager Database v1

## Boundary

`LocalMediaManager.db` is the independent business database for Local Media Manager Next. The legacy WPF databases remain read-only migration sources. React never accesses SQLite directly; all access is behind Bridge DTOs and services.

The production data root is `D:\Local Media Manager Next Data\data`. It is intentionally outside the application install directory `D:\Local Media Manager Next`, so an application upgrade cannot replace user data.

## Core model

- `Movies` represents a logical title.
- `MediaFiles` represents local, NAS, network, STRM, URL, or legacy file references. One movie can have many files.
- `Libraries` and `LibraryFolders` model enabled collections and source folders.
- `Actors`, `Tags`, `Genres`, `Studios`, and `Series` are normalized entities connected through explicit relation tables.
- `Images` uses separate nullable `MovieId` and `ActorId` owners with a check constraint instead of an unenforceable polymorphic foreign key.
- `UserMovieState` owns favorites, the 0-5 user rating, play count, last position, and personal notes. These values are not duplicated in `Movies`.
- `PlayHistory` preserves event history and uses `SET NULL` for deleted movies/files so historical events are not silently destroyed.
- `ExternalIds`, `Tasks`, `MigrationWarnings`, and `LegacyIdMappings` provide controlled extension points without converting core metadata into key-value storage.

## Delete rules

- Removing a movie cascades current media files, current relationships, images, and user state.
- Play history keeps the event but nulls deleted movie/file references.
- Removing a library does not delete a movie; media-file library references become null.
- Physical file deletion and database-row deletion remain separate operations.

## Migration and runtime rules

- `SchemaMigrations` is the only allowed schema-change mechanism.
- Migration `0001_InitialSchema.sql` runs transactionally and stores its SHA-256 checksum.
- Foreign keys are enabled. Runtime writes will use transactions and a single Bridge writer.
- WAL is enabled for runtime scalability. Backup code must checkpoint before copying the database and account for `-wal` and `-shm` files.
- Current v1 uses ordinary indexes. FTS5 is deferred until search DTOs and a rebuild strategy are implemented.
- AI is an explicitly deferred module. Database v1 does not create speculative AI tables; concrete AI persistence will be introduced later through formal migrations after the core data, task, privacy, confirmation, and rollback contracts are stable.

## Index rationale

Indexes cover code/title lookup, release/updated sorting, media path and library filtering, normalized actor/tag lookup, relation traversal, play-history timelines, and provider external IDs. Low-selectivity or write-only fields are intentionally not indexed.
