# Local Media Manager — Project Constitution

> **项目百科（Project Constitution）**
>
> 本文档回答一个问题：**这个项目是什么。**
>
> 它不是教程、不是 TODO、不是 README，而是整个项目唯一的百科。
> AI 读完本文后，应已掌握项目约 80% 的知识；只有查字段映射、Release 证据、矩阵明细时才进入 `docs/`。
>
> **Knowledge System：** v1.0（维护期）· 入口见 [`INDEX.md`](INDEX.md) · **产品版本状态**见 [`docs/ROADMAP.md`](docs/ROADMAP.md)（PROJECT 不写版本号）

---

## 文档体系

知识体系结构见 **`INDEX.md`（Knowledge System v1.0）** — 结构已冻结，只补内容。

```text
INDEX.md          → 入口、维护规则、Sprint 模板
PROJECT.md        → 当前真相（本文）
DECISION_LOG.md   → 已定案决策（DEC-xxx + Status）
AGENTS.md         → AI 行为
docs/             → 证据层
```

### 知识闭环（Knowledge Lifecycle）

代码与文档必须同步演进，禁止「代码已变、知识未更新」：

```text
代码完成
    ↓
Human 验收
    ↓
Decision 定案（DECISION_LOG / 未来 ADR 文件）
    ↓
PROJECT 更新（若「当前真相」变化）
    ↓
AGENTS 更新（若 AI 规范变化）
    ↓
docs/ 证据（Release Note、Matrix、验证报告）
    ↓
Release
```

**反向阅读链（AI 会话）：**

```text
Code → Sprint → Decision → Project → Agent → Release
```

每完成一个 Sprint，必须走完 **§25 Definition of Done** 全部步骤，少一步不算 Sprint 完成。

**ADR 可选演进（backlog）：** 单条决策正文可迁至 `docs/decisions/DEC-xxx-slug.md`；`DECISION_LOG.md` 保留索引。**不改变 v1.0 结构。**

### 阅读顺序（AI 会话启动）

```text
0. INDEX.md           → 知识体系入口（可选）
1. PROJECT.md         → 掌握项目 80%（本文）
2. DECISION_LOG.md    → 已定案决策（Settings/MediaStorage/写入相关必读）
3. AGENTS.md          → 工作规范（Mission / Workflow / Rules）
4. docs/ROADMAP.md    → 版本范围（若涉及版本决策）
5. docs/TODO.md       → 未完成项（若涉及开发任务）
6. docs/migration/FEATURE_PARITY_MATRIX.md → Legacy 迁移（若涉及旧版功能）
```

### PROJECT 与 DECISION_LOG 的分工

- **PROJECT** 永远代表**当前版本真相**。例如 MediaStorage 只写最终方案（`MediaStoragePathResolver`、动态 `InstallRoot`），不写「以前写死 D 盘」。
- **DECISION_LOG** 记录所有定案过程、备选方案、踩坑和禁止重犯事项（带 Decision ID）。
- **PROJECT §21 Sprint History** 只保留一行摘要 + 指向 DECISION_LOG 锚点，不重复决策正文。

### docs/ 证据层索引

| 路径 | 用途 | 何时必读 |
|------|------|---------|
| `docs/ROADMAP.md` | 版本范围唯一正式入口 | 定版本、定 Sprint 范围 |
| `docs/TODO.md` | 未完成产品工作 | 开始开发前 |
| `docs/CHANGELOG.md` | 已发布行为记录 | 确认已发布功能 |
| `docs/migration/FEATURE_PARITY_MATRIX.md` | Legacy 迁移状态与证据 | 任何 Legacy 相关任务 |
| `docs/TEST_PLAN.md` | 测试与验收规范 | Self Test / Smoke Test |
| `docs/UI_DESIGN_SPEC.md` | UI 设计规范细节 | 新页面/组件设计 |
| `docs/database/` | 数据库设计、字段映射 | Schema/Migration 变更 |
| `docs/releases/` | Release 验收证据 | 发布/回归 |
| `docs/audits/` | 审计报告 | Sprint 审计 |
| `docs/sprints/` | Sprint 正式文档 | Sprint 归档 |

---

## 目录

| 章节 | 标题 | 状态 |
|------|------|------|
| 00 | 阅读顺序 | ✅ |
| 01 | 项目介绍 | ✅ |
| 02 | 产品定位 | ✅ |
| 03 | 核心设计原则 | ✅ |
| 04 | 总体架构 | ✅ |
| 05 | 项目目录结构 | [待补] |
| 06 | Bridge 架构 | ✅ |
| 07 | React 架构 | [待补] |
| 08 | Tauri 架构 | [待补] |
| 09 | 数据库设计 | ✅ |
| 10 | Settings 系统 | ✅ |
| 11 | MediaStorage | ✅ |
| 12 | Legacy 兼容 | ✅ |
| 13 | 元数据系统 | ✅ |
| 14 | UI Design System | ✅ |
| 15 | 页面规范 | [待补] |
| 16 | 功能模块 | ✅ |
| 17 | Git Workflow | ✅ |
| 18 | Build Workflow | ✅ |
| 19 | Deploy Workflow | ✅ |
| 20 | Testing Workflow | ✅ |
| 21 | Sprint History（摘要） | ✅ |
| 22 | Roadmap | [待补] |
| 23 | 权威文档索引 | [待补] |
| 24 | Knowledge Lifecycle | ✅ |
| 25 | Definition of Done | ✅ |
| 26 | Anti Patterns | ✅ |

---

## 00. 阅读顺序

### AI 首次进入项目

1. **读 PROJECT.md** — 本文，建立全局认知。
2. **读 AGENTS.md** — 了解禁止项、Git 规范、Report 格式、Human Acceptance 边界。
3. **读 DECISION_LOG.md** — 了解已定案决策，避免重发明轮子（Settings/MediaStorage 任务必读）。
4. **按任务选读 docs/** — 见上方「docs/ 证据层索引」。

### 开发任务启动检查

开始任何开发任务前，AI 必须确认：

1. 当前 Git 分支与工作区状态（禁止回退已有修改）。
2. 当前 Sprint 阶段（Planning / Develop / Smoke Test / Release — 不能越级声明完成）。
3. 本次任务涉及的页面、Bridge 服务、Migration、Settings、Tasks 入口（搜索代码库，不猜测）。
4. 是否在 ROADMAP 当前版本范围内；超出范围须先更新 ROADMAP。
5. Legacy 相关任务须对照 FEATURE_PARITY_MATRIX，不得凭记忆判断迁移状态。

### 版本状态实时来源

PROJECT 中的版本快照可能滞后。**以下文件始终优先：**

- 版本范围 → `docs/ROADMAP.md`
- 未完成项 → `docs/TODO.md`
- 已发布行为 → `docs/CHANGELOG.md`
- 迁移证据 → `docs/migration/FEATURE_PARITY_MATRIX.md`

---

## 01. 项目介绍

### 是什么

**Local Media Manager（LMM）** 是一个现代化、本地优先、可扩展的专业媒体管理平台。

它以用户拥有并控制的数据为核心，提供：

- 大规模本地媒体库的浏览、搜索与详情展示
- 元数据管理（标题、标签、演员、评分、收藏、播放历史）
- 媒体资源管理（海报、缩略图、Fanart、预览、截图、GIF、NFO）
- 媒体库扫描、同步、导入与文件整理
- 元数据 Provider 集成（MetaTube 等）
- 任务中心（扫描、同步、缓存、整理、安全删除等长任务）
- 诊断、维护、数据安全与备份恢复
- 未来：插件、NAS、受控 AI（0.6.0+）

### 不是什么

- **不是** Jvedio 的界面改版 — 旧 WPF 只作为业务规则、数据兼容和回归行为参考。
- **不是** 云服务 — 核心功能完全离线可用，不依赖外部 API 即可浏览和管理。
- **不是** Demo — 这是一个有 17+ Sprint 历史、独立部署、正式 Release 流程的真实软件。
- **不是** 单体应用 — 严格分层：Tauri 壳 → React UI → Bridge 业务 → SQLite 数据。

### 技术栈

| 层 | 技术 | 版本 |
|----|------|------|
| 桌面壳 | Tauri | 2.11 |
| 前端 | React + Material UI + Vite | React 19 / MUI 9 / Vite 8 |
| 业务层 | .NET Bridge (HTTP API) | .NET 8 |
| 数据库 | SQLite (Database v1) | checksummed Migrations |
| 迁移工具 | .NET Migration CLI | .NET 8 |
| 打包 | NSIS (via Tauri) | Windows x64 |
| 包管理 | pnpm | 11.x |

### 品牌

- 正式名称：**Local Media Manager**
- 简称：**LMM**
- 品牌 SVG 源文件：`assets/brand/`
- 生成的 PNG/ICO/应用/NSIS 资产从 SVG 派生

### 部署模型

LMM Next 与旧 WPF 版**完全隔离**：

| 路径 | 用途 |
|------|------|
| `D:\LocalMediaManager` | 源码根目录 |
| `D:\Local Media Manager Next` | 独立安装目录（不覆盖旧 WPF） |
| `D:\Local Media Manager Next Data` | 运行时数据根（数据库、MediaStorage、备份、报告） |
| `D:\Local Media Manager Next Backups` | 部署备份根 |

旧 WPF 稳定版、旧数据库和用户媒体**永远不被 Next 构建覆盖**。

---

## 02. 产品定位

### 核心价值

1. **本地优先** — 核心媒体、元数据与用户状态默认保存在本地；基础浏览不依赖云服务。
2. **用户数据优先** — 手工标题、标签、评分、收藏、备注与图片选择**高于**刮削、插件和 AI 结果。
3. **安全可控** — 危险操作先预览影响范围，再确认执行，并提供日志、备份与可行的回滚。
4. **长期可维护** — 业务通过 Bridge，数据库通过 Migration，长任务通过 Tasks，页面复用统一组件。
5. **开放扩展** — Provider、插件、NAS 和 AI 都通过明确边界接入，不侵入核心媒体业务。

### 目标用户

管理大规模本地媒体库的用户，需要：

- 快速浏览和搜索（影片墙、全局搜索、高级筛选）
- 清楚理解文件与元数据状态（诊断中心、元数据中心）
- 安全完成编辑、扫描、同步、整理与修复
- 随时知道操作正在做什么、影响哪些数据、能否撤销

### 导航模型

LMM 使用**持久左侧导航**（不是顶部 Tab）：

**一级模块（primary）：** 首页 · 影片墙 · 媒体库 · 标签 · 收藏 · 最近播放

**工具模块（utility）：** 元数据中心 · 诊断中心 · 查重结果 · Maintenance · 任务中心 · 插件中心 · AI Provider · 设置

导航定义在 `src/layouts/AppShell.tsx`，路由在 `src/app/router.tsx`。

**禁止**恢复顶部主 Tab，**禁止**同时保留两套主导航。

### 版本与发布状态

**PROJECT 不写产品版本号**（如 0.4.x、0.5.x）。版本范围、发布状态、Sprint 归属以 **`docs/ROADMAP.md`** 为唯一权威；已发布行为见 **`docs/CHANGELOG.md`**。

### 与 Legacy（旧 WPF / Jvedio）的关系

- 旧版是**功能行为、数据兼容、业务规则、回归期望**的来源。
- 旧版**不是**界面模板 — LMM 使用 Material UI 现代设计体系，不复制 WPF/XAML 布局。
- 旧版数据库以 `SqliteOpenMode.ReadOnly` 打开，Next 不写 Legacy 库。
- 迁移状态以 `docs/migration/FEATURE_PARITY_MATRIX.md` 为唯一权威。

---

## 03. 核心设计原则

以下原则是 LMM 所有开发决策的宪法。任何新功能、重构或 AI 建议都必须通过这些原则的检验。

### 3.1 本地优先（Local First）

- 核心媒体、元数据、用户状态保存在本地 SQLite 和本地文件系统。
- 基础浏览、搜索、编辑、标签管理**不依赖**任何外部服务。
- 元数据 Provider（MetaTube 等）是**增强**，不是前提。
- 网络不可用时不应阻断本地工作流。

### 3.2 离线优先（Offline First）

- 所有已导入的媒体信息和资源在离线状态下完全可用。
- 同步、刮削、下载等需要网络的操作进入 Tasks，失败可重试，不阻塞 UI。
- Bridge 绑定 loopback（`127.0.0.1:47831`），不暴露到外网。

### 3.3 隐私优先（Privacy First）

- 用户数据不离开本地，除非用户明确发起（如同步到 Provider）。
- API Key、Cookies、Headers 等敏感凭据不写入普通配置文件；Windows 优先使用安全凭据存储。
- AI 默认关闭，所有建议需用户确认后才应用（0.6.0+）。

### 3.4 用户数据神圣不可侵犯（User Data Sovereignty）

- 手工标题、标签、评分、收藏、备注、图片选择**永远高于**自动刮削结果。
- 元数据和图片自动同步默认**补空**，不覆盖手工数据、锁定图片或用户 NFO。
- 收藏、评分、自定义标签、演员关系和播放历史是真实用户数据，必须持久化并在重启后保持。
- Safe Delete 只保留必要历史（MovieCode、Rating、UpdatedAt），不保存完整影片历史。

### 3.5 渐进式升级（Incremental Upgrade）

- 每个版本有明确的 Planning → Design → Develop → Self Test → Smoke Test → Freeze → Release → Archive 生命周期。
- 不跳过阶段直接 Release。
- 中间开发提交保留在本地 Sprint 分支；GitHub `main` 只接收通过验收的稳定版本。
- 数据库变更通过 checksummed Migration，先备份、临时构建、完整性校验，再原子切换。

### 3.6 Legacy 兼容（Legacy Compatibility）

- 旧 WPF 数据库和配置以只读方式访问，用于兼容读取和数据迁移。
- Legacy 业务规则在 `docs/migration/LEGACY_BUSINESS_RULES.md` 中记录。
- 不以同名入口或只读展示冒充完整功能迁移 — 矩阵中必须有真实 UI 操作、Bridge 执行、持久化/重启结果、错误处理、备份/回滚、自动化测试、手动烟测、Git commit 和验收记录。
- **禁止**修改 Legacy 安装目录或数据库。

### 3.7 单一职责与分层边界（Single Responsibility）

每一层只做自己的事，**禁止跨层访问**：

| 层 | 职责 | 禁止 |
|----|------|------|
| **Tauri** | 窗口生命周期、Bridge 子进程管理、系统对话框 | 扫描、刮削、DB 写入、业务规则 |
| **React** | 路由、页面状态、交互、展示、调用 Bridge client | 直连 SQLite、文件系统、系统命令、插件、AI Provider |
| **Bridge** | 所有产品业务的唯一执行边界，公开 DTO | 暴露数据库实体、绕过校验 |
| **Migration** | 数据库建立与升级 | 运行时业务逻辑 |
| **SQLite** | 持久化 | 被 React 或 Tauri Rust 直接修改 |

### 3.8 一致性优于功能数量（Consistency Over Features）

- 新页面必须先寻找可复用组件（`PageHeader`、共享 Card、Empty/Loading/Error、Settings 组件）。
- 发现重复模式时优先提炼共享实现，不增加页面私有风格。
- Settings 统一 Draft → Coordinator → Save，**禁止**局部保存或第二套 Settings 系统。
- MediaStorage 统一 `MediaStoragePathResolver`，**禁止**第二套路径解析或写死磁盘路径。
- 所有长任务进入统一 Tasks 系统，状态机至少为 `Pending / Running / Paused / Completed / Failed / Cancelled`。

### 3.9 一次一个 Sprint（One Sprint at a Time）

- 每个 Sprint 有明确目标和分支（如 `sprint/0.5.0-17-media-resource-write`）。
- Sprint 内不混入无关功能或跨版本工作。
- Sprint 结束后：更新 Roadmap/Changelog/TODO、ARCHIVE 记录、Release 证据。
- 当前已进行 17+ Sprint（0.4.x + 0.5.0-01 ～ 0.5.0-17）。

### 3.10 不破坏已有体验（Non-Regression）

- 新功能不得破坏已发布功能的 UI 行为、数据兼容或性能基线。
- 危险写入必须：`Dry Run → Preview → Confirm → Execute → Audit/Recovery`。
- 文件整理、安全删除、Library 删除等操作必须有 impact preview 和确认步骤。
- 每个版本结束前必须完成 Debug/Release 构建、NSIS 安装包、独立部署烟测。

### 3.11 危险操作安全链（Safety Chain）

所有可能丢失或破坏用户数据的操作必须遵循：

```text
Impact Preview → User Confirmation → Backup/Audit → Execute → Verify → Rollback Strategy
```

具体实现：

- **文件整理：** Dry Run → Preview → Confirm → Execute → Audit
- **安全删除：** Preview → Confirm → Execute → Rating History 保留
- **Library 删除：** Delete Preview → Confirm → Execute
- **数据库 Migration：** Backup → Temp Build → Integrity Check → Atomic Switch
- **Settings 变更：** Draft → Validate → Coordinator Save → 变更字段追踪

### 3.12 AI 边界（AI Boundary）

- AI 是独立模块，实际模型接入不早于 **0.6.0**。
- React 不直接调用模型；AI 不直接访问数据库、文件系统或系统命令。
- AI 流程：`建议 → 预览 → 影响范围 → 用户确认 → Bridge 执行 → 日志/回滚`。
- 所有 AI 建议记录 Provider、Model、PromptVersion、来源、置信度和用户决定。
- 普通搜索、手工管理和本地功能始终独立于 AI。

---

## 04. 总体架构

### 4.1 架构总览

```text
┌─────────────────────────────────────────────────────────┐
│                    Tauri 2 Desktop Shell                 │
│  窗口生命周期 · Bridge 子进程 · 系统对话框 · NSIS 打包    │
│  src-tauri/src/lib.rs                                    │
└────────────────────────┬────────────────────────────────┘
                         │ WebView
                         ▼
┌─────────────────────────────────────────────────────────┐
│              React 19 + Material UI 9 + Vite 8           │
│  路由 · 页面 · 交互 · 展示 · 主题 · 共享组件            │
│  src/                                                    │
│                                                          │
│  AppShell.tsx ── router.tsx ── pages/ ── components/     │
│  themes/              services/bridge.ts (唯一业务入口)   │
└────────────────────────┬────────────────────────────────┘
                         │ HTTP JSON
                         │ http://127.0.0.1:47831
                         │ X-LMM-Session token (写操作)
                         ▼
┌─────────────────────────────────────────────────────────┐
│           LocalMediaManager.Bridge (.NET 8)              │
│  唯一业务执行边界 · DTO API · 服务 · 任务 · 校验         │
│  backend/LocalMediaManager.Bridge/                       │
│                                                          │
│  Program.cs ── Services ── ProductReader/Writer           │
│  SettingsSaveCoordinator · MediaStoragePathResolver       │
│  TaskCommandService · MetadataSyncExecutor · ...          │
└──────────┬──────────────────────────┬───────────────────┘
           │                          │
           ▼                          ▼
┌──────────────────────┐   ┌──────────────────────────────┐
│  SQLite Database v1  │   │  MediaStorage (文件系统)      │
│  LocalMediaManager.db│   │  Posters · Thumbnails ·      │
│                      │   │  Fanart · Previews · NFO ·   │
│  AppSettings 表      │   │  Screenshots · GIF · ...     │
│  Movies · Tags ·     │   │                              │
│  Tasks · Images ·    │   │  MediaStoragePathResolver    │
│  ...                 │   │  统一路径解析与写入            │
└──────────┬───────────┘   └──────────────────────────────┘
           │
           ▼
┌─────────────────────────────────────────────────────────┐
│        LocalMediaManager.Migration (.NET 8 CLI)          │
│  checksummed Schema Migrations · Backup · Integrity      │
│  backend/LocalMediaManager.Migration/                    │
└─────────────────────────────────────────────────────────┘
```

### 4.2 数据流规则

**读路径：**

```text
用户操作 → React 页面 → bridge.ts fetch GET → Bridge ProductReader → SQLite/MediaStorage → DTO → React 渲染
```

**写路径：**

```text
用户操作 → React 页面 → bridge.ts fetch POST/PUT (带 Session Token)
         → Bridge 校验 → Service 事务 → SQLite/MediaStorage 写入
         → DTO 结果 → React 更新状态
```

**长任务路径：**

```text
用户触发 → Bridge TaskCommandService 入队 → HostedService 后台执行
         → TaskLogService 记录进度/日志 → React Tasks 页面轮询/展示
         → 跨重启恢复为可重试状态
```

**关键约束：**

- React **永远**通过 `src/services/bridge.ts` 调用 Bridge，不直连任何后端资源。
- Bridge **永远**返回 DTO，不暴露 SQLite 实体或原始 SQL 结果。
- 写操作**必须**携带 `X-LMM-Session` 令牌（由 Tauri `bridge_session_token` 命令提供）。
- 数据库结构变更**必须**通过 Migration CLI，不在 Bridge 运行时 ALTER。

### 4.3 Tauri 层

**职责：**

- Windows 桌面窗口与应用生命周期管理
- 启动和管理 Bridge 子进程（`LocalMediaManager.Bridge.exe`）
- 启动时运行 Migration（`LocalMediaManager.Migration.exe`）
- 生成并提供 Bridge Session Token（`bridge_session_token` 命令）
- 系统级对话框（如 `choose_directory` 文件夹选择器）
- 随包资源管理（Bridge/Migration 可执行文件在 `src-tauri/resources/`）
- NSIS 安装包生成（Release 部署到 `D:\Local Media Manager Next`）

**不做：**

- 扫描、刮削、数据库写入或任何媒体业务规则
- 直接访问 SQLite 或 MediaStorage 文件

**Bridge 子进程发现顺序**（`lib.rs` `bridge_candidates`）：

1. 环境变量 `LMM_BRIDGE_PATH`
2. Tauri resource dir → `bridge/LocalMediaManager.Bridge.exe`
3. `src-tauri/resources/bridge/LocalMediaManager.Bridge.exe`
4. 开发模式 → `backend/LocalMediaManager.Bridge/bin/Debug/net8.0/`

### 4.4 React 层

**职责：**

- Hash Router 页面路由（`src/app/router.tsx`）
- 统一桌面壳和左侧导航（`src/layouts/AppShell.tsx`）
- 深浅主题（`src/themes/theme.ts` + `ThemeContext.tsx`）
- 共享 UI 组件（`PageHeader`、Card、Empty/Loading/Error、Settings 组件等）
- 页面级状态管理和用户交互
- 通过 `bridge.ts` 调用 Bridge API

**不做：**

- 直接访问 SQLite、文件系统、系统命令
- 直接调用插件实现或 AI Provider
- 在页面线程执行批处理或长任务

**Bridge Client（`src/services/bridge.ts`）：**

- 固定 origin：`http://127.0.0.1:47831`
- GET 请求无需 token；POST/PUT/DELETE 自动附加 `X-LMM-Session`
- Session token 从 Tauri `bridge_session_token` 命令获取，401 时自动刷新重试
- 统一错误解析（`parseBridgeError`），不静默吞掉异常

### 4.5 Bridge 层

**职责：**

- 所有产品业务的**唯一执行边界**
- 公开稳定、面向产品的 DTO（不是数据库实体）
- 输入校验（范围、枚举、路径、存在性）
- 统一错误模型（输入错误、资源不存在、冲突、权限/只读、外部进程失败、内部错误）
- 路径解析（MediaStorage、Legacy 图片根）
- 播放器启动、Provider 集成
- 长任务调度和生命周期管理

**核心服务注册**（`Program.cs`）：

| 服务 | 职责 |
|------|------|
| `ProductReader` / `ProductWriter` | 媒体库 CRUD 与查询 |
| `SettingsSaveCoordinator` | 统一 Settings 读写与校验 |
| `MediaStoragePathResolver` | 媒体资源路径解析与目录创建 |
| `MetadataSyncExecutor` | 元数据同步后台任务 |
| `ImageWorkflowService` / `ImageCacheTaskService` / `ImageGenerationTaskService` | 图片工作流 |
| `NfoService` | NFO 读写 |
| `FileOrganizerService` | 文件整理 |
| `SafeDeleteWorkflowService` | 安全删除 |
| `LibraryWorkflowService` | 媒体库扫描/导入 |
| `TaskCommandService` | 任务命令调度 |
| `TaskLogService` | 任务日志 |
| `DataSafetyService` | 数据安全/备份 |
| `RatingHistoryService` | 评分历史 |
| `PlaybackSettingsService` | 播放设置 |
| `MetaTubeProvider` | MetaTube 元数据 Provider |

**Session 鉴权：**

- 环境变量 `LMM_BRIDGE_TOKEN` 设置 session token
- 非 GET/HEAD/OPTIONS 请求必须携带匹配的 `X-LMM-Session` header
- Token 由 Tauri 在启动 Bridge 时生成并通过 `bridge_session_token` 命令提供给 React

**默认路径**（可通过环境变量覆盖）：

| 变量 | 默认值 |
|------|--------|
| `LMM_BRIDGE_URL` | `http://127.0.0.1:47831` |
| `LMM_DATABASE_PATH` | `D:\Local Media Manager Next Data\data\LocalMediaManager.db` |
| `LMM_LEGACY_ROOT` | `D:\Jvedio\Jvedio5.0` |
| `LMM_CONFIG_DATABASE_PATH` | `{legacyRoot}\data\{UserName}\app_configs.sqlite` |
| `LMM_IMAGE_ROOT` | `{legacyRoot}\data\{UserName}\pic`（或探测到的替代路径） |

### 4.6 数据库层

**Database v1（`LocalMediaManager.db`）** 是 Next 运行时**唯一业务数据库**。

- 页面和 Tauri Rust 层都**不直接修改**数据库
- 表、索引和种子数据变化**必须**使用有序、校验和固定的 Migration
- Migration 文件位于 `backend/LocalMediaManager.Migration/migrations/`
- 迁移流程：先备份 → 临时构建 → 完整性/外键校验 → 抽样报告 → 原子切换

**Settings 存储：** 主存储在 SQLite `AppSettings` 表，统一入口 `SettingsSaveCoordinator`（Bridge `GET/PUT /api/settings`）。

**Legacy 数据库（只读）：**

- `app_datas.sqlite` — 旧版业务数据
- `app_configs.sqlite` — 旧版配置（`SettingsReader` / `PlaybackSettingsService` 读取，Next 不写）

### 4.7 MediaStorage 层

MediaStorage 统一管理所有媒体资源文件的存储路径和写入。

**资源类型：** Posters · Thumbnails · Fanart · Previews · Screenshots · WallCrops · GIF · NFO

**统一入口：** `MediaStoragePathResolver`（`backend/LocalMediaManager.Bridge/MediaStoragePathResolver.cs`）

**路径解析逻辑：**

1. 从 `AppSettings` 读取 `mediaStorage.*` 配置（RootPath、目录模板、文件名模板）
2. 默认值由 `SettingsDefaults.MediaStorageForEnvironment(installRoot, databasePath)` 动态计算
3. 路径结构：`{RootPath}/{ResourceDirectory}/{MovieFolder}/{FileName}`
4. 模板变量：`{Code}`、`{Title}` 等（经 `RenderTemplate` + `SafePathSegment` 处理）
5. 写入前 `EnsureDirectoryForWrite` 自动创建目录
6. `IsInsideMediaStorageAsync` 验证路径在安全边界内

**RootPath 默认策略：**

- 首选：`{InstallRoot} Data\MediaStorage`（如 `D:\Local Media Manager Next Data\MediaStorage`）
- 回退：`{Documents}\Local Media Manager\MediaStorage`（当 InstallRoot 不可写时）

**Legacy 图片根（兼容/封面查找）：**

- Bridge 同时维护 Legacy 图片根（`LMM_IMAGE_ROOT`）用于旧版封面查找
- 新媒体资源写入**只**通过 MediaStorage，不回写 Legacy 路径

### 4.8 Tasks 系统

所有长运行操作进入统一 Tasks 系统：

**任务类型：** 扫描、导入、元数据同步、图片缓存、图片生成、文件整理、安全删除

**状态机：** `Pending → Running → Paused → Completed / Failed / Cancelled`

**每个任务记录：** 进度、当前项、总数、完成数、失败数、日志、取消与重试结果

**执行模型：**

- Bridge `HostedService` 后台执行（不在 React 页面线程）
- `TaskCommandService` 提供 pause/resume/cancel/retry 命令
- `TaskLogService` 持久化任务日志
- 跨重启：未完成任务恢复为可重试状态

### 4.9 横切关注点

**Settings：** 所有设置经 `SettingsSaveCoordinator` → Bridge DTO。UI 使用 Draft/Original/HasUnsavedChanges 模式，统一 Save，禁止局部保存。

**Plugins：** 通过稳定 Provider/Bridge 合同提供能力，不直接获得数据库和任意文件系统访问。0.8.0 前逐步实现。

**AI Provider：** 独立模块，0.6.0 前仅为 Placeholder。React 不直接调用模型。

**错误处理：** Bridge 返回结构化错误（code + message）；React 必须展示加载、空状态和可理解错误。

**国际化：** 边界已预留，完整语言切换在 Low 优先级 TODO。

---

## 05. 项目目录结构 [待补]

## 06. Bridge 架构

Bridge 是 LMM **所有产品业务的唯一执行边界**。React 只调用 typed HTTP JSON API；Bridge 负责校验、事务、路径解析、任务调度、Legacy 只读兼容与 DTO 映射。

**路径：** `backend/LocalMediaManager.Bridge/` · **入口：** `Program.cs` · **客户端：** `src/services/bridge.ts`

### 6.1 运行模型

| 项 | 值 |
|----|-----|
| 进程 | 独立 .NET 8 控制台/Web 进程 `LocalMediaManager.Bridge.exe` |
| 地址 | `http://127.0.0.1:47831`（**仅 loopback**，不绑定 0.0.0.0） |
| 启动者 | Tauri `lib.rs` spawn；开发模式见 §04.3 候选路径 |
| 业务库 | `LMM_DATABASE_PATH` → 默认 `…Next Data\data\LocalMediaManager.db` |
| Legacy 根 | `LMM_LEGACY_ROOT` → 旧 WPF 安装根（只读兼容） |
| Legacy 配置库 | `LMM_CONFIG_DATABASE_PATH` → `app_configs.sqlite`（只读） |
| Legacy 图片根 | `LMM_IMAGE_ROOT` → `{legacy}\data\{User}\pic` |
| InstallRoot | `ResolveInstallRoot(AppContext.BaseDirectory)` → MediaStorage 默认推导 |
| Session | `LMM_BRIDGE_TOKEN` 环境变量；React 头 `X-LMM-Session` |

Bridge **不是** Windows 服务常驻公网 API；它是桌面会话的子进程，写能力绑定 Tauri 颁发的 Session。

### 6.2 请求管道

```text
HTTP Request
    ↓
CORS（AllowAnyOrigin — 仅 loopback 可达）
    ↓
Session 中间件（非 GET/HEAD/OPTIONS → 校验 X-LMM-Session）
    ↓
Minimal API Route Handler
    ↓
Service（校验 → 事务 → 文件/子进程）
    ↓
JSON DTO Response
    ↓
全局异常 → { code, message } + HTTP 状态码
```

**异常映射（`Program.cs` 中间件）：**

| 异常 | HTTP | code |
|------|------|------|
| `ArgumentException` | 400 | `INVALID_INPUT` |
| `KeyNotFoundException` | 404 | `NOT_FOUND` |
| `UnauthorizedAccessException` | 409 | `CONFIRMATION_REQUIRED` |
| `InvalidOperationException` | 409 | `CONFLICT` |
| Session 无效 | 401 | `INVALID_SESSION` |
| 无 Session 配置 | 503 | `WRITE_SESSION_UNAVAILABLE` |
| 其他 | 500 | `INTERNAL_ERROR` |

500 响应消息对用户友好（「数据库未提交更改」），详细堆栈写 stderr。

### 6.3 服务分层

```text
Program.cs（路由 + DI 注册）
│
├── 读模型
│   ├── ProductReader          分页查询、Dashboard、Search、详情
│   ├── MaintenanceReader      维护报告
│   └── ImageAssetService      封面解析、Legacy+MediaStorage 读、缓存
│
├── 写模型
│   ├── ProductWriter          评分/收藏/标签/演员/删除/审计回滚
│   ├── SettingsSaveCoordinator Unified Settings（§10）
│   ├── MetadataWriteService   元数据字段合并（补空策略）
│   └── ImageWorkflowService   图片替换/删除（MediaStorage 写）
│
├── 工作流 / HostedService（后台 Tasks）
│   ├── LibraryWorkflowService      扫描导入
│   ├── MetadataSyncExecutor        元数据同步
│   ├── ImageCacheTaskService       缓存重建
│   ├── ImageGenerationTaskService  预览/GIF/截图生成
│   ├── FileOrganizerService        文件整理
│   └── SafeDeleteWorkflowService   安全删除
│
├── 基础设施
│   ├── MediaStoragePathResolver    资源路径（§11）
│   ├── TaskCommandService          pause/resume/cancel/retry
│   ├── TaskLogService              任务日志
│   ├── DataSafetyService           备份/恢复/诊断
│   ├── NfoService                  NFO 读写
│   ├── PlaybackSettingsService     播放器（读 Next + Legacy fallback）
│   ├── RatingHistoryService        Safe Delete 评分记忆
│   ├── PlatformCommandService      打开目录/Reveal 文件
│   ├── ImageDownloadService        Provider 图片下载
│   └── FfmpegLocator               FFmpeg 路径
│
└── Provider
    └── MetaTubeProvider : IMetadataProvider
```

**规则：**
- 路由处理器**不含** SQL；SQL 在 Service 内
- 长批处理**必须**在 `HostedService` + Tasks，不在 HTTP 请求线程阻塞
- 新增业务**优先**扩展现有 Service，不新建平行 Writer

### 6.4 Session 鉴权

| 方法 | Session |
|------|---------|
| GET / HEAD / OPTIONS | 不需要 |
| POST / PUT / DELETE / PATCH | **必须** `X-LMM-Session` == `LMM_BRIDGE_TOKEN` |

Token 由 Tauri 启动 Bridge 时生成 UUID，经 `bridge_session_token` 命令给 React。401 `INVALID_SESSION` 时 `bridge.ts` 刷新 token 重试一次。

无 Token 启动 Bridge（如手动 dotnet run 未设 env）→ 读可用，**写返回 503**。

### 6.5 DTO 约定

- C# 使用 `record` 定义 DTO（`UnifiedSettingsDto`、`MediaPageResult` 等）
- TypeScript 镜像类型在 `src/types/media.ts`、`src/types/settings.ts`
- 新增字段**向后兼容**；破坏性变更须版本化 + Human 决策
- Bridge **不**返回 SQLite 行实体或原始 `DataReader`
- 错误体：`{ "code": "…", "message": "…" }`

### 6.6 危险写模式

所有可能丢数据的操作：

```text
Preview API → 返回 impact + confirmationToken
Confirm API → 携带 token + 用户确认参数
Execute     → 校验 token 固定时间比较 → 事务/任务
```

示例：Safe Delete · Library/Movie Delete · Image Delete · File Organizer Execute · NFO Import/Export Confirm

`UnauthorizedAccessException` → `CONFIRMATION_REQUIRED`（token 过期或 payload 变化）

### 6.7 API 分组（主要端点）

| 分组 | 方法示例 | 服务 |
|------|---------|------|
| **Health** | `GET /health` | 版本、库路径、读写模式 |
| **Dashboard / Search** | `/api/dashboard`, `/api/search`, `/api/search/advanced` | ProductReader |
| **Media** | `GET/POST /api/videos`, `/api/videos/{id}`, batch favorite/rating/tags | ProductReader/Writer |
| **Libraries** | CRUD + scan + delete-preview | LibraryWorkflowService |
| **Tasks** | list/logs/pause/resume/cancel/retry/cleanup | TaskCommandService |
| **Settings** | `GET/PUT /api/settings/all` | SettingsSaveCoordinator |
| **Settings 辅助** | data-safety, diagnostics, export/import-preview | DataSafetyService |
| **Images** | list/replace/generate/delete-preview/lock/content | ImageWorkflow/Asset |
| **Sync** | `POST /api/videos/{id}/sync`, batch | MetadataSyncExecutor |
| **NFO** | export/import preview + execute | NfoService |
| **Organizer** | dry-run/preview/execute | FileOrganizerService |
| **Safe Delete** | preview + execute | SafeDeleteWorkflowService |
| **Entities** | tags/actors/collections | ProductReader/Writer |
| **Play** | `POST /api/videos/{dataId}/play` | PlaybackSettings + 进程启动 |
| **Platform** | open-directory, reveal-file | PlatformCommandService |
| **Covers** | `GET /api/covers/{code}`, `/api/images/{id}/primary` | ImageAssetService |

完整路由：**唯一权威** `Program.cs` + `bridge.ts` 封装。

### 6.8 ProductReader / ProductWriter

**ProductReader** — 无 side effect 查询：影片墙分页、详情、邻居导航、全局/高级搜索、实体列表、收藏/历史集合、元数据概览、诊断、查重结果。

**ProductWriter** — 用户状态与结构变更：
- 评分、收藏、标签 CRUD、演员关系
- 批量操作、删除预览/执行
- 评分记忆 remember/restore
- 演员修复 preview/apply
- 操作审计 rollback

两者共享 `databasePath`；Writer 内事务 + `UserStateAudit` 记录（Migration 0003）。

### 6.9 HostedService 与 Tasks

实现 `IHostedService` 的工作流服务同时注册为 Singleton + HostedService：

```text
Enqueue（HTTP）→ Tasks 表 INSERT Pending
HostedService 循环 → Running → 更新进度/日志
完成/失败/取消 → Completed/Failed/Cancelled
跨重启 → 未完成项可 Retry
```

`TaskCommandService` 统一 pause/resume/cancel/retry/delete/cleanup。

### 6.10 测试

- 单元/集成：`backend/LocalMediaManager.Bridge.Tests/`
- 新 Service 行为**应**有对应 Tests
- 测试用临时目录 + 内存/SQLite 文件，不触正式 `Next Data`

### 6.11 禁止事项

| 禁止 | 参见 |
|------|------|
| Bridge 返回 HTML/UI | §26 Anti Patterns |
| 路由内直接 SQL | 6.3 分层 |
| 写接口对公网开放 | 6.1 loopback |
| HTTP 线程跑全库扫描/同步 | 6.9 Tasks |
| 新建平行 Settings/MediaStorage API | DEC-001, DEC-008 |
| React 绕过 bridge.ts | §04, §26 |

---

## 07. React 架构 [待补]

## 08. Tauri 架构 [待补]

## 09. 数据库设计

Database v1（`LocalMediaManager.db`）是 Next 运行时**唯一可写业务数据库**。Legacy WPF 库只读；React/Tauri **永不**直连。

**路径：** 默认 `{DataRoot}\data\LocalMediaManager.db`（与安装目录分离，升级不覆盖用户数据）

### 9.1 边界

| 库 | 路径 | 模式 | 用途 |
|----|------|------|------|
| **LocalMediaManager.db** | Next Data\data | ReadWrite | 全部 Next 业务 |
| **app_datas.sqlite** | Legacy\data\{User} | ReadOnly | 旧版业务只读/迁移源 |
| **app_configs.sqlite** | Legacy\data\{User} | ReadOnly | 旧版配置；Playback 等 fallback |

### 9.2 核心模型

```text
Movies（逻辑标题）
  ├── MediaFiles（本地/NAS/STRM/URL 文件引用，一部可多文件）
  ├── Images（Poster/Thumb/…，MovieId 或 ActorId）
  ├── UserMovieState（收藏、评分、播放次数、笔记 — 不冗余在 Movies）
  └── 关系：Tags · Actors · Genres · Studios · Series

Libraries → LibraryFolders（扫描源）
Tasks · TaskLogs（长任务）
AppSettings（Unified Settings 键值）
PlayHistory（事件历史，删片 SET NULL 保留事件）
DeletedMovieRatings（Safe Delete 评分记忆）
LegacyIdMappings · MigrationWarnings · ExternalIds
SchemaMigrations（版本与 checksum）
```

**设计要点：**
- `Movies` 与 `UserMovieState` 分离 — 用户状态不污染标题实体
- `Images` 用 `MovieId`/`ActorId` 可空 + CHECK，不用多态 FK
- 删 Library **不**删 Movie；删 Movie **级联**当前文件/关系/Images/UserState
- 物理文件删除与 DB 行删除**分开**操作

### 9.3 Settings 存储

`AppSettings` 表：`Key` · `ValueJson` · `ValueType` · `UpdatedAt`

Settings 键前缀：`metadata.metatube.*` · `nfo.*` · `playback.*` · `ratingHistory.*` · `appearance.*` · `mediaStorage.*` · `movieWall.*`

**唯一写入口：** `SettingsSaveCoordinator`（§10）

### 9.4 Migration 体系

**工具：** `backend/LocalMediaManager.Migration/` · Tauri 启动时执行

**规则：**
- Schema 变更**仅**通过有序 checksummed SQL 文件
- 已发布 Migration **不可修改** checksum
- 文件名：`NNNN_Description.sql`（0001–0012 当前）

| 版本 | 文件 | 主题 |
|------|------|------|
| 0001 | InitialSchema | 核心表 |
| 0002 | RestoreFavoriteState | 收藏迁移 |
| 0003 | UserStateAuditAndRatingMemory | 审计与评分 |
| 0004 | LibraryScanWorkflow | 扫描 |
| 0005 | MetadataSyncWorkflow | 同步 |
| 0006 | ImageAssetWorkflow | 图片 |
| 0007 | NfoWorkflow | NFO |
| 0008 | FileOrganizerWorkflow | 整理 |
| 0009 | PlaybackSettings | 播放 |
| 0010 | DeletedMovieRatings | 删除评分记忆 |
| 0011 | RemoveRatingRetentionClearSetting | Settings 清理 |
| 0012 | MediaStorageSettings | MediaStorage 键（rootPath 空 = 运行时默认） |

**升级流程：**

```text
备份源库 + 记录哈希
  → 临时库应用全部 Migration
  → PRAGMA integrity_check + foreign_key_check
  → 抽样报告
  → 原子切换（失败不替换正式库）
```

### 9.5 运行时

- **WAL** 模式 — 备份前须 checkpoint；复制时含 `-wal`/`-shm`
- **外键** 启用 — Migration 与运行时写入须一致
- **事务** — 多表写入在 Service 层单事务
- **FTS5** —  deferred，待搜索 DTO 稳定

### 9.6 AI 与扩展

Database v1 **不**预建 AI 表；0.6.0+ 经正式 Migration 引入。

### 9.7 证据与映射

| 文档 | 用途 |
|------|------|
| `docs/database/DATABASE_V1_DESIGN.md` | 设计细节 |
| `docs/database/LEGACY_TO_V1_FIELD_MAPPING.md` | 字段映射 |
| `docs/database/SETTINGS_STORAGE_STRATEGY.md` | 凭据策略 |

### 9.8 禁止事项

| 禁止 | 原因 |
|------|------|
| React/Tauri 直连 SQLite | Bridge 边界 |
| Bridge 运行时 ALTER | Migration only |
| 修改已发布 Migration 文件 | checksum 链 |
| 无备份原子切库 | 数据安全 |
| 在 Migration seed 写死绝对 MediaStorage 路径 | DEC-005 |

---

## 10. Settings 系统

Settings 是 LMM 最容易被 AI 改坏的模块。本项目**只有一套 Settings 系统**，所有可写配置经统一 Coordinator 持久化，UI 使用全局 Draft 模式，**禁止局部保存**。

### 10.1 架构总览

```text
SettingsPage (React)
    │
    │  original ←── bridge.allSettings()     GET /api/settings/all
    │  draft    ←── 用户编辑（仅内存）
    │  defaults ←── bridge.defaultSettings() GET /api/settings/defaults
    │
    │  用户点击「保存设置」
    ▼
bridge.saveAllSettings(draft)                PUT /api/settings/all
    │
    ▼
SettingsSaveCoordinator
    │  Normalize() — 校验、规范化、路径探测
    │  ChangedFields() — 对比变更域
    │  SQLite Transaction — 原子写入 AppSettings 表
    ▼
UnifiedSettingsSaveResult { settings, changedFields, message }
    │
    ▼
React 更新 original + draft（两者同步为 saved 状态）
```

**核心类与文件：**

| 组件 | 路径 | 职责 |
|------|------|------|
| `SettingsSaveCoordinator` | `backend/.../SettingsSaveCoordinator.cs` | 唯一读写协调器：Read / Save / Normalize / 事务 |
| `SettingsDefaults` | 同文件底部 | 环境感知默认值（含 MediaStorage 动态 RootPath） |
| `SettingsPage` | `src/pages/SettingsPage.tsx` | 全局 Draft UI、Leave 保护、统一 Save |
| `bridge.ts` | `src/services/bridge.ts` | `allSettings` / `defaultSettings` / `saveAllSettings` |
| `UnifiedSettings` 类型 | `src/types/settings.ts` | 前端 Settings DTO |

### 10.2 UnifiedSettings 结构

`UnifiedSettingsDto` 包含七个域，**一次 Save 全部提交**：

| 域 | DTO | AppSettings 键前缀 | 说明 |
|----|-----|-------------------|------|
| **MetaTube** | `MetaTubeSettingsDto` | `metadata.metatube.*` | Provider 地址、超时、下载图片、写 NFO |
| **Nfo** | `NfoSettingsDto` | `nfo.*` | 导出策略、输出目录、包含图片 |
| **Playback** | `PlaybackSettingsDto` | `playback.*` | 播放器路径或系统默认 |
| **RatingRetention** | `RatingRetentionSettingsDto` | `ratingHistory.*` | Safe Delete 后评分记忆 |
| **Appearance** | `AppearanceSettingsDto` | `appearance.*` | 主题模式 `dark` / `light` |
| **MediaStorage** | `MediaStorageSettingsDto` | `mediaStorage.*` | 根目录、资源子目录、路径模板 |
| **MovieWallDisplay** | `MovieWallDisplaySettingsDto` | `movieWall.*` | 影片墙卡片海报方向与大小 |

`NonDestructive`（MetaTube 不覆盖手工数据）和 `ImportFillEmptyOnly`（NFO 导入只补空）在 Coordinator 层**强制为 true**，UI 不能关闭。

### 10.3 Draft / Original / HasUnsavedChanges

Settings 页面（`SettingsPage.tsx`）使用**全局 Draft 模式**：

| 状态 | 变量 | 含义 |
|------|------|------|
| **Original** | `original: UnifiedSettings` | 上次成功 Save 后从 Bridge 读取的「已保存真相」 |
| **Draft** | `draft: UnifiedSettings` | 用户当前编辑中的草稿（仅 React 内存） |
| **Defaults** | `defaults: UnifiedSettings` | 环境默认值（Restore Defaults 写入 Draft，不直接 Save） |
| **HasUnsavedChanges** | `stable(original) !== stable(draft)` | JSON 序列化对比，任域变化即视为未保存 |

**编辑流程：**

1. `load()` → `bridge.allSettings()` → 同时设置 `original` 和 `draft`（深拷贝）
2. 用户修改 → `updateDraft(key, value)` → 只改 `draft`
3. Appearance 变更 → 即时预览主题（`setMode`），但仍须 Save 才持久化
4. 「恢复默认值」→ `restoreDefaultsToDraft()` → 把 `defaults` 写入 `draft`，提示「点击保存设置后生效」
5. 「保存设置」→ `saveAll()` → `bridge.saveAllSettings(draft)` → 成功后 `original = draft = result.settings`

**禁止：** 在单个 Settings 分区组件内直接调用 Bridge PUT 并更新 local state 当作已保存。

### 10.4 Leave 保护（LeaveIntent 状态机）

未保存变更时，离开 Settings 必须经用户确认：

| LeaveIntent | 触发场景 | 行为 |
|-------------|---------|------|
| `'none'` | 默认 | 无拦截 |
| `'route'` | `useBlocker(hasUnsavedChanges)` 路由跳转 | 弹出 Leave Dialog：保存 / 放弃 / 取消 |
| `'window'` | Tauri `onCloseRequested` 窗口关闭 | 同上；确认后 `invoke('close_local_media_manager')` |

**Discard 流程：** 从 `original` 深拷贝恢复 `draft`，重置主题预览，然后 `blocker.proceed()` 或关闭窗口。

**关键 ref：** `hasUnsavedChangesRef`（供 async close handler）、`allowWindowCloseRef`（Discard/Save 后允许关闭）、`closingAppRef`（防止 beforeunload 循环）。

### 10.5 Coordinator：Validation

`SettingsSaveCoordinator.SaveAsync` 在事务前执行 `Normalize()`：

**MetaTube：**
- BaseUrl 必须是有效 HTTP/HTTPS URL，末尾规范化加 `/`
- TimeoutSeconds 限制 15–180

**Nfo：**
- ExportPolicy 只允许 `SkipExisting` 或 `SeparateFile`
- OutputDirectory 上级目录必须存在

**Playback：**
- 非系统默认时，PlayerPath 必须是存在的 `.exe` 文件

**Appearance：**
- ThemeMode 规范化为 `dark` 或 `light`

**MovieWallDisplay：**
- PosterOrientation 规范化为 `portrait` 或 `landscape`
- PosterSize 规范化为 `small`、`medium` 或 `large`

**MediaStorage：**（详见 §11）
- RootPath 绝对路径、可写、不在安装目录/resources/dist/assets 内
- 资源子目录只能是相对目录名，不能重复
- 模板必须含 `{MovieCode}` 或 `{MovieTitle}`，只支持这两个 token
- 根目录不存在时：须 `createMissingMediaStorageRoot=true` 才创建

校验失败抛 `ArgumentException`，React 捕获后显示错误并自动跳转到对应 Settings 分区。

### 10.6 Coordinator：Transaction

所有变更在**单个 SQLite 事务**内写入 `AppSettings` 表：

```text
BEGIN TRANSACTION
  INSERT ... ON CONFLICT DO UPDATE  (metadata.metatube.*)
  INSERT ... ON CONFLICT DO UPDATE  (nfo.*)
  INSERT ... ON CONFLICT DO UPDATE  (playback.*)
  INSERT ... ON CONFLICT DO UPDATE  (ratingHistory.*)
  INSERT ... ON CONFLICT DO UPDATE  (appearance.*)
  INSERT ... ON CONFLICT DO UPDATE  (mediaStorage.*)
COMMIT
```

- 无变更时直接返回「设置没有变化」，不写库
- `ChangedFields` 返回变更域列表（`metaTube` / `nfo` / `playback` / `ratingRetention` / `appearance` / `mediaStorage`）
- 每行存储：`Key`、`ValueJson`（JSON 序列化）、`ValueType`、`UpdatedAt`

### 10.7 Bridge API

**统一 Settings（Settings 页面必须使用）：**

| 方法 | 路径 | 用途 |
|------|------|------|
| GET | `/api/settings/all` | 读取完整 UnifiedSettings |
| GET | `/api/settings/defaults` | 读取环境默认值 |
| PUT | `/api/settings/all?createMissingMediaStorageRoot=` | 统一保存 |

**Legacy 只读快照：**

| 方法 | 路径 | 用途 |
|------|------|------|
| GET | `/api/settings` | 旧版兼容字段快照（`SettingsSnapshot`，多数只读） |

**数据安全与诊断（即时操作，不经 Draft）：**

| 方法 | 路径 | 用途 |
|------|------|------|
| GET | `/api/settings/data-safety/overview` | 备份/数据概览 |
| POST | `/api/settings/data-safety/backup` | 创建备份 |
| GET | `/api/settings/diagnostics` | 系统诊断 |
| POST | `/api/settings/providers/metatube/test` | MetaTube 连接测试（用 Draft 值，不 Save） |

**历史遗留的分域 PUT 端点**（`/api/settings/nfo`、`/api/settings/playback`、`/api/settings/rating-history`、`/api/settings/providers/metatube`）仍存在于 Bridge，但 **Settings UI 不使用**。新代码禁止新增类似分域 Save 路径。

### 10.8 为什么不能局部保存

1. **一致性：** 六个域可能在业务上相互依赖（如 MediaStorage RootPath 影响 NFO 输出路径策略）。分域 Save 会导致「部分已保存、部分未保存」的中间态，Restart 后行为不可预测。
2. **Leave 保护：** 全局 Draft 使 `HasUnsavedChanges` 语义清晰。局部 Save 会让用户以为已保存某区，离开时发现其他区丢失。
3. **事务安全：** Coordinator 单事务保证全部成功或全部回滚。局部 Save 无法保证跨域原子性。
4. **AI 防重发明：** 历史上 AI 容易为单个 Settings 分区写独立 Save 按钮和独立 API 调用。统一 Coordinator 是明确边界。

### 10.9 禁止事项

| 禁止 | 原因 |
|------|------|
| 新增第二套 Settings 系统 | 已有 Coordinator + AppSettings |
| 在 React 页面内直接写 `AppSettings` 或 `settings.json` | 违反 Bridge 边界 |
| 单个分区组件内独立 Save 到 Bridge | 破坏 Draft → Coordinator → Save 模式 |
| 绕过 Normalize 直接 SQL UPDATE | 跳过校验与 ChangedFields 追踪 |
| 把 API Key/Cookies/Headers 写入普通 JSON 配置 | 须用安全凭据存储（见 `docs/database/SETTINGS_STORAGE_STRATEGY.md`） |
| 用默认值冒充 Legacy 只读字段的值 | Legacy 字段读失败须显示「读取失败」，不静默 fallback |

---

## 11. MediaStorage

MediaStorage 是 LMM 所有媒体资源文件的**统一存储系统**。当前方案：**动态 RootPath + 统一 Resolver + Legacy 只读兼容**（定案见 DEC-004～DEC-010）。

### 11.1 职责范围

MediaStorage 统一管理以下资源类型的**路径解析、目录创建和写入**：

| 资源类型 | 规范化名 | 默认子目录 | Legacy 别名 |
|---------|---------|-----------|------------|
| 海报 | `Poster` | `Posters` | — |
| 缩略图 | `Thumbnail` | `Thumbnails` | `thumb`, `smallpic` |
| 背景图 | `Fanart` | `Fanart` | `bigpic`, `background` |
| 预览图 | `Preview` | `Previews` | `extrapic` |
| 截图 | `Screenshot` | `Screenshots` | `screen` |
| 卡片封面 | `GeneratedCard` | `WallCrops` | `wallcrop`, `cardcover` |
| GIF | `GIF` | `GIF` | — |
| NFO | `NFO` | `NFO` | — |

### 11.2 统一入口：MediaStoragePathResolver

**类：** `backend/LocalMediaManager.Bridge/MediaStoragePathResolver.cs`

**所有新媒体资源写入必须经过 Resolver**，禁止在 Service 中硬编码路径。

**核心方法：**

| 方法 | 用途 |
|------|------|
| `ResolveForMovieAsync(movieId, resourceType, extension, index?, uniqueSuffix?)` | 解析目标完整路径 |
| `EnsureDirectoryForWrite(resourcePath)` | 写入前创建目录 |
| `TemporaryRootAsync()` | 创建 `{RootPath}/.lmm-temp/{guid}/` 临时目录 |
| `IsInsideMediaStorageAsync(path)` | 验证路径在 RootPath 边界内 |
| `NormalizeResourceType(type)` | 别名 → 规范类型名（静态方法） |

**路径结构：**

```text
{RootPath}/{ResourceDirectory}/{MovieFolder}/{FileName}

示例：
D:\Local Media Manager Next Data\MediaStorage\Posters\ABC-123\ABC-123_user-20260718143052123.jpg
```

**模板渲染：**

- `{MovieCode}` → 影片番号（空则用 `movie-{id}`）
- `{MovieTitle}` → 影片标题（空则回退番号）
- 文件名非法字符替换为 `_`；index 后缀 `_001`；uniqueSuffix 用于用户替换防冲突

### 11.3 RootPath 与 InstallRoot 策略

**默认值计算：** `SettingsDefaults.MediaStorageForEnvironment(installRoot, databasePath)`

**决策链：**

```text
1. InstallRoot 已知且非受保护目录？
   └─ DataRoot = {InstallRoot} Data
   └─ DataRoot 可写？
      └─ YES → {DataRoot}\MediaStorage          (UsingFallback = false)

2. databasePath 可推导 DataRoot？
   └─ YES → {DataRoot}\MediaStorage              (UsingFallback = false)

3. 回退 Documents：
   └─ {Documents}\Local Media Manager\MediaStorage  (UsingFallback = true)
```

**受保护 InstallRoot：** Program Files、Program Files (x86)、Windows 目录 — 不可在其旁创建 DataRoot。

**UsingFallbackDefault 标志：** 当使用 Documents 回退时为 `true`；Settings 页面首次加载时显示一次性通知，引导用户到「设置 → 媒体存储」修改。

**用户自定义 RootPath：** 通过 Settings 保存到 `AppSettings` 键 `mediaStorage.rootPath`；Save 时校验绝对路径、可写、不在安装目录/resources/dist/assets 内。

### 11.4 Documents Fallback

当 InstallRoot 位于受保护目录（如 `C:\Program Files\Local Media Manager`）时：

- 默认 RootPath 回退到 `{Documents}\Local Media Manager\MediaStorage`
- Bridge 启动时 stderr 输出：`Media storage default path fallback: install root is protected...`
- UI 通过 `usingFallbackDefault` 字段感知并提示用户

**禁止写死 `D:\` 或任何固定盘符路径。** 所有默认路径必须经 `SettingsDefaults` 动态计算。

### 11.5 Unified Write（统一写入管线）

**写入 Resolver 的服务：**

| 服务 | 写入场景 |
|------|---------|
| `ImageWorkflowService.ReplaceAsync` | 用户替换图片 → Resolver 路径 → Copy → 注册 Images 表 |
| `MetadataSyncExecutor` | 同步下载图片 → Resolver 路径 |
| `ImageGenerationTaskService` | FFmpeg 生成预览/GIF/截图 → Resolver 路径 |
| `NfoService` | NFO 导出 → Resolver NFO 目录 |

**ImageWorkflowService 写入流程（典型）：**

```text
1. 校验图片类型（SupportedTypes）
2. ImageFileValidator 校验源文件
3. pathResolver.ResolveForMovieAsync(movieId, type, ext, suffix)
4. pathResolver.EnsureDirectoryForWrite(target)
5. File.Copy(source, target)
6. RegisterImageAsync → Images 表
7. InvalidateMovieCacheAsync
```

**Images 表** 记录 `Path`（MediaStorage 完整路径）、`ImageType`、`IsLocked`、`Source` 等；封面展示经 `ImageAssetService` 读取。

### 11.6 Legacy Read（Legacy 只读兼容）

Bridge 同时维护 **Legacy 图片根**（`LMM_IMAGE_ROOT`，默认 `{legacyRoot}\data\{UserName}\pic`）用于：

- 旧版已存在图片的查找与封面展示（`ImageAssetService`、`/api/covers/{code}`）
- Legacy 缓存目录（`{imageRoot}/.lmm-cache/thumbnails`）

**关键规则：**

| 操作 | 路径 |
|------|------|
| **读取** Legacy 图片 | `imageRoot`（Legacy pic 目录） |
| **写入** 新媒体资源 | `MediaStoragePathResolver` → MediaStorage RootPath |
| **回写** Legacy 路径 | **禁止** |

`ImageWorkflowService` 删除时：`IsInsideControlledImageRootAsync` 判断文件是否在 Legacy 或 MediaStorage 受控目录内，决定是否物理删除文件。

### 11.7 Directory Strategy

**AppSettings 存储的目录配置：**

| 键 | 默认值 | 约束 |
|----|--------|------|
| `mediaStorage.rootPath` | 动态计算 | 绝对路径，可写 |
| `mediaStorage.directory.posters` | `Posters` | 相对目录名 |
| `mediaStorage.directory.thumbnails` | `Thumbnails` | 相对目录名 |
| `mediaStorage.directory.fanart` | `Fanart` | 相对目录名 |
| `mediaStorage.directory.previews` | `Previews` | 相对目录名 |
| `mediaStorage.directory.screenshots` | `Screenshots` | 相对目录名 |
| `mediaStorage.directory.wallCrops` | `WallCrops` | 相对目录名 |
| `mediaStorage.directory.gif` | `GIF` | 相对目录名 |
| `mediaStorage.directory.nfo` | `NFO` | 相对目录名 |
| `mediaStorage.template.movieFolder` | `{MovieCode}` | 含 `{MovieCode}` 或 `{MovieTitle}` |
| `mediaStorage.template.fileName` | `{MovieCode}` | 同上 |

**目录约束（Coordinator Normalize）：**
- 子目录只能是相对名称 — 禁止绝对路径、盘符、`..`
- 八个子目录名不能重复
- 写入前不存在则 `EnsureDirectoryForWrite` 自动创建

### 11.8 禁止事项

| 禁止 | 原因 |
|------|------|
| 新增第二套 MediaStorage 或路径解析器 | 已有 `MediaStoragePathResolver` |
| 硬编码 `D:\` 或固定磁盘路径 | InstallRoot/Documents 动态策略 |
| 直接 `File.Write` 到任意路径而不经 Resolver | 绕过模板、SafePathSegment、边界检查 |
| 新媒体资源写入 Legacy pic 目录 | Legacy 只读；写入只走 MediaStorage |
| 在 React 中拼接资源路径 | 路径解析是 Bridge 职责 |
| RootPath 指向安装目录/resources/dist/assets | Coordinator 拒绝 |
| 跳过 `IsInsideMediaStorageAsync` 做删除/移动 | 防止误删 MediaStorage 外文件 |

## 12. Legacy 兼容

Legacy（旧 WPF / Jvedio）是 LMM 的**行为权威、数据兼容源、回归期望**——**不是** UI 模板，**不是** 写入目标。

### 12.1 定位

| 维度 | Legacy | LMM Next |
|------|--------|----------|
| UI | WPF/XAML — **仅参考功能** | Material UI — 产品界面 |
| 业务库 | `app_datas.sqlite` | `LocalMediaManager.db` |
| 配置库 | `app_configs.sqlite` | `AppSettings` + Coordinator |
| 图片 | `{legacy}\data\{User}\pic` | MediaStorage（写）+ Legacy pic（读） |
| 安装 | 旧 WPF 稳定目录 | `D:\Local Media Manager Next` |
| 写入 | **Next 禁止写入** | 唯一可写运行时库 |

**原则：** 旧安装、旧库、用户正式媒体**永远不被 Next 构建覆盖**。

### 12.2 只读访问

Bridge 以 `SqliteOpenMode.ReadOnly` 打开 Legacy 库（迁移工具除外）：

| 资源 | 环境变量 / 路径 | 用途 |
|------|----------------|------|
| Legacy 安装根 | `LMM_LEGACY_ROOT` | 默认 `D:\Jvedio\Jvedio5.0` |
| 业务库 | `{legacy}\data\{User}\app_datas.sqlite` | 迁移源、只读对照 |
| 配置库 | `LMM_CONFIG_DATABASE_PATH` | Playback 等 fallback 读取 |
| 图片根 | `LMM_IMAGE_ROOT` | 封面查找、Legacy 缓存 |

`SettingsReader` / `PlaybackSettingsService` 可读 Legacy 配置；Next **不写** `app_configs.sqlite`。

### 12.3 迁移与 Database v1

**全量迁移：** `LocalMediaManager.Migration` CLI

```text
只读打开 Legacy 库 + 哈希记录
  → 临时库应用 SchemaMigrations + 数据迁移
  → integrity_check + FK + 抽样报告
  → 用户 confirmSwitch 后原子替换 LocalMediaManager.db
  → 备份在 …Next Data\data\backups\
```

**增量 Schema：** Tauri 启动前运行 Migration exe，应用 0002–0012 等升级脚本。

字段映射证据：`docs/database/LEGACY_TO_V1_FIELD_MAPPING.md`

### 12.4 业务规则（必须遵守）

权威清单：`docs/migration/LEGACY_BUSINESS_RULES.md`。核心摘要：

| 规则 | Next 实现要点 |
|------|--------------|
| **用户数据优先** | 同步/刮削只补空；不覆盖手工标题、标签、评分、收藏、锁定图 |
| **删除评分记忆** | Safe Delete 只记 MovieCode+Rating；导入时 restore |
| **收藏与重命名** | `UserMovieState.IsFavorite`；重命名失败不撤销收藏 |
| **演员修复** | 先 preview；不批量猜同名 |
| **扫描** | 支持 .vob/.mpg/.mpeg；单文件失败不中止全库 |
| **同步** | 手工同步入口保留；Provider 结果记来源 |
| **图片** | BigPic/ExtraPic/卡图兼容；不覆盖源图 |
| **播放** | 启动成功才记历史 |
| **搜索** | 关键词搜索永远存在 |

### 12.5 Feature Parity 矩阵

**唯一迁移完成权威：** `docs/migration/FEATURE_PARITY_MATRIX.md`

标记「已完整迁移」**必须**含：UI 操作 · Bridge 执行 · 持久化/重启 · 错误处理 · 备份/回滚 · 测试 · 烟测 · commit · 验收。

### 12.6 Legacy Read / Unified Write

见 §11.6 · **DEC-009**：读 Legacy pic；写仅 MediaStorage；禁止回写 Legacy。

### 12.7 禁止事项

| 禁止 | 原因 |
|------|------|
| 修改/覆盖 Legacy 安装或数据库 | 稳定版隔离 |
| 复制 WPF 布局 | 产品已选 MUI |
| 凭记忆判断迁移状态 | 必须以矩阵为准 |
| Next 写 Legacy 库 | 只读边界 |

---

## 13. 元数据系统

元数据是 LMM **核心业务**：番号、标题、演员、标签、Provider 刮削、图片、NFO 与同步任务。

### 13.1 架构

```text
React → bridge.ts → Bridge
  MetaTubeProvider (IMetadataProvider)
  MetadataSyncExecutor (HostedService + Tasks)
  MetadataWriteService（补空合并）
  ImageDownloadService → MediaStoragePathResolver
  NfoService → MediaStorage NFO 目录
  SettingsSaveCoordinator.metaTube / .nfo
```

### 13.2 MetaTube Provider

**配置：** `UnifiedSettings.metaTube` — enabled · baseUrl · timeout · downloadImages · writeNfo · autoExecute · **nonDestructive（强制 true）**

**测试：** `POST /api/settings/providers/metatube/test`（Draft 值，不 Save）

### 13.3 同步工作流

**触发：** `POST /api/videos/{id}/sync` · batch · 扫描后入队 · autoExecute

**执行：** Enqueue → Provider 详情 → MetadataWriteService 补空 → 下载图片 → 可选写 NFO → TaskLog

**原则：** 不覆盖手工字段 · 锁定图不覆盖 · 关系事务写入

### 13.4 图片与 NFO

- 下载：`ImageDownloadService` + Resolver → MediaStorage（§11.5）
- NFO：Export/Import preview → confirm；Import **fillEmptyOnly**；Provider 写 MediaStorage/NFO

### 13.5 UI 入口

`/metadata` 概览 · 详情页同步/NFO/图片 · Settings 元数据分区 · `/tasks` 进度

### 13.6 禁止事项

| 禁止 | 原因 |
|------|------|
| React 直连 Provider HTTP | Bridge 边界 |
| 覆盖用户手工元数据 | LEGACY_BUSINESS_RULES |
| HTTP 线程全库同步 | Tasks + HostedService |
| NFO/图片写 Legacy 路径 | DEC-009 |

---

## 14. UI Design System

LMM Next **没有** `DesignSystem.xaml`。设计体系基于 **Material UI 9 + Emotion**，由主题文件和共享组件共同构成。Clash Verge Rev 仅作现代桌面信息架构参考，**不复制**其品牌或界面。

**权威源文件：**

| 文件 | 职责 |
|------|------|
| `src/themes/theme.ts` | 调色板、圆角、字体、MUI 组件 override |
| `src/themes/ThemeContext.tsx` | 深浅模式切换、`useColorMode()` |
| `src/layouts/AppShell.tsx` | 桌面壳、左侧导航（220px）、Bridge 状态 |
| `src/components/PageHeader.tsx` | 页面标题区 |
| `src/components/ProductComponents.tsx` | StatCard、SurfaceSection、EmptyState、HealthMeter |
| `src/components/workspace/Workspace.tsx` | WorkspacePage、FilterBar、WorkspaceToolbar |
| `src/components/MediaCard.tsx` | 影片卡片网格与卡片 |
| `src/components/settings/SettingsComponents.tsx` | Settings 布局与只读 Legacy 字段展示 |
| `docs/UI_DESIGN_SPEC.md` | 设计规范详细版（证据层；本章为 AI 决策用百科） |

### 14.1 设计目标

- 现代、安静、信息清晰的桌面工具视觉语言
- 所有页面在统一壳层（AppShell）中工作
- 功能可持续增加，但**不为单个模块建立另一套配色、间距或控件风格**
- 旧 WPF 页面仅用于理解**功能行为**，不作为布局/样式参考

### 14.2 页面框架

```text
AppShell (100vh grid: 220px nav + main)
├── 左侧导航 Paper
│   ├── BrandMark
│   ├── 全局搜索 TextField
│   ├── primary 导航（首页/影片墙/媒体库/...）
│   ├── utility 导航（元数据/诊断/任务/设置/...）
│   └── Bridge 连接状态
└── main (overflow-y: auto, padding 16–24px)
    └── 页面内容
        ├── PageHeader 或 WorkspacePage 头部
        ├── FilterBar（若有筛选）
        └── SurfaceSection / Card 内容区
```

**规则：**
- 主内容使用 AppShell 的统一滚动容器；页面**不得**创建重复的全屏滚动条
- 一级导航进入固定产品页；二级详情（如 `/movies/:id`）使用统一返回上下文
- 左侧导航宽度固定 220px；选中态 `ListItemButton selected`

### 14.3 间距（4px 基础网格）

| 用途 | 值 | MUI 等价 |
|------|-----|---------|
| 紧密图标/文字 | 4–8px | `gap: 0.5–1` |
| 同组控件 | 8–12px | `gap: 1–1.5` |
| 卡片内部 | 16–20px | `p: 2–2.5` |
| 区块之间 | 20–24px | `mb: 2.5–3` |
| 页面边距 | 小 16px / 常规 24px | `p: { xs: 2, md: 3 }` |
| 导航项间距 | ~3px | `mb: 0.4` |
| 导航圆角 | 14px | `borderRadius: 1.75` |

- 交互控件高度一致；工具栏优先 `size="small"`
- 圆角由主题统一：`shape.borderRadius: 10`；SurfaceSection 可用 `borderRadius: 3`（24px）
- 点击目标 ≥ 36×36px；主要按钮建议 ≥ 40px 高

### 14.4 颜色与主题

**主题创建：** `createLmmTheme(mode: 'dark' | 'light')`

| Token | Dark | Light |
|-------|------|-------|
| `primary.main` | `#0A84FF` | `#007AFF` |
| `secondary.main` | `#FF9F0A` | `#FC9B76` |
| `success.main` | `#30D158` | `#06943D` |
| `error.main` | `#FF453A` | `#FF3B30` |
| `background.default` | `#0f1117` | `#f5f5f7` |
| `background.paper` | `#181b23` | `#ffffff` |

**规则：**
- 所有颜色来自 MUI theme token（`primary.main`、`text.secondary`、`divider`、`action.hover`）
- **禁止**为深色模式写死 `#000`/`#fff` 背景
- **禁止**在单页定义品牌色
- 状态语义：成功=绿、警告=橙、错误=红、信息=蓝
- 深浅主题必须具有相同信息结构和可读对比度
- Card 边框：`rgba(255,255,255,.08)`（dark）/ `rgba(15,23,42,.09)`（light）；`boxShadow: none`

### 14.5 Typography

| 层级 | MUI variant | 字重 | 用途 |
|------|------------|------|------|
| 页面标题 | `h4` | 750 | PageHeader |
| Workspace 标题 | `h5` | 900 | WorkspacePage |
| 区块标题 | `h6` | 800 | SectionTitle |
| Settings 分区 | `subtitle1` | 750 | SettingsSection |
| 正文 | `body1` | 400 | 默认正文 |
| 辅助信息 | `body2` / `caption` | — | `text.secondary` |
| 按钮 | `button` | 600 | `textTransform: none` |

- 字体栈：`-apple-system, BlinkMacSystemFont, "Microsoft YaHei UI", "Microsoft YaHei", Roboto, sans-serif`
- 番号、路径、日期等不可换行字段：`noWrap` + Tooltip 完整内容
- **不用纯颜色表达状态** — 颜色必须配合文字、图标或 Chip

### 14.6 Button

| 类型 | 使用场景 |
|------|---------|
| `contained` | 每区块最多**一个**主要操作（保存、确认、执行） |
| `outlined` | 次要操作（预览、测试连接、导出） |
| `text` | 低优先级（取消、返回、刷新） |
| `IconButton` | 卡片 Hover 播放等；必须配 Tooltip 和 `aria-label` |

- `MuiButton`: `disableElevation: true`
- 危险操作使用 `color="error"`，执行前必须 Preview + Confirm Dialog
- 危险确认动词明确（「删除」「永久删除」），不用含糊「确定」

### 14.7 Card

**StatCard：** 42×42 图标容器 + h5 数值 + body2 标签；Hover 轻微 `translateY(-2px)`

**MediaCard（影片卡片）：**
- 固定比例 `aspectRatio: '2 / 3'`
- 信息顺序：海报 → 番号 → 状态标签 → 导入日期 → 星级
- `新加入` = success 色；`已收藏` = error/强调色
- 单击 → 详情；双击 → 播放；Hover 显示播放按钮
- 图片 `object-fit: cover`；`SmartImage` 按需加载
- Hover：`translateY(-5px)` + 阴影；尊重 `prefers-reduced-motion`

**EmptyState：** 44px Inbox 图标 + 标题 + 描述，最小高度 220px

**SurfaceSection：** `Paper variant="outlined"` + `borderRadius: 3` + 内边距 16–20px

### 14.8 Dialog

- 标题描述**操作对象**（不是泛泛「确认」）
- 正文展示：影响范围、不可逆内容、错误恢复方式
- 按钮顺序：**取消在左、确认在右**
- Settings Leave Dialog：「继续编辑 / 放弃更改 / 保存并离开」
- `DangerConfirmDialog`（`src/components/workspace/DangerConfirmDialog.tsx`）用于删除等危险操作

### 14.9 Toolbar 与 FilterBar

**WorkspaceToolbar**（`Workspace.tsx`）：
- `primaryActions` + `secondaryActions`，右对齐
- 动作定义：`WorkspaceAction { key, label, icon?, onClick, variant?, color?, disabled? }`

**FilterBar**（`Workspace.tsx`）：
- 显示 `activeCount` 活跃筛选数
- 「清除筛选」入口必须可见
- 筛选变化默认回到第一页
- 窄窗口自动换行，不产生水平滚动
- 同栏输入框、下拉框、按钮高度一致（`size="small"`）

**视图切换：** `ToggleButtonGroup` 切换 grid/list（`ViewModuleRoundedIcon` / `ViewListRoundedIcon`）

### 14.10 Icon

- 优先 **MUI Icons Rounded** 风格（如 `HomeRoundedIcon`、`SettingsRoundedIcon`）
- 同一功能全局使用同一图标（导航 vs 操作 vs 状态职责分开）
- 禁止 emoji 替代正式产品图标
- 禁止与行为无关的装饰图标

### 14.11 响应式与缩放

- 必测 Windows **100% / 125% / 150%** 缩放
- 目标视口：1920×1080、1536×864、1280×720
- 不得出现页面级横向溢出
- Tauri 最小窗口：900×600
- 窄窗口：卡片列数减少（`auto-fill, minmax(148px, 1fr)`）、工具栏换行
- Grid 布局：`gridTemplateColumns: { xs: '1fr', lg: '230px minmax(0,1fr)' }` 等响应式断点

### 14.12 状态展示

| 状态 | 组件 | 要求 |
|------|------|------|
| 加载 | `CircularProgress` / `LinearProgress` | WorkspacePage `loading` prop |
| 空数据 | `EmptyState` | 统一文案，不用空白页 |
| Bridge 断开 | `Alert severity="error"` | AppShell 底部 + 页面级 |
| 错误 | `Alert severity="error"` 或 Dialog | 说明发生了什么、什么未改变、用户如何处理 |
| 成功 | `Snackbar` | 短暂反馈（如「设置已保存」） |
| Bridge 健康 | `HealthMeter` | Settings 诊断区 |

### 14.13 禁止事项

| 禁止 | 原因 |
|------|------|
| 复制 WPF/XAML 布局、间距、控件样式 | Legacy 是行为参考，不是 UI 模板 |
| 新建页面私有配色/间距体系 | 破坏 Design System 一致性 |
| 页面级全屏滚动条 | AppShell 已提供统一滚动 |
| 恢复顶部 Tab 主导航 | 产品已确定左侧导航 |
| 纯图标按钮无 Tooltip/aria-label | 可访问性 |
| 危险操作无 Preview/Confirm | 安全链 |
| 写死颜色值而不用 theme token | 深浅主题一致性 |

---

## 15. 页面规范 [待补]

## 16. 功能模块

本节是 LMM 产品模块的全景图。AI 读完后应知道整个软件有哪些模块、各自职责、对应页面与 Bridge 入口。

### 16.1 模块总览

```text
Local Media Manager
│
├── 浏览与发现
│   ├── Dashboard（首页）
│   ├── Movie Wall（影片墙）
│   ├── Movie Detail（影片详情）
│   ├── Global Search（全局搜索）
│   └── Advanced Search（高级搜索）
│
├── 媒体库管理
│   ├── Libraries（媒体库 CRUD）
│   ├── Scan & Import（扫描导入 → Tasks）
│   └── File Organizer（文件整理 → Tasks）
│
├── 元数据
│   ├── Metadata Center（元数据概览）
│   ├── MetaTube Provider（同步 → Tasks）
│   ├── NFO Import/Export
│   └── Metadata Sync（单部/批量 → Tasks）
│
├── 实体与集合
│   ├── Tags Category（标签分类页）
│   │   ├── Directors（导演）
│   │   ├── Movie Tags（影片标签）
│   │   ├── Series（系列）
│   │   └── Custom Tags（自定义标签）
│   ├── Actors（演员，实体能力）
│   ├── Favorites（收藏）
│   └── History（最近播放）
│
├── 媒体资源
│   ├── Images（Poster/Thumb/Fanart/Preview/...）
│   ├── Image Cache（缓存清理/重建 → Tasks）
│   ├── Image Generation（预览/GIF/截图 → Tasks）
│   └── MediaStorage（路径配置，见 §11）
│
├── 用户状态
│   ├── Rating & Favorite
│   ├── Rating History（Safe Delete 记忆）
│   └── Tag/Actor 编辑
│
├── 任务与自动化
│   ├── Tasks Center（统一任务中心）
│   ├── Library Scan Tasks
│   ├── Metadata Sync Tasks
│   ├── Image Cache/Generation Tasks
│   ├── File Organizer Tasks
│   └── Safe Delete Tasks
│
├── 数据安全
│   ├── Safe Delete（安全删除）
│   ├── Backup & Restore
│   ├── Data Safety Overview
│   └── Maintenance Report
│
├── 诊断与维护
│   ├── Diagnostics Center
│   ├── Duplicate Results（查重）
│   └── System Diagnostics
│
├── 播放
│   └── Player Launch（外部播放器）
│
├── 设置
│   └── Settings Center（见 §10）
│
├── 扩展（Placeholder / Future）
│   ├── Plugin Center（0.8.0）
│   ├── AI Provider（0.6.0）
│   └── NAS（0.7.0）
│
└── 基础设施
    ├── Bridge（业务边界）
    ├── Migration（数据库升级）
    ├── Tauri Shell（桌面壳）
    └── Tasks System（长任务引擎）
```

### 16.2 浏览与发现

| 模块 | 路由 | 页面 | Bridge 入口 | 状态 |
|------|------|------|------------|------|
| **Dashboard** | `/` | `HomePage.tsx` | `GET /api/dashboard` | ✅ 已发布 |
| **Movie Wall** | `/media` | `MediaPage.tsx` | `GET /api/videos` | ✅ 已发布 |
| **Movie Detail** | `/movies/:id` | `MovieDetailPage.tsx` | `GET /api/videos/{id}`、`/neighbors` | ✅ 已发布 |
| **Global Search** | AppShell 搜索框 → `/search` | `SearchPage.tsx` | `GET /api/search` | ✅ 已发布 |
| **Advanced Search** | `/search`（筛选参数） | `SearchPage.tsx` | `GET /api/search/advanced` | ✅ 已发布 |

**影片卡片：** `MediaCard` + `MediaCardGrid`；封面 `GET /api/images/{movieId}/primary`

**Smart Search：** 影片墙与搜索页共用 `GET /api/search/advanced`。普通关键词以 AND 组合；每个关键词在番号、标题、原始标题、简介、文件路径/名、演员、导演、标签、自定义标签、Genre、厂商、系列、媒体库名之间 OR 匹配。结构化字段支持 `演员:`、`导演:`、`标签:`、`自定义标签:`、`系列:`、`厂商:`、`媒体库:`、年份与评分比较、收藏/观看布尔条件。当前数据不能可靠用 `Tags.Source` 区分标签来源，`标签:` 与 `自定义标签:` 均恢复历史 `Tags/MovieTags` 查询语义，并排除状态 Badge。分类入口参数、FilterBar 与 Smart Search 条件统一 AND。

**MovieWall：** 所有本质属于影片列表的页面复用 `MovieWall`：全部影片、收藏、最近播放、搜索结果、导演/系列/标签/自定义标签/媒体库进入后的结果页。`MovieWall` 统一 Smart Search、FilterBar、排序、分页、卡片/列表视图和详情返回滚动恢复。影片墙卡片显示偏好通过 `UnifiedSettings.movieWallDisplay` 持久化并在所有 MovieWall 页面共享：竖版海报比例 `2:3`，横版海报比例 `16:9`，尺寸为 `small` / `medium` / `large`。卡片模式使用响应式 CSS Grid 自动计算列数；列表模式不受海报方向与大小影响。

**MovieWall 分页：** 分页由 `MovieWall` 统一管理为右下角半透明悬浮控件，显示上一页、当前页/总页数、下一页。点击当前页数字进入页码输入，`Enter` 跳转，`Esc` 取消，跳页后滚动回影片墙顶部并保留搜索、FilterBar、排序和视图状态。MovieWall 页面支持左/右方向键翻页和 `Ctrl+G` 聚焦页码输入；输入框、表单、下拉框或弹窗获得焦点时不抢占按键，详情页不使用这组快捷键。

**列表返回状态：** `MovieWall` 进入详情前记录当前列表状态签名与 scrollY；从详情返回且默认条件、搜索、FilterBar、排序、页码、视图未变时恢复滚动。用户改变查询条件后不复用旧滚动。

### 16.3 媒体库管理

| 模块 | 路由 | Bridge 入口 | 工作流 |
|------|------|------------|--------|
| **Libraries** | `/libraries` | `GET/POST/PUT /api/libraries` | CRUD + 删除预览 |
| **Library Scan** | Libraries 页触发 | `POST /api/libraries/{id}/scan` | → `LibraryWorkflowService` → Tasks |
| **File Organizer** | 详情/Maintenance | `POST /api/organizer/dry-run` → preview → execute | → `FileOrganizerService` → Tasks |

**Library 删除：** `GET .../delete-preview` → `POST .../delete`（ConfirmCommand）

### 16.4 元数据

| 模块 | 路由 | Bridge 入口 | 说明 |
|------|------|------------|------|
| **Metadata Center** | `/metadata` | `GET /api/metadata/overview` | 刮削状态概览 |
| **MetaTube Sync** | 详情/Tasks | `POST /api/videos/{id}/sync`、batch sync | → `MetadataSyncExecutor` |
| **MetaTube Settings** | `/settings` metadata 区 | `UnifiedSettings.metaTube` | 经 Coordinator Save |
| **MetaTube Test** | Settings 页 | `POST /api/settings/providers/metatube/test` | 不 Save，用 Draft 值测试 |
| **NFO Export** | 详情页 | `GET/POST /api/videos/{id}/nfo/export-*` | Preview → Confirm |
| **NFO Import** | 详情页 | `GET/POST /api/videos/{id}/nfo/import-*` | fillEmptyOnly |

**原则：** 自动同步默认补空，不覆盖手工数据、锁定图片或用户 NFO。

### 16.5 实体与集合

| 模块 | 路由 | Bridge 入口 |
|------|------|------------|
| **Tags Category** | `/tags` | 二级分类页：导演、标签、系列、自定义标签 |
| **Custom Tags** | `/tags/custom` | `GET /api/entities/tags`、CRUD `/api/tags` |
| **Movie Tags** | `/tags/movie-tags` | `GET /api/entities/movie-tags` |
| **Actors** | `/actors` | `GET /api/entities/actors`、PUT `/api/actors/{id}` |
| **Directors** | `/tags/directors` | `GET /api/entities/directors` |
| **Series** | `/tags/series` | `GET /api/entities/series` |
| **Actor Repair** | Metadata/Maintenance | `GET/POST /api/actors/repair-*` |
| **Favorites** | `/favorites` | `GET /api/collections/favorites` |
| **History** | `/history` | `GET /api/collections/history` |

**批量操作：** `POST /api/videos/batch/favorite`、`/batch/rating`、`/batch/tags`

**实体来源：** 标签与自定义标签当前均基于历史 `Tags/MovieTags` 查询语义，不按 `Tags.Source` 强行拆分；真实库中 `LegacyLabel` 同时承载了用户维护标签（如“五星”“高颜值”）和状态标识（如“已收藏”），因此 `Source` 不能可靠区分标签来源。状态 Badge（如 `新加入`、`已收藏`）不是标签分类，不进入 `/tags` 二级分类与标签统计。Genre 使用独立 `Genres/MovieGenres`，不是标签。系列使用 `Series/MovieSeries`；导演使用 `Directors/MovieDirectors`（旧库可不存在，UI 显示空态）。实体列表数量使用 `COUNT(DISTINCT MovieId)`，点击实体后跳转影片墙并传递明确 ID 参数（如 `directorId`、`seriesId`、`movieTagId`、`customTagId`），不创建第二套影片列表。

### 16.6 媒体资源（Images & MediaStorage）

| 模块 | 触发位置 | Bridge 入口 | 执行 |
|------|---------|------------|------|
| **Image List** | 详情页 | `GET /api/videos/{id}/images` | 读 Images 表 |
| **Image Replace** | 详情页 | `POST /api/videos/{id}/images/{type}/replace` | `ImageWorkflowService` → MediaStorage |
| **Image Generate** | 详情页 | `POST .../images/{type}/generate` | `ImageGenerationTaskService` → Tasks |
| **Image Delete** | 详情页 | preview → `POST /api/image-assets/{id}/delete` | Preview + Confirm |
| **Image Lock** | 详情页 | `PUT /api/image-assets/{id}/lock` | 锁定防覆盖 |
| **Image Cache Cleanup** | Settings | `GET/POST /api/images/cache/cleanup-*` | 即时或 Tasks |
| **Image Cache Rebuild** | Settings | `POST /api/images/cache/rebuild` | → Tasks |
| **MediaStorage Config** | Settings → 媒体存储 | `UnifiedSettings.mediaStorage` | 经 Coordinator Save |

### 16.7 用户状态

| 操作 | Bridge 入口 | 服务 |
|------|------------|------|
| 设置评分 | `POST /api/videos/batch/rating` | `ProductWriter` |
| 收藏/取消 | `POST /api/videos/batch/favorite` | `ProductWriter` |
| 记住评分（Safe Delete 前） | `POST /api/videos/{id}/remember-rating` | `ProductWriter` + `RatingHistoryService` |
| 恢复评分（同名再导入） | `POST /api/videos/{id}/restore-rating` | `RatingHistoryService` |
| 编辑标签 | `POST/PUT/DELETE /api/tags` | `ProductWriter` |
| 编辑演员 | `PUT /api/actors/{id}`、`PUT /api/videos/{id}/actors` | `ProductWriter` |
| 操作回滚 | `POST /api/operations/{auditId}/rollback` | `ProductWriter` |

### 16.8 任务中心（Tasks）

| 模块 | 路由 | Bridge 入口 |
|------|------|------------|
| **Tasks Center** | `/tasks` | `GET /api/tasks`、`GET /api/tasks/{id}/logs` |
| **Pause/Resume/Cancel/Retry** | Tasks 页 | `POST /api/tasks/{id}/pause|resume|cancel|retry` |
| **Cleanup** | Tasks 页 | `POST /api/tasks/cleanup` |
| **Batch Cancel Sync** | Tasks 页 | `POST /api/tasks/batch/cancel-sync` |

**任务来源：** Library Scan、Metadata Sync、Image Cache Rebuild、Image Generation、File Organizer、Safe Delete

**状态机：** `Pending → Running → Paused → Completed / Failed / Cancelled`

**HostedService 执行器：** `LibraryWorkflowService`、`MetadataSyncExecutor`、`ImageCacheTaskService`、`ImageGenerationTaskService`、`FileOrganizerService`、`SafeDeleteWorkflowService`

### 16.9 数据安全

| 模块 | 位置 | Bridge 入口 | 工作流 |
|------|------|------------|--------|
| **Safe Delete** | 详情/批量 | `POST /api/delete/preview` → `POST /api/delete/execute` | Preview → Confirm → Task |
| **Movie Delete** | 详情 | `GET/POST /api/videos/{id}/delete-*` | Preview → Confirm |
| **Backup** | Settings → 数据 | `POST /api/settings/data-safety/backup` | 即时 |
| **Restore Plan** | Settings → 数据 | `POST /api/settings/data-safety/restore-plan` | Preview |
| **Settings Import/Export** | Settings → 数据 | `GET /api/settings/export`、`POST /api/settings/import-preview` | Preview |
| **Maintenance Report** | `/maintenance` | `GET /api/maintenance/report` | 只读 |

### 16.10 诊断与维护

| 模块 | 路由 | Bridge 入口 |
|------|------|------------|
| **Diagnostics Center** | `/diagnostics` | `GET /api/diagnostics` |
| **Duplicate Results** | `/duplicates` | `GET /api/duplicates` |
| **System Diagnostics** | Settings → 日志与诊断 | `GET /api/settings/diagnostics` |
| **Library Summary** | 首页/多处 | `GET /api/library/summary` |
| **Data Safety Overview** | Settings | `GET /api/settings/data-safety/overview` |

### 16.11 播放（Player）

| 操作 | Bridge 入口 | 说明 |
|------|------------|------|
| 播放影片 | `POST /api/videos/{dataId}/play` | 读取 PlaybackSettings → 启动外部播放器 |
| 播放器配置 | `UnifiedSettings.playback` | 系统默认或自定义 `.exe` 路径 |

Bridge **不内嵌播放器**；只负责路径解析和进程启动。无法返回可靠退出码时需诊断与降级（见 TODO）。

### 16.12 设置（Settings Center）

见 **§10 Settings 系统**。路由 `/settings`，页面 `SettingsPage.tsx`。

Settings 分区：常规 · 媒体库 · 扫描与导入 · 元数据与同步 · 图片与缓存 · 媒体存储 · 播放器 · 搜索与筛选 · 快捷键 · 外观 · 数据与备份 · 日志与诊断 · 关于。影片墙显示偏好位于「外观」分区。

### 16.13 扩展模块（Placeholder）

| 模块 | 路由 | 当前状态 | 目标版本 |
|------|------|---------|---------|
| **Plugin Center** | `/plugins` | Placeholder UI | 0.8.0 |
| **AI Provider** | `/ai-providers` | Placeholder UI | 0.6.0 |
| **NAS** | — | 未实现 | 0.7.0 |

这些模块在 UI 中存在入口，但**不得**在 Placeholder 阶段冒充完整功能或直连 Provider/模型。

### 16.14 基础设施模块

| 模块 | 技术 | 职责 |
|------|------|------|
| **Bridge** | .NET 8 HTTP | 所有业务唯一执行边界（§06） |
| **Migration** | .NET 8 CLI | checksummed Schema 升级（§09） |
| **Tauri Shell** | Rust + Tauri 2 | 窗口、Bridge 子进程、打包（见 §08 待补） |
| **Tasks Engine** | Bridge HostedServices | 所有长任务后台执行与恢复 |

### 16.15 模块间依赖关系

```text
Libraries ──scan──→ Tasks ──→ Movies 入库
Movies ──sync──→ MetadataSyncExecutor ──→ Images + NFO (MediaStorage)
Movies ──play──→ PlaybackSettings ──→ 外部 Player
Movies ──delete──→ SafeDeleteWorkflow ──→ RatingHistory 保留
Settings ──save──→ Coordinator ──→ AppSettings ──→ 影响 MediaStorage/Provider/Playback
Images ──replace──→ MediaStoragePathResolver ──→ MediaStorage 文件 + Images 表
Organizer ──dry-run──→ Preview ──confirm──→ Execute ──→ Tasks
```

### 16.16 禁止事项

| 禁止 | 原因 |
|------|------|
| 在 React 页面内新建绕过 Bridge 的业务路径 | 架构分层 |
| 以 Placeholder 页面冒充已迁移功能 | Feature Parity 矩阵要求 |
| 在 Tasks 外执行长批处理 | 统一任务生命周期 |
| 跳过 Preview 直接 Execute 危险操作 | 安全链 |
| 为新模块创建独立 Settings/MediaStorage 子系统 | 见 §10、§11 |

## 17. Git Workflow

### 分支模型

```text
main                          ← 仅 Release；可构建、可发布
  └── sprint/0.x.x-xx-topic   ← 全部开发
```

**命名：** `sprint/0.5.0-17-media-resource-write` · 一 Sprint 一主题

### 规则

- 中间 Commit 留 Sprint 分支；未验收不推 `main`
- 禁止 force push `main`；Commit/Push 须 Human 明确要求
- Release：`v0.x.x` Tag + Rollback Tag 成对

### 发布前文档

`ROADMAP` · `TODO` · `CHANGELOG` · `docs/releases/` · 知识闭环 §24–§25

详见 **AGENTS.md §06–§12**

---

## 18. Build Workflow

### Self Test

```powershell
pnpm install --frozen-lockfile
pnpm build:web
dotnet test backend/LocalMediaManager.Bridge.Tests/LocalMediaManager.Bridge.Tests.csproj -c Release
cargo check --manifest-path src-tauri/Cargo.toml
```

### 开发

```powershell
pnpm bridge:dev    # Bridge
pnpm dev           # Vite（需 Bridge）
pnpm tauri:dev     # 全栈
```

### Release 全量

```powershell
.\scripts\build-next.ps1
```

Web → Bridge publish → Migration publish → Tauri NSIS  
**输出：** `src-tauri/target/release/bundle/nsis/*.exe`

---

## 19. Deploy Workflow

| 路径 | 用途 |
|------|------|
| `D:\Local Media Manager Next` | 安装（不覆盖旧 WPF） |
| `D:\Local Media Manager Next Data` | DB · MediaStorage · 备份 |
| `D:\Local Media Manager Next Backups` | 部署备份 |

```powershell
.\scripts\deploy-local-release.ps1   # -SkipBuild -Launch
```

禁止覆盖 Legacy 安装；危险烟测用隔离目录与样本库。

---

## 20. Testing Workflow

### 阶段

`Planning → Design → Develop → Self Test → Smoke → Freeze → Release → Archive`

### Self Test（AI）

Web build · Bridge.Tests · cargo check · 文档与 Decision 同步

### Smoke（Human）

独立安装版 · TEST_PLAN TP-* · 主题/缩放 · 写入 `docs/releases/`

### 阻断级 ★★★★★

启动 · Bridge · Migration · 收藏 · 评分 · 标签 · Safe Delete 记忆 · 演员 · 播放

**禁止：** 编译即 Release；伪造烟测。

---

## 21. Sprint History（摘要）

完整决策见 **`DECISION_LOG.md`**。此处仅索引。

| Sprint | 分支 | 摘要 | Decision ID |
|--------|------|------|-------------|
| 0.5.0-15 | `sprint/0.5.0-15-settings-global-save` | Settings 全局 Draft + Coordinator 统一 Save + LeaveIntent | DEC-001 ～ DEC-003 |
| 0.5.0-16 | `sprint/0.5.0-16-media-storage-settings` | MediaStorage 纳入 Settings；动态 RootPath；Documents 回退 | DEC-004 ～ DEC-007 |
| 0.5.0-17 | `sprint/0.5.0-17-media-resource-write` | 统一写入 Resolver；Legacy Read 保留；WallCrops 纳入 | DEC-008 ～ DEC-010 |
| 0.5.0-20 | `sprint/0.5.0-20-moviewall-display` | MovieWall 显示偏好、响应式卡片尺寸和悬浮分页交互 | DEC-012 |

Sprint 0.5.0-02 ～ 0.5.0-14 待 backlog 考古后追加（DEC-011+，不阻塞开发）。

## 22. Roadmap [待补]

## 23. 权威文档索引 [待补]

---

## 24. Knowledge Lifecycle

知识必须**随代码闭环**，禁止「代码已变、文档未变」。

### 24.1 知识流（宏观）

```text
        Code（实现）
           ↓
        Sprint（主题边界）
           ↓
        Decision（定案 + DEC-xxx）
           ↓
        Project（当前真相百科）
           ↓
        Agent（执行规范）
           ↓
        Release（证据归档）
           ↓
        docs/（Release · Matrix · Audit — 不可变证据）
```

**读的方向（AI 开工）：** Project → Decision Log → Agents → 任务相关 docs  
**写的方向（Sprint 结束）：** Code → Decision → Project → Agents → Release docs

### 24.2 Sprint 结束同步链（强制）

每个 Sprint 在 Human 验收后**必须**走完：

```text
① Code Complete        — 功能/修复按主题完成
② Build                — Web + Bridge (+ Tauri 若涉及)
③ Test                 — Bridge.Tests + 类型检查
④ Smoke                — Human 安装版关键路径
⑤ Human Acceptance     — 体验与产品确认
⑥ Decision Updated     — DECISION_LOG 新增/更新（Status: Accepted）
⑦ Project Updated      — 当前真相章节同步（若行为/边界变化）
⑧ Agent Updated        — AGENTS 若流程/边界变化（按需）
⑨ Release Note         — CHANGELOG + docs/releases + ROADMAP/TODO
```

缺 **⑥⑦** 中任一项（当代码改变了设计边界时）→ Sprint **不算知识闭环完成**。见 **§25 Definition of Done**。

### 24.3 各层何时更新

| 层 | 触发条件 | 更新什么 |
|----|---------|---------|
| **Code** | 每个 Develop Commit | 实现 |
| **Decision** | 新定案 / 踩坑 / 返工 | DECISION_LOG（Status + DEC-xxx） |
| **Project** | 模块边界、API、Settings、MediaStorage 等**当前方案**变化 | 对应 § 章节 |
| **Agents** | AI 流程、Decision Boundary、Report 格式变化 | AGENTS 章节 |
| **docs/** | Release、矩阵行、Migration 证据 | releases / matrix / audits |

**PROJECT 不写历史演变** — 历史进 DECISION_LOG；PROJECT 只更新「现在是什么样」。

### 24.4 ADR 演进（Phase 5 计划）

当决策数量增长后：

```text
DECISION_LOG.md          ← 永远只是索引表
  DEC-001  Settings 全局保存  Accepted  →  docs/decisions/DEC-001-settings-unified-save.md
  DEC-002  LeaveIntent        Accepted  →  docs/decisions/DEC-002-settings-leaveintent.md
  …
```

**好处：** 单文件不膨胀 · 单决策 Diff 隔离 · AI 按需读一条 · 修改不牵动全书

Phase 5 前：正文暂存 `DECISION_LOG.md` 锚点章节（当前做法）。

### 24.5 反模式（知识债务）

| ❌ | 后果 |
|----|------|
| 只改代码不更新 Decision | 下一会话 AI 重发明轮子 |
| PROJECT 写历史演变 | 百科膨胀、真相不清 |
| chat 里定方案不进 ROADMAP | 虚假版本承诺 |
| Release 无 docs/releases | 无法回归验证 |
| 矩阵无证据标「已迁移」 | 功能假象 |

---

## 25. Definition of Done

**Sprint Done** 须全部满足；缺一项则 Sprint **未完成**：

| # | 门槛 | 负责 |
|---|------|------|
| ① | **Code** — 功能/修复按 Sprint 主题完成 | AI + Human 审 |
| ② | **Build** — Web + Bridge (+ Tauri 若涉及) 通过 | AI |
| ③ | **Test** — 相关 Bridge.Tests + 类型检查 | AI |
| ④ | **Smoke** — 安装版关键路径（TEST_PLAN 适用项） | Human |
| ⑤ | **Human Acceptance** — UI/体验/产品确认 | Human |
| ⑥ | **Decision Updated** — DECISION_LOG（+ 未来 ADR 文件） | AI 起草 + Human 审 |
| ⑦ | **Project Updated** — PROJECT 章节与当前真相一致 | AI 起草 + Human 审 |
| ⑧ | **Agent Updated** — AGENTS 若流程/边界变化 | 按需 |
| ⑨ | **Release Note** — CHANGELOG + docs/releases 验证报告 | Human 发布时 |

Feature Parity 项另须矩阵证据行更新（见 §12.5）。

---

## 26. Anti Patterns

以下方向**绝对禁止**。细节与 Decision ID 见 `DECISION_LOG.md` · `AGENTS.md` §25。

### 架构

| ❌ Anti Pattern | 正确做法 |
|---------------|---------|
| 第二套 Settings 系统 | UnifiedSettings + Coordinator（DEC-001） |
| 第二套 MediaStorage / 路径解析 | `MediaStoragePathResolver`（DEC-008） |
| 第二套 Database Access（React/Tauri 直连 SQLite） | Bridge DTO only |
| React 直接访问 SQLite / 文件系统 / Provider | `bridge.ts` |
| Bridge 内嵌 UI 或返回 HTML | JSON DTO |
| Rust 写媒体业务逻辑 | Tauri 仅壳 + Bridge 子进程 |
| Bridge 旁路（页面内偷偷 fetch 别的后端） | 统一 Bridge API |

### Settings & Storage

| ❌ Anti Pattern | 正确做法 |
|---------------|---------|
| Settings 局部保存 / 分域 Save | Draft → `PUT /api/settings/all`（DEC-001） |
| 绕过 Coordinator 写 AppSettings | `SettingsSaveCoordinator`（DEC-003） |
| 写死 `D:\` 或固定盘符路径 | `SettingsDefaults` 动态 InstallRoot（DEC-005） |
| Save 时静默创建 MediaStorage 根目录 | 显式 `createMissingMediaStorageRoot`（DEC-007） |
| 新媒体资源写入 Legacy pic | Unified Write → MediaStorage（DEC-009） |
| 绕过 Resolver 写资源文件 | Resolver + EnsureDirectoryForWrite（DEC-008） |
| WallCrop 独立路径逻辑 | GeneratedCard → WallCrops（DEC-010） |

### Legacy & 产品

| ❌ Anti Pattern | 正确做法 |
|---------------|---------|
| 复制 WPF / 恢复顶部 Tab | Material UI + 左导航（§02） |
| 只读展示冒充功能已迁移 | FEATURE_PARITY_MATRIX 全证据 |
| 覆盖用户手工数据 / 锁定图 / 用户 NFO | 补空 + non-destructive |
| 无 Preview 的危险写 | Preview → Confirm → Execute |
| Placeholder 冒充完整功能 | 矩阵 + Roadmap 版本 |

### 流程

| ❌ Anti Pattern | 正确做法 |
|---------------|---------|
| 跳过 Build/Test 声明完成 | §25 Definition of Done |
| 伪造 Smoke / 迁移验收 | docs/releases 真实记录 |
| 代码发布不更新 Decision/PROJECT | §24 Knowledge Lifecycle |
| 未授权 Push main / Release Tag | AGENTS §06 Git |

---
