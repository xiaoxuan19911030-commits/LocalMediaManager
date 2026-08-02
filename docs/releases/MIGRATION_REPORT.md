# Local Media Manager Directory Migration Report

**Scope:** Step 7 and Step 8 migration closeout only. No business behavior, media data, database content, legacy directory, or backup was deleted.

## 1. Directory Mapping

| Category | Previous path | Current path | Status |
|---|---|---|---|
| Source / Git | `D:\LocalMediaManager` | `D:\自用软件\源代码目录\本地媒体管理器` | Migrated; current unique formal worktree |
| Deployment | `D:\Local Media Manager Next` | `D:\自用软件\部署安装目录\本地媒体管理器` | Migrated and running |
| Formal data | `D:\Local Media Manager Next Data` | `D:\自用软件\部署安装目录\本地媒体管理器\数据` | Migrated; runtime database uses this root |
| Test media | `D:\LMM_RC004_TestLibrary` | `D:\自用软件\测试目录\本地媒体管理器\媒体库\LMM_RC004_TestLibrary` | Migrated |
| Validation artifacts | Source `artifacts\validation` | `D:\自用软件\测试目录\本地媒体管理器\artifacts` | Migrated; 16,863 files |
| Temporary build output | Ad hoc source / root locations | `D:\自用软件\临时目录\本地媒体管理器` | Current migration logs use this root |
| Backups | `D:\Local Media Manager Next Backups` (absent at this verification) | `D:\自用软件\备份目录\本地媒体管理器` | Historical collection moved intact during migration; no migration cleanup |

The new source path is the only formal Git, Visual Studio, Codex, and ChatGPT development worktree. It contains the repository, solution files, source, and build output. No second current source worktree was created.

## 2. Git Verification

```text
Git Status: normal; git fsck exited successfully (dangling historical objects only)
Current Branch: codex/sprint-v0.7.9-stage4.2-image-memory-growth-shape
HEAD Commit: afe3870 test(sync): rerun native heap sampling with corrected heapwalk layout
Source Root: D:\自用软件\源代码目录\本地媒体管理器
```

All pre-migration uncommitted changes remain present. Modified tracked areas include Bridge tests and services, MetaTubeSmoke, Migration, ProductionValidation, deployment script, Tauri shell, Settings UI/types. Untracked evidence and validation files remain present: `ImagePipelineValidationDiagnostics.cs`, directory-standard and Stage 4 documents. No migration operation reset, checked out, or discarded any change.

## 3. Build and Deployment Verification

| Check | Result |
|---|---|
| Bridge Debug build | PASS, 0 warnings / 0 errors |
| Migration Debug build | PASS, 0 warnings / 0 errors |
| Bridge Release build | PASS, 0 warnings / 0 errors |
| Migration Release build | PASS, 0 warnings / 0 errors |
| Web build | PASS |
| Tauri Release / NSIS | PASS |
| Bridge Release Tests | PASS, 465 / 465 |
| Deployment | PASS to `D:\自用软件\部署安装目录\本地媒体管理器` |
| Managed backup | Created `D:\自用软件\备份目录\本地媒体管理器\v0.7.9` |
| `git diff --check` | PASS |

The first Tauri build after moving the source directory failed because its generated target cache referenced the former absolute source path. `cargo clean` removed only the re-creatable `src-tauri\target` cache; the repeat Release and NSIS build passed.

## 4. Runtime and Smoke Verification

| Check | Result |
|---|---|
| Application process | PASS: launched from new deployment root |
| Bridge process | PASS: launched from new deployment root |
| `/health` | PASS, HTTP 200, `status=ok` |
| `/api/dashboard?refresh=true` | PASS, HTTP 200 |
| `/api/settings/all` | PASS, HTTP 200 |
| Formal database | PASS: `D:\自用软件\部署安装目录\本地媒体管理器\数据\data\LocalMediaManager.db` |
| Data separation | PASS: `dataSeparated=true`, `legacyDatabaseUsedForRuntime=false` |
| Formal log root | PASS: new Bridge and Migration logs under the new deployment data root |
| Docker | PASS: Docker containers running |
| MetaTube | PASS: `metatube` running and `http://127.0.0.1:8080/` returns HTTP 200 |
| MDC-NG | PASS: `mdcng2-rollback-20260722` running; Bridge reports API reachable, version `v1.36.0` |

The application was started and the main shell issued successful Dashboard, Settings, plugin, image, provider, and update calls through the Bridge. The desktop automation runtime was unavailable in this session, so direct click-through verification of the Settings page, Sync Center, search screen, and detail screen was not independently automated. Their underlying Bridge endpoints and the application startup path passed; visual interaction remains a Human smoke-test gap.

`configDatabaseAvailable=false` is an existing state, not a migration loss. The required runtime location is correctly under the new data root, but no direct `config\app_configs.sqlite` existed before migration; only historical legacy backup copies were found. No configuration database was fabricated.

## 5. Test Directory Verification

All migrated test data is under `D:\自用软件\测试目录\本地媒体管理器`:

- test media library: `媒体库\LMM_RC004_TestLibrary`;
- isolated validation databases, MediaStorage copies, logs, dumps, downloads, and evidence: `artifacts` (16,863 files);
- future smoke output default: `smoke`.

There are zero files under the source worktree's former `artifacts\validation` location. Formal data and test artifacts are separate.

## 6. Retained Old Directories

| Previous path | Current state | Used at runtime | Deletion status |
|---|---|---|---|
| `D:\LocalMediaManager` | Empty source-root shell, 0 files | No | Requires separate approval |
| `D:\LocalMediaManager-release-v0.7.2` | Old source candidate | No | Requires inventory and approval |
| `D:\LocalMediaManager-v078-clean` | Old source candidate | No | Requires inventory and approval |
| `D:\Local Media Manager Next` | No longer exists | No | N/A |
| `D:\Local Media Manager Next Data` | Two pre-fix log files only | No; last old Bridge log write predates final relaunch | Requires separate approval |

No old directory was deleted in this migration.

## 7. Historical Backup Inventory

`D:\Local Media Manager Next Backups` no longer exists. Its historical backup collection is located at `D:\自用软件\备份目录\本地媒体管理器`.

The migration-closeout inventory was a point-in-time, first-level-only snapshot: **171 directories, 349.055 GiB**. It described the collection at the end of migration, not a live assertion that the former path still exists. Subsequent managed deployments added `v0.7.10`; the 2026-07-28 read-only recheck found **172 directories, 355.213 GiB** at the new root. `v0.7.10` accounts for 6.155 GiB of that later total.

Migration records state that the historical collection was moved and that no old directory was deleted during migration. The old root's absence and the retained oldest snapshot (`rc-bug-001-20260717-155355`, created 2026-07-17) are consistent with that move. No copy/move operation log or immutable pre-migration manifest remains, so the exact filesystem operation cannot be independently reconstructed from current metadata alone. No evidence of historical-backup deletion was found in this read-only recheck.

| Classification | First-level directories | Size GiB | Date range | Inferred purpose | Recommendation |
|---|---:|---:|---|---|---|
| Managed current | 1 | 12.139 | 2026-07-28 | `v0.7.9` migration deployment rollback | Retain |
| Legacy deployment snapshots | 158 | 336.248 | 2026-07-17 to 2026-07-25 | `deploy-*` program and user-data snapshots | Human confirmation required |
| Legacy version-labelled | 5 | 0.200 | 2026-07-21 to 2026-07-22 | `v0.6.9-*` exploratory backups | Human confirmation required |
| Diagnostic / smoke | 5 | 0.352 | 2026-07-22 to 2026-07-23 | Bridge, NAS, and smoke evidence | Human confirmation required |
| Other legacy | 2 | 0.119 | 2026-07-17 to 2026-07-20 | RC / image-path migration backup | Human confirmation required |

Largest legacy deployment snapshots are `deploy-20260725-211905` (5.954 GiB), `deploy-20260725-204526` (5.907 GiB), and `deploy-20260724-151801` (5.290 GiB). None is marked safe to delete: names and sizes alone cannot establish that a snapshot is redundant. **Suggested deletion: none in this stage.**

The three-version automatic retention rule applies only to future exact-version directories such as `v0.7.9`; it intentionally excludes migration-era directories with suffixes.

## 8. Final Conclusion

1. **Directory standard switched:** Yes. New source, deployment, data, test, temporary, and backup roots are in use. Runtime database and logs resolve to the new formal paths.
2. **Ready for further development:** Yes. The new source path is the formal worktree, builds and Bridge tests pass, and the deployed application/Bridge/API smoke checks pass.
3. **Human confirmation required:** Yes. Do not delete the empty old source shell, the two old source candidates, the old log-only data directory, or any part of the 349.055 GiB historical backup collection without a dedicated inventory review and explicit deletion approval. Visual page-level smoke is also pending Human confirmation because desktop automation was unavailable.
