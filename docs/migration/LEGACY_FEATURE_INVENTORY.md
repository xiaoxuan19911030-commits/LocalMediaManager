# 旧版功能清单

> 审计基线：`PrivateVideoManager-WPF` 当前源码；审计日期：2026-07-15。旧版仅作为功能、业务规则、数据兼容和回归基准，不迁移 XAML 布局与控件样式。

| # | 功能 / 用户用途 | 旧版入口与代码位置 | 数据库 / 配置依赖 | 隐藏规则摘要 |
|---:|---|---|---|---|
| 1 | 新增影片自动同步 | 扫描完成；`Core/Scan/ScanTask.cs`、`Core/Features/VideoAutomation.cs` | 扫描配置、下载队列 | 新增成功后入队，失败不能回滚导入 |
| 2 | 原同步保存逻辑 | 影片列表/详情“同步信息”；`Core/UserControls/VideoList.xaml.cs`、`Windows/Window_Details.xaml.cs`、`Core/Net/DownLoadTask.cs` | metadata、actor/tag 关系、图片目录 | 保留用户评分/收藏/标签，下载结果按字段合并 |
| 3 | 删除时记忆评分 | 删除影片；`VideoList.xaml.cs:624,635`、`Window_Details.xaml.cs:808,819`、`VideoAutomation.RememberDeletedRating` | `common_deleted_rating_memory` | 仅保存文件名与评分，不保存路径和其他用户数据 |
| 4 | 重新导入恢复评分 | 扫描导入；`Core/Scan/ScanTask.cs:613`、`VideoAutomation.RestoreDeletedRating` | 同上 | 用同名文件匹配；恢复后不应覆盖新评分 |
| 5 | 收藏兼容 | 卡片/详情收藏；`VideoList.xaml.cs`、`Window_Details.xaml.cs`、`TagStamp.TAG_ID_FAVORITE` | tagstamp 关系 | 收藏历史上以特殊标签实现；需兼容新 `UserMovieState` |
| 6 | 演员关联写入 | 编辑/同步；`ViewModels/VieModel_Edit.cs`、`Core/Net/DownLoadTask.cs` | actor_info、metadata_to_actor | 名称去重，图片 URL 可辅助恢复关系 |
| 7 | ActorID=0 修复 | 启动维护；`WindowStartUp.xaml.cs:153-157`、`VideoAutomation.RemoveInvalidActorData/RepairActorLinksFromImageUrls` | actor_info、关系表、演员图片 URL | 删除无效 0 关系后按可靠来源补链，不生成空演员 |
| 8 | 自定义标签 | 标签弹窗/编辑页；`Windows/Dialog/Window_TagStamp.xaml.cs`、`VieModel_Edit.cs` | tagstamp、metadata_to_tagstamp | 系统标签与用户标签需区分，用户标签优先保留 |
| 9 | 评分 | 卡片/详情/编辑；`Entity/Data/Video.cs`、`Window_Details.xaml.cs` | `Grade` | 0 表示未评分；删除记忆仅处理有效评分 |
| 10 | 收藏 | 卡片/详情；同 #5 | 收藏标签、自动重命名配置 | 标签动作成功优先，自动重命名失败不能阻断收藏 |
| 11 | 播放次数 | 播放入口；`VieModel_Main.cs`、`VideoList.xaml.cs` | `PlayCount` | 启动播放器成功后计数，不能仅因点击就误计 |
| 12 | 播放历史 | 左侧历史；`Core/UserControls/PlayHistoryView.xaml.cs` | common_play_history、metadata_video | 记录时间、位置并按最近播放排序 |
| 13 | 上一部/下一部 | 详情页；`Window_Details.xaml.cs:922-925,1229-1234` | 当前筛选结果顺序 | 必须沿用进入详情时的结果集和排序 |
| 14 | 外部播放器 | 卡片/详情播放；`VideoList.xaml.cs`、`Core/Config/WindowConfig/Settings.cs` | PlayerPath、系统默认播放器 | 自定义路径可选；不存在时回退系统关联并提示错误 |
| 15 | 图片来源规则 | 卡片/详情；`Entity/Data/Video.cs`、`Mapper/Common/PictureMapper.cs` | poster/thumb/fanart、图片模式 | 横竖用途独立，用户手选主图不得被自动覆盖 |
| 16 | BigPic / ExtraPic | 详情图片；`Core/Config/PathManager.cs`、`Entity/Data/Video.cs` | BigPic、ExtraPic 目录 | 兼容旧目录和相对影片目录两种模式 |
| 17 | 图片缓存 | 卡片/详情；`Core/Media/ImageCache.cs` | ImageCache 配置和缓存目录 | 缓存可清理，源图不可被清理动作删除 |
| 18 | 高清图片 | 详情图；`Window_Details.xaml.cs`、`ImageCache.cs` | 原图路径、缓存 | 列表缩略图与详情原图分层加载 |
| 19 | 智能卡图 | 右键/设置自动补全；`SmartCardCoverManager.cs`、`CardCoverTask.cs` | `AutoCompleteSmartCardCovers`、专用卡图目录 | 已有专用图直接复用；重识别/左/中/右裁切可人工纠正 |
| 20 | 卡片/列表视图 | 影片/演员工具栏；`VideoList.xaml`、`ActorList.xaml` | VideoConfig 视图值 | 切换不改变筛选、排序和页码 |
| 21 | 海报大小 | 显示设置；`VideoConfig.GlobalImageWidth`、`Window_Settings.xaml.cs` | VideoConfig | 密度联动每页数量，缺少竖图时需明确回退 |
| 22 | 分页 | 影片/演员；`VideoList.xaml.cs`、`ActorList.xaml.cs` | PageSize | 筛选变化回第一页，总页数按筛选后总量计算 |
| 23 | 页码输入 | 影片工具栏；`VideoList.xaml.cs:409` | 当前页/总页 | Enter 跳转，超界钳制并给出反馈 |
| 24 | 左右翻页快捷键 | 主窗口/详情；`Window_Main.xaml.cs:901-907`、`VideoList.xaml.cs:2012-2018` | 快捷键配置 | 输入框聚焦时不能抢键；Ctrl 组合保留旧语义 |
| 25 | 搜索 | 顶部搜索；`VieModel_VideoList.cs`、`VideoAutomation.BuildSmartSearchSql` | 影片、演员、标签等表 | 普通搜索独立于未来 AI；特殊智能语法需明确迁移 |
| 26 | 筛选 | 筛选栏；`Core/UserControls/Filter.xaml.cs` | metadata、tag、actor、file | 多条件使用 AND，单字段多选使用 OR |
| 27 | 排序 | 工具栏/ViewModel | 当前查询和配置 | 稳定次排序避免分页重复/遗漏 |
| 28 | 媒体库/来源目录 | 启动库页、扫描页；`WindowStartUp.xaml.cs`、`Window_DataBase.xaml.cs`、`Core/Scan/ScanTask.cs` | app/database config、扫描目录 | 子目录、排除项、扩展名、启停与最近扫描时间均需保留 |
| 29 | 元数据状态 | 筛选/详情；`VideoAutomation.IsMetaStatus`、`Filter.xaml.cs:750` | metadata 状态字段 | 未刮削、已刮削、缺信息必须区分 |
| 30 | 智能查重 | 左侧入口；`VideoSideMenu.xaml.cs:551`、`VideoAutomation.BuildDuplicateWrapper` | 番号、路径、文件属性 | 只列疑似重复，禁止自动删除 |
| 31 | MetaTube 同步 | PrivateCrawler；`PrivateCrawler.cs:62-264` | Provider URL、服务器资源 | 搜索结果挑选、详情补全、演员图与预览图分别映射 |
| 32 | NFO 导入/导出 | 扫描和设置；`VieModel_Settings.cs`、`Window_Settings.xaml.cs:971-1089` | NFO parse/save 配置 | 图片、演员图、截图、覆盖行为分别可控 |
| 33 | 重命名/整理 | 卡片右键；`VideoList.xaml.cs:706-754`、`RenameConfig.cs` | 格式模板、标签、文件路径 | 目标存在时禁止覆盖；失败不破坏数据库/收藏动作 |
| 34 | 缓存清理 | 主窗口/设置；`Window_Main.xaml.cs:1205`、`Window_Settings.xaml.cs:1863` | 缓存目录 | 仅清派生缓存，保留源图片与用户专用卡图 |
| 35 | 老板键 | 设置；`Window_Settings.xaml.cs:123-205` | 快捷键配置 | 支持组合键，注册失败需提示且不覆盖旧值 |
| 36 | 主题/语言/关闭行为 | 设置；`Window_Settings.xaml.cs`、`WindowConfig/Settings.cs` | Theme、CurrentLanguage、CloseToTaskBar | 即时项即时生效，语言等重启项需明确标记 |
| 37 | 插件 | 设置/启动；`Window_Settings.xaml.cs`、`Window_Main.xaml.cs` | PluginList、DeleteList、插件目录 | 加载失败隔离，不应阻止主程序启动 |
| 38 | 服务器资源 | 设置/服务器窗；`Window_Server.xaml.cs`、`Core/Crawler/CrawlerServer.cs` | Servers、cookies、headers | 敏感字段不得明文展示；可用性与启用状态分开 |
| 39 | 端口监听 | 服务窗口/配置；`Core/Server/ServerManager.cs`、`Window_Server.xaml.cs` | JavaServer.Port 等 | 端口冲突需可诊断，不得静默换端口造成客户端失联 |
| 40 | 任务队列 | 任务页；`Core/UserControls/Tasks/TaskList.xaml.cs`、各 `AbstractTask` | 任务内存状态、日志 | 长任务统一状态、进度、失败日志、取消与重试 |
| 41 | 其他旧版能力 | 各窗口/ViewModel/Mapper | 多个旧表和配置 | 必须逐项审计；没有证据不得标记已迁移 |

## 审计边界

- 本清单确认了入口和主要依赖，不表示写操作已迁移。
- `LocalMediaManager.db` 当前承载影片、关系、用户状态、媒体库、任务和迁移警告；旧配置仍通过 Settings Bridge 只读兼容。
- 后续每项实现必须建立 Bridge DTO/Service；数据库变化必须通过 Migration，长任务必须进入 Tasks。
