# Local Media Manager — Decision Log

> **决策定案（Decision Log）**
>
> 本文档回答一个问题：**为什么今天会变成这样。**
>
> 这里记录的不是「历史资料」，而是**已经定案的设计决策**。每条决策有 ID、证据 Commit、禁止事项；AGENTS 将来只引用 ID，不再重复解释原因。
>
> **当前范围：** Sprint 0.5.0-15 ～ 0.5.0-17（Phase 3）
> **完成日期：** 2026-07-18

---

## 知识层级

```text
PROJECT.md          → 当前系统是什么（百科，只写最终方案）
DECISION_LOG.md     → 为什么会这样（已定案决策，本文）
AGENTS.md           → AI 必须遵守什么（执行规范，引用 Decision ID）
docs/               → 证据、Release 验收、字段映射、矩阵
```

**阅读顺序：** `PROJECT.md` → `DECISION_LOG.md`（涉及 Settings/MediaStorage/写入时）→ `AGENTS.md`

**与 PROJECT 的分工：**
- PROJECT §10/§11 写**现在怎么做**
- DECISION_LOG 写**为什么定案、曾经考虑过什么、踩过什么坑**
- PROJECT §21 Sprint History 只保留一行摘要 + 指向本文 Decision ID

---

## 决策索引

| ID | 标题 | 模块 | Sprint | 主 Commit |
|----|------|------|--------|-----------|
| [DEC-001](#dec-001-settings-global-unified-save) | Settings 全局统一 Save | Settings | 0.5.0-15 | `6e85395` |
| [DEC-002](#dec-002-settings-leaveintent-leave-protection) | Settings LeaveIntent 离开保护 | Settings | 0.5.0-15 | `16ea584` |
| [DEC-003](#dec-003-settings-coordinator-single-transaction) | Settings Coordinator 单事务边界 | Settings | 0.5.0-15 | `6e85395` |
| [DEC-004](#dec-004-mediastorage-unified-settings-domain) | MediaStorage 纳入 UnifiedSettings | MediaStorage | 0.5.0-16 | `5ffd9e8` |
| [DEC-005](#dec-005-mediastorage-dynamic-rootpath) | MediaStorage 动态 RootPath | MediaStorage | 0.5.0-16 | `69363f6` |
| [DEC-006](#dec-006-mediastorage-documents-fallback) | Documents 回退策略 | MediaStorage | 0.5.0-16 | `69363f6` |
| [DEC-007](#dec-007-mediastorage-root-create-explicit-consent) | RootPath 创建须显式确认 | MediaStorage | 0.5.0-16 | `5ffd9e8` |
| [DEC-008](#dec-008-mediastorage-resolver-write-authority) | MediaStoragePathResolver 唯一写入权威 | MediaStorage | 0.5.0-17 | `2437e39` |
| [DEC-009](#dec-009-legacy-read-unified-write-separation) | Legacy Read / Unified Write 分离 | MediaStorage | 0.5.0-17 | `2437e39` |
| [DEC-010](#dec-010-generatedcard-wallcrops-unified-pipeline) | GeneratedCard/WallCrops 纳入统一管线 | MediaStorage | 0.5.0-17 | `2437e39` |
| [DEC-011](#dec-011-smart-search-and-entity-taxonomy) | Smart Search 与实体标签语义边界 | Search / Entities | 0.5.0-18 | `5b68aa5` |
| [DEC-012](#dec-012-moviewall-display-preferences-and-floating-pagination) | MovieWall 显示偏好与悬浮分页 | MovieWall / Settings | 0.5.0-20 | `bbed780` |
| [DEC-013](#dec-013-metadata-ownership-and-user-data-boundary) | 元数据归属与用户数据边界 | Metadata / User Data | Repository Stabilization | `docs(product)` |
| [DEC-014](#dec-014-image-setas-product-cancelled) | Image SetAs 产品取消 | Images / Feature Parity | Feature Parity | `docs(product)` |
| [DEC-015](#dec-015-duplicate-management-and-batch-organizer-convergence) | 查重与批量整理统一入口 | Organizer / Feature Parity | Feature Parity | `refactor(organizer)` |
| [DEC-016](#dec-016-final-system-features-and-feature-freeze) | 最终系统功能迁移与 Feature Freeze | Settings / System / Feature Parity | Feature Parity | `feat(system)` |
| [DEC-017](#dec-017-legacy-settings-migration-completion) | 旧设置迁移收口与兼容字段隐藏 | Settings / Feature Freeze | Feature Freeze | `fix(settings)` |
| [DEC-021](#dec-021-extensible-media-library-types) | 可扩展媒体库类型与普通库隔离 | Libraries / Metadata | Library Types | `feat(libraries)` |
| [DEC-022](#dec-022-standard-metadata-health-statistics) | Standard 元数据健康统一口径 | Dashboard / Metadata Health | 0.7.4-A | `fix(metadata)` |

---

## Sprint 0.5.0-15 — Settings 全局 Save

**分支：** `sprint/0.5.0-15-settings-global-save`
**时间：** 2026-07-17 ～ 2026-07-18
**目标：** 结束 Settings 分域保存与局部持久化，建立 Draft → Coordinator → Save 唯一路径。

---

### DEC-001: Settings Global Unified Save

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-001 |
| **模块** | Settings |
| **Sprint** | 0.5.0-15 |
| **日期** | 2026-07-17 |

#### 背景 — 为什么提出

Settings 页面原先存在「分层设置」思路：Legacy 字段只读、Next 原生字段（MetaTube、NFO、Playback 等）由各分区**各自 Save**。这导致：

- 用户改 MetaTube 保存了，但 Appearance 未保存，Restart 后主题与 Provider 不一致
- AI 容易为新分区新增独立 `PUT /api/settings/xxx` 和独立 Save 按钮
- 无法回答「当前哪些是已保存、哪些是草稿」
- Bridge 已有分域 PUT 端点（`/api/settings/nfo`、`/api/settings/playback` 等），与 UI 行为不一致

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. 分域 Save** | 每个 Settings 分区独立 Save 到对应 Bridge 端点 | ❌ 拒绝 — 中间态不可预测，Leave 保护无法统一 |
| **B. 全局 Draft + 分域 Save** | UI 有 Draft，但各分区仍可单独提交 | ❌ 拒绝 — Draft 语义被破坏 |
| **C. 全局 Draft + 统一 Save** | 全部编辑在 Draft，一次 `PUT /api/settings/all` | ✅ **定案** |

#### 最终方案

```text
SettingsPage
  original  ← GET /api/settings/all（已保存真相）
  draft     ← 用户编辑（仅 React 内存）
  defaults  ← GET /api/settings/defaults（Restore 写入 Draft，不直接持久化）

用户点击「保存设置」
  → PUT /api/settings/all（UnifiedSettingsDto）
  → SettingsSaveCoordinator.SaveAsync()
  → 成功后 original = draft = result.settings
```

六个域一次提交：`metaTube` · `nfo` · `playback` · `ratingRetention` · `appearance` · `mediaStorage`

#### 为什么选它

- **单一真相源：** `original` 与数据库始终一致，Discard 只需深拷贝 `original`
- **Leave 保护可实施：** `HasUnsavedChanges = stable(original) !== stable(draft)` 语义清晰
- **AI 边界明确：** 只有一个 Save 入口，不会「又发明一套 Settings」
- **与 Coordinator 天然匹配：** 校验 + 事务本就是为全量 UnifiedSettings 设计的

#### 实现

- `SettingsPage.tsx`：`original` / `draft` / `defaults` 三态；`updateDraft()` 只改 Draft；`saveAll()` 调 `bridge.saveAllSettings()`
- `SettingsSaveCoordinator`：`ReadAsync()` / `SaveAsync()` / `ChangedFields()`
- `Program.cs`：`PUT /api/settings/all` 注册为统一写入口
- 分域 PUT 端点保留但 **Settings UI 不再使用**

#### Commit

| Hash | 说明 |
|------|------|
| `6e85395` | feat(settings): add global save workflow |

#### 影响范围

- `src/pages/SettingsPage.tsx`
- `src/services/bridge.ts` — `saveAllSettings()`
- `backend/LocalMediaManager.Bridge/SettingsSaveCoordinator.cs`
- `backend/LocalMediaManager.Bridge/Program.cs`
- `backend/LocalMediaManager.Bridge.Tests/SettingsSaveCoordinatorTests.cs` — `MultipleCategoriesSaveInSingleCoordinatorCall`

#### 以后必须遵守

- Settings UI **必须**使用 Draft / Original / 统一 Save
- 新 Settings 域加入 `UnifiedSettingsDto`，经 Coordinator 一次 Save
- 用户可见的 Save 动作**必须**走 `PUT /api/settings/all`

#### 禁止事项

- 禁止新增 Settings 分区独立 Save 按钮
- 禁止新增绕过 Coordinator 的 Settings PUT 端点供 UI 使用
- 禁止在组件内直接 `bridge.saveXxx()` 并当作已持久化
- 禁止第二套 Settings 状态机（如页面级 localStorage 持久化）

#### 相关文件

- PROJECT §10 Settings 系统
- `SettingsSaveCoordinator.cs`
- `SettingsPage.tsx`

---

### DEC-002: Settings LeaveIntent Leave Protection

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-002 |
| **模块** | Settings |
| **Sprint** | 0.5.0-15 |
| **日期** | 2026-07-17 ～ 2026-07-18 |

#### 背景 — 为什么提出

DEC-001 引入全局 Draft 后，用户在有未保存变更时：

- 切换 Settings 分区或路由离开 → 静默丢失 Draft
- 点击窗口关闭 → Tauri 直接退出，Draft 丢失
- 早期实现：Discard 后窗口无法关闭（LeaveIntent 状态机错误）

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. 自动 Save** | 离开时自动保存 Draft | ❌ 拒绝 — 用户可能不想保存错误修改 |
| **B. 静默 Discard** | 离开时不提示 | ❌ 拒绝 — 数据丢失不可接受 |
| **C. LeaveIntent 状态机 + Dialog** | 路由/窗口两种 Leave 意图，Save/Discard/Cancel | ✅ **定案** |

#### 最终方案

**LeaveIntent 三态：**

| 值 | 触发 | 行为 |
|----|------|------|
| `'none'` | 默认 | 无拦截 |
| `'route'` | `useBlocker(hasUnsavedChanges)` | Dialog：继续编辑 / 放弃 / 保存并离开 |
| `'window'` | Tauri `onCloseRequested` | 同上；确认后 `invoke('close_local_media_manager')` |

**关键 ref：**
- `hasUnsavedChangesRef` — 供 async close handler 读取最新状态
- `allowWindowCloseRef` — Save/Discard 后允许真正关闭
- `closingAppRef` — 防止 `beforeunload` 循环

**Discard：** 从 `original` 恢复 `draft` + 主题预览 → `blocker.proceed()` 或设置 `allowWindowCloseRef` 后关闭。

#### 为什么选它

- 路由离开与窗口关闭是**两种不同通道**，必须区分 LeaveIntent
- Discard 必须恢复 `original`，不能留 half-saved 状态
- 窗口关闭在 Tauri 中需要 `preventDefault` + 二次确认，不能依赖浏览器 alone

#### 实现

- `useBlocker(hasUnsavedChanges)` — React Router 路由拦截
- `getCurrentWindow().onCloseRequested` — Tauri 窗口关闭拦截
- `window.beforeunload` — WebView 兜底
- 三连修：`654e3eb` → `5a73294` → `16ea584`

#### Commit

| Hash | 说明 |
|------|------|
| `654e3eb` | fix(settings): harden global save close prompt |
| `5a73294` | fix(settings): repair window close workflow |
| `16ea584` | fix(settings): force app exit after close confirmation |

#### 影响范围

- `src/pages/SettingsPage.tsx` — Leave Dialog、`finishLeave()`、`closeLeavePrompt()`

#### 以后必须遵守

- 任何有 Draft 的 Settings 类页面**必须**实现 Leave 保护
- Discard **必须**从 `original` 恢复，不能只清空 Draft
- 窗口关闭路径**必须**设置 `allowWindowCloseRef` 避免二次拦截

#### 禁止事项

- 禁止有未保存变更时静默路由跳转
- 禁止 Discard 后不重置 `leaveIntentRef` / `blocker`
- 禁止在 Leave Dialog 未处理时直接 `app.exit()`

#### 相关文件

- `SettingsPage.tsx` — `LeaveIntent`、`finishLeave`、`onCloseRequested`
- PROJECT §10.4 Leave 保护

---

### DEC-003: Settings Coordinator Single Transaction

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-003 |
| **模块** | Settings |
| **Sprint** | 0.5.0-15 |
| **日期** | 2026-07-17 |

#### 背景 — 为什么提出

Unified Save 若各域分别写库，任一域校验失败会导致**部分持久化**——例如 Appearance 已写入 light，MediaStorage 因非法目录回滚失败，Restart 后主题变了但用户以为没保存成功。

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. 分域独立事务** | 每个域单独 COMMIT | ❌ 拒绝 — 部分成功中间态 |
| **B. 单事务全有或全无** | 一个 `BEGIN…COMMIT` 写全部 AppSettings | ✅ **定案** |
| **C. 先 Validate 全部再写** | 内存校验后逐条写无事务 | ❌ 拒绝 — 并发/崩溃仍可能部分写 |

#### 最终方案

```text
SaveAsync(input):
  clean = Normalize(input)           // 全部域校验，任一失败抛异常，不写库
  before = ReadAsync()
  changed = ChangedFields(before, clean)
  if changed.Count == 0 → 返回「设置没有变化」

  BEGIN TRANSACTION
    StoreAsync(metadata.metatube.*)
    StoreAsync(nfo.*)
    StoreAsync(playback.*)
    StoreAsync(ratingHistory.*)
    StoreAsync(appearance.*)
    StoreAsync(mediaStorage.*)
  COMMIT
```

测试证据：`MediaStorageInvalidSaveDoesNotPartiallyPersistOtherCategories` — MediaStorage 非法时 Appearance 也不持久化。

#### 为什么选它

- 用户理解 Save 为**原子操作**：「要么全部成功，要么全部失败」
- `ChangedFields` 只在事务成功后有语义
- 与 DEC-001 全局 Save 一致

#### 实现

- `SettingsSaveCoordinator.SaveAsync` — `SqliteTransaction` 包裹全部 `StoreAsync`
- `Normalize()` 在事务**之前**执行，失败零写入

#### Commit

| Hash | 说明 |
|------|------|
| `6e85395` | feat(settings): add global save workflow |

#### 影响范围

- `SettingsSaveCoordinator.cs` — `SaveAsync`、`StoreAsync`
- `SettingsSaveCoordinatorTests.cs` — 部分持久化拒绝测试

#### 以后必须遵守

- Settings 写入**必须**经 Coordinator 单事务
- 校验**必须**在事务开始前完成（Normalize）
- 无变更时**不得**写库

#### 禁止事项

- 禁止 Settings 域绕过 Coordinator 直接 UPDATE AppSettings
- 禁止部分域成功、部分域失败的 Save API
- 禁止 UI 层自行拼接多次 PUT 模拟一次 Save

#### 相关文件

- `SettingsSaveCoordinator.cs`
- PROJECT §10.6 Coordinator：Transaction

---

## Sprint 0.5.0-16 — MediaStorage 路径策略

**分支：** `sprint/0.5.0-16-media-storage-settings`
**时间：** 2026-07-18
**目标：** 将 MediaStorage 纳入 Unified Settings；废除写死路径；建立 InstallRoot / Documents 动态策略。

---

### DEC-004: MediaStorage Unified Settings Domain

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-004 |
| **模块** | MediaStorage / Settings |
| **Sprint** | 0.5.0-16 |
| **日期** | 2026-07-18 |

#### 背景 — 为什么提出

0.5.0-17 需要统一媒体资源写入，但路径配置没有产品化入口。若 MediaStorage 独立于 Settings，会出现第二套配置 UI 和第二套持久化路径。

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. 独立 MediaStorage 设置页 + 独立 API** | 与 Settings 分离 | ❌ 拒绝 — 第二套 Settings（违反 DEC-001 精神） |
| **B. MediaStorage 作为 UnifiedSettings 第六域** | 随 `/api/settings/all` 一起 Save | ✅ **定案** |
| **C. 仅环境变量配置** | 无 UI | ❌ 拒绝 — 用户无法修改存储位置 |

#### 最终方案

`UnifiedSettingsDto.MediaStorage` 包含：

- `RootPath` — 媒体资源根目录
- 八类资源相对子目录（Posters / Thumbnails / Fanart / … / NFO）
- `MovieFolderTemplate` / `FileNameTemplate` — `{MovieCode}` / `{MovieTitle}`
- `UsingFallbackDefault` — 是否使用 Documents 回退（只读标志）

Migration `0012_MediaStorageSettings.sql` 种子 `mediaStorage.rootPath = ""`（空字符串 = 运行时使用动态默认）。

#### 为什么选它

- 与 DEC-001 统一 Save 一致，Leave 保护自动覆盖 MediaStorage 编辑
- Coordinator 的 Normalize 可统一校验 RootPath、目录、模板
- Settings UI「媒体存储」分区与其他分区同一 Save 栏

#### 实现

- `SettingsSaveCoordinator` — MediaStorage Normalize + Store
- `SettingsPage.tsx` — `MediaStorageSection`
- Migration `0012_MediaStorageSettings.sql`
- `MediaStoragePathResolver.ReadSettingsAsync` — 读 AppSettings，空 RootPath 用运行时默认

#### Commit

| Hash | 说明 |
|------|------|
| `5ffd9e8` | feat(settings): add media storage configuration |

#### 影响范围

- `UnifiedSettingsDto` / `MediaStorageSettingsDto`
- `AppSettings` 表 `mediaStorage.*` 键
- Settings UI 媒体存储分区

#### 以后必须遵守

- MediaStorage 配置**必须**是 UnifiedSettings 的一部分
- 路径模板变更**必须**经 Coordinator Save
- Migration 不得 seed 固定绝对路径

#### 禁止事项

- 禁止独立 MediaStorage Settings API 供 UI 使用
- 禁止 Migration 写入 `D:\...` 或 `C:\...` 固定 RootPath
- 禁止第二套 MediaStorage 配置存储

#### 相关文件

- PROJECT §10、§11
- `0012_MediaStorageSettings.sql`
- `SettingsSaveCoordinatorTests.MediaStorageMigrationDoesNotSeedFixedAbsoluteRoot`

---

### DEC-005: MediaStorage Dynamic RootPath

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-005 |
| **模块** | MediaStorage |
| **Sprint** | 0.5.0-16 |
| **日期** | 2026-07-18 |

#### 背景 — 为什么提出

早期实现与 Migration 曾出现写死 `D:\Local Media Manager Next Data\MediaStorage` 的做法。问题：

- 用户安装到 `E:\Apps\...` 时默认路径错误
- Program Files 安装不可写
- 换机/换盘符后路径失效
- AI 容易继续写死 `D:\` 常量

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. 写死 D:\ 默认路径** | 简单 | ❌ **废弃** |
| **B. 仅 Documents 默认** | 总是我的文档 | ❌ 拒绝 — 独立安装版希望数据在安装目录旁 |
| **C. InstallRoot 动态推导** | `{InstallRoot} Data\MediaStorage` | ✅ **定案**（首选） |
| **D. 用户 Saved Root 最高优先** | DB 有值则用 DB | ✅ **定案**（覆盖默认） |

#### 最终方案

**运行时默认链（`SettingsDefaults.MediaStorageForEnvironment`）：**

```text
1. AppSettings mediaStorage.rootPath 非空 → 用户已保存值（最高优先）
2. InstallRoot 已知且 DataRoot 可写 → {InstallRoot} Data\MediaStorage
3. 从 databasePath 推导 DataRoot → {DataRoot}\MediaStorage
4. 回退 → {Documents}\Local Media Manager\MediaStorage  (UsingFallback=true)
```

InstallRoot 由 `ResolveInstallRoot(AppContext.BaseDirectory)` 解析，**不是**硬编码盘符。

#### 为什么选它

- 独立 Next 安装（`D:\Local Media Manager Next`）数据自然在旁路 `Data` 目录
- 支持任意盘符（测试：`E:\Apps\Local Media Manager Data\MediaStorage`）
- 空 RootPath + Migration 不 seed 绝对路径 = 新库自动适配环境

#### 实现

- `SettingsDefaults.ResolveMediaStorageDefaultRoot`
- `MediaStoragePathResolver.ReadSettingsAsync` — 空 root 用 defaults
- 测试：`MediaStorageDefaultFollowsInstallRoot`、`MediaStorageDefaultCanResolveDifferentDriveInstallRoot`

#### Commit

| Hash | 说明 |
|------|------|
| `69363f6` | fix(settings): improve default media storage path strategy |
| `5ffd9e8` | feat(settings): add media storage configuration（Migration 空 root） |

#### 影响范围

- `SettingsDefaults.cs`
- `SettingsSaveCoordinator.ReadMediaStorageAsync`
- `MediaStoragePathResolver`
- Migration 0012

#### 以后必须遵守

- 默认 RootPath **必须**经 `SettingsDefaults.MediaStorageForEnvironment` 计算
- 代码中**禁止**出现写死 `D:\Local Media Manager Next Data` 作为默认值
- 用户 Saved Root 优先于 InstallRoot 推导

#### 禁止事项

- **禁止写死 `D:\` 或任何固定盘符路径**
- 禁止在 Migration seed 绝对 RootPath
- 禁止在 React 拼接默认 MediaStorage 路径

#### 相关文件

- PROJECT §11.3 RootPath 与 InstallRoot 策略
- `SettingsSaveCoordinatorTests` — 动态默认测试套件

---

### DEC-006: MediaStorage Documents Fallback

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-006 |
| **模块** | MediaStorage |
| **Sprint** | 0.5.0-16 |
| **日期** | 2026-07-18 |

#### 背景 — 为什么提出

当 LMM 安装在 **Program Files**、**Windows** 等受保护目录，或 `{InstallRoot} Data` 不可写时，旁路 DataRoot 策略失败。必须有安全、可写的回退，否则首次运行无法写入任何媒体资源。

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. 安装失败/拒绝启动** | 不可写则报错退出 | ❌ 拒绝 — 用户体验差 |
| **B. 静默写到 Temp** | 临时目录 | ❌ 拒绝 — 数据易丢失 |
| **C. Documents 回退 + UI 通知** | `{Documents}\Local Media Manager\MediaStorage` | ✅ **定案** |

#### 最终方案

触发回退条件（任一）：

- `IsProtectedInstallRoot(installRoot)` — Program Files / Windows
- `CanUseBesideDataRoot(dataRoot) == false` — 旁路 DataRoot 不可写

回退路径：`{Documents}\Local Media Manager\MediaStorage`

标志：`UsingFallbackDefault = true` → Settings 页一次性 Alert 引导用户修改。

Bridge stderr：`Media storage default path fallback: install root is protected...`

#### 为什么选它

- Documents 是 Windows 用户可写、可预期的位置
- `UsingFallbackDefault` 让用户知道「当前不是首选路径」
- 用户 Saved Root 可随时覆盖回退

#### 实现

- `SettingsDefaults.IsProtectedInstallRoot` / `CanUseBesideDataRoot`
- `SettingsPage` — `mediaStorageFallbackNoticeKey` localStorage 一次性通知
- 测试：`MediaStorageDefaultFallsBackFromProgramFiles`、`MediaStorageDefaultFallsBackWhenBesideDataRootIsNotWritable`

#### Commit

| Hash | 说明 |
|------|------|
| `69363f6` | fix(settings): improve default media storage path strategy |

#### 影响范围

- 默认路径策略
- Settings UI 首次提示
- `MediaStorageSettingsDto.UsingFallbackDefault`

#### 以后必须遵守

- 受保护 InstallRoot **必须**回退 Documents，不得写安装目录内
- 回退时**必须**设置 `UsingFallbackDefault` 并通知用户
- 回退路径**必须**可写探测（`CanUseBesideDataRoot` 同类逻辑）

#### 禁止事项

- 禁止在 Program Files 内创建 MediaStorage
- 禁止回退时不告知用户
- 禁止回退到 Temp 或安装目录 resources

#### 相关文件

- PROJECT §11.4 Documents Fallback
- `SettingsDefaults.cs`

---

### DEC-007: MediaStorage Root Create Explicit Consent

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-007 |
| **模块** | MediaStorage / Settings |
| **Sprint** | 0.5.0-16 |
| **日期** | 2026-07-18 |

#### 背景 — 为什么提出

用户输入一个尚不存在的 RootPath 时，若在 Save 时**静默 `Directory.CreateDirectory`**，可能误创建到错误路径（ typo、错误盘符）。Settings Save 应是**配置意图**，不是**文件系统操作**。

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. Save 时自动创建** | 默认 create | ❌ 拒绝 — 误路径风险 |
| **B. Save 拒绝不存在目录** | 必须预先存在 | ⚠️ 部分 — 太严格 |
| **C. 默认拒绝 + 显式 `createMissingMediaStorageRoot=true`** | UI Dialog 确认后创建 | ✅ **定案** |

#### 最终方案

```text
SaveAsync(draft, createMissingMediaStorageRoot: false)  // 默认
  → RootPath 不存在 → ArgumentException「媒体存储根目录不存在，需要确认创建。」

SaveAsync(draft, createMissingMediaStorageRoot: true)   // 用户 Dialog 确认后
  → Directory.CreateDirectory(root)
  → VerifyWritable(root)
  → 事务写入
```

Settings UI：`createMediaRootOpen` Dialog → 用户确认 → `saveAll(true)`

#### 为什么选它

- 分离「保存配置意图」与「创建目录副作用」
- 给用户一次确认错误路径的机会
- 测试：`MediaStorageMissingRootRequiresExplicitCreate`

#### 实现

- `SettingsSaveCoordinator.NormalizeMediaStorageRoot(value, createMissingRoot)`
- `SettingsPage.saveAll(createMissingMediaStorageRoot)`
- `PUT /api/settings/all?createMissingMediaStorageRoot=true`

#### Commit

| Hash | 说明 |
|------|------|
| `5ffd9e8` | feat(settings): add media storage configuration |

#### 影响范围

- Coordinator Save API 参数
- Settings UI 创建目录 Dialog
- `bridge.saveAllSettings(draft, createMissingMediaStorageRoot)`

#### 以后必须遵守

- Save Settings **默认不创建** MediaStorage RootPath
- 创建目录**必须**经用户显式确认（Dialog → query param true）
- 创建后**必须** `VerifyWritable` 探针

#### 禁止事项

- 禁止 Save Settings 时静默 `mkdir` 不存在的 RootPath
- 禁止跳过可写性探针

#### 相关文件

- `SettingsSaveCoordinator.cs` — `NormalizeMediaStorageRoot`
- `SettingsPage.tsx` — `createMediaRootOpen`

---

## Sprint 0.5.0-17 — 统一媒体资源写入管线

**分支：** `sprint/0.5.0-17-media-resource-write`
**时间：** 2026-07-18
**目标：** 所有新媒体资源写入经 `MediaStoragePathResolver`；保留 Legacy 只读；纳入 WallCrops/GeneratedCard。

---

### DEC-008: MediaStoragePathResolver Write Authority

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-008 |
| **模块** | MediaStorage |
| **Sprint** | 0.5.0-17 |
| **日期** | 2026-07-18 |

#### 背景 — 为什么提出

Sprint 16 解决了**配置**，但写入仍分散：`ImageWorkflowService` 写 Legacy pic、`MetadataSyncExecutor` 各自拼路径、`NfoService` 输出目录不统一。AI 极易在新区块再写一套路径逻辑。

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. 各 Service 自行拼路径** | 复制模板逻辑 | ❌ **废弃** |
| **B. 统一 MediaStoragePathResolver** | 所有写入调 Resolver | ✅ **定案** |
| **C. React 算路径传给 Bridge** | 前端拼 FullPath | ❌ 拒绝 — 违反 Bridge 边界 |

#### 最终方案

**唯一写入权威：** `MediaStoragePathResolver`

```text
ResolveForMovieAsync(movieId, resourceType, extension, index?, uniqueSuffix?)
  → MediaStorageResourcePath { FullPath, Directory, FileName, ... }
EnsureDirectoryForWrite(fullPath)
  → Directory.CreateDirectory
```

**已接入 Resolver 的写入服务：**

| 服务 | 场景 |
|------|------|
| `ImageWorkflowService.ReplaceAsync` | 用户替换图片 |
| `MetadataSyncExecutor` / `ImageDownloadService` | 同步下载 |
| `ImageGenerationTaskService` | FFmpeg 生成 Preview/GIF/Screenshot |
| `NfoService` | NFO 导出到 MediaStorage NFO 目录 |

#### 为什么选它

- 路径模板、SafePathSegment、ResourceType 规范化**只维护一处**
- Settings 改模板后所有写入自动生效
- `IsInsideMediaStorageAsync` 提供安全边界

#### 实现

- `2437e39` — 接入 ImageWorkflow、MetadataSync、Nfo、ImageGeneration
- `MediaStoragePathResolverTests` — 各 ResourceType 路径解析
- `ImageAssetWorkflowTests` — 下载写入 MediaStorage

#### Commit

| Hash | 说明 |
|------|------|
| `2437e39` | feat(storage): integrate media resource write pipeline |

#### 影响范围

- `ImageWorkflowService.cs`
- `MetadataSyncWorkflow.cs`
- `NfoWorkflow.cs`
- `Program.cs` — DI 注册 `MediaStoragePathResolver`

#### 以后必须遵守

- **任何**新媒体资源文件写入**必须**经 `MediaStoragePathResolver`
- 写入前**必须** `EnsureDirectoryForWrite`
- ResourceType**必须**经 `NormalizeResourceType`

#### 禁止事项

- **禁止第二套路径解析器或写入逻辑**
- 禁止 Service 内硬编码 `Posters\` 等路径拼接
- 禁止 React 传入绝对写入路径

#### 相关文件

- PROJECT §11.2、§11.5
- `MediaStoragePathResolver.cs`

---

### DEC-009: Legacy Read / Unified Write Separation

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-009 |
| **模块** | MediaStorage / Legacy |
| **Sprint** | 0.5.0-17 |
| **日期** | 2026-07-18 |

#### 背景 — 为什么提出

用户已有大量 Legacy 图片在 `{legacyRoot}\data\{UserName}\pic`。若强制迁移或回写 Legacy 路径，会破坏旧 WPF 共存。若完全放弃 Legacy 读取，现有封面会大量丢失。

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. 写入仍用 Legacy pic** | 保持旧行为 | ❌ **废弃** — 无法统一模板/Settings |
| **B. 读取 Legacy + 写入 MediaStorage** | 双轨 | ✅ **定案** |
| **C. 一次性迁移 Legacy → MediaStorage** | 批量复制 | ❌ 推迟 — 危险，非 0.5.0-17 范围 |

#### 最终方案

```text
读取（Legacy Read）:
  ImageAssetService / /api/covers/{code} / /api/images/{id}/primary
  → 查找 Legacy imageRoot（LMM_IMAGE_ROOT）已有文件
  → Legacy 缓存：{imageRoot}/.lmm-cache/thumbnails

写入（Unified Write）:
  所有新资源 → MediaStoragePathResolver → MediaStorage RootPath
  → Images 表 Path 字段记录 MediaStorage 完整路径

禁止:
  新媒体资源回写 Legacy pic 目录
```

删除逻辑：`IsInsideControlledImageRootAsync` 判断 Legacy 或 MediaStorage 内文件是否可物理删除。

#### 为什么选它

- 旧封面立即可用，无需迁移项目
- 新写入统一、可配置、可审计
- 与「Legacy 只读」产品原则一致

#### 实现

- `ImageAssetService` — 仍用 `imageRoot` 查找
- `ImageWorkflowService.ReplaceAsync` — 写入改走 `pathResolver`
- `ImageWorkflowService` — `IsInsideControlledImageRootAsync` 删除边界

#### Commit

| Hash | 说明 |
|------|------|
| `2437e39` | feat(storage): integrate media resource write pipeline |

#### 影响范围

- 封面展示链路（读）
- 图片替换/同步/生成/NFO 导出（写）
- 图片删除 Preview 文案（是否在受控目录）

#### 以后必须遵守

- Legacy pic **只读** — 允许查找、展示、删除已登记文件
- 新媒体资源**只写** MediaStorage
- Images 表新记录 Path **指向** MediaStorage

#### 禁止事项

- **禁止**新媒体资源写入 Legacy pic 目录
- 禁止假设所有图片都在 MediaStorage（读路径须兼容 Legacy）
- 禁止无边界检查删除 MediaStorage 外文件

#### 相关文件

- PROJECT §11.6 Legacy Read
- `ImageWorkflowService.cs` — `imageRoot` vs `pathResolver`
- `ImageAssetService.cs`

---

### DEC-010: GeneratedCard/WallCrops Unified Pipeline

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-010 |
| **模块** | MediaStorage / Images |
| **Sprint** | 0.5.0-17 |
| **日期** | 2026-07-18 |

#### 背景 — 为什么提出

Legacy 有 WallCrop / CardCover 概念，Sprint 0.4.x 图片工作流已支持 `GeneratedCard`，但写入路径未纳入 MediaStorage 统一管线，导致 WallCrop 资源与其他类型路径策略不一致。

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. WallCrop 继续写 Legacy/临时目录** | 特殊 case | ❌ 拒绝 — 破坏统一性 |
| **B. GeneratedCard → WallCrops 子目录** | 纳入 Resolver | ✅ **定案** |

#### 最终方案

```text
NormalizeResourceType:
  "generatedcard" | "cardcover" | "wallcrop" | "wallcrops"
    → "GeneratedCard"

ResourceDirectory:
  "GeneratedCard" → settings.WallCropsDirectory（默认 "WallCrops"）

路径示例:
  {RootPath}/WallCrops/{MovieCode}/{MovieCode}_user-{timestamp}.jpg
```

`ImageWorkflowService.SupportedTypes` 包含 `GeneratedCard`。

#### 为什么选它

- WallCrop 与 Poster/Fanart 同等对待，共享模板与 Settings
- Legacy 别名 `cardcover`/`wallcrop` 在 Resolver 层消化，UI 不需感知
- 测试：`GeneratedCardReplacementWritesToWallCropsDirectory`

#### 实现

- `MediaStoragePathResolver.NormalizeResourceType` / `ResourceDirectory`
- `ImageWorkflowService` — Replace/Generate 支持 GeneratedCard
- `MediaStoragePathResolverTests` — GeneratedCard / CardCover 别名

#### Commit

| Hash | 说明 |
|------|------|
| `2437e39` | feat(storage): integrate media resource write pipeline |

#### 影响范围

- 影片墙卡片封面生成/替换
- MediaStorage Settings 中 WallCrops 目录名
- Images 表 `ImageType = 'GeneratedCard'`

#### 以后必须遵守

- 所有资源类型（含 GeneratedCard）**必须**在 Resolver 的 `NormalizeResourceType` 有映射
- 新增资源类型**必须**同时更新：Settings 目录字段、Resolver、SupportedTypes、测试

#### 禁止事项

- 禁止 WallCrop/CardCover 走独立路径逻辑
- 禁止新增 ResourceType 不经 Resolver 直接写文件

#### 相关文件

- `MediaStoragePathResolver.cs` — `NormalizeResourceType`
- `ImageAssetWorkflowTests.cs` — `GeneratedCardReplacementWritesToWallCropsDirectory`
- PROJECT §11.1 资源类型表

---

## Sprint 0.5.0-18 — Smart Search 与实体分类

**目标：** 在不重做影片墙、不新增中心页的前提下，完成跨字段 Smart Search，并补齐导航中的导演、系列、影片标签入口。

### DEC-011: Smart Search And Entity Taxonomy

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-011 |
| **模块** | Search / Entities |
| **Sprint** | 0.5.0-18 |
| **日期** | 2026-07-18 |

#### 背景 — 为什么提出

影片墙需要支持关键词与结构化条件组合搜索，同时导航的“标签”分组缺少导演、系列、影片标签入口。数据库中 `Tags/MovieTags` 同时承载用户标签与导入标签，如果不区分 Source，`标签:`、导航统计和 FilterBar 会把不同概念混在一起。

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. 全部 Tags 合并展示** | `Tags` 不分来源 | ❌ 拒绝 — 影片标签与自定义标签语义混乱 |
| **B. 新建导演/系列/标签中心** | 每类实体独立详情页 | ❌ 拒绝 — 超出本 Sprint，重复影片墙 |
| **C. Source 区分 + 复用实体列表和影片墙** | 列表只显示实体，点击后带 ID 进入影片墙 | ✅ **定案** |

#### 最终方案

```text
ordinary keywords:
  terms AND; each term OR across movie fields, files, actors, directors,
  movie tags, custom tags, genres, studios, series, libraries

structured fields:
  标签:          → Tags/MovieTags, excluding status badges
  自定义标签:    → Tags/MovieTags, excluding status badges
  导演:          → Directors/MovieDirectors when tables exist
  系列:          → Series/MovieSeries

navigation:
  /tags              → category page
  /tags/directors    → directors
  /tags/movie-tags   → movie tags
  /tags/series       → series
  /tags/custom       → custom tags
  /actors            → actors entity ability

entity click:
  /media?directorId=...
  /media?seriesId=...
  /media?movieTagId=...
  /media?customTagId=...
```

分类条件、FilterBar、Smart Search、排序和分页全部进入 `ProductReader.AdvancedSearchAsync`，以 AND 关系组合。实体列表数量使用 `COUNT(DISTINCT MovieId)`，避免多文件或多关系 JOIN 造成重复计数。

#### 为什么选它

- 保留 Bridge → ProductReader → SQLite 的既有边界
- 不新增第二套影片卡片列表，分类浏览复用影片墙
- `Tags.Source` 已由 Migration/NFO/ProductWriter 写入，可区分 LegacyStamp/User 与 NFO/Scraper/LegacyLabel
- `新加入`、`已收藏` 是状态 Badge，不进入标签分类统计
- 旧库缺少 `Directors/MovieDirectors` 时返回空列表，避免安装版崩溃

#### 实现

- `SearchQueryParser` — 结构化条件解析
- `ProductReader.AdvancedSearchAsync` — 查询组合、Source 区分、去重分页
- `GET /api/entities/{type}` — actors/tags/movie-tags/directors/series
- `MediaPage` — 分类参数 + FilterBar + Smart Search AND，详情返回恢复状态和滚动
- `EntityPage` — 复用统一实体列表样式，点击进入影片墙

#### 以后必须遵守

- `标签:` 与 `自定义标签:` 当前都使用历史 `Tags/MovieTags` 查询语义，不按 `Source` 强行拆分
- 两者都必须排除 `新加入`、`已收藏` 等状态 Badge
- `新加入`、`已收藏` 只作为影片状态 Badge；收藏浏览走左侧 `收藏`
- Genre 是 `Genres/MovieGenres`，不是影片标签
- 新实体入口应先复用实体列表 + 影片墙筛选，除非 Human 明确批准中心页

#### 禁止事项

- 禁止合并影片标签与自定义标签统计
- 禁止把状态 Badge 写入标签导航或标签统计
- 禁止为导演/系列/影片标签新建第二套影片列表
- 禁止把导演、系列、影片标签放入媒体库分组
- 禁止未参数化拼接用户输入；实体类型只能来自白名单

#### 相关文件

- `SearchQueryParser.cs`
- `ProductReader.cs`
- `Program.cs`
- `MediaPage.tsx`
- `EntityPage.tsx`
- `AppShell.tsx`
- `ProductReaderSmartSearchTests.cs`

#### 2026-07-18 修正

安装版真实库验证后，DEC-011 中“用 `Tags.Source` 区分影片标签与自定义标签”的假设被撤回。实际数据中：

- `LegacyLabel` 同时包含用户维护标签（如“五星”“高颜值”）和状态标识（如“已收藏”）。
- `LegacyStamp` 包含“新加入”等状态标识。
- 因此 `Tags.Source` 不能可靠区分标签来源，继续按 Source 拆分会导致历史标签不可见。

当前定案修正为：

```text
标签 / 自定义标签:
  均恢复历史 Tags + MovieTags 查询语义
  不按 Source 强行拆分
  仅排除状态 Badge：新加入、已收藏
```

`新加入` 与 `已收藏` 仍只作为影片状态 Badge；收藏浏览继续走左侧“收藏”入口。

---

## Sprint 0.5.0-20 — MovieWall Display Optimization

**分支：** `sprint/0.5.0-20-moviewall-display`
**时间：** 2026-07-18
**目标：** 在不修改 Smart Search、FilterBar、标签导航、详情页和图片生成逻辑的前提下，优化影片墙卡片显示、海报方向/大小、悬浮分页和页码交互。

---

### DEC-012: MovieWall Display Preferences And Floating Pagination

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-012 |
| **模块** | MovieWall / Settings |
| **Sprint** | 0.5.0-20 |
| **日期** | 2026-07-18 |

#### 背景

MovieWall Consistency 后，所有影片列表已经复用同一 `MovieWall`，但默认卡片过小、大屏单行数量过多，分页仍位于内容流底部，导致下方留白和分页位置体验不稳定。用户还需要统一控制海报方向和大小，且这些偏好必须跨全部 MovieWall 页面共享。

#### 讨论方案

| 方案 | 描述 | 结论 |
|------|------|------|
| **A. 每个页面单独保存显示偏好** | 收藏、搜索、标签等页面各自保存卡片大小 | ❌ 拒绝 — 破坏 MovieWall 统一与状态恢复 |
| **B. localStorage 页面偏好** | React 本地持久化，不进 Settings | ❌ 拒绝 — 形成第二套 Settings |
| **C. `UnifiedSettings.movieWallDisplay` + MovieWall 统一渲染** | 显示偏好作为统一 Settings 域；MovieWall 读取后传给卡片网格 | ✅ 定案 |

#### 最终方案

`UnifiedSettingsDto` 新增 `MovieWallDisplaySettingsDto`：

```text
movieWall.posterOrientation = portrait | landscape
movieWall.posterSize        = small | medium | large
```

默认值：`portrait / medium`。

显示规则：

- 竖版海报比例：`2:3`
- 横版海报比例：`16:9`
- 小/中/大映射为不同 CSS Grid `minmax()` 最小宽度
- 卡片模式使用响应式 Grid 自动计算列数，不写死列数
- 列表模式不受海报方向与大小影响

分页规则：

- 分页从 `MovieResultContainer` 移到 `MovieWall`
- 右下角固定半透明悬浮卡片
- 显示上一页、当前页/总页数、下一页
- 点击当前页进入输入状态，`Enter` 跳转，`Esc` 取消
- 左/右方向键翻页，`Ctrl+G` 聚焦页码输入
- 输入框、表单、下拉框或弹窗获得焦点时不触发 MovieWall 快捷键

#### 为什么选它

- 保持 MovieWall 是唯一影片墙交互入口，不新增第二套列表或分页
- 遵守 DEC-001：用户偏好进入统一 Settings，随 `PUT /api/settings/all` 单事务保存
- 显示偏好不进入搜索依赖，不触发重新查询、不重置搜索/筛选/排序/页码
- 分页由 MovieWall 统一管理后，所有复用页面自动一致

#### 实现

- `SettingsSaveCoordinator`：读写 `movieWall.*` 键，非法值归一化到默认语义
- `SettingsPage`：外观分区新增可视化卡片选择器
- `MovieWall`：读取显示偏好、计算总页数、悬浮分页、页码输入、键盘快捷键
- `MediaCard` / `MediaCardGrid`：支持方向、比例与尺寸映射

#### 以后必须遵守

- 影片墙显示偏好必须通过 `UnifiedSettings.movieWallDisplay` 保存
- 不得为收藏页、搜索页、标签页等单独保存海报大小/方向
- 不得在 Dashboard、详情页或非 MovieWall 页面强行套用 MovieWall 分页快捷键
- 不得用固定列数代替响应式 Grid

#### 禁止事项

- 禁止修改 Smart Search / FilterBar / 标签导航来实现显示优化
- 禁止新增第二套 MovieWall、第二套分页或第二套影片卡片列表
- 禁止在本决策范围内新建复杂图片生成或智能卡图系统

---

## Repository Stabilization — Metadata Ownership Decision

**分支：** `sprint/0.5.0-20-moviewall-display`
**时间：** 2026-07-19
**目标：** 收缩 Local Media Manager 的元数据写入边界，明确影片元数据由刮削、NFO 导入和元数据同步维护；LMM 只维护用户个人数据与用户选择的媒体资源。

---

### DEC-013: Metadata Ownership And User Data Boundary

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-013 |
| **模块** | Metadata / User Data |
| **Sprint** | Repository Stabilization |
| **日期** | 2026-07-19 |

#### 背景

旧版 Jvedio 暴露了大量影片字段编辑能力。继续迁移“完整影片编辑器”会让 LMM 同时成为刮削器、NFO 编辑器和个人媒体管理器，带来三类问题：

- 同一字段有多个写入来源，标题、演员、导演、厂商、系列、Genre 等容易冲突。
- 重新刮削或 NFO 导入可能覆盖手工修改，用户难以判断哪个来源是权威。
- 为显示标题、自定义标题、第二标题等派生字段增加长期维护成本，却不能解决元数据源错误。

#### 最终方案

影片元数据的权威来源仅包括：

- 刮削
- NFO 导入
- 元数据同步

LMM 不提供完整影片元数据手动编辑器，也不新增“显示标题”“自定义标题”“第二标题”等额外标题字段。MovieWall 和详情页直接展示刮削/NFO/同步得到的标题或原标题。元数据错误时，用户通过重新刮削、重新同步或重新导入 NFO 修正来源数据，而不是手动改数据库字段。

#### 不再开发

- 标题、原始标题、番号、简介、上映日期、年份、时长
- 显示标题、自定义标题、第二标题
- 手动修改导演、厂商、系列、Genre / 影片标签
- 添加演员、删除演员、搜索演员、手动修改演员资料
- 独立“已观看”开关
- 完整 Movie Editor / 全字段影片编辑器

#### 继续保留

这些是用户个人数据或用户明确选择的资源，不属于刮削元数据：

- 星级评分
- 收藏
- 自定义标签
- 海报调整、人工裁切
- 演员显示排序
- 播放次数、最后播放时间、最近播放
- 未来明确提出后才开发的用户备注
- AI 推荐相关用户行为数据

重新刮削、NFO 导入或元数据同步不得覆盖评分、收藏、自定义标签、播放历史、用户选择的图片资源或演员显示排序。

#### 演员边界

演员实体和影片演员集合由刮削/NFO/同步维护。LMM 只允许调整演员显示顺序：仅修改排序字段或关联顺序，不新增演员、不删除演员关联、不修改演员实体。重新刮削后，对仍存在的演员尽量保留用户排序；新增演员按默认顺序追加。

#### 已观看边界

不新增 `Watched` 布尔开关。播放状态由播放次数、最后播放时间和最近播放表达，避免与历史记录重复。

#### 以后必须遵守

- 不得新建完整 Movie Editor 或全字段影片编辑 API。
- 不得为显示标题、自定义标题或第二标题新增数据库字段。
- 不得把自定义标签、评分、收藏、演员排序和图片选择标记为取消。
- AI 后续只能建议或写入用户确认的自定义标签/用户行为数据，不能污染刮削标签或 Provider 元数据。

#### 后续开发顺序

所有后续 Sprint 固定遵循：

1. 先完成所有决定保留的旧版功能迁移，达到以保留功能为准的 Feature Parity。
2. 新实现稳定后，再统一执行 Legacy Cleanup，删除已经完全替代且无引用的旧代码、旧页面、旧资源和无引用实现；必要的数据迁移与升级兼容逻辑继续保留。
3. Legacy Cleanup 完成后，再进入产品优化、UI 重构和新功能开发；该阶段以 Human 的体验需求为最高优先级，不再以旧版一致性为主要目标。

禁止边迁移边大规模重构，禁止边迁移边删除旧实现。每完成一个保留功能，必须更新 Feature Parity 状态。

---

### DEC-014: Image SetAs Product Cancelled

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-014 |
| **模块** | Images / Feature Parity |
| **Sprint** | Feature Parity |
| **日期** | 2026-07-19 |

#### 背景

旧版 Jvedio 提供从图片列表手动“设为海报 / 缩略图 / 横幅”的操作。Local Media Manager 当前图片资源链路已经由 MetaTube 刮削、NFO 导入和元数据同步统一维护，并保留图片查看、放大、人工裁切、刷新和重新下载图片能力。继续迁移 SetAs 会增加一条手工图片主图维护路径，和当前元数据与图片资源归属边界重复。

#### 最终方案

Image SetAs 功能不再迁移，标记为 Product Cancelled。不开发以下入口：

- 设为海报
- 设为缩略图
- 设为横幅

保留并继续维护：

- 图片查看
- 图片放大
- 人工裁切
- 图片刷新
- 刮削 / NFO / MetaTube 重新下载图片

#### 以后必须遵守

- 不新增 SetAs 海报、SetAs 缩略图或 SetAs 横幅菜单。
- 不为 SetAs 修改数据库 Schema、图片字段或主图选择模型。
- 图片资源错误时通过刷新、重新刮削、NFO 导入或 MetaTube 同步修复。
- 人工裁切仍属于用户可维护的图片调整能力，不因取消 SetAs 而移除。

#### 相关文档

- `docs/ROADMAP.md`
- `docs/CHANGELOG.md`
- `docs/migration/FEATURE_PARITY_MATRIX.md`

---

### DEC-015: Duplicate Management and Batch Organizer Convergence

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-015 |
| **模块** | Organizer / Feature Parity |
| **Sprint** | Duplicate Management & Batch Organizer |
| **日期** | 2026-07-19 |

#### 背景

旧版功能把查重、删除、移动、重命名、刮削和同步分散在不同菜单、页面和命令中。Local Media Manager 已经有只读查重结果、Safe Delete、File Organizer、MovieWall 批量状态和 Tasks，但入口仍然分散，容易让“查重”和“批量整理”被误解为两个独立产品功能。

#### 最终方案

查重与批量整理合并为同一个 Feature Parity Sprint：Duplicate Management & Batch Organizer。左侧工具区只提供统一入口：

- 整理工具

整理工具内部按流程承载：

- 重复影片
- 批量整理
- 后续批量删除 / 移动 / 重命名 / 刮削 / 同步 / 标签 / 收藏 / 评分

#### 架构边界

- 整理工具不是新数据库，不新增媒体类型，不修改 Schema。
- 重复影片继续使用当前 `GET /api/duplicates` 只读结果。
- 批量整理继续使用 `POST /api/organizer/dry-run` → preview → execute → Tasks。
- 影片范围必须复用统一 MovieWall、Smart Search、FilterBar、分页、排序、媒体库范围和状态恢复。
- 后续批量删除、移动、标签、收藏、评分等操作必须基于当前 MovieWall 查询范围或用户明确选择集，不复制第二套查询逻辑。

#### 第一阶段

第一阶段仅完成架构迁移：

- `/organizer` 成为统一整理工具入口。
- 旧 `/duplicates` 只作为兼容重定向，不再作为主导航入口。
- 重复影片和批量整理在同一页面内切换。
- 批量整理阶段复用 MovieWall 选择影片，并走现有 File Organizer Dry Run / Preview / Execute 链路。

#### 执行完成规则

Duplicate Management & Batch Organizer 的执行阶段遵循同一条安全链路：

- 重复影片删除必须先选择重复组，再为每组明确选择一个保留项；系统不得自动选择删除候选。
- 删除候选进入 `DuplicateOrganizerWorkflowService` 预览，最终实际删除委托现有 `SafeDeleteWorkflowService`，不新增第二套文件删除逻辑。
- Safe Delete 的媒体删除模式先将原始媒体、图片和 NFO 送入系统回收站，原始媒体删除成功后才删除数据库记录；metadata 模式只删除数据库信息。
- 执行前必须重新预览并校验确认令牌，防止文件或数据库状态变化后继续使用过期预览。
- 重复删除前只合并用户个人数据：收藏取 OR、自定义标签取并集、播放次数合并、最后播放时间取最新；评分和备注冲突必须阻止执行或等待后续显式解决，不能静默覆盖。
- 批量移动和批量重命名必须复用 `FileOrganizerService` 的 dry-run / preview / execute / task 链路，并以用户当前选择集为范围，不对整个筛选结果隐式执行。
- 普通 MovieWall 批量选择不开放任意批量删除；批量删除只允许经重复影片 Safe Delete 流程进入。

#### 禁止

- 不开发 AI 整理、AI 分类、AI 评分。
- 不新增数据库或 Schema。
- 不复制第二套影片查询、筛选或列表。
- 不把 Legacy Cleanup 混入本 Sprint。

---

### DEC-016: Final System Features and Feature Freeze

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-016 |
| **模块** | Settings / System / Feature Parity |
| **Sprint** | Final System Features Migration |
| **日期** | 2026-07-19 |
| **状态** | Accepted |

#### 背景

保留功能迁移阶段剩余的旧版系统能力集中在日志清理、语言设置、托盘与关闭行为、快捷键管理和检查更新。它们属于 Settings / Tauri 壳层能力，不应扩展为新的系统管理中心，也不应引入 Legacy Cleanup 或新功能。

#### 最终方案

- 日志清理进入设置页：支持 7/14/30/90 天和永久保留；执行前必须预览；只清理应用日志目录的历史日志，不删除活动日志、数据库、配置、任务记录或用户媒体。
- 语言设置进入统一 Settings：当前仅开放系统默认和简体中文；英文资源不完整前不作为可选语言暴露。
- 托盘与关闭行为进入统一 Settings 和 Tauri 壳层：支持关闭时退出或最小化到托盘、启动后最小化到托盘、托盘显示/隐藏/退出；退出前检查运行中任务。
- 快捷键管理进入设置页：提供当前可用快捷键说明、总开关和恢复默认；不开发复杂按键录制器。
- 检查更新进入关于页：只检查 GitHub Releases、显示结果并打开发布页；不自动下载、不自动安装。

#### Feature Freeze

以 2026-07-19 的产品取舍为准，保留旧版功能迁移完成后进入 Feature Freeze。后续顺序固定为：

1. Legacy Cleanup
2. Testable Release
3. Human Testing
4. Product Optimization

Feature Freeze 期间不得新增 UI 优化、新产品功能或旧版范围外能力。

#### 禁止事项

- 不把日志清理扩展为删除用户数据、数据库、配置或媒体资源。
- 不新增 Tauri 自动更新安装器。
- 不新增快捷键录制器或复杂冲突编辑器。
- 不把语言设置误标为完整多语言翻译。
- 不在 Feature Freeze 后直接进入产品优化；必须先完成 Legacy Cleanup。

---

### DEC-017: Legacy Settings Migration Completion

| 字段 | 值 |
|------|-----|
| **Decision ID** | DEC-017 |
| **模块** | Settings / Feature Freeze |
| **Sprint** | Settings Migration Completion |
| **日期** | 2026-07-19 |
| **状态** | Accepted |

#### 背景

旧配置读取能力已经存在，但设置页仍把 `WindowConfig.*`、`ScanConfig.*` 等内部 Key 作为“已读取的兼容设置”展示给用户。这会把兼容层误认为正式设置，并让危险旧开关绕开当前 Safe Delete、MovieWall 状态恢复和统一 Settings 规则。

#### 最终方案

- 普通设置页不再展示“已读取的兼容设置”分组，不显示内部 Key、命名空间、配置路径或原始 JSON 字段。
- 旧配置读取逻辑继续保留在 Bridge 后端，只作为一次性迁移输入、插件/Provider 兼容证据和诊断资料。
- `ScanConfig.MinFileSize` 迁移为正式 `UnifiedSettings.scan.minFileSizeMb`，设置页显示为“最小影片文件大小（MB）”，扫描服务真实使用该阈值过滤候选视频。
- 一次性迁移遵循优先级：新版用户设置 > 已迁移值 > 旧版兼容值 > 安全默认值。迁移只在 marker 不存在时执行；非默认的新值不会被旧值覆盖。
- 旧配置损坏或缺失时使用安全默认值，记录错误但不阻止启动。

#### Product Cancelled / Replaced

- `WindowConfig.Main.DetailWindowShowAllMovie`：取消旧开关。详情页上一部/下一部按当前 MovieWall 查询上下文、筛选、排序和媒体库范围浏览，不提供“全库浏览”破坏上下文的选项。
- `WindowConfig.Settings.DelInfoAfterDelFile`：取消旧危险开关。删除行为统一服从当前 Safe Delete / 删除预览 / 确认 / 数据保护流程，不允许通过旧设置绕过安全链。
- `ScanConfig.FetchVID`：取消旧开关。Next 扫描固定识别文件名番号，不提供关闭项。

#### 以后必须遵守

- 新设置必须进入 `UnifiedSettingsDto` 和 `SettingsSaveCoordinator`，经 `PUT /api/settings/all` 单事务保存。
- 兼容字段不得作为普通设置项展示；确需展示未迁移项目时必须使用用户可理解名称，不显示内部 Key。
- 不得把旧危险开关直接迁移成新版危险开关；必须以当前产品安全工作流为准。

---

## PROJECT §21 Sprint History（摘要）

| Sprint | 摘要 | 决策 |
|--------|------|------|
| 0.5.0-15 | Settings 全局 Draft + Coordinator 统一 Save + LeaveIntent | DEC-001, DEC-002, DEC-003 |
| 0.5.0-16 | MediaStorage 纳入 Settings；动态 RootPath；Documents 回退 | DEC-004 ～ DEC-007 |
| 0.5.0-17 | 统一写入 Resolver；Legacy Read 保留；WallCrops 纳入 | DEC-008 ～ DEC-010 |
| 0.5.0-18 | Smart Search；实体标签语义区分；导航分类补齐 | DEC-011 |
| 0.5.0-20 | MovieWall 显示偏好、响应式卡片尺寸和悬浮分页交互 | DEC-012 |
| Repository Stabilization | 元数据归属规则：取消完整影片编辑器，只维护用户个人数据 | DEC-013 |
| Feature Parity | Image SetAs 产品取消：图片资源由刮削、NFO 和 MetaTube 统一管理 | DEC-014 |
| Feature Parity | 查重与批量整理合并为整理工具统一入口，并完成重复 Safe Delete、批量移动、批量重命名执行流 | DEC-015 |
| Feature Parity | 日志清理、语言、托盘/关闭、快捷键管理和检查更新完成保留范围迁移，进入 Feature Freeze | DEC-016 |
| Feature Freeze | 旧设置迁移收口：兼容字段隐藏，最小扫描文件大小成为正式设置，危险旧开关取消 | DEC-017 |

---

## 待补充（Phase 5）

### Sprint 考古

Sprint 0.5.0-02 ～ 0.5.0-14 决策将从 git 分支考古后追加，使用相同模板与 ID 序列（DEC-011+）。

### ADR 拆分（Architecture Decision Record）

当决策条目增多后，`DECISION_LOG.md` **演进为纯索引**：

```text
DECISION_LOG.md（索引）
  DEC-001  Settings 全局保存     Accepted  Sprint 0.5.0-15
      ↓
  docs/decisions/DEC-001-settings-unified-save.md（ADR 正文）
```

| 阶段 | DECISION_LOG | ADR 文件 |
|------|--------------|----------|
| **当前（Phase 3）** | 索引 + 全文锚点 | — |
| **Phase 5 起** | 仅索引表（ID、标题、状态、Sprint、链接） | 每条决策独立 Markdown |

**状态枚举：** `Accepted` · `Superseded` · `Deprecated`

**好处：** 文件不膨胀 · 单决策 Git Diff · AI 按需读一条 · 修改不牵动全书

详见 **PROJECT.md §24.4** · **§25 Definition of Done ⑥**

---

### DEC-018: Release Polish V1 Settings And Navigation

| Field | Value |
|------|-----|
| **Decision ID** | DEC-018 |
| **Module** | Settings / Navigation / Release |
| **Sprint** | Release Polish V1 |
| **Date** | 2026-07-20 |
| **Status** | Accepted |

#### Decision

Settings must expose only user-facing configuration. The final 0.6.0 Settings sections are 常规, 外观, 搜索与筛选, 元数据, 插件中心, 媒体资源, 快捷键, 数据与备份, 关于.

Plugin Center moves into Settings. MetaTube provider configuration and FFmpeg tool detection belong there. NFO no longer has a standalone Settings page or user toggle because NFO output is fixed metadata behavior.

Duplicate Movies remains a dedicated navigation entry. Organizer and Batch Organizer are removed as standalone navigation concepts; future batch management belongs in MovieWall batch actions.

#### Prohibited

- Do not reintroduce standalone Settings pages for 扫描和导入 or NFO.
- Do not expose internal paths, Commit, Bridge implementation details, legacy keys, or developer field names to ordinary users.
- Do not add separate Organizer or Batch Organizer navigation entries.

---

### DEC-019: Trunk-Based Development And Release Tags

| Field | Value |
|------|-----|
| **Decision ID** | DEC-019 |
| **Module** | Git / Release |
| **Sprint** | 0.6.0 |
| **Date** | 2026-07-20 |
| **Status** | Accepted |

#### Decision

Starting with 0.6.0, Local Media Manager uses trunk-based development. `main` is the only long-lived development branch. Temporary branches are allowed only for high-risk work and must be merged, verified, and deleted promptly.

Official version recovery uses Git release tags. Every stable release must update the single product version, update the changelog, commit, create an immutable tag such as `v0.6.0`, and push commits plus tags.

#### Prohibited

- Do not keep long-lived `feature/*`, `ui/*`, `migration/*`, `temp/*`, `debug/*`, `test/*`, `experiment/*`, `sprint/*`, or rollback branches after their work is merged and verified.
- Do not delete release tags.
- Do not use rollback branches as the long-term release restore mechanism.

---

### DEC-020: Structured Actor Profile Enrichment And Field Provenance

| Field | Value |
|------|-----|
| **Decision ID** | DEC-020 |
| **Module** | Database / Actor Metadata |
| **Sprint** | 0.6.2 |
| **Date** | 2026-07-20 |
| **Status** | Accepted |

#### Decision

Database v1 adds nullable structured actor fields `HeightCm`, `Cup`, `BirthPlace`, and `ActivityPeriod` through checksummed migration 0014. Field-level source, update time, and user-edit state are stored in one extensible JSON object, `ProfileFieldSourcesJson`, rather than per-field source columns.

Actor profile providers must use the existing Bridge, service, and actor repository boundaries. Merge behavior is fill-empty-only: user-edited and existing non-empty values have priority, provider conflicts are logged and never silently overwrite data, aliases are normalized and deduplicated without deleting existing aliases, and birth dates are stored as dates rather than dynamic ages.

Minnano is preferred for `BirthDate`, `HeightCm`, and `Cup`. Wikipedia JP is preferred for `BirthPlace`, `ActivityPeriod`, `Description`, and `BirthDate`. Provenance remains internal to merge decisions, Data Center diagnostics, debug logs, and troubleshooting; actor detail pages do not expose it.

#### Prohibited

- Do not create a second actor persistence path or let React write SQLite.
- Do not overwrite non-empty or user-edited actor fields automatically.
- Do not store derived age or embed structured height/cup values in description.
- Do not display field provenance on actor detail pages in this scope.

---

### DEC-021: Extensible Media Library Types

| Field | Value |
|------|-----|
| **Decision ID** | DEC-021 |
| **Module** | Libraries / Metadata / MediaStorage |
| **Sprint** | Library Types |
| **Date** | 2026-07-23 |
| **Status** | Accepted |

#### Decision

Libraries use the extensible `LibraryType` enum. Existing and number-based libraries are `Standard`; local videos without a standard number are `Local`. Standard libraries preserve the existing provider, merge, image, NFO, health, and repair workflows. Local scans use the source filename as the initial title and must not enqueue or execute metadata Provider tasks.

Local generated resources use the stable MovieId rather than a filename or movie number. Screenshot and cover lifecycle state is stored as the fixed `ScreenshotStatus` and `CoverSource` enums. This sprint reserves the screenshot service boundary and storage paths; FFmpeg frame extraction and quality scoring remain later work.

#### Prohibited

- Do not model library behavior as a scraping boolean.
- Do not send Local library movies to MDC-NG, MetaTube, or JavBus.
- Do not classify Local movies as missing a number or scrape-failed in Standard metadata health.
- Do not key Local covers or screenshots by mutable filenames.
- Do not change the Standard metadata workflow or the movie detail UI for this feature.

---

### DEC-022: Standard Metadata Health Statistics

| Field | Value |
|------|-----|
| **Decision ID** | DEC-022 |
| **Module** | Dashboard / Metadata Health / MediaStorage Read |
| **Sprint** | 0.7.4-A |
| **Date** | 2026-07-24 |
| **Status** | Accepted |

#### Decision

Dashboard metadata coverage and Metadata Health use one shared `MetadataHealthDefinition` and one `MetadataHealthSummary`. Their denominator is active Standard movies: an enabled Standard library, a primary video, and a media state other than `Missing`. Local, unassigned, disabled, missing, and non-video-only records do not enter the Standard denominator.

For metadata health only, `MovieGenres` is Provider metadata and `MovieTags` is user-maintained metadata. `MovieGenres` controls Provider tag coverage and completeness; `MovieTags` is reported separately and never controls completeness. This decision does not change DEC-011 Smart Search and historical tag-navigation behavior, and does not migrate or rewrite existing relations.

A complete Standard movie requires all nine conditions: uppercase normalized number, title, release date, valid studio relation, valid actor relation, valid `MovieGenres` relation, physical Poster, physical Fanart, and physical NFO. Image coverage requires a registered `Images.FilePath` whose file exists. NFO coverage accepts registered paths from `Movies.NfoPath` or `NfoDocuments`, requires an absolute `.nfo` path, and requires the file to exist.

Database records with missing files are reported as invalid resources and do not count as physical coverage. Files under configured MediaStorage that have no exact database path registration are reported as unregistered resources and are not repaired automatically. Network MediaStorage may return the current physical coverage first and finish the full unregistered-resource inventory in the background; the UI must identify the inventory as pending rather than present partial anomaly counts as final.

#### Prohibited

- Do not use all movies, Local movies, or unassigned movies as the Standard metadata denominator.
- Do not use `MovieTags` as Provider tag coverage or a completeness requirement.
- Do not treat a non-empty image/NFO path or database status as proof that the file exists.
- Do not maintain separate Dashboard and Analyzer completeness formulas.
- Do not auto-register, move, delete, download, regenerate, or otherwise repair resources while calculating health.
