# 功能等价矩阵

> 本文档是旧功能迁移的唯一正式总账。状态只使用约定枚举；“能显示”“存在同名页面”“模拟成功”都不等于完整迁移。目标版本以 Roadmap 为准，实施顺序先满足依赖再开发 UI。

## 字段定义

### 优先级

- **P0**：数据安全、用户数据写入、不可替代核心功能。
- **P1**：高频媒体管理能力。
- **P2**：体验和效率功能。
- **P3**：扩展和低频功能。

### 数据风险

- **无写入**
- **低风险写入**
- **数据库写入**
- **文件系统写入**
- **删除风险**
- **需要备份与回滚**

### 用户重要度

- **必须保留**
- **重要**
- **一般**
- **待确认**

### 迁移状态

- 已完整迁移
- 已部分迁移
- 仅只读
- 数据已迁移但操作未迁移
- 尚未迁移
- 需要重构
- 已废弃，等待用户确认
- 不适用于 Next

## 完整迁移门槛

只有同时满足以下条件，功能才能标记为“已完整迁移”：

1. UI 操作真实可用，不是模拟按钮。
2. Bridge 接口真实执行，并完成 DTO、输入校验和统一错误处理。
3. 数据正确持久化；重启应用后状态保持。
4. 涉及数据库结构时具备正式 Migration 编号和校验。
5. 涉及长任务时接入 Tasks 状态机、日志、取消/失败处理。
6. 涉及文件、删除或批量修改时具备影响预览、备份和回滚。
7. 自动化测试通过。
8. 人工烟测与文档中的验收方式通过。
9. 完成证据记录 Bridge 接口、Migration、测试、烟测、Git Commit 和验收记录。

任一条件不满足，只能标记为“已部分迁移”“仅只读”“数据已迁移但操作未迁移”或“尚未迁移”。

## 功能总账

| # | 功能 | 优先级 | 用户重要度 | Next 当前状态 | 依赖项 | 数据风险 | 缺失内容 | 目标版本 | 验收方式 | 完成证据 |
|---:|---|---|---|---|---|---|---|---|---|---|
| 1 | 新增后自动同步 | P1 | 必须保留 | 尚未迁移 | 扫描/导入；Bridge 同步服务；Sync DTO；Tasks；同步设置 | 数据库写入 | 导入成功事件、自动入队、失败隔离 | 0.4.2 | 导入 3 部后产生 3 个同步任务；同步失败不删除影片 | 无完整证据；旧规则见 `ScanTask.cs`/`VideoAutomation.cs` |
| 2 | 同步保存逻辑 | P0 | 必须保留 | 尚未迁移 | 写接口鉴权；Bridge Metadata Service；DTO；事务；来源字段；备份/回滚 | 需要备份与回滚 | 非破坏合并、来源追踪、关系事务、回滚 | 0.4.2 | 同步不覆盖手工标题、标签、评分、收藏 | 无完整证据；旧入口已审计 |
| 3 | 删除记忆评分 | P0 | 必须保留 | 已完整迁移 | DeletedRating Migration；Bridge 删除服务；Delete DTO；确认 Dialog；审计 | 删除风险 | 无 | 0.4.1 | 删除后只保存文件名+评分；其他用户数据不进入记忆表 | Bridge: GET/POST `/api/videos/{id}/delete*`；Migration: `0003_UserStateAuditAndRatingMemory`；Automated: `MovieDeleteRequiresPreviewBacksUpAndRemembersOnlyFilenameAndRating`；Smoke: 数据库副本预览/删除/备份；Commit `da9fcdf`；Acceptance: `0.4.1-VERIFICATION.md` |
| 4 | 同名恢复评分 | P0 | 必须保留 | 已部分迁移 | #3 Migration；#28 导入流程；Bridge 写入；幂等；回滚 | 数据库写入 | 扫描导入成功后的自动触发仍依赖 0.4.2 | 0.4.2 | 同名重导恢复评分；已有新评分不被覆盖 | Bridge: POST `/api/videos/{id}/restore-rating`；Migration: `0003`；Automated: `DeletedRatingRestoresOnlyWhenTargetHasNoNewRating`；Commit `da9fcdf`；尚无导入 UI/任务证据，故不标完整 |
| 5 | 收藏兼容 | P0 | 必须保留 | 已完整迁移 | Bridge UserState Service；Favorite DTO；旧收藏标签映射；事务 | 数据库写入 | 无 | 0.4.1 | 旧收藏数量一致；切换后卡片/详情/筛选和重启一致 | Bridge: PATCH state/POST batch favorite；Migration: N/A（复用 UserMovieState/MovieTags）；Automated: `FavoriteAndRatingPersistAndClearWithoutConflatingZero`；Smoke: 安装版脱敏样本收藏/取消并恢复原状态；Commit `da9fcdf`；Acceptance: `0.4.1-VERIFICATION.md` |
| 6 | 演员关联 | P0 | 必须保留 | 已完整迁移 | Bridge Actor Service；Actor DTO；关系事务；去重规则 | 数据库写入 | 无 | 0.4.1 | 编辑后关系、演员计数和影片详情即时一致；重启保持 | Bridge: GET/PUT `/api/actors/{id}`、PUT `/api/videos/{id}/actors`；Migration: N/A；Automated: `ActorRelationsAndPlaybackHistoryAreTransactional`；Smoke: 数据库副本演员资料/关系往返；Commit `da9fcdf`；Acceptance: `0.4.1-VERIFICATION.md` |
| 7 | ActorID=0 修复 | P0 | 必须保留 | 已完整迁移 | Diagnostics；Repair DTO；Tasks；修复 Migration（如需）；预览/确认/回滚 | 需要备份与回滚 | 无 | 0.4.1 | 无 ActorID=0/空关系；不误删有效演员；抽样通过 | Bridge: GET `/api/actors/repair-preview`、POST repair；Migration: N/A；Automated: `ActorIdZeroRepairUsesPreviewTaskAndDatabaseBackup`；Smoke: 真实库候选 0、未执行无意义写入；Commit `da9fcdf`/`cc99a9f`；Acceptance: `0.4.1-VERIFICATION.md` |
| 8 | 自定义标签 | P0 | 必须保留 | 已完整迁移 | Bridge Tag Service；Tag DTO；关系事务；系统/用户标签标识 | 数据库写入 | 无 | 0.4.1 | 用户标签完整往返；系统标签不可误删；重启保持 | Bridge: POST/PUT tag、delete preview/delete、rollback、single/batch relation；Migration: `0003` audit；Automated: `TagsSupportCreateBindBatchAndConfirmedDelete`；Smoke: 副本新建/绑定/删除；Commit `da9fcdf`；Acceptance: `0.4.1-VERIFICATION.md` |
| 9 | 评分 | P0 | 必须保留 | 已完整迁移 | Bridge UserState Service；Rating DTO；校验；乐观更新回退 | 数据库写入 | 无 | 0.4.1 | 卡片/详情修改一致；重启保持；0 与未评分区分 | Bridge: PATCH `/api/videos/{id}/state`；Migration: `0003` HasUserRating；Automated: user-state/restore tests；Smoke: 副本写入 4.5 后详情回读；Commit `da9fcdf`；Acceptance: `0.4.1-VERIFICATION.md` |
| 10 | 收藏 | P0 | 必须保留 | 已完整迁移 | #5；自动重命名隔离；错误 DTO | 数据库写入 | 无；自动重命名仍未接入，且不会阻断收藏 | 0.4.1 | 收藏成功不被自动重命名失败阻断 | Bridge: single/batch favorite；Migration: N/A；Automated: user-state test；Smoke: 安装版收藏/取消；Commit `da9fcdf`；Acceptance: `0.4.1-VERIFICATION.md` |
| 11 | 播放次数 | P0 | 必须保留 | 已完整迁移 | Bridge Player Service；PlayHistory DTO；成功启动判断；事务 | 数据库写入 | 无 | 0.4.1 | 启动成功并正常退出 +1；失败不增加；重启保持 | Bridge: POST play + `RecordPlaybackAsync`；Migration: N/A；Automated: `ActorRelationsAndPlaybackHistoryAreTransactional`；Smoke: 受控测试播放器正常退出写入；Commit `da9fcdf`；Acceptance: `0.4.1-VERIFICATION.md` |
| 12 | 播放历史 | P0 | 必须保留 | 已完整迁移 | #11；History DTO；位置/时间字段；清理规则 | 数据库写入 | 无 | 0.4.1 | 最近播放顺序、次数和位置与实际操作一致 | Bridge: POST play、GET history；Migration: N/A；Automated: playback transaction test；Smoke: 受控播放器写入后 history 回读；Commit `da9fcdf`；Acceptance: `0.4.1-VERIFICATION.md` |
| 13 | 上一部/下一部 | P1 | 必须保留 | 已完整迁移 | 查询上下文 DTO；排序/筛选签名；详情缓存/预取 | 无写入 | 无 | 0.4.1 | 保持原筛选/排序连续切换；首尾行为明确 | Bridge: GET `/api/videos/{id}/neighbors`；Migration: N/A；Automated: SQL stable order covered by build/integration smoke；Smoke: 脱敏副本验证前后邻居与安装版按钮；Commit `da9fcdf`；Acceptance: `0.4.1-VERIFICATION.md` |
| 14 | 外部播放器 | P1 | 必须保留 | 已部分迁移 | Bridge Player Service；Player Settings；路径校验；系统回退 | 低风险写入 | 自定义路径保存、回退和失败诊断 | 0.4.1 | 系统/自定义播放器均可启动；无效路径错误可理解 | 部分：POST play + 安装版 toast 烟测；Commit `2d52dfc`；记录 `0.4.0-VERIFICATION.md` |
| 15 | 图片来源规则 | P1 | 必须保留 | 已部分迁移 | Image Service；Image DTO；Settings；poster/thumb/fanart 映射；人工锁定 | 数据库写入 | 三种用途选择、来源优先级、手选保护 | 0.4.2 | 各页面使用正确资源；手选图片不被自动覆盖 | 部分：GET `/api/images/{id}/primary`；主图只读烟测 |
| 16 | BigPic/ExtraPic | P1 | 必须保留 | 数据已迁移但操作未迁移 | Path Resolver；Image Service；文件系统读取；旧目录配置 | 无写入 | 两种旧目录解析与详情画廊 | 0.4.2 | 统一目录/相对目录样本均能显示 | 数据迁移记录存在；无完整读取验收 |
| 17 | 图片缓存 | P1 | 重要 | 已部分迁移 | Cache Service；Settings；文件系统；限额；Tasks | 文件系统写入 | 本地缓存、失效、限额和安全清理 | 0.4.2 | 离线可读；清理不删除源图；失败可恢复 | 浏览器图片缓存不等于产品缓存；无完整证据 |
| 18 | 高清图片 | P1 | 重要 | 已部分迁移 | Image Service；缩略/原图 DTO；按需加载；缓存 | 无写入 | 原图端点、画廊和分层加载 | 0.4.2 | 列表不拉原图；详情按需加载高清图 | 部分：primary image 流；无画廊/网络测试 |
| 19 | 智能卡图 | P1 | 重要 | 尚未迁移 | CardCover Service；Tasks；Settings；专用目录；图像分析；右键菜单 | 文件系统写入 | 自动补全、重识别、左/中/右裁切和日志 | 0.4.2 | 自动补全及四种人工修正；源图不被覆盖 | 无完整证据；旧算法已审计 |
| 20 | 卡片/列表 | P2 | 重要 | 已部分迁移 | View Settings；共享卡片/列表组件；状态持久化 | 低风险写入 | 列表视图与视图选择持久化 | 0.4.1 | 切换不改变筛选、排序、页码；重启保持 | 部分：共享卡片组件；无列表/持久化测试 |
| 21 | 海报大小 | P2 | 重要 | 仅只读 | Appearance Settings；密度 DTO；分页容量；响应式布局 | 低风险写入 | 小/中/大写设置及 96/80/60 页容量 | 0.4.1 | 12/10/6 列和页容量规则；重启保持 | 部分：旧设置只读；100/125/150 视口烟测通过 |
| 22 | 分页 | P1 | 必须保留 | 已部分迁移 | Bridge page DTO；稳定排序；Pagination 组件；自动化测试 | 无写入 | 缺自动化边界/筛选复位测试和正式验收证据 | 0.4.1 | 总页数、前后页、筛选复位、末页和空页正确 | 部分：GET `/api/videos?limit&offset`；UI/人工烟测；Commit `2d52dfc`/`83c53b5`；缺自动化测试，故不得标完整 |
| 23 | 页码输入 | P2 | 重要 | 尚未迁移 | #22；页码输入组件；输入校验 | 无写入 | Enter 跳转、越界钳制和反馈 | 0.4.1 | 输入 2、0、负值、超大值均符合规则 | 无完整证据 |
| 24 | 快捷翻页 | P2 | 重要 | 尚未迁移 | #22/#23；Shortcut Settings；焦点作用域 | 低风险写入 | 键盘监听、冲突和输入焦点保护 | 0.4.1 | 输入框不抢键；列表和详情快捷键正确 | 无完整证据 |
| 25 | 搜索 | P1 | 必须保留 | 已部分迁移 | Bridge Search Service；Search DTO；索引；分页 | 无写入 | 日期条件、旧智能语法和自动化测试 | 0.4.1 | 番号/标题/演员/标签/路径/厂商/系列命中 | 部分：GET `/api/search/advanced`；ABF=104 烟测；Commit `83c53b5` |
| 26 | 筛选 | P1 | 必须保留 | 已部分迁移 | #25；演员/标签选择器；Filter DTO；稳定分页 | 无写入 | 日期、实体选择器、组合保存 | 0.4.1 | 跨维度 AND、同维度 OR 与旧版样本一致 | 部分：收藏/评分/元数据/文件/媒体库筛选已接 Bridge；无自动化组合测试 |
| 27 | 排序 | P1 | 必须保留 | 已部分迁移 | Bridge 查询；Sort DTO；稳定次排序；Settings | 低风险写入 | 更多旧排序项、默认值持久化、跨页测试 | 0.4.1 | 跨页无重复遗漏；重启保留默认排序 | 部分：newest/code/rating 等 Bridge 排序；无持久化/自动化证据 |
| 28 | 媒体库、扫描与导入 | P1 | 必须保留 | 仅只读 | Library CRUD Migration；Bridge Library/Scan Service；DTO；Tasks；文件系统；Settings | 需要备份与回滚 | CRUD、来源、子目录、排除、扫描、导入、格式识别 | 0.4.2 | 创建媒体库到扫描导入完整流程；失败可恢复 | 部分：GET `/api/libraries`；多媒体库大型脱敏样本烟测；Commit `83c53b5` |
| 29 | 元数据状态 | P1 | 重要 | 仅只读 | Metadata Service；Status DTO；Diagnostics；Tasks | 无写入 | 批量修复、筛选联动和任务入口 | 0.4.2 | 状态统计与抽样 SQL 一致；修复后即时刷新 | 部分：GET `/api/metadata/overview`；脱敏样本统计与 SQL 抽样一致；Commit `83c53b5` |
| 30 | 智能查重 | P1 | 重要 | 仅只读 | Duplicate Service；候选 DTO；文件哈希/属性；Tasks；确认/回滚 | 删除风险 | 候选详情、忽略、合并预览与安全执行 | 0.5.0 | 不自动删除；用户确认后执行且可回滚 | 部分：Diagnostics 重复番号计数 25；无管理流程 |
| 31 | MetaTube | P1 | 重要 | 尚未迁移 | Plugin/Provider Service；Server Settings；Metadata DTO；Tasks；网络 | 数据库写入 | Provider、同步任务、字段/图片映射和来源追踪 | 0.4.2 | 单部同步与旧版结果对照；失败保留旧数据 | 无完整证据；旧 `PrivateCrawler` 已审计 |
| 32 | NFO 导入导出 | P1 | 必须保留 | 数据已迁移但操作未迁移 | NFO Service；DTO；Settings；Tasks；文件系统；覆盖预览 | 文件系统写入 | 解析、导出、图片/演员图独立覆盖策略 | 0.4.2 | 样本往返；用户字段和图片选择不丢失 | 部分：NfoPath 数据已迁移；无读写接口/测试 |
| 33 | 重命名与整理 | P1 | 必须保留 | 尚未迁移 | Organizer Service；Preview DTO；Tasks；文件系统；冲突检测；回滚 | 需要备份与回滚 | 预览、冲突检测、文件/数据库一致性 | 0.4.2 | 目标存在不覆盖；中途失败可回滚 | 无完整证据；旧 `RenameConfig`/规则已审计 |
| 34 | 缓存清理 | P2 | 重要 | 尚未迁移 | #17；Cache Service；空间估算；Tasks；确认 | 删除风险 | 缓存分类、估算、安全删除和结果报告 | 0.4.3 | 只删除派生缓存；源图/卡图不受影响 | 无完整证据 |
| 35 | 老板键 | P3 | 一般 | 仅只读 | Shortcut Settings；Tauri global shortcut；冲突处理 | 低风险写入 | 注册、隐藏/恢复和错误提示 | 0.5.0 | 组合键可靠；冲突不覆盖旧值 | 部分：旧设置只读；无 Tauri 实现 |
| 36 | 主题、语言、关闭行为 | P2 | 重要 | 已部分迁移 | Settings Service；Theme Context；i18n；Tauri tray/lifecycle | 低风险写入 | 语言、托盘和关闭行为写设置 | 0.4.3 | 两主题、重启项、托盘/关闭行为均通过 | 部分：深浅主题和缩放烟测；Commit `2d52dfc`/`83c53b5`；语言/关闭只读 |
| 37 | 插件 | P3 | 重要 | 仅只读 | Plugin contracts；Manifest；权限；隔离；Settings；Tasks | 需要备份与回滚 | 安装、启停、更新、卸载和故障隔离 | 0.8.0 | 故障插件不阻塞主程序；操作可回滚 | 部分：Plugin Center 只读；Commit `83c53b5` |
| 38 | 服务器资源 | P3 | 重要 | 仅只读 | Provider Settings；安全凭据；Connection Test DTO；Tasks | 低风险写入 | 编辑、密钥保护、测试连接 | 0.5.0 | 敏感值不回显；启用/可用分离；测试不隐式保存 | 部分：Settings Bridge 隐藏敏感值；Plugin 页面只读 |
| 39 | 端口监听 | P2 | 重要 | 需要重构 | Bridge lifecycle；Settings；端口探测；会话鉴权；重连 | 低风险写入 | 可配置端口、占用诊断、客户端重连 | 0.4.3 | 冲突可见；Bridge/React 自动恢复一致连接 | 部分：固定 loopback 47831 可用；无冲突恢复测试 |
| 40 | 任务队列 | P0 | 必须保留 | 已部分迁移 | Tasks Schema；Bridge Task Service；Task DTO；Runner；持久日志 | 数据库写入 | 创建、暂停、继续、取消、重试、恢复和日志 | 0.5.0 | 完整状态机、重启恢复、失败重试和日志通过 | 部分：GET `/api/tasks` 与 Tasks UI；Commit `83c53b5`；无写入/Runner |
| 41 | 其他旧版能力 | P3 | 待确认 | 需要重构 | 持续源码审计；用户确认；Roadmap/TODO | 需要备份与回滚 | 逐项拆分并进入本矩阵 | 0.5.x | 每项建立独立依赖、风险和验收后实施 | 三份旧版审计文档；Commit `83c53b5`；不代表功能完成 |

## 证据记录格式

功能进入“已完整迁移”前，完成证据单元格必须至少包含：

```text
Bridge: METHOD /api/...
Migration: Mxxx_description（不需要时写 N/A + 原因）
Automated: 测试项目/用例及结果
Smoke: 人工烟测场景及结果
Commit: Git SHA
Acceptance: 发布验证记录或独立验收文档
```

## 当前结论

- 按严格门槛，矩阵中暂时没有功能标记为“已完整迁移”；这是证据标准提升后的正常结果，不代表 0.4.0 页面无效。
- 0.4.1 优先完成 P0 用户状态、标签、评分恢复、演员关系、播放记录与 Tasks 写入基础。
- 0.4.2 集中处理扫描导入、自动同步、MetaTube、NFO、图片和文件整理。
- 0.5.0 完成主要媒体管理功能等价；0.5.5 建立 LTS 稳定基线；0.6.0 才开始真实 AI Provider 接入。
