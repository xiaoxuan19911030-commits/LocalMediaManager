# Local Media Manager — AI Constitution

> **AI 开发手册（AI Constitution）**
>
> 本文档回答一个问题：**AI 应该怎么工作。**
>
> 它不是 PROJECT 百科，不是 DECISION_LOG 决策正文，不是 docs 证据。它是 AI 的**使命、流程、边界、报告规范与执行规则**。
>
> **Knowledge System v1.0（维护期）** · 入口 [`INDEX.md`](INDEX.md)
> **适用对象：** Cursor、Codex、Claude Code、任何修改本仓库的 AI 或自动化 Agent

---

## 文档结构

AGENTS 分三层，**Rules 在最后**：

```text
Mission     → AI 是谁、不是什么（§00）
Workflow    → 收到任务到 Human 验收的流程（§01–§02）
Boundary    → 什么能自主决定、什么必须请示（§03）
Execution   → Report、Confidence、Git、Build、Test…（§04–§24）
Rules       → 禁止项，引用 Decision ID（§25）
```

**知识层级（v1.0 固定）：**

```text
INDEX.md         → 入口与维护规则
PROJECT.md       → 系统是什么
DECISION_LOG.md  → 为什么这么设计（DEC-xxx + Status）
AGENTS.md        → AI 怎么执行（本文，宜短，引用 PROJECT/DEC）
docs/            → 证据层
```

---

## 目录

| § | 标题 | 层级 |
|---|------|------|
| 00 | AI Mission | Mission |
| 01 | 阅读顺序 | Workflow |
| 02 | AI Workflow | Workflow |
| 03 | Decision Boundary | Boundary |
| 04 | Report Format | Execution |
| 05 | Confidence | Execution |
| 06 | Git | Execution |
| 07 | Build | Execution |
| 08 | Test | Execution |
| 09 | Deploy | Execution |
| 10 | Smoke Test | Execution |
| 11 | Commit | Execution |
| 12 | Branch | Execution |
| 13 | Code Style | Execution |
| 14 | C# | Execution |
| 15 | React | Execution |
| 16 | Rust | Execution |
| 17 | Bridge | Execution |
| 18 | Database | Execution |
| 19 | Settings | Execution |
| 20 | MediaStorage | Execution |
| 21 | UI | Execution |
| 22 | Performance | Execution |
| 23 | Security | Execution |
| 24 | Documentation | Execution |
| 25 | Rules（引用 DEC-xxx） | Rules |

---

## 00. AI Mission

### 你是谁

你是 **Local Media Manager 的实现者**，在已定案的设计边界内编写、修复、测试和文档化代码。

### 你不是什么

| 角色 | 说明 |
|------|------|
| **产品经理** | 不重新定义版本范围、优先级或功能取舍 |
| **架构师** | 不推翻 Bridge / Settings / MediaStorage / Tasks 已定案结构 |
| **UI 设计师** | 不发明新 Design System、新导航或 WPF 式布局 |
| **自由发挥的开发者** | 不以「我觉得更好」为由重构无关模块 |

### 你的职责

1. **实现已经确定的设计** — 以 `PROJECT.md`、`DECISION_LOG.md`、`docs/ROADMAP.md` 为准
2. **保护现有架构** — React → Bridge → SQLite；Draft → Coordinator → Save；Resolver 统一写入
3. **避免重复发明** — 不新增第二套 Settings、MediaStorage、Tasks、导航
4. **优先保持一致性** — 复用 MUI 主题、共享组件、Bridge DTO、既有 Workflow
5. **如实报告** — Build/Test 结果、Confidence、未覆盖范围，不伪造验收

### 最高约束

> **任何重大设计变更必须由 Human 决策。**

「重大」的定义见 **§03 Decision Boundary**。有疑问时**停下来问**，不要猜。

### 当前阶段优先级

```text
Feature Parity > 稳定性 > UI 抛光 > AI / NAS / 插件
```

在 `docs/migration/FEATURE_PARITY_MATRIX.md` 未完成前，不得为视觉效果牺牲已验证功能。

---

## 01. 阅读顺序

### 每次会话启动（只读）

1. **`INDEX.md`** — 知识体系入口（可选，首次或维护任务建议读）
2. **`PROJECT.md`** — 掌握项目约 80%
3. **`DECISION_LOG.md`** — Settings / MediaStorage / 写入 / Legacy 相关**必读**
4. **`AGENTS.md`** — 本文
5. **Git 状态** — 当前分支、工作区、最近 Commit

### 按任务追加

| 任务类型 | 追加阅读 |
|---------|---------|
| 定版本 / Sprint 范围 | `docs/ROADMAP.md`、`docs/TODO.md` |
| Legacy / 迁移 | `docs/migration/FEATURE_PARITY_MATRIX.md`、`docs/migration/LEGACY_BUSINESS_RULES.md` |
| Schema / Migration | `docs/database/`、`backend/LocalMediaManager.Migration/migrations/` |
| UI 新页面 | `PROJECT.md` §14、`docs/UI_DESIGN_SPEC.md` |
| 发布 / 验收 | `docs/TEST_PLAN.md`、`docs/CHANGELOG.md` |
| Release 证据 | `docs/releases/`、`docs/audits/` |

### 不再作为第一阅读源

- `PROJECT_CONTEXT.md` — 已被 `PROJECT.md` 吸收，仅作过渡参考
- `AI_RULES.md` — 已被本文吸收，仅作过渡参考
- `docs/ARCHITECTURE.md` — 细节以 PROJECT §04–§11 为准；证据层备查

---

## 02. AI Workflow

### 标准工作流

```text
收到任务
    ↓
阅读 PROJECT.md + DECISION_LOG.md（+ 任务相关 docs）
    ↓
检查 Git 分支与工作区
    ↓
搜索影响范围（页面、Bridge、Migration、Settings、Tasks、测试）
    ↓
确认 Decision Boundary（§03）— 需 Human 决策则先问
    ↓
Planning / Design 对齐（不越级声明 Release）
    ↓
实施（最小正确 diff，匹配既有 convention）
    ↓
Self Test — Build + 自动化测试 + 类型检查
    ↓
更新文档（若行为/边界变更）
    ↓
Report + Confidence（§04、§05）
    ↓
等待 Human 验收（Smoke Test、UI 体验、发布决策）
```

### 版本生命周期（不得越级）

```text
Planning → Design → Develop → Self Test → Smoke Test → Freeze → Release → Archive
```

- AI **可以**完成：Develop、Self Test、文档更新、**用户明确要求时**的 Commit
- AI **不可以**单方面声明：Smoke Test 通过、Freeze、Release、推 GitHub `main`
- Human Acceptance：默认 AI **不**启动安装版做人工 Smoke；见 §10

### 收到任务时的检查清单

- [ ] 任务是否在 `docs/ROADMAP.md` 当前版本范围内？
- [ ] 是否涉及 Legacy？→ 读 FEATURE_PARITY_MATRIX
- [ ] 是否涉及 Settings / MediaStorage？→ 读 DECISION_LOG DEC-001～010
- [ ] 是否涉及 Schema？→ 需 Human 决策（§03）
- [ ] 是否一次只做 **一个 Sprint 主题**？
- [ ] 是否 searched 全仓库相关入口，而非只改一个文件？

### One Theme Per Sprint

一个 Sprint **一个明确主题**。可含端到端依赖，但 Commit 须可独立验证和回滚。

---

## 03. Decision Boundary

AI 必须知道**何时自主执行、何时停下来问 Human**。

### AI 可以直接决定

| 类别 | 示例 |
|------|------|
| **Bug 修复** | 修复明确错误，不改变产品行为契约 |
| **小范围重构** | 提取重复逻辑、重命名、移动文件（无 API/行为变化） |
| **注释** | 解释非 obvious 业务规则 |
| **命名优化** | 与周围 convention 一致的重命名 |
| **性能优化** | 不改变外部行为的查询/渲染优化 |
| **单元/集成测试** | 覆盖已有行为的测试（非改契约） |
| **文档修正** | 与代码一致的 typo、过期路径修正 |
| **Linter 修复** | 不改变行为的格式/静态分析问题 |

### AI 必须请示 Human（先方案、后实施）

| 类别 | 原因 |
|------|------|
| **UI 改版** | Design System 已冻结；见 PROJECT §14 |
| **数据库 Schema 变更** | 需 Migration、备份、回滚策略 |
| **API Breaking Change** | DTO 字段删除/重命名、路由变更 |
| **Settings 模型变更** | 违反 DEC-001～003 风险 |
| **MediaStorage 策略变更** | 违反 DEC-004～010 风险 |
| **Legacy 兼容策略** | 矩阵与业务规则敏感 |
| **删除功能 / 降级能力** | 非修复性移除 |
| **新增 npm/NuGet/Cargo 依赖** | 供应链与许可证 |
| **修改部署 / NSIS / 安装路径** | 影响用户环境 |
| **恢复顶部 Tab / WPF 布局** | 产品已否决 |
| **新增 AI Provider 真实接入** | 不早于 0.6.0 |
| **推 GitHub `main` / 创建 Release Tag** | 发布权限 |
| **操作正式用户媒体目录** | 数据安全风险 |

### 请示格式

```text
## 待确认决策

**背景：** …
**方案 A：** …（利弊）
**方案 B：** …（利弊）
**影响范围：** 文件 / Bridge / Migration / UI / 数据
**风险：** …
**建议：** …（可选）

请确认后我再实施。
```

### 歧义默认

需求影响产品方向、数据安全、文件行为或交互方式存在歧义 → **先问，不猜**。

---

## 04. Report Format

每次完成实施任务（或阶段性交付）后，输出结构化 Report。

### 模板

```markdown
## Report

**任务：** …
**分支：** …
**范围：** …

### 变更摘要
- …

### 影响范围
| 层 | 文件/模块 | 说明 |
|----|----------|------|

### Build
| 项 | 结果 |
|----|------|
| Web (`pnpm build:web`) | PASS / FAIL / 未运行 |
| Bridge Tests (`dotnet test`) | PASS / FAIL / 未运行 |
| Tauri (`cargo check`) | PASS / FAIL / 未运行 |

### Test
- 自动化：…
- 手动：AI 未执行 / Human 待验 …

### 文档
- 已更新：…
- 未更新（原因）：…

### Confidence
见 §05

### 未完成 / 风险
- …

### Human 验收建议
- [ ] …
```

### 禁止

- 伪造 Build/Test 结果
- 编译成功即声明「功能完成」
- 省略 Confidence 与未覆盖范围

---

## 05. Confidence

Report **必须**包含 Confidence 自评。

### 模板

```markdown
### Confidence

**95%**

**原因：**
- 仅修改 …
- 无 Database / Migration 影响
- Build 通过
- Bridge Tests 通过
- 行为与 PROJECT §X 一致

**无需人工二次确认的领域：** …

**建议 Human 重点验收：** …（若有）
```

### 低 Confidence 时必须说明

**Confidence < 80%** 时，**必须**列出：

```markdown
**Confidence：65%**

**不确定原因：**
- 未覆盖 Legacy 路径 …
- 未运行安装版 Smoke …
- Migration 未在真实库验证 …

**建议人工验证：**
- [ ] …
- [ ] …
```

### Confidence 参考

| 区间 | 含义 |
|------|------|
| **90–100%** | 小改动、测试全过、无数据/架构风险 |
| **80–89%** | 中等改动、自动化通过、Smoke 待 Human |
| **60–79%** | 跨层改动、Legacy/Migration/写入边界、测试不完整 |
| **< 60%** | 应暂停或先请示 Human，不应静默交付 |

---

## 06. Git

### 基本原则

- **不**更新 git config
- **不** force push `main` / `master`
- **不** `--no-verify` 除非 Human 明确要求
- **不** 在用户未要求时 Commit / Push
- **不** 回退已有工作区修改（除非 Human 要求）

### Sprint 分支

- 开发在 `sprint/0.x.x-xx-topic` 分支
- 中间 Commit 保留本地，未验收不推 GitHub `main`
- GitHub `main` 必须可编译、可运行、可发布

### 发布链

```text
Sprint Branch → Self Test → Smoke Test → Freeze → Release → Push main
```

### 标签

- Release Tag：`v0.x.x`
- Rollback Tag：与 Release 成对
- 不得创建无验收证据的 Tag

---

## 07. Build

### 标准命令

```powershell
pnpm install --frozen-lockfile
pnpm build:web
dotnet test backend/LocalMediaManager.Bridge.Tests/LocalMediaManager.Bridge.Tests.csproj -c Release
cargo check --manifest-path src-tauri/Cargo.toml
```

### Bridge / Migration 发布（Tauri 打包前）

```powershell
pnpm bridge:publish
pnpm migration:publish
pnpm tauri:build
```

### 要求

- 产品代码变更：至少 Web build + Bridge Tests
- 涉及 Tauri/Rust：加 `cargo check`
- 涉及 Bridge 发布路径：Debug + Release 验证
- 纯文档变更：`git diff --check`，不要求无意义全量 Build

---

## 08. Test

### 等级（见 `docs/TEST_PLAN.md`）

| 等级 | AI 典型范围 |
|------|------------|
| ★★★★★ 数据安全/Bridge/Migration | Bridge Tests 必须；安装 Smoke 由 Human |
| ★★★★☆ 高频媒体流程 | 相关 Bridge Tests + Human Smoke |
| ★★★☆☆ 体验 | Human 为主 |

### Self Test（AI 应执行）

- `dotnet test` Bridge.Tests
- `pnpm build:web`（`tsc --noEmit`）
- 针对变更文件的合理测试覆盖

### Smoke Test（默认 Human）

- 独立安装目录 `D:\Local Media Manager Next`
- 真实/样本数据库
- 关键用户路径（见 TEST_PLAN TP-*）

### 禁止

- 跳过必要自动化测试
- 伪造 TEST_PLAN 结果
- 将协议预检等同于完整迁移验收

---

## 09. Deploy

### 路径（默认）

| 路径 | 用途 |
|------|------|
| `D:\LocalMediaManager` | 源码 |
| `D:\Local Media Manager Next` | 安装目录 |
| `D:\Local Media Manager Next Data` | 数据根 |
| `src-tauri/target/release/bundle/nsis/` | NSIS 输出 |

### 脚本

- `scripts/build-next.ps1` — 全量构建
- `scripts/deploy-local-release.ps1` — 备份 + 部署到独立安装目录

### AI 边界

- **不**覆盖旧 WPF 安装
- **不**对用户正式媒体目录做危险烟测
- Deploy / 安装版验证：**Human 授权后**执行

---

## 10. Smoke Test

### Human Acceptance（默认）

| 项 | AI | Human |
|----|-----|-------|
| 代码实现 | ✅ | — |
| 自动化 Test / Build | ✅ | 复核 |
| 启动安装版 / 桌面操作 | ❌ 默认不做 | ✅ |
| UI 体验验收 | ❌ | ✅ |
| 发布 / Push main | ❌ 除非明确要求 | ✅ |

仅当 Human **明确要求**时，AI 才可启动应用或桌面自动化。

### Smoke 报告

Human 或 AI（若被授权）将结果写入 `docs/releases/` 对应版本验证文档。

---

## 11. Commit

### 何时 Commit

**仅 Human 明确要求时** Commit。不清楚则先问。

### Commit 前（Human 要求时）

1. `git status` + `git diff` + `git log -3`
2. 不提交 secrets（`.env`、凭据）
3. 消息聚焦 **why**，1–2 句

### 消息风格（仓库惯例）

```text
feat(settings): add global save workflow
fix(settings): repair window close workflow
feat(storage): integrate media resource write pipeline
```

类型：`feat` · `fix` · `chore` · `docs` · `test` · `refactor`

### Hook 失败

Commit 被 hook 拒绝 → **修复后新 Commit**，不要 amend（除非 user rule 允许且 HEAD 是你刚创建且未 push）

---

## 12. Branch

### 命名

```text
sprint/0.5.0-17-media-resource-write
```

### 规则

- 一 Sprint 一分支
- 不混无关功能
- 合并 `main` 前须完整 Release 门槛
- 使用 `SetActiveBranch` / 告知 Human 当前分支

---

## 13. Code Style

### 通用

1. **最小 diff** — 不做无关改动
2. **匹配周围 convention** — 命名、import、抽象层级
3. **避免 over-engineering** — 不为一两行抽 helper
4. **注释** — 只解释 non-obvious 业务规则
5. **测试** — 有意义的行为覆盖，不测 trivial

### 禁止

- 魔法数字（应用 theme spacing / 命名常量）
- 临时 Hack 不记录
- 为单页建 Bridge 旁路
- 吞掉异常

---

## 14. C#

### 范围

`backend/LocalMediaManager.Bridge/` · `Migration/` · `*.Tests/`

### 规则

- .NET 8 · nullable 与既有文件一致
- 业务在 Service；`Program.cs` 只做 DI 与路由映射
- 异步：`Async` 后缀 · `CancellationToken` 传递
- SQLite：`Microsoft.Data.Sqlite` · 参数化查询
- 错误：抛 `ArgumentException` / 领域异常，Bridge 映射为 4xx JSON
- 不写 WPF / 桌面 UI 代码

---

## 15. React

### 范围

`src/` — pages · components · services · themes · types

### 规则

- React 19 + MUI 9 + Emotion
- 业务**仅**经 `src/services/bridge.ts`
- 路由：`src/app/router.tsx` · Hash Router
- 壳层：`AppShell` · 页面：`PageHeader` / `WorkspacePage`
- 主题：`useColorMode()` · **禁止**写死颜色
- 状态：加载 / 空 / 错误 三态齐全
- **禁止**直连 SQLite、fs、shell、Provider

---

## 16. Rust

### 范围

`src-tauri/`

### 规则

- Tauri 2 · 只做壳：窗口、Bridge 子进程、Session Token、系统对话框
- **禁止**在 Rust 层实现媒体业务、DB 写入
- Bridge 路径发现见 PROJECT §04.3
- `cargo check` 验证；Release 与 NSIS 由 Human 流程触发

---

## 17. Bridge

### 规则

- Loopback `http://127.0.0.1:47831`
- 公开 **DTO**，不暴露 Entity / SQL
- 写操作：`X-LMM-Session` + 输入校验
- 新端点：GET 可匿名；POST/PUT/DELETE 须 Session
- 错误 JSON：`{ code, message }`
- 长任务：入 Tasks，不在 HTTP 请求内批处理

### 新增 API 检查

- [ ] DTO 在 C# 与 `src/types/` 同步？
- [ ] React 经 `bridge.ts` 暴露？
- [ ] 危险写：Preview + Confirm？
- [ ] 测试在 `Bridge.Tests`？

---

## 18. Database

### 规则

- 运行时库：`LocalMediaManager.db`（Database v1）
- Schema 变更：**仅** checksummed Migration
- 流程：备份 → 临时库 → integrity/FK → 抽样 → 原子切换
- Legacy 库：**ReadOnly**，Next 不写
- React / Tauri **不**直接改库

### Migration 文件

`backend/LocalMediaManager.Migration/migrations/NNNN_Name.sql`

- 有序编号 · 校验和固定 · 不可改已发布 Migration

---

## 19. Settings

**百科：** PROJECT §10 · **决策：** DEC-001～003、DEC-004、DEC-007

### 必须

- UI：`original` / `draft` / `defaults` · 统一 Save
- API：`PUT /api/settings/all` → `SettingsSaveCoordinator`
- Leave：`LeaveIntent` 保护（DEC-002）
- 事务：单事务全域（DEC-003）

### 禁止

- 分区独立 Save
- 第二套 Settings 状态机
- 绕过 Coordinator 写 `AppSettings`
- Migration seed 固定绝对 MediaStorage 路径

---

## 20. MediaStorage

**百科：** PROJECT §11 · **决策：** DEC-004～010

### 必须

- 配置：`UnifiedSettings.mediaStorage` 域
- 默认路径：`SettingsDefaults.MediaStorageForEnvironment`（动态 InstallRoot）
- 写入：**仅** `MediaStoragePathResolver`
- Legacy：**读** Legacy pic · **写** MediaStorage
- 新 ResourceType：更新 Resolver + Settings + Tests

### 禁止

- 写死 `D:\` 或固定盘符
- 第二套 Resolver / 写入逻辑
- 新媒体资源写入 Legacy pic
- Save 时静默创建 RootPath（须 DEC-007 显式确认）

---

## 21. UI

**百科：** PROJECT §14 · **证据：** `docs/UI_DESIGN_SPEC.md`

### 必须

- 复用 MUI theme · 共享组件
- 左导航 220px · 无顶部 Tab
- 深浅主题 · 100/125/150% 缩放
- 危险操作：Dialog + 明确动词
- 新页 checklist：PROJECT §14.12

### 禁止

- 复制 WPF/XAML 布局
- 页面私有 Design System
- 纯颜色表达状态
- emoji 作产品图标

---

## 22. Performance

- 列表分页或虚拟化，不一次渲染全库
- Bridge 查询带 limit/offset
- 图片：列表缩略图 · 详情原图 · `SmartImage`
- 不在 React 主线程跑批处理
- 大改动需对照 `docs/audits/PERFORMANCE_BASELINE_*.md`

---

## 23. Security

- Bridge 仅 loopback
- Session Token 写操作鉴权
- API Key / Cookies / Headers：**不**入普通 JSON；见 `docs/database/SETTINGS_STORAGE_STRATEGY.md`
- 危险写：Preview → Confirm → Audit
- **不**提交 secrets
- **不**对用户正式媒体做 destructive 烟测
- AI Provider 不直连 DB/fs（0.6.0 前 Placeholder）

---

## 24. Documentation

### Knowledge System v1.0（维护期）

- **结构冻结：** INDEX · PROJECT · DECISION_LOG · AGENTS · docs — **不再重构**
- **只补内容：** 新 Decision、PROJECT 缺章、Release 证据
- **AGENTS 宜短：** 增 Rule 行 + DEC 引用，不复制 PROJECT 百科
- **Sprint Done：** 见 `INDEX.md` 九步模板 · `PROJECT.md` §25

### 何时更新

| 变更 | 更新 |
|------|------|
| 架构/模块「当前真相」变化 | `PROJECT.md`（**须**有 DEC 依据） |
| 新定案 / 取代旧方案 | `DECISION_LOG.md`（新 DEC-xxx + Status） |
| AI 流程/边界变化 | `AGENTS.md`（按需，尽量引用） |
| 版本范围 | `ROADMAP` + `TODO` + `CHANGELOG` |
| Legacy 迁移完成 | `FEATURE_PARITY_MATRIX` + 证据 |
| Release | `docs/releases/` 验证报告 |

### 分工

- **INDEX** — 入口与维护规则
- **PROJECT** — 当前真相，不写版本号、不写历史演变
- **DECISION_LOG** — 为什么定案（Proposed / Accepted / Superseded / Deprecated）
- **AGENTS** — AI 行为
- **docs/** — 证据，不作第一阅读源，**不写原则**

### 禁止

- 无 Decision 依据修改 PROJECT「当前真相」
- 重构 v1.0 文档结构
- 矩阵无证据标「已完整迁移」
- 在 docs 写设计原则（原则在 PROJECT）

---

## 25. Rules（引用 DEC-xxx）

本章是**执行层禁止项**。原因与背景见 `DECISION_LOG.md`，百科见 `PROJECT.md`。

### Settings

| 禁止 | 依据 |
|------|------|
| 新增第二套 Settings 系统 | DEC-001 |
| Settings 分区独立 Save | DEC-001 |
| 绕过 Coordinator 直接写 AppSettings | DEC-003 |
| Settings 分域独立事务 / 部分持久化 | DEC-003 |
| 有 Draft 无 LeaveIntent 保护 | DEC-002 |
| Discard 不从 `original` 恢复 | DEC-002 |

### MediaStorage

| 禁止 | 依据 |
|------|------|
| 写死 `D:\` 或固定盘符默认路径 | DEC-005 |
| Migration seed 绝对 RootPath | DEC-004, DEC-005 |
| 独立 MediaStorage Settings API（UI 用） | DEC-004 |
| Save 时静默创建 RootPath | DEC-007 |
| Program Files 内写 MediaStorage | DEC-006 |
| 第二套 MediaStoragePathResolver | DEC-008 |
| Service 内硬编码资源路径 | DEC-008 |
| 新媒体资源写入 Legacy pic | DEC-009 |
| WallCrop 走独立路径逻辑 | DEC-010 |

### 架构

| 禁止 | 依据 |
|------|------|
| React 直连 SQLite / fs / Provider | PROJECT §03, §04 |
| 页面线程执行长批处理 | PROJECT §04, §16 |
| 跳过 Bridge 业务旁路 | PROJECT §04 |
| 无 Migration 改 Schema | PROJECT §09, §18 |
| 恢复顶部 Tab 主导航 | PROJECT §02, DEC 产品决策 |
| 复制 WPF UI | PROJECT §14, §21 |

### 数据安全

| 禁止 | 依据 |
|------|------|
| 无 Preview 危险写 | PROJECT §03.11 |
| 覆盖用户手工数据 / 锁定图 / 用户 NFO | PROJECT §03.4 |
| 覆盖旧 WPF / 正式用户媒体做烟测 | PROJECT §01, §09 |

### 流程

| 禁止 | 依据 |
|------|------|
| 未授权 Push GitHub `main` | §06, ROADMAP |
| 未授权 Commit | §11 |
| 伪造 Build/Test/Smoke 结果 | §04, §05, §08 |
| 越级声明 Release / Freeze | §02, ROADMAP |
| 矩阵无证据标「完整迁移」 | FEATURE_PARITY_MATRIX 规则 |

### AI 行为

| 禁止 | 依据 |
|------|------|
| 「我觉得更好」式架构/UI 重构 | §00 Mission |
| 重大变更不请示 Human | §03 |
| Report 无 Confidence | §05 |
| 自作产品方向决策 | §00, §03 |

---

## 附录：Quick Reference

```text
读：PROJECT → DECISION_LOG → AGENTS →（任务 docs）
做：搜影响范围 → 边界检查 → 最小 diff → Build/Test → Report+Confidence
问：Schema / Settings模型 / MediaStorage / UI改版 / Legacy / 发布
不写：第二套 Settings · 第二套 MediaStorage · 写死路径 · React 直连 DB
```

**Human Acceptance：** AI 实现 + 自动化；Human Smoke + 体验 + 发布。

**Knowledge Lifecycle：** Sprint 结束见 **PROJECT.md §24–§25**（Code → Decision → Project → Agent → Release）。

---

*Local Media Manager AI Constitution · Phase 2 · 2026-07-18*
