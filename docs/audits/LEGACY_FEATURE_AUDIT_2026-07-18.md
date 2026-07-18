# Legacy Feature Audit Report

**Sprint:** Legacy Feature Audit
**Date:** 2026-07-18
**Scope:** Compare legacy Jvedio user-facing features with Local Media Manager.
**Rule:** Audit only. No new feature development, UI redesign, DesignSystem change, Dashboard/AI/resource-health work, or visual polish.

## Evidence

### Legacy sources inspected

- `D:\Jvedio\ProjectFiles\私人影片管理器-v5.5.0-git-source.zip`
- Extracted audit copy: `C:\Users\Administrator\AppData\Local\Temp\lmm-legacy-source-audit-20260718\私人影片管理器-v5.5.0\Jvedio-WPF`
- Legacy inventory: `docs/migration/LEGACY_FEATURE_INVENTORY.md`
- Legacy rules: `docs/migration/LEGACY_BUSINESS_RULES.md`
- Migration matrix: `docs/migration/FEATURE_PARITY_MATRIX.md`

Key legacy files searched:

- `Jvedio-WPF/WindowStartUp.xaml`
- `Jvedio-WPF/WindowStartUp.xaml.cs`
- `Jvedio-WPF/Windows/Window_Main.xaml`
- `Jvedio-WPF/Windows/Window_Main.xaml.cs`
- `Jvedio-WPF/Windows/Window_Details.xaml`
- `Jvedio-WPF/Windows/Window_Details.xaml.cs`
- `Jvedio-WPF/Windows/Window_Edit.xaml`
- `Jvedio-WPF/Windows/Window_EditActor.xaml`
- `Jvedio-WPF/Windows/Window_Settings.xaml`
- `Jvedio-WPF/Windows/Window_Server.xaml`
- `Jvedio-WPF/Windows/Window_ScanDetail.xaml`
- `Jvedio-WPF/Windows/Window_DataBase.xaml`
- `Jvedio-WPF/Windows/Dialog/Window_TagStamp.xaml`
- `Jvedio-WPF/Windows/Dialog/Window_SearchAsso.xaml`
- `Jvedio-WPF/Core/UserControls/VideoList.xaml`
- `Jvedio-WPF/Core/UserControls/VideoSideMenu.xaml`
- `Jvedio-WPF/Core/UserControls/ActorList.xaml`
- `Jvedio-WPF/Core/UserControls/Filter.xaml`
- `Jvedio-WPF/Core/UserControls/Tasks/TaskList.xaml`
- `Jvedio-WPF/Core/Scan/ScanTask.cs`
- `Jvedio-WPF/Core/Media/SmartCardCoverManager.cs`
- `Jvedio-WPF/Core/Media/CardCoverTask.cs`
- `Jvedio-WPF/Core/Config/Common/RenameConfig.cs`
- `Jvedio-WPF/Core/Config/Common/ScanConfig.cs`
- `Jvedio-WPF/Core/Config/Common/ServerConfig.cs`
- `Jvedio-WPF/Core/Crawler/CrawlerServer.cs`
- `Jvedio-WPF/Core/Server/ServerManager.cs`
- `Jvedio-WPF/Entity/CommonSQL/TagStamp.cs`
- `Jvedio-WPF/Properties/Settings.Designer.cs`

### Local Media Manager sources inspected

- `src/app/router.tsx`
- `src/layouts/AppShell.tsx`
- `src/components/workspace/MovieWall.tsx`
- `src/pages/MediaPage.tsx`
- `src/pages/MovieDetailPage.tsx`
- `src/pages/EntityPage.tsx`
- `src/pages/LibrariesPage.tsx`
- `src/pages/FavoritesPage.tsx`
- `src/pages/HistoryPage.tsx`
- `src/pages/MetadataPage.tsx`
- `src/pages/DiagnosticsPage.tsx`
- `src/pages/DuplicatesPage.tsx`
- `src/pages/MaintenancePage.tsx`
- `src/pages/TasksPage.tsx`
- `src/pages/SettingsPage.tsx`
- `src/services/bridge.ts`
- `backend/LocalMediaManager.Bridge/Program.cs`

## Summary

| Metric | Count |
|---|---:|
| Legacy user functions audited | 88 |
| ✅ 已迁移 | 51 |
| 🟡 部分迁移 | 14 |
| 🔄 已升级替代 | 3 |
| ❌ 未迁移 | 20 |

This audit counts user-usable actions, not just pages. For example, detail-page image replacement, image deletion, image directory reveal, and image cache rebuild are counted separately.

## Feature Matrix

| # | 功能名称 | 旧版位置 | 新版位置 | 迁移状态 | 说明 |
|---:|---|---|---|---|---|
| 1 | 影片墙列表 | `Window_Main` / `VideoList` | `/media`, `MovieWall` | ✅ 已迁移 | 新版统一影片墙承载全部影片列表。 |
| 2 | 卡片/列表视图切换 | `VideoList` | `MovieWall` | ✅ 已迁移 | 已统一到 MovieWall。 |
| 3 | 海报/缩略图展示 | `VideoList`, `ImageCache` | `SmartImage`, image endpoints | ✅ 已迁移 | 新版使用缓存与资源端点。 |
| 4 | 新加入/收藏状态标识 | `VideoList` badge/state | 影片卡片、详情页 | ✅ 已迁移 | 保持为状态 Badge，不进入标签系统。 |
| 5 | 打开影片详情 | `Window_Details` | `/movies/:id` | ✅ 已迁移 | 详情页已迁移。 |
| 6 | 随机影片/随机播放 | 旧版工具栏/快捷入口 | 无独立入口 | ❌ 未迁移 | 设置页快捷键区也显示为规划项。 |
| 7 | 最近播放列表 | 播放历史 | `/history` | ✅ 已迁移 | 最近播放作为一级导航入口。 |
| 8 | 播放次数/历史写入 | 播放流程与历史表 | `bridge.playMovie`, history route | 🟡 部分迁移 | 有播放记录能力，安装版真实播放器路径仍需重点验收。 |
| 9 | 外部播放器设置 | `Window_Settings` playback | 设置中心/播放 | 🟡 部分迁移 | 有默认/自定义播放器配置；旧版播放器细节未完全等价。 |
| 10 | 打开/定位影片文件 | 详情/右键文件操作 | 详情页、媒体页右键 | ✅ 已迁移 | 使用平台 reveal/open directory 能力。 |
| 11 | 打开应用目录 | `WindowStartUp` 关于/菜单 | 无明确入口 | ❌ 未迁移 | 新版关于页有信息展示，但未发现打开应用目录动作。 |
| 12 | 单片收藏/取消收藏 | 详情/列表 | 详情页、媒体页批量栏 | ✅ 已迁移 | 用户状态 API 已覆盖。 |
| 13 | 收藏页 | 旧版收藏筛选/入口 | `/favorites` | ✅ 已迁移 | 收藏页面已统一 MovieWall。 |
| 14 | 单片评分 | 详情/列表 | MovieWall/详情状态 | ✅ 已迁移 | 已经支持评分状态写入。 |
| 15 | 清除评分 | 评分菜单/编辑 | 评分状态 API | ✅ 已迁移 | 支持清除评分。 |
| 16 | 删除时记忆评分 | 旧版删除/重导业务规则 | Bridge user state restore | ✅ 已迁移 | 迁移矩阵标记为已覆盖。 |
| 17 | 重新导入恢复评分 | 扫描/导入业务规则 | Bridge restore logic | 🟡 部分迁移 | 逻辑存在，真实库重导场景仍需安装版验收。 |
| 18 | 标签分类浏览 | `TagStamp`, 侧栏标签 | `/tags/movie-tags` | ✅ 已迁移 | 本轮前已恢复旧标签可见逻辑；不按 Source 强拆。 |
| 19 | 自定义标签 CRUD | `Window_TagStamp` | `/tags/custom` | ✅ 已迁移 | 支持创建、编辑、删除与撤销删除。 |
| 20 | 单片标签关系编辑 | 详情/编辑窗口 | 详情页标签编辑 | ✅ 已迁移 | 标签关系经 Bridge 写入。 |
| 21 | 批量标签 | 列表批量操作 | 媒体页批量栏 | ✅ 已迁移 | 支持批量标签更新。 |
| 22 | 演员列表 | `ActorList` | `/actors` | ✅ 已迁移 | 支持演员列表和进入影片墙。 |
| 23 | 演员编辑 | `Window_EditActor` | 演员页编辑 | ✅ 已迁移 | 基础演员编辑已迁移。 |
| 24 | 单片演员关系编辑 | 详情/编辑窗口 | 详情页演员编辑 | ✅ 已迁移 | 支持影片演员关系维护。 |
| 25 | ActorID=0 修复 | Legacy data repair | 演员页 repair action | ✅ 已迁移 | 有预览和应用流程。 |
| 26 | 演员头像/资料完整维护 | `Window_EditActor` | 演员页/actor image endpoint | 🟡 部分迁移 | 基础资料与图片能力存在，旧版资料字段不完全等价。 |
| 27 | 导演列表与导演筛选 | 旧版分类/详情字段 | `/tags/directors` | 🟡 部分迁移 | 新版有入口和筛选语义，但历史库导演数据质量需验收。 |
| 28 | 系列列表与系列筛选 | 旧版系列字段/分类 | `/tags/series` | ✅ 已迁移 | 支持系列列表并进入 MovieWall。 |
| 29 | 厂商分类浏览 | 旧版厂商/Studio 分类 | 无独立厂商入口 | ❌ 未迁移 | 新版详情/搜索有厂商信息，但缺厂商分类页。 |
| 30 | Genre 分类浏览 | 旧版类别/Genre | 详情/搜索局部支持 | 🟡 部分迁移 | Genre 不等同标签；未发现独立 Genre 分类入口。 |
| 31 | 普通关键词搜索 | `SearchAsso`, 主搜索 | Smart Search 普通关键词 | ✅ 已迁移 | 支持普通关键词。 |
| 32 | 结构化搜索 | 旧版关联/条件搜索 | Smart Search v1.0 | ✅ 已迁移 | 支持评分、收藏、导演、系列、标签等结构化语义。 |
| 33 | FilterBar 筛选 | `Filter` 控件 | MovieWall FilterBar | ✅ 已迁移 | 评分、元数据、图片、媒体库筛选已统一。 |
| 34 | 旧顶部标签下拉筛选 | `Filter` 标签项 | 标签导航 + 二级分类页 | 🔄 已升级替代 | 已按新导航语义移出顶部 FilterBar。 |
| 35 | 排序 | 旧版排序菜单 | MovieWall sort | 🟡 部分迁移 | 常用排序已迁移；旧版 ViewNumber、数据库创建时间等排序未完全等价。 |
| 36 | 分页 | `VideoList` page logic | MovieWall Pagination | ✅ 已迁移 | 已统一分页。 |
| 37 | 页码输入跳转 | 旧版分页控件 | 无明确输入跳页 | ❌ 未迁移 | 新版只发现分页控件，未发现手动页码输入。 |
| 38 | 左右翻页快捷键 | 旧版快捷键 | 设置页快捷键为规划项 | ❌ 未迁移 | 快捷键配置未落地。 |
| 39 | 详情上一部/下一部 | `Window_Details` | 详情页 neighbors | ✅ 已迁移 | 支持前后影片导航。 |
| 40 | 复制影片信息 | `Window_Details` CopyVideoInfo | 无对应按钮 | ❌ 未迁移 | 详情页未发现复制影片信息动作。 |
| 41 | 翻译影片简介 | `Window_Details` TranslateMovie | 无对应按钮 | ❌ 未迁移 | 新版未发现翻译动作。 |
| 42 | 完整影片字段编辑 | `Window_Edit` | 局部编辑 | ❌ 未迁移 | 标题、编号、简介、日期、厂商、Genre 等完整编辑页未迁移。 |
| 43 | 详情标签/演员编辑 | `Window_Edit`, details | 详情页 dialogs | ✅ 已迁移 | 标签与演员关系可编辑。 |
| 44 | 单片同步/刮削 | 详情/右键同步 | 详情页、媒体页右键 | ✅ 已迁移 | Sync metadata 已迁移。 |
| 45 | 批量同步/刮削 | 批量任务 | 媒体页批量/任务中心 | ✅ 已迁移 | 支持批量同步任务。 |
| 46 | 导入后自动同步 | 扫描配置 | 库扫描 + 任务体系 | 🟡 部分迁移 | 自动读取/同步设置仍有规划项。 |
| 47 | MetaTube 设置与连接测试 | 爬虫/服务器配置 | 设置中心 metadata | 🟡 部分迁移 | Base URL、timeout、test 存在；旧版 header/cookie 资源细节不足。 |
| 48 | 爬虫服务器资源管理 | `Window_Server`, `CrawlerServer` | 插件/MetaTube 局部 | ❌ 未迁移 | 旧版服务器资源、端口、cookie/header 管理未完整迁移。 |
| 49 | NFO 导出 | NFO 菜单/设置 | 详情页 NFO export | ✅ 已迁移 | 有预览与确认。 |
| 50 | NFO 导入 | NFO 菜单/扫描 | 详情页 NFO import | ✅ 已迁移 | 有预览与确认。 |
| 51 | NFO 详细设置 | `VieModel_Settings`, NFO switches | 设置中心 NFO | 🟡 部分迁移 | 输出目录、冲突策略、图片包含存在；旧版演员图/截图/预览复制细项未全覆盖。 |
| 52 | 多图资源展示 | 图片列表/详情 | 详情页 images | ✅ 已迁移 | Poster、thumbnail、preview、screenshot、GIF 等资源可显示。 |
| 53 | 原图查看/缩放 | 图片查看器 | 详情页 image viewer | ✅ 已迁移 | 支持查看和缩放。 |
| 54 | 图片替换 | 图片右键/编辑 | 详情页 replace image | ✅ 已迁移 | 支持替换。 |
| 55 | 图片锁定/解锁 | 图片保护规则 | 详情页 lock/unlock | ✅ 已迁移 | 支持锁定保护。 |
| 56 | 图片删除 | 图片右键 | 详情页 delete image | ✅ 已迁移 | 有删除预览与确认。 |
| 57 | 图片目录/文件定位 | 图片右键 | 详情页 reveal/open | ✅ 已迁移 | 支持打开目录和定位文件。 |
| 58 | 生成封面/预览/截图/GIF | ffmpeg/image tasks | 媒体页右键与批量 | ✅ 已迁移 | 支持单片与部分批量生成。 |
| 59 | 从图片列表设为缩略图/海报/两者 | `Window_Details` SetAs menu | 无对应动作 | ❌ 未迁移 | 新版可替换/生成，但未发现直接 SetAs。 |
| 60 | 删除当前图片列表全部图片 | `Window_Details` DeleteAllImageInList | 无对应动作 | ❌ 未迁移 | 新版为单资源删除。 |
| 61 | 智能卡图与人工裁切修正 | `SmartCardCoverManager` | 无完整等价入口 | ❌ 未迁移 | 新版有资源生成，但旧版卡图修正流程未发现。 |
| 62 | 图片缓存检查/清理/重建 | `ImageCache` | 设置中心/详情页 | ✅ 已迁移 | 支持清理预览和重建。 |
| 63 | 媒体库 CRUD | 旧版数据库/库概念 | `/libraries` | ✅ 已迁移 | 新版以媒体库替代用户库组织。 |
| 64 | 来源文件夹配置 | 扫描路径选择 | `/libraries` source folders | ✅ 已迁移 | 支持来源目录、排除规则、递归。 |
| 65 | 增量/全量扫描 | `ScanTask`, `ScanManager` | `/libraries` scan actions | ✅ 已迁移 | 支持 normal/full scan。 |
| 66 | 扫描详情与失败 NFO 明细 | `Window_ScanDetail` | 任务日志/概览 | 🟡 部分迁移 | 任务日志可见，但旧版扫描详情分类视图未完整迁移。 |
| 67 | 旧多数据库 UI | `WindowStartUp`, `Window_DataBase` | 无旧式多 DB UI | ❌ 未迁移 | 新版转为单运行库 + 媒体库，不再提供旧数据库卡片管理。 |
| 68 | 数据安全备份/恢复计划 | 旧数据库恢复/备份 | 设置中心 data safety | 🔄 已升级替代 | 新版用备份验证、恢复计划和数据安全页替代。 |
| 69 | 查重 | 旧工具/数据库维护 | `/duplicates` | 🟡 部分迁移 | 可查看重复，未发现合并、忽略或删除工作流。 |
| 70 | 维护/诊断统计 | 旧数据库工具 | `/diagnostics`, `/maintenance` | 🔄 已升级替代 | 新版以诊断和维护报告替代。 |
| 71 | 元数据状态中心 | 旧状态/扫描结果 | `/metadata` | ✅ 已迁移 | 有元数据概览。 |
| 72 | 文件整理/重命名执行 | RenameConfig / organizer | 详情页 organizer | ✅ 已迁移 | 有 dry run 和 execute。 |
| 73 | 重命名模板完整度 | `RenameConfig` | MediaStorage templates / organizer | 🟡 部分迁移 | 新版模板能力存在，但旧版模板变量和规则不完全等价。 |
| 74 | 安全删除信息/媒体文件 | 右键删除 | 媒体页 SafeDeleteDialog | ✅ 已迁移 | 支持预览与确认。 |
| 75 | 批量操作栏 | `VideoList` batch actions | 媒体页 edit mode | ✅ 已迁移 | 收藏、评分、标签、同步、删除、图片任务已迁移。 |
| 76 | 批量文件整理/移动 | 旧版批量整理 | 详情页 organizer / Bridge movieIds | 🟡 部分迁移 | Bridge 支持多 movieIds，但 UI 未发现完整批量整理入口。 |
| 77 | 任务列表与日志 | `TaskList` | `/tasks` | ✅ 已迁移 | 任务状态、日志查看已迁移。 |
| 78 | 任务暂停/恢复/取消/重试/删除/清理 | `TaskList` commands | `/tasks` actions | ✅ 已迁移 | 操作按钮已覆盖。 |
| 79 | 日志清理 | 旧设置/日志工具 | 设置中心 logs | ❌ 未迁移 | 新版显示规划文案，未执行清理。 |
| 80 | 主题设置 | `Window_Settings` appearance | 设置中心 appearance | ✅ 已迁移 | 深浅主题支持。 |
| 81 | 语言设置 | 旧设置语言项 | 无明确语言切换 | ❌ 未迁移 | 未发现新版语言设置。 |
| 82 | 托盘/关闭到任务栏 | 旧设置/窗口行为 | 无等价设置 | ❌ 未迁移 | 未发现 close-to-tray 用户配置。 |
| 83 | 快捷键配置 | 旧快捷键/设置 | 设置中心 shortcuts planned | ❌ 未迁移 | 仅显示规划说明。 |
| 84 | 设置导入/导出 | 旧配置管理 | 设置中心 data safety | ✅ 已迁移 | 支持导出和导入预览。 |
| 85 | 端口监听配置 | `ServerConfig`, `JavaServerConfig` | Bridge fixed loopback | ❌ 未迁移 | 新版未提供旧式端口监听 UI。 |
| 86 | 插件管理 | 旧插件/服务器生态 | `/plugins` | ❌ 未迁移 | 新版插件页为占位/只读风格，未发现完整安装启停管理。 |
| 87 | 关于信息 | `WindowStartUp` about | 设置中心 about | ✅ 已迁移 | 基础关于信息已迁移。 |
| 88 | 检查更新 | `WindowStartUp` CheckUpgrade | 无明确入口 | ❌ 未迁移 | 未发现新版检查更新动作。 |

## Unmigrated Items

| Priority | 功能 | 建议 |
|---|---|---|
| P0 | 完整影片字段编辑 | 下一轮优先补齐。旧版 `Window_Edit` 是高频核心能力，新版目前只覆盖标签/演员/状态。 |
| P0 | 厂商分类浏览 | 用户导航范围中仍包含厂商，新版缺独立入口。 |
| P0 | 随机播放 | 用户明确列入审计范围，且新版未发现入口。 |
| P1 | 复制影片信息 | 详情页小功能，但用户可见且迁移成本低。 |
| P1 | 页码输入与翻页快捷键 | 旧版效率功能，新版 MovieWall 可作为统一入口补齐。 |
| P1 | 图片 SetAs 缩略图/海报/两者 | 旧版详情页右键功能，新版图片管理缺这个直接动作。 |
| P1 | 智能卡图/人工裁切修正 | 旧版图片体验更细，需独立评估是否作为资源管理 Sprint。 |
| P1 | 日志清理 | 新版设置页已有位置但仍是规划项。 |
| P2 | 翻译影片简介 | 旧版按钮存在；需决定是否保留，避免误触 AI/在线服务范围。 |
| P2 | 打开应用目录、检查更新 | 旧版启动页/关于菜单功能；新版未发现对应入口。 |
| P2 | 语言设置、托盘/关闭到任务栏、快捷键配置 | 属于设置中心功能缺口。 |
| P2 | 端口监听配置、爬虫服务器资源管理、插件管理 | 涉及架构和安全边界，不建议顺手补。 |
| P3 | 删除图片列表全部图片、旧多数据库 UI | 需要 Human 决策是否仍保留旧交互。 |

## Partial Migration Notes

- Playback: basic play and history exist, but true installed-app player integration should be smoke-tested with default player and custom player.
- Directors: navigation and filter semantics exist; historical data quality and empty director records still need sample database verification.
- Genre: should remain separate from tags. Current UI does not expose a full Genre category page.
- NFO: import/export are implemented, but old detailed copy switches for actor pictures, screenshots, previews, and path choices are not fully equivalent.
- Scan: library scan and task logs exist, but old `Window_ScanDetail` style categorized scan result review is only partially represented.
- Duplicate detection: listing exists, but no full resolution workflow was found.
- Rename/file organizer: detail-level dry run and execute exist; full batch organizer entry needs confirmation.

## Regression Bugs

No regression bug was fixed during this audit. The gaps above are migration gaps or partial migrations based on source inspection. I did not run installed-app smoke tests in this sprint, so runtime-only regressions remain a Human verification item.

## Places Where Legacy Is Still Better

- Legacy has a more complete full-field movie edit workflow.
- Legacy image context menu supports setting an existing image as thumbnail, poster, or both.
- Legacy has more detailed NFO image-copy settings.
- Legacy startup/database UI supports multi-database card management, rename, hide/show, image setting, sorting, and restore.
- Legacy exposes more productivity features: page jump, keyboard navigation, random play, copy movie info, and translation.
- Legacy scan detail UI appears more granular for import/update/failure review.

## Suggested Next Sprint

**Recommended:** `Movie Editing Parity`

Reason: the largest P0 user-facing gap is not a new center page, but restoration of old editing capability. Suggested scope:

- Full movie metadata edit dialog/page using existing Bridge patterns.
- Fields audited from legacy `Window_Edit`: title, original title, code, release date, runtime, rating, overview, studio, director, series, Genre, tags, actors, paths where already supported.
- Reuse current detail page and MovieWall state restoration.
- Do not change DesignSystem or navigation.

Second choice after that: `File And Image Operations Parity`, covering image SetAs, random play, copy info, page jump, and remaining right-click gaps.

## Verification

| Item | Result |
|---|---|
| Source audit | PASS |
| Code changes | None |
| UI changes | None |
| Build | Not run; audit/documentation only |
| Installed app smoke | Not run; Human acceptance required |

