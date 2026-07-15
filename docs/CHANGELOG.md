# Changelog

本项目遵循语义化版本。未发布内容进入 `[Unreleased]`；发布时归入对应版本，并同步更新 Roadmap 与 TODO。

## [Unreleased]

### Added

- Migration `0004_LibraryScanWorkflow`：来源排除规则与持久任务日志。
- 媒体库新建、编辑、删除影响预览、数据库备份和安全解除关联。
- 增量/全量扫描任务，支持视频扩展名过滤、来源排除规则、重复路径跳过和缺失状态刷新。
- 新影片导入后自动恢复同名评分，并为每部新增影片创建待处理同步任务。
- 扫描任务暂停、继续、取消、失败重试和日志查看。

### Changed

- 媒体库页面从只读状态升级为真实 Bridge 操作界面。
- 任务中心补齐行为、状态、名称、进度、日期和任务控制列。

### Safety

- 删除媒体库只删除定义并解除文件关联，不删除影片记录或源媒体文件。
- 每个影片独立事务导入；单文件失败不终止整个扫描批次。
- 自动同步保持为独立 `Pending` 任务，元数据执行器完成前不伪装为同步成功。

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
