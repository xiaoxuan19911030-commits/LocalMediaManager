# Bridge API Audit — 0.5.0-01

Date: 2026-07-16
Scope: API boundary consistency audit of `backend/LocalMediaManager.Bridge/Program.cs` and associated services. This report does not certify every endpoint as feature-complete.

## Verified boundary

- React has a single business transport client at `src/services/bridge.ts`; the audit found no direct SQLite, filesystem or Provider client in `src`.
- Bridge owns read/write endpoints, path operations, player launch, task control, image/NFO workflows and organizer execution.
- Checksummed migrations `0001` through `0009` back all existing Next schema changes.

## Current API groups

| Group | Examples | Audit outcome |
|---|---|---|
| Health/settings | `/health`, `/api/settings*` | Present; settings domains are split and need a single response/error envelope inventory. |
| Read models | dashboard, search, entities, collections, videos, metadata, diagnostics | Present; some are legacy/read-only product views. |
| User state | movie state, favorites, tags, actors, deleted-rating memory | Real write boundary with 0.4.1 evidence. |
| Libraries/tasks/sync | libraries, scan, tasks, sync | Real services; runner-type convergence remains open. |
| Assets/NFO/organizer | image assets/cache, NFO, organizer | Real safety-oriented endpoints; complete installed workflow coverage remains open. |
| Playback | settings and play | Settings is persistent; custom executable installed proof remains open. |

## Gaps to close before 0.5.0 release

1. Standardize successful and failed command responses across all write endpoints; document a stable error DTO with code, user-safe message, correlation/task/audit identifier and retryability.
2. Add a single exception-to-HTTP mapping policy. Current minimal endpoints frequently use `Results.Ok` directly, so service exceptions need an audited uniform handler rather than endpoint-by-endpoint behavior.
3. Document timeout and retry ownership: UI must not invent retries for task-backed commands; the Bridge task runner owns retry count and state.
4. Make task status/stage enums a shared DTO contract; avoid untyped string drift between scan, sync, cache and organizer runners.
5. Extend API-level integration tests for invalid input, 404, conflict, authorization, cancellation and provider failure cases.

## Acceptance for the convergence work item

- One documented response/error contract consumed by `src/services/bridge.ts`.
- Endpoint integration tests cover both success and expected failure paths.
- No error response exposes full private paths, headers, cookies, API keys or raw provider credentials.
- Task-backed endpoints return task identity/state consistently and do not claim completion before the runner completes.
