# 旧版业务规则

本文件记录从旧源码确认、迁移时容易被“页面看起来能用”掩盖的规则。Next 实现必须通过 Bridge；写库使用 Migration 后的新结构，危险操作必须可预览、确认和回滚。

## 1. 用户数据优先

1. 同步和刮削默认只补空字段；用户手工标题、自定义标签、评分、收藏、备注和播放历史不得被覆盖。
2. AI 或外部 Provider 结果必须记录来源，永远不能冒充用户数据。
3. 用户手选海报/主图具有锁定语义，自动下载、智能裁切和缓存刷新不得覆盖。

证据：`Windows/Window_Details.xaml.cs` 的同步后用户数据恢复路径、`Core/Net/DownLoadTask.cs` 的字段/图片写入、`ViewModels/VieModel_Edit.cs` 的独立保存步骤。

## 2. 删除评分记忆

- 删除前调用 `VideoAutomation.RememberDeletedRating`。
- 记忆范围必须严格限制为“文件名 + 评分”；不要扩展为路径、标签、收藏或其他隐私数据。
- 扫描导入在关系写入完成前后调用 `RestoreDeletedRating`，同名命中后恢复评分。
- 新记录已有用户评分时不得覆盖；重复导入需幂等。

证据：`Core/Features/VideoAutomation.cs:125`、`Core/Scan/ScanTask.cs:613`、`Core/UserControls/VideoList.xaml.cs:624,635`、`Windows/Window_Details.xaml.cs:808,819`。

## 3. 收藏与自动重命名

- 旧版收藏依赖 `TagStamp.TAG_ID_FAVORITE`，新库使用 `UserMovieState.IsFavorite`；迁移期必须双向兼容而非只读其中之一。
- 收藏动作是主动作，自动重命名是附加动作。
- `AutoRenameWhenFavorite` 关闭时不得重命名；目标存在时绝不覆盖；重命名失败只记录错误，不能撤销收藏。

证据：`Core/UserControls/VideoList.xaml.cs:1513,1770-1792`、`Core/Config/Common/RenameConfig.cs`。

## 4. 演员关系修复

- `ActorID=0`、空名和包含无效字符的演员关系不能显示为正常演员。
- 启动维护先移除无效关系，再从可信的元数据演员图片 URL 恢复可证明的关联。
- 修复必须可预览，不能仅按相似名称批量猜测；同名演员合并需用户确认。

证据：`WindowStartUp.xaml.cs:153-157`、`VideoAutomation.RemoveInvalidActorData`、`VideoAutomation.RepairActorLinksFromImageUrls`、`VideoAutomation.IsValidActorName`。

## 5. 扫描与导入

- 识别视频扩展名时必须覆盖旧版实际支持格式，包括 `.vob`、`.mpg`、`.mpeg`，并排除 NFO、图片、字幕等非视频资源。
- 来源目录的启用、子目录、排除规则和媒体库归属共同决定扫描范围。
- 单个文件失败不能中止整个扫描；结果区分导入、更新、未导入和失败。
- 新增成功后恢复删除评分，并按配置进入自动同步队列；同步失败不得删除已导入影片。

证据：`Core/Scan/ScanTask.cs`、`Windows/Window_ScanDetail.xaml.cs`、`Windows/Window_Main.xaml.cs:836-837`。

## 6. 同步与元数据合并

- 保留原有手工“同步信息”入口，自动同步是附加能力，不是替代。
- 番号、标题、简介、日期、演员、标签、系列、制作商和图片各自记录来源与结果。
- 下载或解析失败保留旧值；关系表更新应在事务中完成，不能留下半写入状态。
- MetaTube 搜索结果需先按番号选择，再取详情、预览图和演员图；Provider 类型作为来源保存。

证据：`Core/UserControls/VideoList.xaml.cs:1146-1160`、`Core/Net/DownLoadTask.cs`、`PrivateCrawler/PrivateCrawler.cs:62-264`。

## 7. 图片与智能卡图

- 横版、竖版和详情主图是不同用途，来源优先级不得写死在页面。
- BigPic、ExtraPic、相对影片目录和统一图片目录都需兼容。
- 列表只预载缩略图；详情按需加载原图和宣传图/截图。
- 智能卡图优先复用专用目录已有结果；缺失时才建立 `CardCoverTask`。
- 人工操作包括重新识别、居中、居左、居右；任何结果都不得覆盖源图。
- 自动补全由设置控制，长任务进入统一 Tasks，可取消并有日志。

证据：`Core/Media/SmartCardCoverManager.cs`、`Core/Media/CardCoverTask.cs`、`Core/Media/ImageCache.cs`、`Core/Config/PathManager.cs`。

## 8. 播放与历史

- 外部播放器启动成功后才增加播放次数和历史；进程启动失败必须反馈。
- 自定义播放器路径有效时优先，否则使用系统默认关联。
- 上一部/下一部沿用进入详情时的筛选、排序和结果集，不按数据库主键盲跳。
- 方向键在详情用于切换；在文本输入获得焦点时不得触发。

证据：`Core/UserControls/VideoList.xaml.cs`、`Windows/Window_Details.xaml.cs:922-927`、`Core/UserControls/PlayHistoryView.xaml.cs`。

## 9. 搜索、筛选、排序与分页

- 普通关键词搜索必须始终存在，不能被未来 AI 搜索替代。
- 不同筛选维度使用 AND；同一维度的多个候选使用 OR。
- 筛选/每页数量变化回到第一页；总页数基于过滤后的总量。
- 排序必须带稳定次排序键，避免跨页重复或遗漏。
- 页码输入 Enter 生效，非法和越界值钳制并反馈；左右快捷键需尊重输入焦点。

证据：`Core/UserControls/Filter.xaml.cs`、`VideoList.xaml.cs:409,2012-2018`、`ActorList.xaml.cs:660-663`。

## 10. NFO、重命名与文件整理

- NFO 的图片、演员图、截图、预览图、保存路径和覆盖策略是独立开关。
- 重命名必须先生成预览，目标存在时禁止覆盖；文件和数据库更新需保持一致。
- 文件移动、重命名、删除均需确认、影响范围、备份/回滚和操作日志。

证据：`ViewModels/VieModel_Settings.cs:322-368`、`Windows/Window_Settings.xaml.cs:971-1089`、`VideoList.xaml.cs:706-754`。

## 11. 插件、服务器与端口

- 插件加载失败必须隔离，不能阻止核心媒体浏览启动。
- Cookies、Headers、API Key 等敏感字段不在普通设置 JSON 或 UI 中明文显示。
- 服务器“启用”和“可用”是两个状态；连接测试不得隐式保存。
- 端口冲突要明确诊断并保持客户端与 Bridge 配置一致，不能静默漂移。

证据：`Window_Settings.xaml.cs`、`Core/Crawler/CrawlerServer.cs`、`Core/Server/ServerManager.cs`、`Window_Server.xaml.cs`。

## 12. 统一任务状态机

扫描、导入、刮削、同步、图片下载、缓存、裁切、重命名、整理、迁移、查重和未来 AI 必须统一进入 Tasks。至少支持 `Pending / Running / Paused / Completed / Failed / Cancelled`，并保存总量、完成量、当前项、失败数、日志、重试和取消结果。页面线程不得直接执行批处理。

证据：`Core/UserControls/Tasks/TaskList.xaml.cs`、`Core/Media/CardCoverTask.cs` 及各旧任务实现。

## 迁移验收底线

- 页面显示真实数据但不能编辑时，只能标记“仅只读”或“数据已迁移但操作未迁移”。
- 没有 Bridge 写接口、确认、错误处理和回滚的危险操作，不得标记完整迁移。
- 旧 XAML 仅用于定位入口和理解流程，不复制到 Next。
