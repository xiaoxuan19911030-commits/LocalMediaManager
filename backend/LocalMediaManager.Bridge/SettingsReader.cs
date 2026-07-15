using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

internal sealed record SettingsSourceDto(
    string Kind,
    string Path,
    bool Exists,
    bool ReadOnly,
    long? Size,
    DateTime? LastModified,
    string? Sha256,
    string Status,
    string? Error);

internal sealed record SettingFieldDto(
    string Key,
    string Category,
    string Section,
    string Label,
    string Source,
    string ValueType,
    object? Value,
    object? DefaultValue,
    bool Nullable,
    bool RequiresRestart,
    bool Immediate,
    bool Dangerous,
    bool LegacyCompatible,
    bool SafeToWrite,
    bool Sensitive,
    string ReadStatus,
    string? Error,
    bool Mapped);

internal sealed record CrawlerServerDto(
    string PluginId,
    string Url,
    bool Enabled,
    int Available,
    string LastRefreshDate,
    string Cookies,
    string Headers);

internal sealed record SettingsSnapshotDto(
    bool ReadOnly,
    string Mode,
    DateTime ReadAt,
    IReadOnlyList<SettingsSourceDto> Sources,
    IReadOnlyList<SettingFieldDto> Fields,
    IReadOnlyList<CrawlerServerDto> Servers,
    MetaTubeSettingsDto MetaTube,
    int MappedCount,
    int UnmappedCount,
    IReadOnlyList<string> Errors);

internal sealed record SettingDefinition(
    string Config,
    string Property,
    string Category,
    string Section,
    string Label,
    string ValueType,
    object? DefaultValue,
    bool Nullable = false,
    bool RequiresRestart = false,
    bool Immediate = false,
    bool Dangerous = false,
    bool LegacyCompatible = true,
    bool SafeToWrite = false,
    bool Sensitive = false);

internal static class SettingsReader
{
    private const string SourceName = "app_configs.sqlite/app_configs.ConfigValue";

    private static readonly SettingDefinition[] Definitions = [
        D("WindowConfig.Settings", "OpenDataBaseDefault", "general", "启动", "默认打开上一次关闭的库", "boolean", false),
        D("ScanConfig", "ScanOnStartUp", "general", "启动", "启动时扫描媒体库关联目录", "boolean", false),
        D("WindowConfig.Settings", "CloseToTaskBar", "general", "窗口行为", "关闭按钮最小化到托盘", "boolean", true, immediate: true),
        D("WindowConfig.Settings", "CurrentLanguage", "general", "语言", "显示语言", "string", "zh-CN", restart: true),

        D("WindowConfig.Main", "DetailWindowShowAllMovie", "library", "行为", "详情窗口左右浏览数据库全部影片", "boolean", false),
        D("WindowConfig.Settings", "DelInfoAfterDelFile", "library", "行为", "删除文件时同时删除影片信息", "boolean", true, dangerous: true),
        D("ScanConfig", "MinFileSize", "library", "扫描与导入", "最小影片文件大小（MB）", "number", 0d),
        D("ScanConfig", "FetchVID", "library", "扫描与导入", "扫描时识别番号", "boolean", true),
        D("ScanConfig", "ScanNfo", "library", "扫描与导入", "扫描 NFO", "boolean", true),
        D("ScanConfig", "LoadDataAfterScan", "library", "扫描与导入", "扫描后加载数据", "boolean", true),
        D("ScanConfig", "DataExistsIndexAfterScan", "library", "扫描与导入", "扫描后建立资源存在索引", "boolean", true),
        D("ScanConfig", "ImageExistsIndexAfterScan", "library", "扫描与导入", "扫描后建立图片索引", "boolean", true),
        D("WindowConfig.Settings", "DefaultDBID", "library", "数据库", "默认数据库 ID", "integer", 0L),
        D("WindowConfig.Settings", "AutoBackup", "library", "数据库", "自动备份数据库", "boolean", true),
        D("WindowConfig.Settings", "AutoBackupPeriodIndex", "library", "数据库", "自动备份周期索引", "integer", 1L),
        D("WindowConfig.Settings", "PlayableIndexCreated", "library", "数据库", "可播放索引已建立", "boolean", false),
        D("WindowConfig.Settings", "PictureIndexCreated", "library", "数据库", "图片索引已建立", "boolean", false),

        D("WindowConfig.Settings", "VideoPlayerPath", "playback", "播放器", "指定播放器路径", "path", null, nullable: true),

        D("WindowConfig.Settings", "SkipExistImage", "metadata", "同步与网络", "跳过已下载图片", "boolean", false),
        D("WindowConfig.Settings", "DownloadWhenTitleNull", "metadata", "同步与网络", "仅标题为空时同步", "boolean", true),
        D("WindowConfig.Settings", "IgnoreCertVal", "metadata", "同步与网络", "忽略证书错误", "boolean", true, dangerous: true),
        D("WindowConfig.Settings", "AutoHandleHeader", "metadata", "同步与网络", "自动处理请求头", "boolean", false),
        D("ProxyConfig", "HttpTimeout", "metadata", "同步与网络", "请求超时（秒）", "integer", 10L),
        D("ProxyConfig", "ProxyMode", "metadata", "代理", "代理方式", "enum", 1L),
        D("ProxyConfig", "ProxyType", "metadata", "代理", "自定义代理协议", "enum", 1L),
        D("ProxyConfig", "Server", "metadata", "代理", "自定义代理地址", "string", null, nullable: true),
        D("ProxyConfig", "Port", "metadata", "代理", "自定义代理端口", "integer", 0L),
        D("ProxyConfig", "UserName", "metadata", "代理", "自定义代理用户名", "string", null, nullable: true),
        D("ProxyConfig", "Password", "metadata", "代理", "自定义代理密码", "secret", null, nullable: true, sensitive: true),
        D("DownloadConfig", "DownloadInfo", "metadata", "同步内容", "同步影片信息", "boolean", true),
        D("DownloadConfig", "DownloadThumbNail", "metadata", "同步内容", "同步缩略图", "boolean", true),
        D("DownloadConfig", "DownloadPoster", "metadata", "同步内容", "同步海报", "boolean", true),
        D("DownloadConfig", "DownloadPreviewImage", "metadata", "同步内容", "同步预览图", "boolean", false),
        D("DownloadConfig", "DownloadActor", "metadata", "同步内容", "同步演员信息及头像", "boolean", true),
        D("DownloadConfig", "OverrideInfo", "metadata", "同步内容", "强制同步信息", "boolean", false, dangerous: true),
        D("DownloadConfig", "AutoDownloadAfterScan", "metadata", "同步内容", "扫描后自动同步", "boolean", true),
        D("WindowConfig.Settings", "SaveInfoToNFO", "metadata", "NFO", "保存信息到 NFO", "boolean", false),
        D("WindowConfig.Settings", "NFOSavePath", "metadata", "NFO", "NFO 保存路径", "path", null, nullable: true),
        D("WindowConfig.Settings", "OverwriteNFO", "metadata", "NFO", "覆盖已有 NFO", "boolean", false, dangerous: true),
        D("ScanConfig", "CopyNFOPicture", "metadata", "NFO", "复制 NFO 图片", "boolean", true),
        D("ScanConfig", "CopyNFOActorPicture", "metadata", "NFO", "复制演员头像", "boolean", true),
        D("ScanConfig", "CopyNFOPreview", "metadata", "NFO", "复制预览图", "boolean", true),
        D("ScanConfig", "CopyNFOScreenShot", "metadata", "NFO", "复制截图", "boolean", true),
        D("ScanConfig", "CopyNFOOverwriteImage", "metadata", "NFO", "NFO 图片覆盖现有图片", "boolean", false, dangerous: true),
        D("WindowConfig.Settings", "PicPathMode", "metadata", "图片路径", "图片路径模式", "enum", 1L),
        D("WindowConfig.Settings", "PicPathJson", "metadata", "图片路径", "图片路径配置", "json", null, nullable: true),
        D("WindowConfig.Settings", "AutoGenScreenShot", "metadata", "图片", "无封面时使用截图作为封面", "boolean", true),
        D("WindowConfig.Settings", "ImageCache", "metadata", "缓存", "启用图片缓存", "boolean", true),
        D("WindowConfig.Settings", "CacheExpiration", "metadata", "缓存", "图片缓存有效期（天）", "integer", 10L),
        D("FFmpegConfig", "Path", "metadata", "视频处理", "FFmpeg 路径", "path", null, nullable: true),
        D("FFmpegConfig", "ThreadNum", "metadata", "视频处理", "FFmpeg 线程数", "integer", 2L),
        D("FFmpegConfig", "ScreenShotNum", "metadata", "视频处理", "截图数量", "integer", 5L),
        D("FFmpegConfig", "ScreenShotIgnoreStart", "metadata", "视频处理", "跳过开头（分钟）", "integer", 1L),
        D("FFmpegConfig", "ScreenShotIgnoreEnd", "metadata", "视频处理", "跳过结尾（分钟）", "integer", 1L),
        D("FFmpegConfig", "SkipExistScreenShot", "metadata", "视频处理", "跳过已有截图", "boolean", true),
        D("FFmpegConfig", "ScreenShotAfterImport", "metadata", "视频处理", "导入后生成截图", "boolean", true),
        D("FFmpegConfig", "SkipExistGif", "metadata", "视频处理", "跳过已有 GIF", "boolean", true),
        D("FFmpegConfig", "GifAutoHeight", "metadata", "视频处理", "GIF 保持原视频宽高比", "boolean", true),
        D("FFmpegConfig", "GifWidth", "metadata", "视频处理", "GIF 宽度", "integer", 300L),
        D("FFmpegConfig", "GifHeight", "metadata", "视频处理", "GIF 高度", "integer", 168L),
        D("FFmpegConfig", "GifDuration", "metadata", "视频处理", "GIF 时长（秒）", "integer", 3L),
        D("RenameConfig", "RemoveTitleSpace", "metadata", "重命名", "移除标题空格", "boolean", false),
        D("RenameConfig", "AddRenameTag", "metadata", "重命名", "添加重命名标签", "boolean", false),
        D("RenameConfig", "AutoRenameWhenFavorite", "metadata", "重命名", "收藏后自动重命名", "boolean", true),
        D("RenameConfig", "FormatString", "metadata", "重命名", "重命名格式", "string", ""),

        D("ThemeConfig", "ThemeIndex", "appearance", "主题", "旧程序主题索引", "enum", 0L, immediate: true),
        D("ThemeConfig", "ThemeID", "appearance", "主题", "旧程序主题 ID", "string", "", immediate: true),
        D("VideoConfig", "ImageMode", "appearance", "影片卡片", "海报显示模式", "enum", 1L, immediate: true),
        D("VideoConfig", "GlobalImageWidth", "appearance", "影片卡片", "影片卡片宽度", "integer", 300L, immediate: true),
        D("VideoConfig", "MainImageAutoMode", "appearance", "海报", "自动选择主图模式", "boolean", true, immediate: true),
        D("VideoConfig", "BlurBackground", "appearance", "影片卡片", "模糊背景", "boolean", true, immediate: true),
        D("VideoConfig", "DisplayID", "appearance", "影片卡片", "显示番号", "boolean", true, immediate: true),
        D("VideoConfig", "DisplayTitle", "appearance", "影片卡片", "显示标题", "boolean", true, immediate: true),
        D("VideoConfig", "DisplayDate", "appearance", "影片卡片", "显示日期", "boolean", true, immediate: true),
        D("VideoConfig", "DisplayStamp", "appearance", "影片卡片", "显示标签", "boolean", true, immediate: true),
        D("VideoConfig", "DisplayFavorites", "appearance", "影片卡片", "显示收藏状态", "boolean", true, immediate: true),
        D("WindowConfig.Settings", "DetailShowBg", "appearance", "详情页面", "显示详情背景", "boolean", true, immediate: true),

        D("WindowConfig.Settings", "HotKeyEnable", "shortcuts", "老板键", "启用老板键", "boolean", false),
        D("WindowConfig.Settings", "HotKeyString", "shortcuts", "老板键", "当前快捷键", "shortcut", null, nullable: true),
        D("WindowConfig.Settings", "HotKeyModifiers", "shortcuts", "老板键", "快捷键修饰键值", "integer", 0L),
        D("WindowConfig.Settings", "HotKeyVK", "shortcuts", "老板键", "快捷键虚拟键值", "integer", 0L),

        D("WindowConfig.Settings", "ListenEnabled", "advanced", "端口监听", "启用端口监听", "boolean", false, restart: true, dangerous: true),
        D("WindowConfig.Settings", "ListenPort", "advanced", "端口监听", "监听端口", "string", null, nullable: true, restart: true),
        D("WindowConfig.Settings", "RemoteIndex", "advanced", "端口监听", "远程服务索引", "integer", 0L, restart: true),
        D("JavaServer", "Port", "advanced", "服务器资源", "Java 服务端口", "integer", 9527L, restart: true),
        D("WindowConfig.Settings", "Debug", "advanced", "日志", "调试日志", "boolean", false, restart: true),
        D("WindowConfig.Settings", "PluginEnabledJson", "advanced", "插件", "插件启用状态", "json", null, nullable: true),
        D("PluginConfig", "PluginList", "advanced", "插件", "插件清单", "json", ""),
        D("PluginConfig", "DeleteList", "advanced", "插件", "插件删除清单", "json", null, nullable: true, dangerous: true),
    ];

    public static async Task<SettingsSnapshotDto> ReadAsync(string configDatabasePath, MetaTubeSettingsDto metaTube)
    {
        var errors = new List<string>();
        var sources = new List<SettingsSourceDto>();
        var fields = new List<SettingFieldDto>();
        var servers = new List<CrawlerServerDto>();
        var rows = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        var sourceInfo = await InspectSourceAsync(configDatabasePath, "SQLite 配置库");
        sources.Add(sourceInfo);

        if (!sourceInfo.Exists) {
            errors.Add($"找不到旧配置数据库：{configDatabasePath}");
        } else {
            try {
                await using var connection = await OpenReadOnlyAsync(configDatabasePath);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT ConfigName, ConfigValue FROM app_configs ORDER BY ConfigName";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) {
                    string name = reader.GetString(0);
                    string json = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    try {
                        rows[name] = string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
                    } catch (JsonException ex) {
                        rows[name] = null;
                        errors.Add($"配置 {name} 的 JSON 无法解析：{ex.Message}");
                    }
                }
            } catch (Exception ex) {
                errors.Add($"读取配置数据库失败：{ex.Message}");
            }
        }

        var mappedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SettingDefinition definition in Definitions) {
            string key = $"{definition.Config}.{definition.Property}";
            mappedKeys.Add(key);
            object? value = null;
            string status;
            string? error = null;
            if (!rows.TryGetValue(definition.Config, out JsonNode? configNode)) {
                status = "missing-config";
                error = $"未找到配置组 {definition.Config}";
            } else if (configNode is not JsonObject configObject) {
                status = "invalid-config";
                error = $"配置组 {definition.Config} 不是对象结构";
            } else if (!TryGetProperty(configObject, definition.Property, out JsonNode? node)) {
                status = "missing-field";
                error = $"旧配置中没有字段 {definition.Property}";
            } else {
                status = "ok";
                value = definition.Sensitive ? MaskSensitive(node) : node?.DeepClone();
            }
            fields.Add(ToField(definition, key, value, status, error, true));
        }

        foreach ((string configName, JsonNode? configNode) in rows) {
            if (configName.Equals("Servers", StringComparison.OrdinalIgnoreCase) && configNode is JsonArray serverArray) {
                ReadServers(serverArray, servers, errors);
                continue;
            }
            if (configNode is not JsonObject configObject)
                continue;
            foreach ((string property, JsonNode? node) in configObject) {
                string key = $"{configName}.{property}";
                if (mappedKeys.Contains(key))
                    continue;
                bool sensitive = IsSensitive(property);
                fields.Add(new SettingFieldDto(
                    key, "advanced", "兼容字段", key, SourceName, InferType(node),
                    sensitive ? MaskSensitive(node) : node?.DeepClone(), null, true, false, false,
                    false, true, false, sensitive, "ok", null, false));
            }
        }

        int mappedCount = fields.Count(field => field.Mapped && field.ReadStatus == "ok");
        int unmappedCount = fields.Count(field => !field.Mapped);
        return new SettingsSnapshotDto(false, "统一设置服务", DateTime.Now, sources, fields, servers, metaTube,
            mappedCount, unmappedCount, errors);
    }

    private static SettingDefinition D(string config, string property, string category, string section,
        string label, string type, object? defaultValue, bool nullable = false, bool restart = false,
        bool immediate = false, bool dangerous = false, bool sensitive = false) =>
        new(config, property, category, section, label, type, defaultValue, nullable, restart,
            immediate, dangerous, true, false, sensitive);

    private static SettingFieldDto ToField(SettingDefinition d, string key, object? value,
        string status, string? error, bool mapped) => new(
        key, d.Category, d.Section, d.Label, SourceName, d.ValueType, value, d.DefaultValue,
        d.Nullable, d.RequiresRestart, d.Immediate, d.Dangerous, true, d.SafeToWrite,
        d.Sensitive, status, error, mapped);

    private static bool TryGetProperty(JsonObject source, string name, out JsonNode? value)
    {
        foreach ((string key, JsonNode? node) in source) {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                value = node;
                return true;
            }
        }
        value = null;
        return false;
    }

    private static void ReadServers(JsonArray array, List<CrawlerServerDto> target, List<string> errors)
    {
        foreach (JsonNode? item in array) {
            if (item is not JsonObject server) {
                errors.Add("服务器资源中存在无法识别的记录");
                continue;
            }
            target.Add(new CrawlerServerDto(
                Text(server, "PluginID"), Text(server, "Url"), Bool(server, "Enabled"),
                Int(server, "Available"), Text(server, "LastRefreshDate"),
                HasValue(server, "Cookies") ? "已配置（内容已隐藏）" : "未配置",
                HasValue(server, "Headers") ? "已配置（内容已隐藏）" : "未配置"));
        }
    }

    private static string Text(JsonObject source, string name) =>
        TryGetProperty(source, name, out JsonNode? node) ? node?.ToString() ?? "" : "";
    private static bool Bool(JsonObject source, string name) =>
        TryGetProperty(source, name, out JsonNode? node) && bool.TryParse(node?.ToString(), out bool value) && value;
    private static int Int(JsonObject source, string name) =>
        TryGetProperty(source, name, out JsonNode? node) && int.TryParse(node?.ToString(), out int value) ? value : 0;
    private static bool HasValue(JsonObject source, string name) => !string.IsNullOrWhiteSpace(Text(source, name));

    private static object MaskSensitive(JsonNode? node) =>
        string.IsNullOrWhiteSpace(node?.ToString()) ? "未配置" : "已配置（内容已隐藏）";

    private static bool IsSensitive(string name) =>
        name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("header", StringComparison.OrdinalIgnoreCase);

    private static string InferType(JsonNode? node) => node switch {
        JsonArray => "array",
        JsonObject => "json",
        JsonValue value when value.TryGetValue<bool>(out _) => "boolean",
        JsonValue value when value.TryGetValue<long>(out _) => "integer",
        JsonValue value when value.TryGetValue<double>(out _) => "number",
        _ => "string",
    };

    private static async Task<SettingsSourceDto> InspectSourceAsync(string path, string kind)
    {
        if (!File.Exists(path))
            return new SettingsSourceDto(kind, path, false, true, null, null, null, "missing", "文件不存在");
        try {
            var info = new FileInfo(path);
            await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            return new SettingsSourceDto(kind, path, true, true, info.Length, info.LastWriteTime,
                hash, "ok", null);
        } catch (Exception ex) {
            return new SettingsSourceDto(kind, path, true, true, null, null, null, "error", ex.Message);
        }
    }

    private static async Task<SqliteConnection> OpenReadOnlyAsync(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }
}
