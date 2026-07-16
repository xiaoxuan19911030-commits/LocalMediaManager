# Database Health Report — 0.5.0-01

Date: 2026-07-16
Scope: schema and migration design review. It is not a replacement for a fresh production-database integrity run.

## Current schema evidence

- Migrations are sequential and checksummed: `0001_InitialSchema` through `0009_PlaybackSettings`.
- The schema contains explicit unique constraints and indexes for legacy identity, normalized paths, tag/actor names, media relations, user state, history, task status, image assets/cache, NFO documents and organizer journal records.
- Foreign-key-sensitive product data is migrated through the formal migration project, not React or Tauri.
- 0.4.3’s isolated real MetaTube smoke recorded `PRAGMA integrity_check = ok` and zero `foreign_key_check` rows.

## Positive findings

| Area | Evidence |
|---|---|
| User-state safety | Migration `0003`; deleted-rating memory has a normalized filename uniqueness rule and operation audit. |
| Scan/task workflow | Migration `0004`; persistent logs and task indexes exist. |
| Metadata rollback context | Migration `0005`; sync snapshots and task indexes exist. |
| Asset/NFO ownership | Migrations `0006`/`0007`; source/cache and NFO document identity are modeled separately. |
| File-operation recovery | Migration `0008`; operation journal and media-file indexes exist. |
| Playback settings | Migration `0009`; settings move out of environment-only compatibility handling. |

## Remaining audit work

1. Run a fresh migration upgrade on a database copy and record checksums, integrity, foreign keys and representative row counts for 0.5.0.
2. Add schema-level tests for every FK/unique/transaction rule introduced by migrations 0004–0009, especially task recovery, image ownership and organizer compensation.
3. Audit backup retention, restore drills and operation-journal recovery under interruption; the existing 0.4.3 evidence is isolated, not a production destructive test.
4. Define a duplicate-resolution data model before adding merge UI. No direct delete workflow may be built on the current read-only diagnostics count.
5. Keep malformed legacy source text in `MigrationWarnings`; do not "repair" it automatically.

## Release gate

Before 0.5.0 Freeze, execute and archive: migration from a representative legacy copy, integrity/foreign-key checks, user-state sampling, task recovery sampling, image/NFO ownership sampling, organizer recovery sampling and restore-from-backup verification.
