# Legacy Cleanup Readiness Audit

**Date:** 2026-07-19  
**Sprint:** Legacy Cleanup Readiness Audit  
**Scope:** audit only; no bulk deletion, no new feature development, no push.

## 1. Repository State

| Item | Result |
|---|---|
| Source root | `D:/LocalMediaManager` |
| Branch | `sprint/0.5.0-20-moviewall-display` |
| HEAD | `12f9116` (`docs(decisions): record moviewall display decision`) |
| Remote | `origin https://github.com/xiaoxuan19911030-commits/LocalMediaManager.git` |
| Remote contains `bbed780` | No remote branch contains it after `git fetch origin --prune` |
| Remote contains `12f9116` / HEAD | No remote branch contains it after `git fetch origin --prune` |
| Latest branch pushed | No evidence that `sprint/0.5.0-20-moviewall-display` is pushed |

`git status --short --branch` at audit start:

```text
## sprint/0.5.0-20-moviewall-display
 M AGENTS.md
 M backend/LocalMediaManager.Bridge.Tests/ImageAssetWorkflowTests.cs
 M backend/LocalMediaManager.Bridge/ImageAssetWorkflow.cs
 M backend/LocalMediaManager.Bridge/ImageWorkflowService.cs
 M backend/LocalMediaManager.Bridge/Program.cs
 M src/pages/CollectionPage.tsx
 M src/pages/MediaPage.tsx
 M src/pages/MovieDetailPage.tsx
 M src/services/bridge.ts
 M src/types/media.ts
?? INDEX.md
```

Notes:

- `AGENTS.md` and `INDEX.md` pre-existed as user/workspace changes and were not modified by this audit.
- The image crop, enlarged detail poster, and collection-card context menu changes are already implemented and locally deployed, but are not committed or pushed.
- This audit adds only this report file. No source code deletion was performed.

## 2. Sources Read

Read in the requested order:

1. `INDEX.md`
2. `PROJECT.md`
3. `DECISION_LOG.md`
4. `AGENTS.md`
5. `docs/audits/LEGACY_FEATURE_AUDIT_2026-07-18.md`
6. `docs/migration/LEGACY_FEATURE_INVENTORY.md`
7. `docs/migration/LEGACY_BUSINESS_RULES.md`
8. `docs/migration/FEATURE_PARITY_MATRIX.md`

Key constraints confirmed:

- Legacy is behavior/data evidence, not a UI template.
- Legacy DB/config/image paths remain read-only compatibility sources.
- Media resource writes must use `MediaStoragePathResolver`.
- WallCrop/CardCover maps to `GeneratedCard` and `WallCrops`.
- Knowledge System v1.0 structure is frozen; audits belong in `docs/audits`.

## 3. Product Decisions: Old Features Cancelled

These should no longer be treated as "unmigrated" in future parity summaries:

| Feature | Old location | New status | Cleanup meaning |
|---|---|---|---|
| 打开应用目录 | Legacy startup/about menu | 不再保留 / 产品决策取消 | Do not add a replacement just for parity. No runtime code found for this old action. |
| 旧多数据库 UI | `WindowStartUp`, `Window_DataBase` | 不再保留 / 产品决策取消 | Next uses one writable runtime DB plus media libraries. Migration compatibility remains. |
| 端口监听配置 | `ServerConfig`, `ServerManager` | 不再保留 / 产品决策取消 | Bridge fixed loopback/session model remains; do not expose old server port UI. |

Do not delete migration/config compatibility merely because these UI concepts are cancelled.

## 4. Current Migration Completion Snapshot

This is a readiness snapshot, not a replacement for `FEATURE_PARITY_MATRIX.md`.

| Bucket | Count | Notes |
|---|---:|---|
| Already migrated / upgraded or now implemented | 58 | Includes previous 51 migrated + upgraded items + MovieWall page jump, left/right page keys, display preferences, and current manual crop entry. |
| Product-cancelled legacy UI | 3 | Open app dir, old multi-DB UI, port-listening config. |
| Still partial | 17 | Playback verification, full image source semantics, NFO settings, scan details, duplicate resolution, batch organizer, actor completeness, etc. |
| Still missing / not complete | 10 | Full movie edit, studio category, random movie, copy movie info, translate plot, image SetAs, batch image-list delete, app log cleanup, language/tray/shortcut/update items. |

Items updated from the 2026-07-18 audit based on real code:

| Feature | Previous status | Current status | Evidence |
|---|---|---|---|
| 页码输入跳转 | ❌ 未迁移 | ✅ 已迁移 locally | `MovieWall.tsx` `FloatingPagination`, current-page click input, Enter/Esc behavior. |
| 左右方向键翻页 | ❌ 未迁移 | ✅ 已迁移 locally | `MovieWall.tsx` handles `ArrowLeft` / `ArrowRight` and ignores text inputs. |
| `Ctrl+G` page input focus | Not listed separately | ✅ 已迁移 locally | `MovieWall.tsx` `pageInputFocusSignal`. |
| 横版/竖版海报、小/中/大尺寸 | Partial/readonly in matrix | ✅ 已迁移 locally | `UnifiedSettings.movieWallDisplay`, `MediaCard`, Settings appearance section, DEC-012. |
| 悬浮分页 | Partial | ✅ 已迁移 locally | `MovieWall.tsx` floating pagination. |
| 人工裁切卡图 | ❌ 未迁移 | 🟡 部分迁移 | `POST /api/videos/{movieId}/images/crop-card`, details crop dialog, media/favorites card context menu. Auto smart-card recognition remains incomplete. |
| 收藏页卡片右键图片菜单 | Missing before local changes | ✅ 已添加 locally | `CollectionPage.tsx` context menu routes to `cropMovieCard` and image generation. |
| 详情页大海报 | Small poster | ✅ 已调整 locally | `MovieDetailPage.tsx` uses `Poster` resource and larger grid column. |

Because these local changes are not committed/pushed, future reports should cite the commit only after Human approves commit/push.

## 5. Old Reference Classification

Legend:

- **A. Current runtime still depends**
- **B. Migration/data upgrade still depends**
- **C. Audit evidence only**
- **D. Completely unused / deletion candidate**
- **E. Cannot confirm yet**

| Reference / path | Class | Evidence | Cleanup decision |
|---|---|---|---|
| `backend/LocalMediaManager.Migration/**` | B | Tauri launch and deploy scripts publish/use `LocalMediaManager.Migration.exe`; migrations contain `LegacyIdMappings`, `MigrationWarnings`, legacy table mapping. | Keep. Forbidden to delete. |
| `backend/LocalMediaManager.Migration/migrations/*.sql` | B | Checksummed schema and upgrade path. | Permanently keep. |
| `src-tauri/src/lib.rs` migration resource lookup | B | Starts installed migration binary before app runtime. | Keep. |
| `src-tauri/resources/migration/**` | B | Packaged migration resources. | Keep. |
| `backend/LocalMediaManager.Bridge/Program.cs` `LMM_LEGACY_ROOT`, `LMM_CONFIG_DATABASE_PATH`, `LMM_IMAGE_ROOT` | A | Runtime fallback for legacy config/image compatibility. | Keep until a formally accepted end-of-compat decision. |
| `PlaybackSettingsService` legacy player fallback | A | Reads legacy config when Next playback path is empty. | Keep; user config compatibility. |
| `SettingsReader` + `CrawlerServerDto` | A / E | Reads compatible legacy settings/servers; plugin/provider UI still uses read-only compatibility. | Keep; cannot delete until provider/settings replacement complete. |
| `ImageAssetService.ImportLegacyActorAssetsAsync` | A | Cache rebuild imports legacy actor portraits into Images. | Keep; user image compatibility. |
| `/api/covers/{code}` and `FindCover` legacy `CardCovers` / `SmallPic` lookup | A | Runtime cover fallback for legacy image roots. | Keep until legacy image import is fully migrated and smoke-tested. |
| `ProductWriter.SyncLegacyFavoriteTagAsync` | A | Keeps legacy favorite tag compatibility while writing `UserMovieState`. | Keep; user data compatibility. |
| `LegacySource`, `LegacyId`, `LegacyIdMappings`, `MigrationWarnings` schema fields | A / B | Used by migration, actor repair, diagnostics, and data provenance. | Permanently keep for installed upgrade and auditability. |
| `FfmpegLocator.LegacyFallback` | A / E | Runtime candidate path for legacy bundled ffmpeg. | Keep for now; candidate for future removal only after packaged ffmpeg strategy is verified. |
| `docs/migration/**` | C | Required migration evidence and feature matrix. | Permanently keep. |
| `docs/audits/**` | C | Audit evidence, including this report. | Permanently keep. |
| `docs/database/LEGACY_TO_V1_FIELD_MAPPING.md` | C | Field mapping evidence. | Keep. |
| `docs/releases/**` legacy mentions | C | Release/smoke evidence. | Keep. |
| `AI_RULES.md` | E | Transitional root doc, superseded by `AGENTS.md` according to AGENTS. | Do not delete in this sprint; Human may approve a docs cleanup later. |
| `PROJECT_CONTEXT.md` | E | Transitional root doc, PROJECT says it is no longer first reading source. | Do not delete in this sprint; Human may approve a docs cleanup later. |
| `docs/MIGRATION_PLAN.md` | E | Older migration planning doc; not runtime. | Keep pending Human decision; may still be historical evidence. |
| `website/**` | E | Marketing/static site not in Tauri runtime path. | No deletion recommendation without separate website/deploy audit. |

No item currently qualifies for immediate deletion under the user's eight deletion conditions.

## 6. Required Feature Recheck

| # | Feature | Current entry | Current code | Usable? | Migration status | Missing | Legacy dependency |
|---:|---|---|---|---|---|---|---|
| 1 | 完整影片编辑 | Detail page has tag/actor/rating/favorite only | `MovieDetailPage.tsx`, `ProductWriter` lacks full movie update DTO | Partially | ❌ Not migrated | Full title/code/date/runtime/description/studio/director/genre/series/path edit workflow | No old code dependency; legacy audit evidence only |
| 2 | 厂商分类浏览 | Dashboard shows top studios; detail relation shows studio | `HomePage.tsx`, `MovieDetailPage.tsx`; no `/tags/studios`; entity API whitelist excludes studios | Partially readable | ❌ Not migrated | Add studio category under Tags and MovieWall default filter | DB has `Studios/MovieStudios`; no legacy code needed |
| 3 | 随机影片 | No toolbar action found | no `random` route/API/action | No | ❌ Not migrated | Random from current MovieWall query result range | No legacy code dependency |
| 4 | 复制影片信息 | No button/action found | no clipboard/copy movie info command | No | ❌ Not migrated | Detail/right-click copy formatted info | No legacy code dependency |
| 5 | 翻译简介 | No action found | no translate API; AI/provider not enabled | No | ❌ Not migrated / requires product decision | Translation provider boundary and privacy decision | No legacy code dependency |
| 6 | 图片 SetAs | No SetAs endpoint found | image replace/delete/generate/crop exist, no SetAs primary/type conversion | No | ❌ Not migrated | Set selected image as Poster, Thumbnail, or both with lock semantics | Depends on current Images schema, not old code |
| 7 | 智能卡图 | Details/media/favorites have manual crop | `ImageWorkflowService.CropCardAsync`, `/images/crop-card`, `GeneratedCard` thumbnail source | Partially | 🟡 Partial | Automatic recognition, re-detect workflow, task logs, setting-controlled auto-complete | Legacy rules retained as evidence |
| 8 | 图片列表批量删除 | Detail image cards delete one asset | `previewDeleteImage`, `deleteImage` only single asset | Partially | ❌ Not migrated | Delete all images in a current image list with preview | No legacy code dependency |
| 9 | 日志清理 | Settings has button but no delete | `SettingsPage.tsx` returns planned message; task cleanup exists separately | Task logs yes; app logs no | 🟡 Partial | Real app log cleanup with preview/scope | Log path from `DataSafetyService` |
| 10 | 语言设置 | No language category/setting found | Settings categories exclude language | No | ❌ Not migrated | i18n model and restart behavior | Legacy config read only, not runtime needed |
| 11 | 托盘与关闭行为 | Tauri close command exists for settings leave; no tray setting | `src-tauri/src/lib.rs`, no tray config | No | ❌ Not migrated | Tray icon, close-to-tray settings, lifecycle tests | No legacy code dependency |
| 12 | 快捷键配置 | Settings shows planned shortcut list | `ShortcutSection` displays neutral "规划" | No config | ❌ Not migrated | Save/configure shortcuts and conflict handling | No legacy code dependency |
| 13 | 检查更新 | About shows version/build only | `buildInfo`, Settings about; no update check endpoint | No | ❌ Not migrated | Update check policy/source | No legacy code dependency |
| 14 | 查重处理流程 | Read-only duplicate page | `/duplicates`, `DuplicateService`, `DuplicatesPage` | View only | 🟡 Partial | Ignore/merge/delete workflow with preview | No legacy code dependency |
| 15 | 批量整理 | Detail organizer supports one movie; Bridge accepts `movieIds` | `FileOrganizerService`, `organizerDryRun`, `MediaPage` no batch organizer action | Bridge yes, UI incomplete | 🟡 Partial | Batch selection entry and installed smoke | No old code dependency |
| 16 | NFO 详细设置 | Basic NFO settings exist | `NfoService`, `SettingsPage` output dir/policy/include images | Partially | 🟡 Partial | Old per-image/actor/screenshot/previews/path switches | No old code dependency |
| 17 | 扫描详情 | Task logs and library scan exist | `TasksPage`, `LibraryWorkflowService` | Partially | 🟡 Partial | Old categorized scan result detail page | No old code dependency |
| 18 | 播放器安装版验证 | Play endpoint and settings exist | `Program.cs` play route, `PlaybackSettingsService` | Code exists | 🟡 Partial | Default/custom player installed-app smoke | Legacy config fallback still used |
| 19 | Genre 分类 | Detail and Smart Search have genres | `ProductReader`, `MovieDetailPage`; no entity route | Partially | 🟡 Partial | Genre category route/list/filter if product keeps it | DB has `Genres/MovieGenres` |
| 20 | 演员资料完整度 | Actor list/edit basic fields + image endpoint | `EntityPage`, `ProductWriter.UpdateActorAsync`, `/api/actors/{id}/image` | Partially | 🟡 Partial | Full old actor fields and image management parity | Legacy actor portrait import still useful |

## 7. Running Legacy Compatibility Logic

These are not cleanup candidates:

- Read-only legacy database/config bootstrap through `LMM_LEGACY_ROOT`, `LMM_CONFIG_DATABASE_PATH`.
- Legacy image root and cover fallback through `LMM_IMAGE_ROOT`, `/api/covers/{code}`, and image cache.
- Legacy actor portrait import during image cache rebuild.
- Legacy favorite tag synchronization.
- Legacy source/provenance fields and mapping tables.
- Migration warnings and field mapping evidence.
- Playback settings fallback from old config.
- `Tags.Source` historical values (`LegacyLabel`, `LegacyStamp`) used only with status-badge exclusion, not as reliable custom/movie-tag split.

## 8. Cleanup Readiness

### 8.1 Can delete immediately

None.

Reason: no candidate satisfies all required proof conditions plus Web/Bridge/Tauri build and installed smoke after deletion.

### 8.2 Candidate only after a dedicated docs cleanup approval

| Candidate | Why it might be removable | Required proof before deletion |
|---|---|---|
| `AI_RULES.md` | AGENTS says it is no longer first reading source. | Confirm no active agent/tool references; update docs links if any; `rg`; `git diff --check`. |
| `PROJECT_CONTEXT.md` | PROJECT says it is absorbed into `PROJECT.md`. | Confirm no docs/user workflow links depend on it; preserve any missing unique facts. |
| `docs/MIGRATION_PLAN.md` | May be historical planning rather than current truth. | Human approval because it may be migration evidence. |

### 8.3 Delete only after feature completion

| Area | Keep until | Risk if deleted early |
|---|---|---|
| Legacy image root reading and `/api/covers/{code}` | All legacy images are imported or a formal end-of-compat decision exists | Existing covers/posters disappear. |
| `ImageAssetService.ImportLegacyActorAssetsAsync` | Actor image import parity and smoke complete | Actor portraits may disappear on rebuild. |
| Playback legacy config fallback | Playback settings migration and installed player smoke complete | Users lose old player path. |
| `SyncLegacyFavoriteTagAsync` | Favorite compatibility no longer needed and old tag path is fully retired | Legacy favorite state may desync. |
| SettingsReader compatible server/config reader | Provider/settings replacement is complete | Plugin/provider read-only evidence breaks. |
| `FfmpegLocator.LegacyFallback` | Packaged ffmpeg or user-configured ffmpeg path is verified | Image/GIF generation may fail on systems relying on old tools path. |

### 8.4 Permanently keep

- `docs/migration/**`
- `docs/audits/**`
- `docs/releases/**`
- `backend/LocalMediaManager.Migration/migrations/**`
- `LegacyIdMappings`, `MigrationWarnings`, `LegacySource`, `LegacyId` schema/data provenance
- Release/deploy backups under `D:\Local Media Manager Next Backups`

## 9. Recommended Cleanup Order

1. Finish uncommitted local feature work: commit/push MovieWall Display, detail poster, manual crop, and collection context-menu changes after Human review.
2. Update `FEATURE_PARITY_MATRIX.md` only after committed evidence exists.
3. Complete P0/P1 parity gaps: full movie edit, studio category, random movie, image SetAs, duplicate workflow, batch organizer.
4. Run installed smoke for player, NFO, image crop, and MovieWall state restoration.
5. Only then start a dedicated `Legacy Compatibility Retirement` design if Human wants to remove read fallbacks.
6. Start with non-runtime transitional docs only if Human explicitly approves.
7. Never remove migrations, audit evidence, or user-data compatibility in the same batch as UI feature cleanup.

## 10. Deletion Verification Method

For any future deletion batch:

1. `rg` exact file/type/function name across source, docs, scripts, project files.
2. Check `.csproj`, `package.json`, `tauri.conf.json`, `Cargo.toml`, PowerShell scripts.
3. Verify no runtime dynamic loading or environment fallback uses it.
4. Verify migration/upgrade path and installed smoke do not rely on it.
5. Delete one small batch only.
6. Run:
   - `pnpm build:web`
   - `dotnet test backend/LocalMediaManager.Bridge.Tests/LocalMediaManager.Bridge.Tests.csproj -c Release`
   - `cargo check --manifest-path src-tauri/Cargo.toml`
   - release build/deploy smoke if runtime behavior changed.
7. Record rollback point and exact deletion list.

## 11. Build / Test / Release

This sprint is documentation/audit only.

| Item | Result |
|---|---|
| Source deletion | None |
| Web build | Not run for this audit report; last local functional verification before audit passed |
| Bridge tests | Not run for this audit report; last local functional verification before audit passed 145 tests |
| Tauri / release build | Not run for this audit report |
| Installed smoke | Not run for this audit report |
| Git commit | Not created |
| Push | Not performed |
| Rollback point | Current branch HEAD `12f9116`; uncommitted working tree remains |

## 12. Next Recommended Sprint

Recommended next feature sprint:

1. **Movie Editing Parity**: full movie field edit with Preview/Save and existing detail/MovieWall state restoration.
2. **Studio Category + Random Movie**: small high-value MovieWall reuse tasks once editing scope is not active.
3. **Image Operations Parity**: image SetAs, image-list batch delete, and complete smart-card task workflow.

Do not start cleanup deletion until the uncommitted local changes are committed/pushed or intentionally reverted by Human.

## 13. Confidence

**82%**

Reasons:

- Repository/remote state and uncommitted files were directly checked.
- Required knowledge and legacy documents were read.
- Old references were searched across runtime source, scripts, migrations, and docs.
- Key feature status was verified against routes, pages, Bridge API, and services.

Limits:

- No installed smoke was run for this audit.
- Some PROJECT output was large/truncated in terminal, so conclusions were cross-checked with targeted `rg` and source reads.
- No deletion was attempted, so deletion verification remains a future plan rather than executed proof.
