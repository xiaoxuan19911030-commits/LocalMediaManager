# Local Media Manager TODO

> TODO 记录尚未完成的产品工作；版本归属以 Roadmap 为准，迁移完整性以 `migration/FEATURE_PARITY_MATRIX.md` 为准。完成项应从本文件移除并写入 Changelog。

## High

### 0.7.4-C Metadata Completion Acceptance

- [ ] Review `docs/releases/0.7.4-C-METADATA-COMPLETION.md` and the 523-row CSV plan.
- [ ] Resolve or explicitly defer 88 inaccessible media records, 134 low-confidence numbers, and 82 Code conflicts before expanding the completion scope.
- [ ] After explicit Human confirmation, create a fresh production database backup and execute one small completion session before the remaining 219 eligible movies.
- [ ] Verify Provider request counts, field-level AddedFields, pause/resume, rollback, and Metadata Health changes on the real session.
- [ ] Keep production execution and deployment disabled until the Dry Run is accepted.

### 0.7.4-B Metadata Repair Acceptance

- [ ] Human smoke the Data Center -> Metadata Repair scan, plan filters, cancel, export, confirmation dialog, and rollback controls in an isolated installation.
- [ ] Review `docs/releases/0.7.4-B-METADATA-REPAIR.csv`, especially 4 Poster conflicts, 1537 low-confidence candidates, and the inaccessible `FC2-3537141` Fanart directory.
- [ ] After explicit confirmation, create a production database backup and execute one Repair Session; verify before/after health and rollback before wider use.
- [ ] Keep production deployment and all Provider resync disabled until the Dry Run plan is accepted.

### 0.7.2 Release Verification

- [ ] 在三个 Provider 均启用的真实配置下复核 MDC-NG、MetaTube、JavBus 都收到 Movie Number Extractor 的标准番号；当前 Desktop Smoke 仅确认 JavBus 收到 `WAAA-448`。
- [ ] 由 Human 决定 Freeze、Release Tag 与推送流程。

### 0.5.0 Feature Parity Finalization

- [ ] 统一扫描、同步、图片、NFO 与整理任务的生命周期、DTO、日志、暂停/继续/取消/重试和异常恢复。
- [ ] 在独立安装版验证真实目录扫描、同名评分恢复、自动同步入队、跨重启恢复和单文件失败隔离。
- [ ] 完成图片来源/锁定、BigPic/ExtraPic、缓存、NFO 用户发起导入导出等剩余安装版交互与安全验收。
- [ ] 建立智能查重候选、忽略、合并预览、确认与可恢复执行；禁止自动删除。
- [ ] 在隔离目录验证文件整理的权限、卷不可用和中断恢复；不得对用户正式媒体进行危险烟测。
- [ ] 为播放进程无法返回可靠退出码的系统关联播放器补充诊断与降级记录。

## Medium

### MetaTube Provider

- [ ] 扩展 MetaTube 的敏感 Headers/Cookies 安全凭据支持；当前首版按旧版默认本地无凭据接口运行。

### 浏览体验

- [ ] 卡片/列表视图切换。
- [ ] 小/中/大海报密度与 96/80/60 页容量。
- [ ] 页码输入和键盘翻页。
- [ ] 日期、演员、标签可视化筛选器。
- [ ] 路由级代码拆分，消除 Vite 500 KB 主包提示。

### 任务与诊断

- [ ] 将现有同步任务控制与跨重启恢复继续扩展到独立图片、NFO 与整理任务。
- [ ] Duplicate Manager 候选、忽略与安全合并。
- [ ] 缓存清理的分类、空间估算和安全边界。
- [ ] Bridge 端口冲突诊断和客户端重连。

## Low

- [ ] 老板键和全局快捷键冲突提示。
- [ ] 完整语言切换与重启提示。
- [ ] 托盘和关闭行为设置。
- [ ] 插件安装、启停、隔离和市场。
- [ ] NAS 架构与路径映射设计。
- [ ] 0.6.0 AI Provider 设置、凭据和连接测试。

## 长期技术债

- [ ] 为 Bridge 读写服务按领域拆分 `ProductReader`。
- [ ] 为核心 Bridge 查询和迁移增加自动化测试。
- [ ] 为安装、升级、回滚和大型媒体库建立可重复烟测脚本。
- [ ] 持续处理 `MigrationWarnings` 和损坏文本数据，但不自动覆盖用户内容。
