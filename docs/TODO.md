# Local Media Manager TODO

> TODO 记录尚未完成的产品工作；版本归属以 Roadmap 为准，迁移完整性以 `migration/FEATURE_PARITY_MATRIX.md` 为准。完成项应从本文件移除并写入 Changelog。

## High

### 0.4.3 Media Assets & File Organization

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
