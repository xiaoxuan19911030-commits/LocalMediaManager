# Changelog

本项目遵循语义化版本。未发布内容进入 `[Unreleased]`；发布时归入对应版本，并同步更新 Roadmap 与 TODO。

## [Unreleased]

### Product Decisions

- Metadata Ownership Decision (DEC-013): LMM no longer plans a full Movie Editor or manual movie metadata editing. Movie metadata is owned by scraping, NFO import, and metadata sync; incorrect metadata should be fixed by re-scrape/re-sync/NFO re-import.
- Product-cancelled legacy expectations: full-field movie edit, manual edits for titles/code/plot/date/runtime/director/studio/series/tags/Genre, actor add/delete/search/manual profile edit, display/custom/second title fields, and standalone watched toggle.
- Product-cancelled Image SetAs: LMM will not migrate manual "set as poster / thumbnail / banner" actions because image resources are managed by MetaTube scraping, NFO, and metadata sync.
- Duplicate Management & Batch Organizer (DEC-015): duplicate review and batch organizer now share one Organizer Tools entry instead of separate product tracks.
- Retained user-data scope: rating, favorite, custom tags, actor display ordering, poster/image adjustment, manual crop, playback history, and future Human-approved notes.
- Development order is now fixed: retained Feature Parity first, Legacy Cleanup second, Human-experience-driven optimization/new features third.

### Added

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
