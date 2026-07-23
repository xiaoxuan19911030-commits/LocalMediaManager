# Local Media Manager Roadmap

## Current Release

- **Version:** 0.6.2
- **Release:** MovieWall Polish
- **Branch policy:** Trunk-based development. `main` is the only long-lived development branch after this release.
- **Restore policy:** Official version recovery uses immutable release tags, starting with `v0.6.0`; rollback branches are no longer long-term restore points.

## Unreleased - Media Library Types

**Status:** Develop / self-test
**Date:** 2026-07-23

Scope:

- Add extensible Standard and Local media library types; migrate existing libraries to Standard.
- Preserve the Standard metadata workflow unchanged.
- Isolate Local scans from number recognition and all metadata Providers.
- Reserve stable MovieId-based cover/screenshot paths, lifecycle fields, and screenshot service boundary.
- Add library creation type selection without changing the movie detail UI.

## 0.6.2 - MovieWall Polish

**Status:** Release candidate / verification in progress
**Date:** 2026-07-20

Scope:

- MovieWall only. Details page polish was skipped by explicit product instruction.
- Filters open by default and remember the last expand/collapse preference.
- MovieWall hero/header spacing is reduced.
- Card/list ratings can be edited directly from the MovieWall.
- Context menu behavior is fixed and simplified.
- Edit mode actions are consolidated into the filter toolbar.
- Random movie stays inside the MovieWall result surface instead of opening Details.

## Unreleased - Data Center Consolidation

**Status:** Develop / verification in progress
**Date:** 2026-07-20

Scope:

- Metadata Center, Diagnostics Center, Maintenance, and Duplicate Movies are consolidated into one Data Center navigation entry.
- Data Center uses three tabs: 概览, 问题, 重复影片.
- Duplicate Movies remains backed by the existing organizer and Safe Delete workflow.
- Legacy routes redirect to the matching Data Center tab.
- Polish pass turns the overview into a compact health dashboard and renames 诊断与修复 to 问题 with search, source, reason, status, and recommendation details.
- JavBus is restored as an optional metadata source and wired into the existing Sync task pipeline, settings plugin center, and Data Center repair guidance.
- No database schema changes, no Details changes, and no AI features.

## 0.6.1 - Task Center Polish

**Status:** Released
**Date:** 2026-07-20

Scope:

- Task Center only.
- Status filter: 全部任务, 执行中, 已完成, 已失败, 已取消.
- Type filter: 全部类型, 同步信息, 扫描影片, 生成截图, 生成 GIF, 重命名, 删除影片, with future task types surfaced automatically.
- Single dynamic action button: 清除任务 by default, 取消任务 while viewing 执行中.
- Clear tasks removes only terminal tasks and never removes running, queued, or paused work.
- Task cards no longer expose database IDs, TaskID, MovieID, or full file paths; they show movie code first and file name as fallback.
- Sync tasks are consumed automatically by the worker whenever MetaTube is enabled; the old AutoExecute value no longer blocks queued tasks.

## 0.6.0 — Release Polish V1

**Status:** Release candidate / verification in progress
**Date:** 2026-07-20

Scope:

- Settings is reduced to 常规、外观、搜索与筛选、元数据、插件中心、媒体资源、快捷键、数据与备份、关于.
- Plugin Center moves into Settings; FFmpeg becomes a system tool entry under Plugin Center.
- NFO is no longer a visible configuration page. It remains fixed automatic metadata output behavior.
- Appearance owns MovieWall/detail image source, card/list default view, poster direction, and poster size.
- Search & Filter keeps only default sort and default filter.
- Data & Backup keeps backup/restore and adds automatic backup on exit with frequency and retention.
- Duplicate Movies stays as a dedicated navigation entry; Organizer/Batch Organizer navigation is removed.
- Tags page 全部 summarizes 导演、标签、系列、厂商、自定义标签; each concrete category still uses its own source table.
- Library pages hide repeated source folder details; library editing uses a folder picker, standard scanning, and a maximum of three source folders.

After 0.6.0, development continues on `main` by default. Temporary branches are allowed only for high-risk work and must be merged, verified, and deleted promptly.

> 本文档是版本范围的唯一正式入口。范围调整必须同时更新 Roadmap、TODO 和 Changelog；聊天中的临时想法未进入本文档前不视为正式版本承诺。

## 版本生命周期

所有版本统一经过以下阶段：

```text
Planning → Design → Develop → Self Test → Smoke Test → Freeze → Release → Archive
```

- **Planning**：确定目标、优先级、依赖、风险与验收方式。
- **Design**：完成 Bridge/DTO/Migration/Tasks/UI 设计，不先做孤立页面按钮。
- **Develop**：按架构和 UI 规范实施，持续更新功能矩阵证据。
- **Self Test**：开发者执行自动化测试、静态检查和目标功能自测。
- **Smoke Test**：在真实数据库与独立安装目录执行关键用户路径。
- **Freeze**：停止新增功能，只处理阻塞发布的缺陷。
- **Release**：完成全量构建、安装包、备份部署、标签和发布记录。
- **Archive**：冻结发布证据、迁移状态和遗留事项，后续不移动版本标签。

## 0.4.0 — Modern Media Experience

**状态：Release / Frozen**
**发布日期：2026-07-15**

`v0.4.0` 标签已经冻结。该版本不再增加功能；必要缺陷修复必须单独记录，功能开发进入 0.4.1。

- Modern UI Experience
- Dashboard
- Movie Wall 与现代影片卡片
- Information Layout 影片详情
- Search 2.0
- Library 只读产品视图
- Actors、Tags、Favorites、History
- Metadata Center
- Diagnostics Center
- Tasks Center 基础
- Plugin Center Placeholder
- AI Provider Placeholder
- 深浅主题、响应式和 Windows 缩放验证

## 0.4.1 — Legacy Feature Migration Part 1

**状态：Release / Frozen**
**发布日期：2026-07-15**

目标是恢复最高频用户写操作与用户状态兼容，不恢复旧 UI。

### 第一优先：用户状态与标签

- 收藏写入与旧收藏标签兼容
- 评分写入
- 自定义标签创建、编辑、删除
- 单部与批量标签绑定
- 所有写操作通过 Bridge，具备错误提示与刷新一致性

### 第二优先：删除评分记忆

- 删除影片前仅保存“文件名 + 评分”
- 同名文件重新导入时恢复评分
- 已有新评分时禁止覆盖
- 删除、恢复和失败场景具备测试

### 第三优先：演员关系

- 演员关系编辑与正确写入
- ActorID=0、空演员和损坏关系诊断
- 修复预览、确认、任务日志和可回滚策略

### 第四优先：播放体验

- 上一部、下一部沿用原结果集
- 播放成功后写入次数和历史
- 外部播放器路径与失败回退

### 第五优先：详情页操作能力

- 收藏、评分、标签、编辑和同步入口
- 打开文件夹、文件信息与用户状态
- 全部复用 Next Information Layout 与公共组件

### 发布结论

- 收藏、评分、自定义标签、演员资料与影片演员关系已恢复真实写入。
- 删除影片记录具备影响预览、数据库备份和文件名评分记忆；不删除媒体文件。
- ActorID=0 修复具备预览、确认、任务记录、自动化测试和操作前数据库备份。
- 播放正常退出后写入播放次数与历史；上一部/下一部保持影片墙搜索和排序上下文。
- 同名评分恢复 Bridge 能力已完成；与扫描导入流程的自动触发在 0.4.2 接入，不提前伪装为完整导入迁移。

## 0.4.2 — Legacy Feature Migration Part 2

**状态：Completed（发布内容并入 0.4.3）**

- 媒体扫描与导入
- 新增后自动同步
- MetaTube Provider
- 同步执行器、非破坏写入与任务恢复
- 扫描和同步长操作接入 Tasks

开发证据：扫描导入、MetaTube 同步执行器、Migration `0004`/`0005`、任务控制与非破坏写入均已完成；真实 Provider 写入验收在 0.4.3 的隔离 30 部烟测中完成，因此该批次随 0.4.3 一并发布。

## 0.4.3 — Media Assets & File Organization

**状态：Released（2026-07-16）**

- poster、thumb、fanart、BigPic、ExtraPic、演员图与安全图片缓存
- 完整 NFO 读取、写入、所有权和覆盖策略
- 文件整理 Dry Run、预览、确认、执行、审计与恢复
- MetaTube 30–50 部真实样本烟测
- Bridge Release 后台运行、日志、单实例和退出生命周期
- 标签编辑、详情海报和播放器路径等限定交互修复

正式范围和验收门槛见 [`sprints/SPRINT_0.4.3.md`](sprints/SPRINT_0.4.3.md)。原 Stability Update 中的数据库、性能、内存和大媒体库优化归入 0.5.5 LTS。

2026-07-16 已完成图片、NFO、文件整理、限定交互修复、30 部真实 MetaTube 隔离烟测、Debug/Release 门禁、NSIS 安装复验及 Bridge Release 生命周期验收。未完成的智能卡图、敏感 Header/Cookie、完整任务类型和浏览体验项保留在 TODO/Feature Parity Matrix，不以本次发布冒充完成。

## 0.5.0 — Feature Parity Release

**状态：Feature Freeze / Retained Feature Parity Complete**

### 2026-07-19 Scope Update — Metadata Ownership

Local Media Manager no longer plans a full Movie Editor or manual metadata editing parity track. Metadata fields are owned by scraping, NFO import, and metadata sync. The following legacy expectations are product-cancelled and do not count as remaining Feature Parity gaps:

- 完整影片编辑器 / 全字段影片编辑
- 手动编辑标题、原始标题、番号、简介、上映日期、年份、时长
- 手动修改导演、厂商、系列、影片标签 / Genre
- 添加演员、删除演员、搜索演员、手动修改演员资料
- 显示标题、自定义标题、第二标题
- 独立“已观看”开关

Retained user-data features remain in scope: scoring, favorite, custom tags, actor display ordering, poster/image adjustment, manual crop, playback history, and future explicit user notes. Image SetAs is product-cancelled because image resources are managed by MetaTube scraping, NFO, and metadata sync.

### Retained Feature Parity Priority

- 当前无待迁移项。以 2026-07-19 的产品取舍为准，保留旧版功能迁移已完成；下一阶段只能启动 Legacy Cleanup，不再追加产品优化或新功能。

### Completed Retained Parity

- Final System Features Migration: Settings now includes app language (`system` / `zh-CN`), tray and close behavior, start minimized to tray, global shortcut enablement, log retention and cleanup preview/execute, and GitHub release update checks. Close exit checks active tasks before quitting, and minimize-to-tray does not stop Bridge.
- Settings Migration Completion: Settings no longer exposes legacy compatibility keys to ordinary users. `ScanConfig.MinFileSize` is migrated into the formal `scan.minFileSizeMb` setting, and library scans use it to skip video files smaller than the configured MB threshold.
- Legacy settings product cancellations: `WindowConfig.Main.DetailWindowShowAllMovie`, `WindowConfig.Settings.DelInfoAfterDelFile`, and `ScanConfig.FetchVID` are not retained as user-facing switches; detail navigation follows MovieWall context, deletion follows Safe Delete, and scan filename recognition is fixed behavior.

- Duplicate Management & Batch Organizer Execution Completion: `/organizer` now supports duplicate-group Safe Delete execution and batch move/rename execution. Duplicate deletion requires one explicit keep item per selected group, previews user-data merge effects, revalidates before execute, and delegates real deletion to Safe Delete. Batch move and batch rename reuse the existing File Organizer dry-run, preview, execute, and Tasks workflow for the current MovieWall selection set.

- Organizer UI Completion: `/organizer` uses a left Organizer Tools rail with Duplicate Movies and Batch Organizer. Duplicate groups show poster, title, code, file name, file size, resolution, rating, favorite, library, file path, true duplicate reason, expand/collapse, select/cancel, and non-binding keep suggestions. Batch Organizer reuses MovieWall and exposes a single batch action bar.

- 厂商分类浏览：`/tags` 横向工具栏接入 `Studios/MovieStudios`，并通过统一 MovieWall 的 `studioId` 默认条件进入厂商影片集合。
- MovieWall 随机影片：所有 MovieWall 页面工具栏提供随机按钮，随机范围复用当前默认条件、Smart Search、FilterBar 和媒体库范围。
- 复制影片信息：详情页更多菜单可将当前详情模型中的标题、番号、演员、厂商、系列、发行日期、评分、文件路径、媒体库和简介复制到系统剪贴板。
- 查重与批量整理第一阶段：新增 `/organizer` 整理工具统一入口，合并重复影片与批量整理架构；旧 `/duplicates` 仅保留兼容重定向。

### Product-Cancelled Parity

- Image SetAs：不再迁移“设为海报 / 设为缩略图 / 设为横幅”。图片资源由 MetaTube 刮削、NFO 和元数据同步统一管理；保留图片查看、放大、人工裁切、刷新和重新下载图片。

### Fixed Development Order

1. Finish all retained legacy feature migration and update `docs/migration/FEATURE_PARITY_MATRIX.md` after each feature.
2. After retained Feature Parity reaches 100% and the new implementation is stable, run a dedicated Legacy Cleanup Sprint.
3. After Legacy Cleanup, move into product optimization, UI redesign, and new feature development driven by Human experience needs rather than legacy UI parity.

### Migration Execution Policy

- Do not create standalone audit sprints for ordinary empty data or low-value sparse fields.
- Pause retained feature migration only for data corruption risk, user-data loss risk, schema incompatibility, clear old/new result mismatch, release build failure, installed app startup failure, or unusable core functionality.
- Record non-blocking empty data and low-use-field gaps in documentation, then continue retained Feature Parity migration.

- 对 0.4.1–0.4.3 的媒体管理功能执行最终等价验收；审计结论见 [`audits/FEATURE_PARITY_AUDIT_0.5.0-01.md`](audits/FEATURE_PARITY_AUDIT_0.5.0-01.md)。
- 首先关闭 P0 的任务生命周期、安装版扫描导入、受保护元数据写入、查重安全工作流和文件操作故障恢复。
- 统一 Bridge DTO、错误、日志、超时、重试和任务状态；数据库、Bridge、性能审计分别记录在 `docs/audits/`。
- 仅修复阻塞功能的 UI 问题；不新增 AI、NAS、插件市场、OCR、语义搜索、推荐、助手或大型 UI 重构。
- 达到完整迁移门槛后才发布主要旧版媒体管理功能等价版本。

## 0.5.5 — LTS Stability

**状态：Future**

0.5.0 功能等价完成后，先建立长期稳定基线，再进入 AI：

- 集中修复 Bug 和迁移边界问题
- 优化启动速度、内存和图片生命周期
- 优化数据库查询、索引和 Bridge 并发
- 验证 5 万至 10 万影片的大媒体库体验
- 扩充自动化测试、安装升级和回滚测试
- 建立性能基准、长期运行和故障恢复报告

0.5.5 不引入新的大型产品功能；Release Polish V1 已占用 0.6.0，真实 AI 继续后移。

## 0.7.0 — AI

**状态：Future**

- Provider 设置与安全凭据
- 连接测试
- 单部影片标签与元数据建议
- 建议预览、用户确认、应用日志
- 不在首个 AI 版本开放无人值守批处理

## 0.8.0 — NAS

**状态：Future**

- NAS 来源与连接状态
- 路径映射、断线恢复和只读扫描
- 凭据安全存储

## 0.9.0 — Plugin Marketplace

**状态：Future**

- 插件清单、安装、启停、更新与隔离
- 权限声明、兼容版本和故障恢复

## 1.0.0 — Beta

**状态：Future**

- 迁移与升级回归
- 大型媒体库压力测试
- 安装、卸载、备份与恢复验证

## 1.0.0 — Stable Release

**状态：Future**

- 稳定的核心媒体管理工作流
- 正式升级策略、支持矩阵与用户文档
- 已知高风险数据操作全部具备确认和回滚

## 版本完成门槛

每个版本结束前必须完成：

1. 更新 Roadmap、Changelog 与 TODO。
2. Web、Bridge、Migration、Tauri 的 Debug/Release 构建。
3. Windows NSIS 安装包。
4. 现有安装目录备份、独立 Next 目录部署和烟测。
5. 深浅主题与目标缩放验证。
6. Git Commit、版本标签和阶段前回滚标签。
7. 发布验证报告和明确的未完成清单。
