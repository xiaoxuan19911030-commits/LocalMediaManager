# Settings storage strategy

## Separation

- `LocalMediaManager.db`: business data and relational registries such as libraries, folders, plugins, and server resources when they need relations or status history.
- Future `settings.json`: application preferences such as language, theme, player path, close behavior, card density, proxy timeout, cache policy, and shortcuts.
- Legacy `app_configs.sqlite`: read-only source during settings migration. Next never writes it.

Stage 2A/2B exposes legacy settings through `GET /api/settings`. Missing fields remain missing and are reported; defaults are metadata only and never replace a failed read.

Before `settings.json` becomes writable, the Bridge must implement a field whitelist, per-field validation, a timestamped backup manifest, temporary-file writes, format validation, atomic replacement, reread comparison, and automatic rollback. Sensitive legacy values such as passwords, cookies, tokens, and headers are masked by the DTO.

## Backup layout

```text
D:\Local Media Manager Next Data\data\
  LocalMediaManager.db
  backups\legacy\
  backups\database\
  backups\migrations\
  reports\
```

Application upgrades go to `D:\Local Media Manager Next` and do not overwrite this data root.
