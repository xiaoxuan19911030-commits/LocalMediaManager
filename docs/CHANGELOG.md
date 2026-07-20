# Changelog

本项目遵循语义化版本。未发布内容进入 `[Unreleased]`；发布时归入对应版本，并同步更新 Roadmap 与 TODO。

## [Unreleased]

### UI Improvements

- 元数据中心、诊断中心和重复影片统一归并为“数据中心”，左侧工具区只保留一个数据维护入口。
- 数据中心提供“概览 / 问题 / 重复影片”分页；重复影片继续复用现有 Safe Delete 工作流。
- 影片墙在元数据或图片缺失筛选下提供“在数据中心查看”入口，跳转后自动带入诊断筛选。
- 数据中心概览改为仪表盘：新增媒体库健康度、建议处理、可点击统计卡和紧凑资源统计。
- 数据中心“诊断与修复”改名为“问题”，并增强问题原因、状态、建议、来源、颜色标识和当前页搜索。

### Compatibility

- 旧 `/metadata`、`/diagnostics`、`/duplicates`、`/maintenance` 路由保留兼容重定向，不删除用户数据，不修改数据库 Schema。
- 本轮不修改详情页、数据库 Schema、Safe Delete 工作流或重复影片算法。

### Bug Fixes

- 修正数据中心和维护统计的影片总数口径：只统计当前有主视频且文件可用的影片，不再把缺失文件记录、无视频记录和历史迁移残留计入“影片总数”。

## [0.6.2] - 2026-07-20

### Features

- MovieWall star rating is now editable directly on cards and list rows; changes are saved immediately through the existing user-state API.

### UI Improvements

- MovieWall filter areas now open by default and remember the user's last expanded/collapsed preference.
- MovieWall header spacing is reduced so the wall shows more useful content above the fold.
- MovieWall edit actions moved into the filter toolbar. Edit mode now keeps the top controls focused on selected count, select/cancel current page, cancel selection, and finish.
- Movie context menus are narrower, denser, icon-free, and close correctly on another right-click, blank click, wheel, Escape, window blur, or command execution.
- Movie context menus remove Edit, Generate Poster, and Generate Preview; Screenshot and GIF generation remain available.
- Batch context menu is simplified to batch sync information, batch screenshot, batch GIF, batch rename placeholder, and delete movie.

### Bug Fixes

- Fixed manually queued metadata sync tasks staying pending when legacy AutoExecute/provider enabled settings disagreed.
- Fixed right-click menus remaining open when another movie is right-clicked.
- Fixed current-page selection so repeated select actions do not duplicate selected movie IDs.

### Behavior Changes

- Random movie now keeps the user on the MovieWall and replaces the current results with one random movie from the active query scope, instead of opening the detail page.
- Detail page polish from the original 0.6.2 request was intentionally skipped after product instruction: "details page: do not change this item."

### Compatibility

- No database schema changes.
- No metadata ownership changes.
- No change to Details page UI.

### Package

- `Local Media Manager_0.6.2_x64-setup.exe`

## [0.6.1] - 2026-07-20

### Features

- 新增任务类型筛选：全部类型、同步信息、扫描影片、生成截图、生成 GIF、重命名、删除影片，并支持未来任务类型自动扩展。
- 新增任务状态筛选：全部任务、执行中、已完成、已失败、已取消；执行中统一包含等待中、排队中和运行中的任务。
- 新增当前筛选结果范围内的批量取消任务能力。

### UI Improvements

- 任务中心顶部改为一行布局：状态、类型、动态主按钮。
- 主按钮根据状态自动切换为“清除任务”或“取消任务”。
- 任务列表移除数据库 ID、TaskID、影片 ID 等内部编号，优先显示番号，没有番号时显示文件名。
- 任务来源继续显示为 MetaTube、本地扫描、FFmpeg、Safe Delete 等用户可理解来源。

### Bug Fixes

- 修复同步任务因旧 AutoExecute 开关关闭而长期停留在等待中的问题。
- 修复“全部任务”清除规则：现在只清除已完成、已失败、已取消任务，不影响执行中任务。
- 修复任务中心批量取消只能处理同步任务的问题，改为支持当前筛选结果内的活动任务。

### Performance

- 任务中心类型筛选在前端基于已加载任务列表组合过滤，不额外增加数据库查询。
- 同步任务队列固定由后台 Worker 自动消费，避免任务创建后等待用户配置触发。

### Known Issues

- GitHub Release 创建需要本机具备 GitHub CLI 或可用 GitHub API Token；无授权环境下只能完成本地构建、Tag 和 Git Push。

### Compatibility

- 不修改数据库 Schema。
- 不修改任务表结构。
- 不需要重新扫描。
- 不需要重新同步。
- 兼容 0.6.0 数据库、配置、任务记录和插件目录。

### Upgrade Notes

- 可直接覆盖升级 0.6.0。
- 已存在的等待中同步任务在 MetaTube 启用后会由 Worker 自动领取执行。

### Package

- `Local Media Manager_0.6.1_x64-setup.exe`

## [0.6.0] - 2026-07-20

### Release Polish V1

- Settings navigation is slimmed to 常规、外观、搜索与筛选、元数据、插件中心、媒体资源、快捷键、数据与备份、关于.
- Plugin Center is moved from the left navigation into Settings, and FFmpeg is exposed there as a system tool with detection, version display, plugin directory access, download link, and refresh.
- Removed ordinary-user Settings pages for 扫描和导入 and NFO. NFO generation is fixed product behavior and remains handled by scraping/NFO/metadata sync code, not by a visible toggle.
- Appearance now owns MovieWall image source, detail image source, poster orientation, poster size, and default card/list view. User-facing image terms are 海报、缩略图、背景图.
- Search & Filter settings now only keep default sort and default filter.
- Data & Backup now keeps immediate backup and restore backup, and adds automatic backup on exit with frequency and retention settings.
- About now shows only software version, build time, and database status; internal Commit, Bridge, and data directory details are no longer shown to ordinary users.
- Duplicate Movies remains a dedicated navigation item. Organizer/Batch Organizer navigation is removed; future batch management belongs in MovieWall 批量操作.
- Duplicate groups are more compact, keep recommendations are shown as small tags, and the page adds 删除所有未保留影片 via the existing Safe Delete confirmation flow.
- Tags page 全部 now summarizes 导演、标签、系列、厂商、自定义标签 totals while each category keeps its own query semantics.
- Library cards no longer repeat source folder lists; edit-library source folders use the Windows folder picker, use standard scan behavior, and are limited to three source folders.

### Versioning

- The official product version is 0.6.0 across package metadata, Tauri config, Bridge/Migration assemblies, runtime health/update checks, About, installer metadata, and documentation.
- Development strategy changes to trunk-based development: `main` is the long-lived branch, official restore points are release tags such as `v0.6.0`.

### Product Decisions

- Metadata Ownership Decision (DEC-013): LMM no longer plans a full Movie Editor or manual movie metadata editing. Movie metadata is owned by scraping, NFO import, and metadata sync; incorrect metadata should be fixed by re-scrape/re-sync/NFO re-import.
- Product-cancelled legacy expectations: full-field movie edit, manual edits for titles/code/plot/date/runtime/director/studio/series/tags/Genre, actor add/delete/search/manual profile edit, display/custom/second title fields, and standalone watched toggle.
- Product-cancelled Image SetAs: LMM will not migrate manual "set as poster / thumbnail / banner" actions because image resources are managed by MetaTube scraping, NFO, and metadata sync.
- Duplicate Management & Batch Organizer (DEC-015): duplicate review and batch organizer now share one Organizer Tools entry instead of separate product tracks.
- Final System Features and Feature Freeze (DEC-016): retained Feature Parity is complete by current product scope; the next phase is Legacy Cleanup.
- Settings Migration Completion (DEC-017): legacy configuration fields are no longer exposed as user settings. Compatibility reading remains backend-only for one-time migration and diagnostics.
- Product-cancelled legacy settings: detail-window full-database browsing, delete-info-after-file-delete, and scan-number-recognition toggles are not retained because current MovieWall context, Safe Delete, and scan filename recognition define the product behavior.
- Retained user-data scope: rating, favorite, custom tags, actor display ordering, poster/image adjustment, manual crop, playback history, and future Human-approved notes.
- Development order is now fixed: retained Feature Parity first, Legacy Cleanup second, Human-experience-driven optimization/new features third.

### Added

- Final System Features Migration: Settings now includes language selection (`system` / `zh-CN`), tray and close behavior, start minimized to tray, global shortcut enablement, log retention and cleanup, and GitHub Release update checks.
- Settings now includes a formal scan minimum file size setting (`scan.minFileSizeMb`) shown as “最小影片文件大小（MB）”; library scans ignore videos smaller than this threshold.
- Legacy settings migration now imports known old values once when the new value is still at its default seed, and never overwrites non-default new settings.
- Log cleanup now uses a preview + confirmation-token flow, supports 7/14/30/90 days and permanent retention, keeps active logs, and never deletes databases, settings, task records, or user media.
- Tauri tray support now provides show, hide, and quit actions. Quit checks running tasks from the frontend before closing; close-to-tray keeps Bridge running.
- Shortcut management now exposes the retained shortcut set and a global enable switch. MovieWall paging shortcuts and Ctrl+G honor the switch and protect input/modal focus; Ctrl+F focuses global search.

- 标签二级页改为横向工具栏浏览，支持范围切换、全部/导演/标签/系列/厂商/自定义分类切换、四种排序，以及返回状态恢复。
- 标签二级页将刮削元数据 Genre 统一显示为“标签”，内部 API 与数据库字段名保持不变。
- 厂商分类浏览接入 `Studios/MovieStudios`，点击厂商通过统一 MovieWall 的 `studioId` 默认条件显示对应影片集合。
- MovieWall 工具栏新增随机影片按钮，按当前默认条件、媒体库范围、Smart Search 和 FilterBar 随机进入详情页。
- 详情页更多菜单新增“复制影片信息”，复用当前详情模型并写入系统剪贴板，不重新查询数据库。
- 整理工具新增统一入口 `/organizer`，在同一页面内切换重复影片和批量整理；批量整理阶段复用 MovieWall 选择集与现有 Organizer Dry Run / Preview / Execute 链路。
- 整理工具完成执行流：重复影片支持显式保留项选择、Safe Delete 预览/确认/执行和用户个人数据合并预览；批量整理支持按当前 MovieWall 选择集执行批量移动与批量重命名。
- MovieWall Display Optimization：影片墙新增统一海报方向（竖版 2:3 / 横版 16:9）、海报大小（小 / 中 / 大）设置，并经 Unified Settings 持久化。
- MovieWall 分页改为右下角悬浮控件，支持点击页码输入、Enter 跳转、Esc 取消、左右方向键翻页和 Ctrl+G 聚焦页码。

### Changed

- Retained Feature Parity is now complete by the current product scope and enters Feature Freeze. The next phase is Legacy Cleanup, not product optimization or new feature development.
- Settings no longer shows the legacy compatibility field list or internal keys such as `WindowConfig.*` and `ScanConfig.*` to ordinary users.
- Organizer execution now routes duplicate deletion through the existing Safe Delete workflow and routes batch move/rename through the existing File Organizer workflow. Ordinary MovieWall batch delete remains disabled; destructive delete is only exposed from duplicate groups after explicit keep selection and preview confirmation.

- 左侧工具区将“查重结果”合并为“整理工具”；旧 `/duplicates` 路由仅作为兼容重定向保留。
- 影片墙卡片网格改为基于显示偏好的响应式 CSS Grid，不写死列数；列表视图不受海报方向和大小设置影响。
- 设置中心「外观」分区增加可视化影片墙显示选项卡，参考现有外观配置交互，不新增图片生成或资源来源设置。

### Planning

- 启动 Sprint 0.5.0-01 `Feature Parity Finalization`，先完成基于旧版源码、Next 实现、Migration、Bridge、测试和安装版证据的审计，再按 P0/P1 补齐功能。
- 新增 Feature Parity、Bridge API、Database Health 和 Performance Baseline 审计报告；不把页面、只读接口或单条预检提升为完整迁移。

## [0.4.3] - 2026-07-16

### Planning

- 新增统一的 `AI_RULES.md` 与 `PROJECT_CONTEXT.md`：任何开发者、AI、账号、模型或 API Provider 都遵守同一架构、UI、数据安全、测试和发布规则，并可读取长期产品决策。
- 正式建立 Sprint 0.4.3 `Media Assets & File Organization` 计划，覆盖图片、NFO、文件整理、MetaTube 真实烟测、Bridge Release 生命周期和限定交互修复。
- GitHub `main` 增加稳定发布门槛：中间 Sprint 提交保留在本地分支，完成 Self Test、Smoke Test、Freeze 和 Release 后才允许推送。
- 2026-07-16 对本地 MetaTube `v1.4.0-c0e053f` 完成只读协议预检，并以数据库副本和隔离资源目录完成 30 部真实写入烟测。

### Added

- Migration `0006_ImageAssetWorkflow`、`0007_NfoWorkflow`、`0008_FileOrganizerWorkflow` 与 `0009_PlaybackSettings`。
- Poster/Thumb/Fanart/BigPic/ExtraPic、演员头像兼容导入、图片锁定、校验、原子保存以及可检查/清理/重建的缩略缓存。
- NFO 导入、导出、Provider 自动写入、用户文件锁定、独立输出目录、非覆盖策略和失败恢复。
- 文件整理 `Dry Run → Preview → Confirm → Task Execute`，包含路径冲突、无覆盖、审计和文件/数据库补偿恢复。
- 可重复的隔离 MetaTube 烟测工具及 30 部真实样本报告。
- 播放器 Settings Service、旧配置回退和 Migration 持久化。
- Migration `0005_MetadataSyncWorkflow`：同步阶段、Provider、重试次数、当前影片、结果摘要、取消标记、Next 原生设置与同步前快照。
- 独立 `IMetadataProvider`、`MetaTubeProvider`、`MetadataSyncExecutor`、非破坏写入、图片下载、NFO 和持久任务日志服务。
- MetaTube Provider 设置、连接测试、详情页手动同步与同步任务全阶段控制。
- Migration `0004_LibraryScanWorkflow`：来源排除规则与持久任务日志。
- 媒体库新建、编辑、删除影响预览、数据库备份和安全解除关联。
- 增量/全量扫描任务，支持视频扩展名过滤、来源排除规则、重复路径跳过和缺失状态刷新。
- 新影片导入后自动恢复同名评分，并为每部新增影片创建待处理同步任务。
- 扫描任务暂停、继续、取消、失败重试和日志查看。

### Changed

- Release 版 Bridge/Migration 隐藏控制台并写入滚动文件日志；端口占用阻止重复实例，主程序退出后等待 Bridge 完整结束，前端持续检测 Bridge 健康状态。
- 版本统一升级到 0.4.3；Debug/Release、NSIS 安装、单实例、无控制台、日志、退出无残留及已安装关键页面烟测通过。
- 发布证据汇总至 `releases/0.4.3-VERIFICATION.md`；未完成项目继续保留在 TODO 与 Feature Parity Matrix。
- 标签编辑改为顶部已选标签与可搜索标签池；详情海报适度放大并保持原 Information Layout。
- MetaTube 搜索 `404` 明确归类为无结果，不再执行三次无意义重试；错误日志保留最后一次 Provider 原因。
- 修正安装版 Tauri 资源解析，Bridge/Migration 从安装目录 `resources` 启动，不再依赖开发机源码路径。
- Pending 同步任务现在由 Bridge 后台执行器领取；异常退出任务会记录“上次异常中断”并进入可重试状态。
- 任务中心显示准备、获取元数据、下载图片、写入元数据、写入 NFO 和重试阶段。
- 媒体库页面从只读状态升级为真实 Bridge 操作界面。
- 任务中心补齐行为、状态、名称、进度、日期和任务控制列。

### Safety

- 元数据同步只补全空字段，演员/类型/厂商/系列关系只增不删；用户标题、标签、评分、收藏、已有图片和 NFO 均不覆盖。
- 图片先下载到临时文件再原子移动；同步失败只清理本次新建文件，数据库写入使用事务并保存同步前快照。
- 删除媒体库只删除定义并解除文件关联，不删除影片记录或源媒体文件。
- 每个影片独立事务导入；单文件失败不终止整个扫描批次。
- MetaTube 本地服务未运行时连接测试和同步任务明确失败并保留原因，不伪装为成功。

## [0.4.1] - 2026-07-15

### Added

- 每次 Tauri 启动生成 Bridge 会话令牌，所有写接口要求 `X-LMM-Session`。
- Migration `0003_UserStateAuditAndRatingMemory`：评分存在状态、删除评分记忆和操作审计。
- 收藏/取消收藏、评分/清除评分、单部和批量标签、批量收藏写入。
- 用户标签新建、编辑、影响预览、删除与应用内撤销。
- 演员完整资料编辑、影片演员关系编辑和 ActorID=0 安全修复流程。
- 删除影片记录前影响预览和数据库备份；只移除数据库记录，不删除媒体文件。
- 播放器正常退出后写入播放次数与 `PlayHistory`；详情上一部/下一部保持查询上下文。
- `LocalMediaManager.Bridge.Tests`，覆盖用户状态、标签撤销、评分记忆、演员关系、播放历史、影片删除和 ActorID=0 修复。

### Changed

- Bridge 从只读产品查询边界升级为经鉴权的事务读写边界；React 仍不接触 SQLite。
- Tauri 在 Bridge 启动前自动运行 checksummed Migration upgrade，失败时阻止带旧 Schema 启动。
- 影片墙增加批量选择工具栏；详情、标签和演员页面继续复用 Material UI 公共规范。

### Safety

- 标签删除和影片记录移除执行前创建数据库备份；标签删除支持即时撤销。
- ActorID=0 只处理可确定身份的候选；空名或无法判断身份时停止，不覆盖用户演员资料。
- 删除评分记忆只保存文件名、评分和必要审计时间，不保存完整媒体路径或其他用户字段。

### Known limitations

- 同名评分恢复 Bridge 服务已完成，但扫描导入自动触发属于 0.4.2。
- 自定义播放器路径仍使用兼容环境变量；正式 Settings 写入在 0.4.2 接续。
- Vite 仍提示主包超过 500 kB，路由拆包保留在性能 TODO。

## [0.4.0] - 2026-07-15

### Added

- 建立正式产品愿景、Roadmap、TODO、UI 设计规范和版本管理流程。
- 功能等价矩阵增加优先级、用户重要度、依赖项、数据风险和完成证据，并采用严格完整迁移门槛。
- 建立统一版本生命周期、测试计划和 0.5.5 LTS 稳定里程碑；0.4.0 标记为 Release/Frozen。

- 基于 Tauri 2、React、TypeScript、Material UI 和 Emotion 的正式产品壳层。
- .NET Bridge、新数据库、Schema Migration 与 Settings 兼容读取。
- Dashboard、现代影片墙、共享影片卡片和 Information Layout 详情页。
- Search 2.0、媒体库、演员、标签、收藏和最近播放页面。
- Metadata Center、Diagnostics Center 与增强 Tasks Center。
- Plugin Center 基础页面和 AI Provider 架构占位。
- Local Media Manager 品牌、SVG/PNG/ICO 与 NSIS 品牌资源。
- 三份旧版迁移审计文档和 0.4.0 发布验证报告。

### Changed

- 软件正式名称统一为 Local Media Manager（LMM）。
- 所有产品数据访问统一经由 Bridge DTO，React 不再承担数据库职责。
- 影片卡片、详情信息组织、导航、加载、空状态和错误反馈统一为 Next 设计体系。

### Fixed

- 大量旧数据可通过新数据库和 Bridge 稳定只读浏览。
- 页面在深浅主题及 100%/125%/150% 逻辑缩放下无横向溢出。
- 数据库诊断确认完整性为 `ok`，外键错误为 0。

### Removed

- Next 产品路线不再复用旧 WPF 页面布局、XAML 控件和 Jvedio 视觉样式。

### Known limitations

- 收藏、评分、标签、演员关系和播放历史等写操作尚未迁移。
- 媒体库 CRUD、扫描、导入、同步、NFO、智能卡图和文件整理尚未迁移。
- 插件中心与 AI Provider 在 0.4.0 仅为基础/占位能力。
