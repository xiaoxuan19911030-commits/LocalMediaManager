# Local Media Manager Architecture

> 本文档是 LMM 技术边界的正式说明。新功能必须优先复用现有层次，不得在页面中建立旁路。

## Upstream baseline

The inspected Clash Verge Rev development baseline uses React 19, Material UI 9, Emotion, React Router, Vite 8, Tauri 2.11, and Rust. Its reusable infrastructure is the desktop window lifecycle, theme model, routed layout, side navigation, Material UI component conventions, loading/error feedback, responsive composition, and internationalization boundary.

Proxy-specific frontend pages, Mihomo APIs, proxy profiles, rules, connections, traffic, subscription state, DNS, TUN, system proxy integration, updater behavior tied to the original product, and their Rust crates are excluded rather than renamed.

## Selected Bridge boundary

LMM uses an independent .NET 8 loopback HTTP process.

```text
Tauri 2 / React / Material UI
          |
          | http://127.0.0.1:47831
          v
LocalMediaManager.Bridge (.NET 8)
          |
          | SQLite Mode=ReadOnly
          v
LocalMediaManager.db + existing media/image paths
```

HTTP provides a debuggable, typed JSON boundary that can be exercised independently from Tauri and does not require duplicating a pipe client in Rust and TypeScript. It binds only to loopback. Authentication and per-session tokens are required before any write endpoint is introduced.

The Bridge exposes health, dashboard, global search, libraries, tasks, paged/searchable/sortable media DTOs, movie details, cover streaming, player launch, and settings DTOs. React never accesses SQLite directly. Runtime media access uses only Database v1.

## Product phase boundary

Version 0.3.0 adds product-facing routes without changing the foundation: Dashboard, movie wall, movie details, global search, media libraries, and task center. Pages contain presentation and interaction only; SQL remains in the Bridge product reader. Database corrections and upgrades remain checksummed migrations.

Version 0.4.0 treats Bridge, Database, Migration, Material UI, and Tauri as stable infrastructure. Experience work is implemented through shared React presentation components and existing DTOs; it does not introduce page-level persistence or alternate business paths.

The official brand is Local Media Manager (LMM). Brand SVG sources live under `assets/brand`, while generated PNG, ICO, application, and NSIS assets are derived from those sources.

## Existing C# reuse assessment

- Reuse as compatibility authority: SQLite schema/mappers, configuration paths, scanning, imports, metadata/NFO, scraper integration, image/cache rules, tags, actors, favorites, ratings, history, player selection, plugins, and server resources.
- Extract after the boundary stabilizes: pure data contracts, query services, path resolution, image resolution, and player service.
- Keep in WPF until later: controls, dialogs, `BitmapSource` handling, routed events, and other presentation-bound code.
- Do not rewrite in Rust during phase 1: scanning, scraping, database writes, media processing, or configuration persistence.

## Safety

- Legacy databases are opened with `SqliteOpenMode.ReadOnly`; their hashes are checked before and after migration.
- The stable WPF installation is never overwritten by the Next build.
- The deployment root remains `D:\Local Media Manager Next` for upgrade compatibility; Database v1 and its reports/backups remain in `D:\Local Media Manager Next Data`.
- Schema changes are formal, checksummed migrations. Full import uses a new temporary database, integrity and foreign-key checks, sampling, a report, and an explicit confirmed switch.
- AI remains an architecture-only future module; see `AI_ARCHITECTURE.md`.

## Layer responsibilities

### Tauri host

- 管理 Windows 窗口、应用生命周期、随包资源和 Bridge 子进程。
- 不承载扫描、刮削、数据库写入或媒体业务规则。
- Release 通过 NSIS 部署到独立 Next 目录，不覆盖旧 WPF。

### React application

- 只负责路由、页面状态、交互、展示和调用 typed Bridge client。
- 页面复用 Material UI 主题、公共卡片、页头、空态、加载和错误组件。
- 禁止直接访问 SQLite、文件系统、系统命令、插件实现或 AI Provider。

### Bridge

- 是所有产品业务的唯一执行边界，公开 DTO 而不是数据库实体。
- 负责查询、校验、路径解析、播放器、未来写入命令和统一错误模型。
- 写接口启用前必须具备会话鉴权；危险命令还需确认令牌、审计与回滚设计。

### Database and Migration

- `LocalMediaManager.db` 是 Next 运行时唯一业务数据库。
- 页面和 Tauri Rust 层都不直接修改数据库。
- 表、索引和种子数据变化必须使用有序、校验和固定的 Migration。
- 迁移先备份、临时构建、完整性/外键校验、抽样报告，再原子切换。

### Settings

- 所有设置经统一 Settings Service/Bridge DTO。
- 立即生效与需重启字段必须明确区分。
- API Key、Cookies、Headers 和凭据不写入普通 `settings.json`，Windows 优先使用安全凭据存储。

### Tasks

- 扫描、导入、刮削、同步、下载、缓存、裁切、整理、迁移、查重、NAS 和 AI 批处理全部进入统一 Tasks。
- 状态机至少为 `Pending / Running / Paused / Completed / Failed / Cancelled`。
- 任务记录进度、当前项、总数、完成数、失败数、日志、取消与重试结果；不在 React 页面线程执行批处理。

### Plugins

- 插件通过稳定 Provider/Bridge 合同提供能力，不直接获得数据库和任意文件系统访问。
- 插件声明版本、权限和兼容范围；单个插件失败必须隔离。
- 安装、更新、卸载和市场能力在 0.8.0 前逐步实现。

### AI Provider

- AI 是独立模块，实际模型接入从 0.6.0 开始。
- React 不直接调用模型；AI 不直接访问数据库、文件系统或系统命令。
- 所有建议记录 Provider、Model、PromptVersion、来源、置信度和用户决定。
- 修改采用“建议 → 预览 → 影响范围 → 用户确认 → Bridge 执行 → 日志/回滚”。

## API and error conventions

- DTO 使用稳定、面向产品的字段；新增字段保持向后兼容，破坏性变化需版本化。
- Bridge 对输入执行范围、枚举、路径和存在性校验。
- 错误至少区分输入错误、资源不存在、冲突、权限/只读、外部进程失败和内部错误。
- React 必须展示加载、空状态和可理解错误，不把异常文本静默吞掉。

## Definition of done

技术变更只有在代码、Migration/DTO、错误处理、测试或可复现验收、Debug/Release 构建、部署烟测和产品文档均更新后才算完成。版本门槛见 `ROADMAP.md`。
