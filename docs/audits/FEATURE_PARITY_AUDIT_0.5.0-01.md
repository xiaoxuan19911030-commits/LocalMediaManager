# Feature Parity Audit — 0.5.0-01

Date: 2026-07-16
Baseline: `v0.4.3` / `a857e5c`
Scope: mature legacy Jvedio capabilities only; this is an audit, not a release certification.

## Method

Each matrix entry was checked against four evidence layers:

1. legacy source inventory and business rules;
2. current Next route/Bridge/Migration implementation;
3. automated test and migration evidence;
4. installed or isolated real-data smoke evidence.

The matrix’s existing complete-migration gate remains binding. A page, a read-only endpoint, a happy-path unit test, or a protocol preflight alone is insufficient.

## Result summary

| Status | Count | Interpretation |
|---|---:|---|
| 已完整迁移 | 10 | Has recorded real UI/Bridge/persistence/test/smoke/acceptance evidence. |
| 已部分迁移 | 21 | Has useful implementation but is missing at least one required workflow or acceptance proof. |
| 仅只读 | 6 | Data can be viewed but the mature management workflow is not available. |
| 尚未迁移 | 3 | No equivalent workflow exists. |
| 需要重构 | 1 | Legacy capability needs a separately designed Next workflow. |

Strict complete parity is therefore **10 / 41 (24%)**, not 95–100%. This does not devalue the 0.4.3 release: it accurately reflects that the product foundation and several core write workflows are real, while broad feature-equivalence closure is still pending.

## Verified complete workflows

The following retain their existing `已完整迁移` status, supported by the 0.4.1 verification and Bridge test evidence:

- deleted-rating memory; favorites compatibility and favorite writes; ratings;
- custom tags and tag relations; actor relations and safe ActorID=0 repair;
- playback count and history; context-preserving previous/next navigation.

These are not reimplemented in this Sprint unless audit evidence identifies a regression.

## Evidence corrections found

The matrix intentionally keeps the following items partial, but some of their narrative evidence was stale after 0.4.3:

- Automatic sync and metadata write were real-tested in the isolated 30-title run: 22 completed, 8 explicit no-result failures, 311 image assets, 22 NFO documents, and protected user data retained. This validates the Provider/write path, not the installed-library scan-to-queue workflow or a final user-facing rollback flow.
- MetaTube’s real write path is verified, but secure sensitive Header/Cookie credential handling remains absent.
- Image, NFO and organizer services are implemented and smoke-tested at their documented boundaries, but complete installed interaction/fault coverage remains incomplete.
- `PROJECT_CONTEXT.md` had an obsolete release snapshot and is corrected by this Sprint planning change; Roadmap/verification documents are the authoritative release record.

## P0 closure backlog

| Work item | Matrix items | Missing proof / capability | Dependencies | Risk | Exit evidence |
|---|---|---|---|---|---|
| Task lifecycle convergence | 1, 2, 40 | Independent image and NFO runners; uniform task recovery and control behavior | Tasks schema, TaskCommandService, runners, UI | Database/file-system write | State-transition tests, restart test, installed Task Center smoke |
| Installed scan/import workflow | 1, 4, 28 | Real directory scan, same-name rating recovery, automatic queue creation, cross-restart handling | Libraries, scan service, sync executor, settings | Database/file-system write | 3+ real test files, task/log evidence, error isolation, database checks |
| Protected metadata/image/NFO completion | 2, 15–18, 31, 32 | User-driven source selection and all ownership/override paths | Image/NFO services, settings, DTOs | Database/file-system write | installed UI workflow + protected assets/NFO before/after evidence |
| Safe duplicate workflow | 30 | Candidate details, ignore, merge preview, confirmation and recovery | Duplicate service, file journal, tasks | Delete risk | no-auto-delete test, preview/confirm/rollback smoke |
| Safe file-operation fault closure | 33 | Permission, interrupted operation and unavailable volume recovery | Organizer journal, tasks | File-system write | isolated fault smoke; user media untouched |

## P1 closure backlog

| Work item | Matrix items | Required completion |
|---|---|---|
| External player | 14 | Installed custom executable launch and system-association failure diagnostics. |
| Search/filter/sort/paging | 20–27 | List view, density persistence, direct page input, keyboard scope, advanced conditions and stable cross-page tests. |
| Cache | 17, 34 | Size limit, restart invalidation, category/space estimate and installed clean/rebuild proof. |
| Metadata status | 29 | Repair workflow, task entry and filter linkage. |
| Settings/platform | 35, 36, 38, 39 | Only when P0/P1 media workflows are closed; keep plugin work out of 0.5.0. |

## Explicitly deferred

- Smart card cover recognition/cropping (item 19) is a real legacy feature and remains `尚未迁移`; it requires a separate safe image-analysis/task design.
- Plugins, NAS, AI, OCR, semantic search, recommendations and assistant features are outside this Sprint.
- Large UI redesigns are outside this Sprint; UI work is limited to defects that block audited workflows.

## Next decision

Proceed with the first P0 implementation pack: **Task lifecycle convergence + installed scan/import proof**. Do not start duplicate merging, AI, NAS, or a redesign before its dependencies and acceptance design are documented.
