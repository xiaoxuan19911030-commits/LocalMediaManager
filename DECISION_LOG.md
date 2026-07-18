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
  标签:          → Tags.Source NOT IN ('User','LegacyStamp')
  自定义标签:    → Tags.Source IN ('User','LegacyStamp')
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

- `标签:` 默认只表示影片自带标签，不得同时查用户自定义标签
- `自定义标签:` 只表示用户标签与 Legacy stamp
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

---

## PROJECT §21 Sprint History（摘要）

| Sprint | 摘要 | 决策 |
|--------|------|------|
| 0.5.0-15 | Settings 全局 Draft + Coordinator 统一 Save + LeaveIntent | DEC-001, DEC-002, DEC-003 |
| 0.5.0-16 | MediaStorage 纳入 Settings；动态 RootPath；Documents 回退 | DEC-004 ～ DEC-007 |
| 0.5.0-17 | 统一写入 Resolver；Legacy Read 保留；WallCrops 纳入 | DEC-008 ～ DEC-010 |
| 0.5.0-18 | Smart Search；实体标签语义区分；导航分类补齐 | DEC-011 |

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
