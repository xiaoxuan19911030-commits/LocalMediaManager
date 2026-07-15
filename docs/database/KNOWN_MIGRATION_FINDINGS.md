# Known migration findings

Authoritative run: `20260715_134327`.

- Database v1 switch was allowed and completed: 2,500 legacy movie rows became 2,500 new logical movies.
- `PRAGMA integrity_check` returned `ok`; `PRAGMA foreign_key_check` returned zero rows.
- All required samples passed: 50 movies and 20 each of actors, tags, user state, play history, and images.
- 242 media paths are currently missing. The movie records and paths were preserved with `ExistsState=Missing`.
- 29 `.nfo` paths were retained as non-playable references rather than being deleted or presented as videos.
- One empty actor name was retained as a deterministic placeholder with a warning.
- One orphan play-history event was retained with nullable movie ownership.
- Six normalized paths and 25 normalized codes are duplicated. They remain separate because automatic ownership/merge decisions could destroy user state.
- The legacy business and configuration database hashes remained unchanged after migration.

These findings are not silent data loss and do not block the validated switch. They are the initial review queue for future database tools. No automatic deletion or duplicate merge should be introduced without preview, explicit confirmation, and rollback.
