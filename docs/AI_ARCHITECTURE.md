# AI architecture boundary

AI is deliberately deferred until database migration, Bridge read/write contracts, settings, movie browsing/details, people/tags/user state, the unified task system, and backup/rollback are stable.

Future AI code will live behind provider-neutral services and controlled Bridge APIs. React must not call model providers directly; AI must not access SQLite, the filesystem, credentials, or system commands directly. Cloud use is opt-in, paths and private notes are excluded by default, image upload requires explicit permission, and Windows credentials must protect API keys.

AI output is advisory. Every result must carry provider, model, prompt version, source-data description, confidence, creation time, and user decision. User edits always win. Metadata, tag, image, rename, move, or delete proposals require preview, impact scope, explicit confirmation, logged Bridge execution, and rollback where appropriate.

Database v1 does not create speculative AI tables. `SchemaMigrations`, `Tasks`, `ExternalIds`, source fields, and the Bridge boundary are the current extension points. Concrete provider, recommendation, suggestion, embedding, conversation, or cache tables will be introduced only with their implementing feature and a formal schema migration.

The first future AI slice should be provider settings, connection test, one-movie tag/metadata suggestions, preview, user-confirmed application, and a complete operation log. Batch automation comes later.
