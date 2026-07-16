# Sprint 0.4.3 — Media Assets & File Organization

## Lifecycle

- Current stage: **Released and Archived (2026-07-16)**
- Previous checkpoint: Sprint 0.4.2 Batch 2 implementation and self-test complete
- Release evidence: 31 Bridge tests in Debug/Release, Rust lifecycle test in Debug/Release, 30-title isolated MetaTube smoke, NSIS install smoke and installed UI acceptance
- Exit condition: `Planning → Design → Develop → Self Test → Smoke Test → Freeze → Release → Archive`

At Sprint entry, the 0.4.2 Batch 2 executor, Provider, Migration 0005 and installed build were available, but MetaTube had not yet passed real-provider sampling. That release gate was completed by the isolated 30-title run documented in `releases/0.4.3-METATUBE-SMOKE.md`.

## Sprint goal

Complete the remaining high-value local media workflows needed before the 0.5.0 feature-parity release:

1. image assets and cache;
2. complete NFO read/write workflow;
3. safe file organization;
4. real MetaTube sample verification;
5. production Bridge lifecycle;
6. three bounded interaction fixes: tag editor, detail poster sizing and player path settings.

This Sprint does not add AI, NAS, plugins, a new application shell or a large page redesign.

## Architecture and safety baseline

- React remains presentation-only and calls typed Bridge DTOs.
- All filesystem and database behavior runs in Bridge services.
- Schema changes use a checksummed Migration; expected next number is 0006, subject to Design review.
- Image, NFO, organization and batch verification work use Tasks with persistent progress, cancellation, retry and logs.
- User edits, user tags, ratings, favorites, notes, locked images and user-owned NFO always win.
- Every file mutation follows `Dry Run → Preview → Confirm → Execute → Audit → Rollback/Recovery report`.
- No target file is overwritten merely because it has the expected name.

## Workstream 1 — Image assets (P0/P1)

### Scope

- Poster, Thumb, Fanart, BigPic, ExtraPic and actor images.
- Legacy unified image root and legacy folder/name compatibility.
- Provider/source tracking and explicit user-lock ownership.
- Product image cache, cache expiry/size accounting and safe cache cleanup.
- Thumbnail-first list loading and on-demand original image loading in details.

### Required design

- Audit legacy folder and naming rules before writing an Image Path Resolver.
- Separate source/original assets, user-selected assets, Provider downloads and derived cache files.
- Download to a task-owned temporary directory, validate status/content/size/format, then atomically move.
- Atomic replacement is allowed only for LMM-owned temporary/cache/derived files. User-owned or locked files are never replaced.
- Cache cleanup may delete only reproducible derived cache entries; source images, smart covers and user selections are excluded.

### Acceptance

- Legacy path samples resolve without copying or renaming source files.
- Failed or cancelled downloads leave no partial target files.
- User-locked image survives sync, cache rebuild and application restart.
- List requests do not load original-resolution images; details load originals only when requested.
- Cache preview reports count and bytes before confirmed cleanup.

## Workstream 2 — NFO (P0/P1)

### Scope

- Read, write, update policy, encoding and special characters.
- Title, original title, plot, rating, release date, actors, tags, genres, series, studio/publisher and image references.
- Legacy-compatible parsing and deterministic output.

### Ownership and overwrite rules

- Existing user-owned NFO is treated as locked and is never overwritten automatically.
- LMM may update only an NFO it previously generated and owns, subject to the configured policy and task audit.
- Import is non-destructive: empty database fields may be filled; conflicting user values are reported for preview rather than overwritten.
- If a user-owned NFO conflicts with an export, the default action is skip; optional output uses a separate previewed destination.

### Acceptance

- UTF-8, Unicode, XML escaping and representative legacy samples round-trip correctly.
- A sample containing actors, tags, series, studio, dates, rating and image references survives read/write/read comparison.
- Permission, malformed XML and interrupted write failures preserve both the database and existing NFO.

## Workstream 3 — File organization (P0/P1)

### Scope

- Rename, move and classification rules.
- Dry-run plan, conflict detection, preview, confirmed execution, task log and recovery report.

### Safety requirements

- Dry Run performs no filesystem or database write other than an auditable task/report record.
- Preview includes source, destination, affected movie/file, conflicts, invalid names and unavailable volumes.
- Existing destinations are never overwritten.
- Execution uses a staged operation journal. Database paths update only after the filesystem step succeeds.
- Mid-operation failure produces a deterministic recovery/rollback plan; successful earlier items are not hidden.
- NAS-specific behavior is out of scope, but unavailable drives and disconnected paths must fail safely.

### Acceptance

- Rename and move samples pass Dry Run and Preview before Execute becomes available.
- Duplicate destination, invalid path, permission denial and interruption do not lose source files.
- Restart after interruption exposes the operation state and recovery action in Tasks.

## Workstream 4 — Real MetaTube verification (P0 release gate)

### Prerequisites

- Protocol preflight passed on 2026-07-16 against local MetaTube `v1.4.0-c0e053f`: `ABP-001` search returned 8 candidates, FANZA detail returned metadata, and the primary image endpoint returned a 92,237-byte JPEG. This was read-only and is not batch acceptance.
- Test is performed against a database copy and a dedicated image/NFO output root first.
- The 30–50 movie sample is fixed before execution and includes missing/partial metadata, existing user values, existing images/NFO and multi-actor titles.

### Evidence per movie

- requested code, selected Provider/external ID and match decision;
- metadata fields before/after and fields deliberately preserved;
- image/NFO writes and ownership/source;
- actors, tags and relationships;
- rating/favorite/manual-title/image-lock protection;
- task stages, logs, retry/cancel result and rollback/recovery result.

### Pass criteria

- 30–50 sampled movies complete with no silent mismatch or destructive overwrite.
- Failures remain Failed/Cancelled with actionable reasons and retry evidence.
- The summary records success, skip, mismatch, network failure and protected-field counts.
- MetaTube remains `已部分迁移` until this evidence, installed smoke test and Git commit are recorded in the Feature Parity Matrix.

## Workstream 5 — Bridge Release lifecycle (P1)

- Release build starts without a visible console window; Debug retains console diagnostics.
- Bridge writes rotating file logs with bounded retention and no secrets.
- One desktop application owns one Bridge instance; port/process conflicts are diagnosable.
- Tauri detects early Bridge exit and surfaces a useful startup error.
- Normal application exit terminates its child Bridge; forced/crash exit recovery does not leave a permanent orphan.
- Installed application must resolve Bridge/Migration only from packaged resources, never from a source checkout fallback.

## Workstream 6 — Bounded interaction fixes (P1/P2)

### Tag editor

- Replace the large Autocomplete dropdown with two clear regions: selected tags at the top and searchable tag pool below.
- Click a pool item to select; remove a selected tag with its `×` action.
- Preserve existing Bridge tag APIs and user-tag priority.

### Detail poster

- Increase useful poster size within the existing Information Layout.
- Do not restructure the whole detail page; validate common window sizes and 100/125/150% scaling.

### Player path

- Read, validate and save through Settings Service.
- Invalid custom paths show an actionable error and do not replace the last valid value.
- System-default fallback remains available and launch failures enter diagnostics/logs.

## Delivery order and dependencies

1. **Design audit:** legacy image, NFO and organizer behavior; ownership/source model; DTOs; Migration 0006 decision.
2. **Shared safety foundation:** file-operation journal, path validation, task recovery, image/NFO ownership and preview contracts.
3. **Image resolver/cache/download workflow.**
4. **NFO parser/export workflow.**
5. **File organizer Dry Run/Preview/Execute workflow.**
6. **Real MetaTube 30–50 sample verification** against the completed image/NFO paths.
7. **Bridge Release lifecycle** and process/log verification.
8. **Bounded UI fixes** and player Settings integration.
9. **Full Self Test, installed Smoke Test, Freeze and Release evidence.**

File organization depends on the shared journal/rollback design. Real MetaTube verification depends on image and NFO workflows. No UI action is considered complete before its Bridge service and DTO exist.

## Required automated and smoke evidence

- Web, Bridge and Migration Debug/Release builds.
- Bridge tests for image ownership, atomic failure cleanup, NFO round-trip, organizer conflicts/rollback, Settings and process lifecycle.
- Migration upgrade on database copy with integrity and foreign-key checks.
- Tauri Debug/Release, published resources and NSIS.
- Installed dark/light theme and Windows 100/125/150% scale smoke test for touched UI.
- Independent installation backup under `D:\Jvedio\Backups\LocalMediaManagerNext` and database upgrade backup.
- Real MetaTube sample report and database/image/NFO before/after evidence.
- Git commit, Sprint tag, rollback tag and release verification record.

## GitHub publication policy

- Development commits stay local on a dedicated Sprint branch and are not pushed to GitHub main.
- GitHub main is updated only after Planning, Develop, Self Test, Smoke Test, Freeze and Release have passed.
- A pushed main commit must be buildable, runnable and publishable, with its release/rollback evidence present in the repository.
- If a release gate fails, keep the work local, record the blocker and do not move or recreate an existing release tag.

## Definition of done

Sprint 0.4.3 can be marked complete only when all six workstreams meet their acceptance criteria, the Feature Parity Matrix contains complete evidence, the installed build passes smoke testing, and the release/tag/rollback records exist. Code completion or mock HTTP tests alone are insufficient.

After Sprint 0.4.3, 0.5.0 performs the final feature-parity acceptance. 0.5.5 remains the LTS performance/stability baseline, and real AI integration starts no earlier than 0.6.0.
