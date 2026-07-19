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
| 详情窗口左右浏览数据库全部影片 | `WindowConfig.Main.DetailWindowShowAllMovie` | 不再保留 / 产品决策取消 | Detail previous/next follows the current MovieWall query context, filters, sorting, and library scope. |
| 删除文件时同时删除影片信息 | `WindowConfig.Settings.DelInfoAfterDelFile` | 不再保留 / 产品决策取消 | Delete behavior is governed only by current Safe Delete / preview / confirm / data-protection workflow. |
| 扫描时识别番号开关 | `ScanConfig.FetchVID` | 不再保留 / 产品决策取消 | Next scan always derives the movie code from the file name. |

Additional product decisions confirmed on 2026-07-19 by DEC-013:

| Feature | Old expectation | New status | Cleanup meaning |
|---|---|---|---|
| 完整影片编辑器 / 全字段影片编辑 | Legacy edit windows and metadata forms | 不再保留 / 产品决策取消 | Do not build a full Movie Editor. Metadata is owned by scraping, NFO import, and metadata sync. |
| 手动编辑影片元数据 | Title, original title, code, plot, date, runtime, director, studio, series, Genre/movie tags | 不再保留 / 产品决策取消 | Incorrect metadata is fixed by re-scrape or NFO re-import, not manual DB edits. |
| 添加演员 / 删除演员 / 搜索演员 / 手动修改演员资料 | Legacy actor relation/editor functions | 不再保留 / 产品决策取消 | Actor entities and movie actor sets are metadata-owned. LMM only keeps display order. |
| 显示标题 / 自定义标题 / 第二标题 | Potential replacement/custom title fields | 不再保留 / 产品决策取消 | Do not add extra title fields; MovieWall/details display scraped/NFO/synced title data. |
| 已观看开关 | Separate watched boolean | 不再保留 / 产品决策取消 | Watched state is represented by play count, last played time, and recent playback. |

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
| 页码输入跳转 | ❌ 未迁移 | ✅ 已迁移 | `MovieWall.tsx` `FloatingPagination`, current-page click input, Enter/Esc behavior. Commit `bbed780`. |
| 左右方向键翻页 | ❌ 未迁移 | ✅ 已迁移 | `MovieWall.tsx` handles `ArrowLeft` / `ArrowRight` and ignores text inputs. Commit `bbed780`. |
| `Ctrl+G` page input focus | Not listed separately | ✅ 已迁移 | `MovieWall.tsx` `pageInputFocusSignal`. Commit `bbed780`. |
| 横版/竖版海报、小/中/大尺寸 | Partial/readonly in matrix | ✅ 已迁移 | `UnifiedSettings.movieWallDisplay`, `MediaCard`, Settings appearance section, DEC-012. Commit `bbed780`. |
| 悬浮分页 | Partial | ✅ 已迁移 | `MovieWall.tsx` floating pagination. Commit `bbed780`. |
| 人工裁切卡图 | ❌ 未迁移 | 🟡 部分迁移 | `POST /api/videos/{movieId}/images/crop-card`, details crop dialog, media/favorites card context menu. Auto smart-card recognition remains incomplete. Commit `93d9a11`. |
| 收藏页卡片右键图片菜单 | Missing before local changes | ✅ 已添加 | `CollectionPage.tsx` context menu routes to `cropMovieCard` and image generation. Commit `7ba4e20`. |
| 详情页大海报 | Small poster | ✅ 已调整 | `MovieDetailPage.tsx` uses `Poster` resource and larger grid column. Commit `93d9a11`. |

These implementation commits were pushed in Repository Stabilization on branch `sprint/0.5.0-20-moviewall-display`.

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
| Legacy settings one-time migration in `SettingsSaveCoordinator` | A | Migrates selected old values into `UnifiedSettings` without exposing internal fields in Settings UI. | Keep until a formal end-of-compat decision and upgrade evidence exist. |
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
| 1 | 完整影片编辑 | Detail page has user-state and metadata workflow actions only | `MovieDetailPage.tsx`, `ProductWriter` intentionally has no full movie update DTO | Not planned | 不再保留 / 产品决策取消 | None. DEC-013 cancels full metadata editor, display/custom title fields, actor add/delete/search, and watched toggle. | Keep legacy evidence only; do not develop. |
| 2 | 厂商分类浏览 | Dashboard shows top studios; detail relation shows studio | `/tags` category toolbar + `GET /api/entities/studios` + MovieWall `studioId` | Yes | ✅ Migrated | None | DB has `Studios/MovieStudios`; no legacy code needed |
| 3 | 随机影片 | Toolbar random action | `GET /api/search/random`; MovieWall random button | Yes | ✅ Migrated | None | No legacy code dependency |
| 4 | 复制影片信息 | Detail more menu copy action | `MovieDetailPage` clipboard formatter; no DB re-query | Yes | ✅ Migrated | None | No legacy code dependency |
| 5 | 翻译简介 | No action found | no translate API; AI/provider not enabled | No | ❌ Not migrated / requires product decision | Translation provider boundary and privacy decision | No legacy code dependency |
| 6 | 图片 SetAs | No SetAs endpoint found | image view/zoom/crop/refresh remain; no SetAs route | No | 不再保留 / 产品决策取消 | Manual SetAs poster/thumbnail/banner is cancelled by DEC-014 | Images are owned by MetaTube, NFO, and metadata sync |
| 7 | 智能卡图 | Details/media/favorites have manual crop | `ImageWorkflowService.CropCardAsync`, `/images/crop-card`, `GeneratedCard` thumbnail source | Partially | 🟡 Partial | Automatic recognition, re-detect workflow, task logs, setting-controlled auto-complete | Legacy rules retained as evidence |
| 8 | 图片列表批量删除 | Detail image cards delete one asset | `previewDeleteImage`, `deleteImage` only single asset | Partially | ❌ Not migrated | Delete all images in a current image list with preview | No legacy code dependency |
| 9 | 日志清理 | Settings 日志与诊断 | `LogMaintenanceService`, `/api/system/logs/cleanup-preview`, `/api/system/logs/cleanup`, `SettingsPage.tsx` | Yes | ✅ Migrated | None in retained scope | No legacy code dependency; keeps active logs and user data |
| 10 | 语言设置 | Settings 常规 | `UnifiedSettings.system.language`, `SettingsSaveCoordinator`, `SettingsPage.tsx` | Yes | ✅ Migrated | Full English translation is not exposed because resources are incomplete | Legacy config read only, not runtime needed |
| 11 | 托盘与关闭行为 | Settings 常规 + Tauri tray | `src-tauri/src/lib.rs`, `AppShell.tsx`, `SettingsPage.tsx` | Yes | ✅ Migrated | None in retained scope | No legacy code dependency |
| 12 | 快捷键配置 | Settings 快捷键 | `UnifiedSettings.system.globalShortcutsEnabled`, `MovieWall.tsx`, `AppShell.tsx` | Yes | ✅ Migrated | Complex shortcut recorder is intentionally out of retained scope | No legacy code dependency |
| 13 | 检查更新 | Settings 关于 | `UpdateCheckService`, `/api/system/update/check`, `SettingsPage.tsx` | Yes | ✅ Migrated | Automatic download/install intentionally not provided | No legacy code dependency |
| 14 | 查重处理流程 | Organizer duplicate workspace | `/organizer`; `DuplicateOrganizerWorkflowService`; `SafeDeleteWorkflowService` | Yes | ✅ Migrated | None in retained scope | Safe Delete remains current dependency; no old code dependency |
| 15 | 批量整理 | Organizer batch workspace | `/organizer`; MovieWall selection; `FileOrganizerService` dry-run/preview/execute/tasks | Yes | ✅ Migrated | None in retained scope | No old code dependency |
| 16 | NFO 详细设置 | Basic NFO settings exist | `NfoService`, `SettingsPage` output dir/policy/include images | Partially | 🟡 Partial | Old per-image/actor/screenshot/previews/path switches | No old code dependency |
| 17 | 扫描详情 | Task logs and library scan exist | `TasksPage`, `LibraryWorkflowService` | Partially | 🟡 Partial | Old categorized scan result detail page | No old code dependency |
| 18 | 播放器安装版验证 | Play endpoint and settings exist | `Program.cs` play route, `PlaybackSettingsService` | Code exists | 🟡 Partial | Default/custom player installed-app smoke | Legacy config fallback still used |
| 19 | Genre 分类 | Detail and Smart Search have genres | `ProductReader`, `MovieDetailPage`; no entity route | Partially | 🟡 Partial | Genre category route/list/filter if product keeps it | DB has `Genres/MovieGenres` |
| 20 | 演员资料完整度 | Actor list/basic image endpoint exist | `EntityPage`, `/api/actors/{id}/image` | Limited by design | 不再保留 / 产品决策取消 for manual actor profile editing | Manual actor profile edits/add/delete/search are cancelled by DEC-013. Actor display ordering remains a retained user-data feature. | Legacy actor portrait import still useful. |

## 7. Running Legacy Compatibility Logic

These are not cleanup candidates:

- Read-only legacy database/config bootstrap through `LMM_LEGACY_ROOT`, `LMM_CONFIG_DATABASE_PATH`.
- Legacy image root and cover fallback through `LMM_IMAGE_ROOT`, `/api/covers/{code}`, and image cache.
- Legacy actor portrait import during image cache rebuild.
- Legacy favorite tag synchronization.
- Legacy source/provenance fields and mapping tables.
- Migration warnings and field mapping evidence.
- Playback settings fallback from old config.
- Legacy settings one-time migration remains in Bridge; the user Settings page no longer displays internal compatibility keys.
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
| SettingsReader compatible server/config reader and one-time settings migration | Provider/settings replacement and installed upgrade compatibility are complete | Legacy settings values stop migrating for existing users; plugin/provider read-only evidence breaks. |
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
3. Retained Feature Parity is complete as of Final System Features Migration. The next Sprint should be Legacy Cleanup, not another migration or product-optimization sprint.
4. Run installed smoke for player, NFO, image crop, MovieWall state restoration, and each retained parity gap.
5. When retained Feature Parity reaches 100% and the new implementation is stable, start a dedicated Legacy Cleanup Sprint.
6. Only during Legacy Cleanup should fully replaced, unreferenced old code/pages/resources be deleted. Keep database migrations, upgrade compatibility, scoring/favorite/tag/image-path compatibility, `docs/migration`, `docs/audits`, and source evidence unless Human explicitly approves retirement.
7. After Legacy Cleanup is complete, enter product optimization/new-feature phase where Human experience needs take priority over legacy UI parity.

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

Recommended next sprint:

1. **Legacy Cleanup**: remove only old code, old pages, old resources, and old references that are fully replaced and no longer referenced.
2. Keep database migrations, upgrade compatibility, scoring/favorite/tag/image-path compatibility, `docs/migration`, `docs/audits`, and source evidence unless Human explicitly approves retirement.

Retained Feature Parity has reached 100% by current product scope. Start cleanup deletion only after Human explicitly approves the Legacy Cleanup Sprint.

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
