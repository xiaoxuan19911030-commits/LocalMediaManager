# Local Media Manager Project Context

> 本文记录长期产品与技术决策，帮助新会话、新维护者和任何 AI 快速进入正确上下文。正式版本范围仍以 `docs/ROADMAP.md` 为准，未完成事项以 `docs/TODO.md` 为准，迁移状态以 `docs/migration/FEATURE_PARITY_MATRIX.md` 为准。

## 1. 产品定位

正式名称为 **Local Media Manager**，简称 **LMM**。

LMM 是现代化、本地优先、可扩展的专业媒体管理平台。旧 Jvedio/WPF 只作为业务规则、数据兼容和回归行为的参考；LMM 不再以“重做旧界面”为目标。

核心原则：

- 用户数据和手工修改优先；
- 本地工作流优先；
- 自动化是增强，不替代用户控制；
- 所有危险写入可预览、确认、审计和恢复；
- 长期可维护性优先于临时快速实现。

## 2. 为什么不恢复顶部 Tab

旧式顶部 Tab 在功能增多后会挤压内容宽度、弱化层级，并让一级模块和临时页面混在一起。LMM 已确定使用持久左侧导航：

- 一级产品模块位置稳定；
- 当前区域和导航层级更清晰；
- 任务、设置等全局入口可固定在底部；
- 内容区可以独立处理搜索、筛选和页面操作；
- 更适合后续媒体库、元数据、诊断和扩展模块。

因此，未经产品确认不得恢复顶部主 Tab，也不得同时保留两套主导航。

## 3. 当前应用骨架

```text
Tauri 2 desktop shell
        ↓
React 19 + Material UI 9
        ↓ typed authenticated API
.NET 8 Bridge
        ↓ services + transactions
SQLite Database v1
        ↑
checksummed Migrations
```

主要边界：

- `src/layouts/AppShell.tsx`：统一桌面壳和左侧导航；
- `src/app/router.tsx`：页面路由；
- `src/themes/`：深浅主题和统一设计令牌；
- `src/components/`：共享页面、卡片、设置和状态组件；
- `src/services/bridge.ts`：React 唯一业务调用边界；
- `backend/LocalMediaManager.Bridge/`：业务、读写、Provider 和任务服务；
- `backend/LocalMediaManager.Migration/`：数据库建立与升级；
- `src-tauri/`：Windows 生命周期、打包资源和 Bridge 子进程。

React 不拥有数据库或文件系统权限。页面可显示和发出意图，真实业务由 Bridge 执行。

## 4. Design System 决策

LMM Next 没有 `DesignSystem.xaml`。统一设计体系基于 Material UI，并由以下位置共同构成：

- `src/themes/theme.ts`
- `src/themes/ThemeContext.tsx`
- `src/layouts/AppShell.tsx`
- `src/components/PageHeader.tsx`
- 共享 Surface、Card、Empty/Loading/Error、Settings 等组件
- `docs/UI_DESIGN_SPEC.md`

设计目标是现代、克制、清晰、专业，支持深色/浅色主题和 Windows 缩放。Clash Verge Rev 仅作为现代桌面信息架构和 Material UI 使用方式的参考，不复制其品牌或产品界面。

新页面必须先寻找可复用组件；发现重复模式时优先提炼共享实现，而不是增加页面私有风格。

## 5. 数据与业务决策

- 所有写入经 Bridge；React 禁止直连 SQLite。
- Schema 变化只通过 checksummed Migration。
- Settings 统一经过 Settings Service。
- 长任务统一进入 Tasks，并提供状态、进度、日志、取消和重试。
- 收藏、评分、自定义标签、演员关系和播放历史是真实用户数据，必须持久化并在重启后保持。
- 元数据和图片自动同步默认补空，不覆盖手工数据、锁定图片或用户 NFO。
- 文件整理必须遵循 `Dry Run → Preview → Confirm → Execute → Audit/Recovery`。
- AI 保持独立 Provider 架构、默认关闭、用户确认后应用；真实 AI 接入不早于 0.6.0。

## 6. 当前版本上下文

- 当前已发布版本：0.4.1，第一阶段旧功能迁移。
- Sprint 0.4.2：扫描导入和 MetaTube 同步执行器已经完成主要实现，仍需安装版与发布验收。
- Sprint 0.4.3：处于 Planning，主题为 Media Assets & File Organization。
- 0.5.0：主要旧版媒体管理功能等价验收。
- 0.5.5：LTS 稳定性、性能、内存、启动速度和大媒体库优化。
- 0.6.0：在稳定底盘上开始受控 AI Provider 能力。

详细状态必须实时查看：

- `docs/ROADMAP.md`
- `docs/TODO.md`
- `docs/CHANGELOG.md`
- `docs/sprints/`
- `docs/migration/FEATURE_PARITY_MATRIX.md`
- `docs/TEST_PLAN.md`

不要仅根据本节的版本快照判断功能是否已经完成。

## 7. Sprint 0.4.3 已确认方向

当前规划包括：

- Poster、Thumb、Fanart、BigPic、ExtraPic、演员图和安全图片缓存；
- NFO 读取、写入、所有权和覆盖策略；
- 文件整理 Dry Run、预览、确认、执行、审计和恢复；
- MetaTube 30–50 部真实样本烟测；
- Bridge Release 后台运行、日志、单实例和退出生命周期；
- 标签编辑、详情海报和播放器路径等限定交互修复。

2026-07-16 已对本地 MetaTube `v1.4.0-c0e053f` 完成只读搜索、详情和主图协议预检。该结果不等于真实写入烟测或完整迁移。

## 8. 开发、部署与发布

- 源码主目录：`D:\LocalMediaManager`。
- 独立安装/升级目录：`D:\Local Media Manager Next`。
- 旧 WPF 稳定版、旧数据库和用户媒体不得被 Next 构建覆盖。
- 中间开发提交只保留在本地 Sprint 分支。
- GitHub `main` 只接收通过 Self Test、Smoke Test、Freeze 和 Release 的稳定版本。
- 每个版本必须留下构建、安装、烟测、Commit、Release Tag 和 Rollback Tag 证据。

## 9. 新会话快速检查顺序

1. 阅读 `AI_RULES.md` 和 `AGENTS.md`。
2. 阅读 Roadmap、TODO、Architecture、UI Spec、Test Plan 和功能矩阵。
3. 检查当前分支、工作区状态和最近提交，禁止回退已有修改。
4. 搜索本次主题的所有页面、共享组件、Bridge、Migration、Settings、Tasks 和测试入口。
5. 确认当前处于 Planning、Develop、Smoke Test 还是 Release，不能越级声明完成。
6. 只在用户授权范围内修改、提交、部署或推送。
