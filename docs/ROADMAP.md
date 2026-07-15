# Local Media Manager Roadmap

> 本文档是版本范围的唯一正式入口。范围调整必须同时更新 Roadmap、TODO 和 Changelog；聊天中的临时想法未进入本文档前不视为正式版本承诺。

## 版本生命周期

所有版本统一经过以下阶段：

```text
Planning → Design → Develop → Self Test → Smoke Test → Freeze → Release → Archive
```

- **Planning**：确定目标、优先级、依赖、风险与验收方式。
- **Design**：完成 Bridge/DTO/Migration/Tasks/UI 设计，不先做孤立页面按钮。
- **Develop**：按架构和 UI 规范实施，持续更新功能矩阵证据。
- **Self Test**：开发者执行自动化测试、静态检查和目标功能自测。
- **Smoke Test**：在真实数据库与独立安装目录执行关键用户路径。
- **Freeze**：停止新增功能，只处理阻塞发布的缺陷。
- **Release**：完成全量构建、安装包、备份部署、标签和发布记录。
- **Archive**：冻结发布证据、迁移状态和遗留事项，后续不移动版本标签。

## 0.4.0 — Modern Media Experience

**状态：Release / Frozen**
**发布日期：2026-07-15**

`v0.4.0` 标签已经冻结。该版本不再增加功能；必要缺陷修复必须单独记录，功能开发进入 0.4.1。

- Modern UI Experience
- Dashboard
- Movie Wall 与现代影片卡片
- Information Layout 影片详情
- Search 2.0
- Library 只读产品视图
- Actors、Tags、Favorites、History
- Metadata Center
- Diagnostics Center
- Tasks Center 基础
- Plugin Center Placeholder
- AI Provider Placeholder
- 深浅主题、响应式和 Windows 缩放验证

## 0.4.1 — Legacy Feature Migration Part 1

**状态：Release / Frozen**
**发布日期：2026-07-15**

目标是恢复最高频用户写操作与用户状态兼容，不恢复旧 UI。

### 第一优先：用户状态与标签

- 收藏写入与旧收藏标签兼容
- 评分写入
- 自定义标签创建、编辑、删除
- 单部与批量标签绑定
- 所有写操作通过 Bridge，具备错误提示与刷新一致性

### 第二优先：删除评分记忆

- 删除影片前仅保存“文件名 + 评分”
- 同名文件重新导入时恢复评分
- 已有新评分时禁止覆盖
- 删除、恢复和失败场景具备测试

### 第三优先：演员关系

- 演员关系编辑与正确写入
- ActorID=0、空演员和损坏关系诊断
- 修复预览、确认、任务日志和可回滚策略

### 第四优先：播放体验

- 上一部、下一部沿用原结果集
- 播放成功后写入次数和历史
- 外部播放器路径与失败回退

### 第五优先：详情页操作能力

- 收藏、评分、标签、编辑和同步入口
- 打开文件夹、文件信息与用户状态
- 全部复用 Next Information Layout 与公共组件

### 发布结论

- 收藏、评分、自定义标签、演员资料与影片演员关系已恢复真实写入。
- 删除影片记录具备影响预览、数据库备份和文件名评分记忆；不删除媒体文件。
- ActorID=0 修复具备预览、确认、任务记录、自动化测试和操作前数据库备份。
- 播放正常退出后写入播放次数与历史；上一部/下一部保持影片墙搜索和排序上下文。
- 同名评分恢复 Bridge 能力已完成；与扫描导入流程的自动触发在 0.4.2 接入，不提前伪装为完整导入迁移。

## 0.4.2 — Legacy Feature Migration Part 2

**状态：Planning（下一规划版本）**

- 媒体扫描与导入
- 新增后自动同步
- MetaTube Provider
- NFO 导入与导出
- poster、thumb、fanart、BigPic、ExtraPic 兼容
- 图片缓存与智能卡图
- 文件整理与重命名
- 所有长操作接入 Tasks

## 0.4.3 — Stability Update

**状态：Planning**

- Bug 修复和 UI 一致性
- Bridge 性能、鉴权与错误模型
- SQLite 查询、索引和连接优化
- 内存、图片加载和路由拆包优化
- Diagnostics 扩展与自动化回归

## 0.5.0 — Media Management

**状态：Future**

- Metadata 工作流
- NFO 管理
- File Organizer
- Duplicate Manager
- Library CRUD
- 完整 Task Manager
- 达到主要旧版媒体管理功能等价

## 0.5.5 — LTS Stability

**状态：Future**

0.5.0 功能等价完成后，先建立长期稳定基线，再进入 AI：

- 集中修复 Bug 和迁移边界问题
- 优化启动速度、内存和图片生命周期
- 优化数据库查询、索引和 Bridge 并发
- 验证 5 万至 10 万影片的大媒体库体验
- 扩充自动化测试、安装升级和回滚测试
- 建立性能基准、长期运行和故障恢复报告

0.5.5 不引入新的大型产品功能；通过 LTS 验收后才允许开始 0.6.0 AI。

## 0.6.0 — AI

**状态：Future**

- Provider 设置与安全凭据
- 连接测试
- 单部影片标签与元数据建议
- 建议预览、用户确认、应用日志
- 不在首个 AI 版本开放无人值守批处理

## 0.7.0 — NAS

**状态：Future**

- NAS 来源与连接状态
- 路径映射、断线恢复和只读扫描
- 凭据安全存储

## 0.8.0 — Plugin Marketplace

**状态：Future**

- 插件清单、安装、启停、更新与隔离
- 权限声明、兼容版本和故障恢复

## 0.9.0 — Beta

**状态：Future**

- 迁移与升级回归
- 大型媒体库压力测试
- 安装、卸载、备份与恢复验证

## 1.0.0 — Stable Release

**状态：Future**

- 稳定的核心媒体管理工作流
- 正式升级策略、支持矩阵与用户文档
- 已知高风险数据操作全部具备确认和回滚

## 版本完成门槛

每个版本结束前必须完成：

1. 更新 Roadmap、Changelog 与 TODO。
2. Web、Bridge、Migration、Tauri 的 Debug/Release 构建。
3. Windows NSIS 安装包。
4. 现有安装目录备份、独立 Next 目录部署和烟测。
5. 深浅主题与目标缩放验证。
6. Git Commit、版本标签和阶段前回滚标签。
7. 发布验证报告和明确的未完成清单。
