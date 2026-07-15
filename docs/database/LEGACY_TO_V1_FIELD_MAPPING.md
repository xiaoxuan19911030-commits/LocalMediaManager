# Legacy to Database v1 field mapping

| Legacy source | Database v1 target | Migration rule |
|---|---|---|
| `app_databases` | `Libraries`, `LibraryFolders` | Allocate new IDs; split source paths; retain disabled state and order. |
| `metadata` + `metadata_video` | `Movies` | Join by `DataID`; normalize dates; convert duration minutes to seconds; preserve legacy IDs only as trace metadata. |
| `metadata.Path`, file fields | `MediaFiles` | Preserve original and normalized paths; classify local/NAS/STRM/URL; mark missing paths without deleting the movie. |
| `metadata.FavoriteCount`, `Grade`, `ViewCount`, `ViewDate` | `UserMovieState` | Convert favorite to 0/1, clamp rating to 0-5, preserve play count and last-played time. |
| `actor_info` | `Actors` | Allocate new IDs; normalize names; preserve an empty name with a deterministic warning placeholder. |
| `metadata_to_actor` | `MovieActors` | Resolve both sides through `LegacyIdMappings`; invalid relations are reported, never silently attached. |
| `metadata_to_label` | `Tags`, `MovieTags` | Normalize non-empty names; de-duplicate through normalized names while retaining relation mappings. |
| `common_tagstamp` + `metadata_to_tagstamp` | `Tags`, `MovieTags` | Preserve custom stamp names/colors as user tags. |
| `metadata_video.Genre`, `Studio`, `Series` | normalized entities and relation tables | Split source values, normalize names, and attach explicit many-to-many relations. |
| `common_play_history` | `PlayHistory` | Preserve each event; unresolved movie references remain as history with a warning and nullable foreign key. |
| legacy image folders | `Images` | Store image type, real path, primary flag, and owner FK; missing images are reported. |
| provider/code/web URL | `ExternalIds` | Preserve provider identity without making legacy IDs new business keys. |
| migration anomalies | `MigrationWarnings` | Persist warning code, legacy identity, and non-sensitive explanation. |

The legacy configuration database is not migrated into the media database. Application preferences will move to a separately backed-up `settings.json`; libraries and source folders remain business data in SQLite.
