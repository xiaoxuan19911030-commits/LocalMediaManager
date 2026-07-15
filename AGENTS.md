# Local Media Manager repository instructions

Before planning or implementing product work, read:

1. `docs/PRODUCT_VISION.md`
2. `docs/ROADMAP.md`
3. `docs/TODO.md`
4. `docs/ARCHITECTURE.md`
5. `docs/UI_DESIGN_SPEC.md`
6. `docs/TEST_PLAN.md`
7. `docs/migration/FEATURE_PARITY_MATRIX.md` when legacy behavior is involved

## Scope authority

- `docs/ROADMAP.md` is the formal version-scope authority.
- Unfinished work belongs in `docs/TODO.md`.
- Released behavior belongs in `docs/CHANGELOG.md`.
- `docs/migration/FEATURE_PARITY_MATRIX.md` is the only authority for legacy feature status, priority, dependencies, risk, and completion evidence.
- Do not introduce a new product direction solely from conversation context without updating these documents.

## Architecture rules

- All business behavior goes through Bridge DTOs and services.
- React must not access SQLite, the filesystem, system commands, plugins, or AI providers directly.
- Database structure changes require checksummed Schema Migrations.
- Settings use the unified Settings Service.
- Long-running work uses the unified Tasks system.
- Dangerous operations require impact preview, confirmation, backup/audit, and a rollback strategy.
- AI remains provider-neutral and independent from core media business; real model integration starts no earlier than 0.6.0.

## UI rules

- Reuse the Material UI theme and shared LMM components.
- Support light/dark themes and the target responsive/scaling matrix.
- Do not copy old WPF/XAML layouts, controls, spacing, or styling.
- Legacy WPF is a source of feature behavior, data compatibility, business rules, and regression expectations only.

## Version completion

Before completing a version, update Roadmap, Changelog, and TODO; run Web, Bridge, Migration, Tauri Debug/Release and NSIS validation; back up and deploy the independent Next installation; smoke test; create a commit, version tag, and rollback tag; and record the verification result.

Never mark a legacy feature as fully migrated unless the matrix contains its real UI operation, Bridge execution, persistence/restart result, error handling, required backup/rollback, automated test, manual smoke test, Git commit, and acceptance record.

Every version follows `Planning → Design → Develop → Self Test → Smoke Test → Freeze → Release → Archive`. Run the applicable cases from `docs/TEST_PLAN.md`; do not skip directly from implementation to release.
