# Legacy relationship and data-quality analysis

## Relationships

The legacy schema declares no SQLite foreign keys. Relationships are implicit:

- `metadata.DataID` -> `metadata_video.DataID`
- `metadata.DataID` -> `metadata_to_actor.DataID` -> `actor_info.ActorID`
- `metadata.DataID` -> `metadata_to_label.DataID`
- `metadata.DataID` -> `metadata_to_tagstamp.DataID` -> `common_tagstamp.TagID`
- `metadata.DataID` -> `common_play_history.DataID`
- `metadata.DBId` -> `app_databases.DBId`

Configuration and business data are already in separate files, but legacy application settings are JSON blobs inside `app_configs.sqlite`, while business entities are in `app_datas.sqlite`.

## Confirmed migration findings

- 2,500 logical movie rows were preserved as 2,500 new movies.
- Legacy NFO/non-video paths are retained as non-playable references and flagged rather than deleted.
- Missing file paths are retained with `ExistsState=Missing` and a warning.
- One empty actor name is preserved using a deterministic placeholder and a warning.
- One orphan play-history row is preserved without a movie relation.
- 6 normalized paths and 25 normalized movie codes are duplicated. They are reported and not auto-merged because the correct ownership cannot be inferred safely.
- No new foreign-key orphan remains after migration.

The full per-record warnings are in the local `migration-report.json`; the report is intentionally kept local because it contains filesystem paths.
