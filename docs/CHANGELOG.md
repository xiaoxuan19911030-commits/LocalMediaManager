# Changelog

本项目遵循语义化版本。未发布内容进入 `[Unreleased]`；发布时归入对应版本，并同步更新 Roadmap 与 TODO。

## [Unreleased]

### Added

- 建立正式产品愿景、Roadmap、TODO、UI 设计规范和版本管理流程。
- 功能等价矩阵增加优先级、用户重要度、依赖项、数据风险和完成证据，并采用严格完整迁移门槛。

## [0.4.0] - 2026-07-15

### Added

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
