# Sprint 0.5.0-01 — Feature Parity Finalization

## Lifecycle

- Current stage: **Planning / Audit complete**
- Base release: `v0.4.3` (`a857e5c`)
- Branch policy: planning and development commits remain on the local Sprint branch until the complete release gate passes.

## Sprint goal

Create an evidence-based finalization plan for the remaining mature Jvedio capabilities. This Sprint starts with audit work, not feature implementation: no feature is promoted to complete merely because a route, page, or endpoint exists.

The Feature Parity Matrix must reach 95–100% only after every remaining “must retain” item satisfies its documented complete-migration gate.

## In scope

1. Re-audit the legacy feature inventory, business rules, Next implementation, automated tests, installed verification and release evidence.
2. Produce the Feature Parity Audit, Bridge API Audit, Database Health Report and first Performance Baseline.
3. Correct stale planning/evidence references without changing a feature’s status unless the complete-migration gate is demonstrably met.
4. Turn the audit into dependency-ordered P0/P1 implementation work.
5. Update Roadmap, TODO, Changelog, Test Plan and Feature Parity Matrix with factual evidence as each work item is actually completed.

## Explicitly out of scope

- AI, NAS, Plugin Marketplace, OCR, semantic search, recommendation systems and an in-app assistant.
- Large UI redesigns or restoration of WPF/XAML pages.
- Claiming final release, pushing `main`, creating a release tag, or operating on production media during audit-only work.

## Audit sources

- `docs/migration/FEATURE_PARITY_MATRIX.md`
- `docs/migration/LEGACY_FEATURE_INVENTORY.md`
- `docs/migration/LEGACY_BUSINESS_RULES.md`
- `docs/releases/0.4.3-VERIFICATION.md`
- `docs/releases/0.4.3-METATUBE-SMOKE.md`
- Legacy source: `C:\Users\Administrator\Documents\Codex\2026-07-08\new-chat\work\Jvedio\PrivateVideoManager-WPF\Jvedio`
- Next source, Bridge endpoints, checksummed migrations and test projects in this repository.

## Delivery order after this audit

1. **P0 — Task convergence:** independent image, NFO, organizer, scan and sync runners must share the persistent task lifecycle and recovery contract.
2. **P0 — Media import proof:** validate real installed directory scanning, same-name rating recovery, queue creation, interruption recovery and non-destructive failure behavior.
3. **P0 — Metadata write proof:** finish source selection, protected-field/UI flows and complete acceptance for image/NFO workflows.
4. **P1 — Duplicate and organization closure:** safe duplicate review/ignore/merge and installed file-operation fault cases.
5. **P1 — Playback, search/paging and image-cache closure:** finish the high-frequency incomplete paths identified by the audit.
6. **P2/P3 — Defer only after P0/P1 evidence closes:** density/list view, shortcuts, language/tray, server resources and plugins remain deliberately outside the 0.5.0 release gate unless individually approved.

## Completion gate

The Sprint cannot move to Freeze until each selected item has: real UI workflow, Bridge execution, persistent data, correct failure behavior, required preview/confirmation/rollback, automated tests, installed smoke evidence, updated matrix evidence and a reproducible release report. Build success alone is not acceptance.
