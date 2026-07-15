# Sprint 0.4.1 — Legacy Feature Migration Part 1

## Sprint goal

Restore the daily user workflows for favorites, ratings, user tags, actor relationships and playback history without restoring the legacy WPF UI.

## Lifecycle

`Planning → Design → Develop → Self Test → Smoke Test → Freeze → Release → Archive`

## Design decisions

- React remains presentation-only and calls typed Bridge commands.
- The Tauri host creates a per-launch session token. Mutating Bridge endpoints reject missing or invalid tokens.
- Existing `UserMovieState`, `PlayHistory`, `Tags`, `MovieTags`, `Actors` and `MovieActors` tables remain the source of truth.
- Migration `0003_UserStateAuditAndRatingMemory` adds rating-presence state, deleted-rating memory and operation audit only.
- User edits are transactional. Destructive tag deletion and actor repair require preview and confirmation; audit records preserve rollback evidence.
- Favorite compatibility reuses migrated legacy favorite tags when they exist, while `UserMovieState.IsFavorite` remains the canonical Next value.
- Playback history is written only after a successfully launched player process exits normally.

## Scope and acceptance

| Area | Acceptance |
|---|---|
| Favorites | Single and batch toggle, legacy tag compatibility, immediate refresh, restart persistence |
| Ratings | Set and clear, distinguish unscored from zero, immediate persistence |
| Rating memory | Remember filename plus rating before record deletion; restore only into an unscored matching import |
| Tags | Create, rename, delete with impact preview, single/batch bind and unbind |
| Actors | Edit actor, edit movie relations, diagnose and safely repair ActorID=0 candidates |
| Playback | Successful normal player exit increments count and history; previous/next preserves query and sort context |

## Out of scope

AI, NAS, plugin installation, scanning/import UI, metadata providers, NFO and image/file organization remain outside this Sprint.

