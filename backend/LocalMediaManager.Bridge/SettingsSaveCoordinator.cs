using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record AppearanceSettingsDto(string ThemeMode);
public sealed record MovieWallDisplaySettingsDto(string PosterOrientation, string PosterSize);
public sealed record SystemSettingsDto(string Language, string CloseBehavior, bool StartMinimizedToTray, int LogRetentionDays,
    bool GlobalShortcutsEnabled, bool AutoCheckUpdates, string? LastUpdateCheckAt = null);

public sealed record MediaStorageSettingsDto(
    string RootPath,
    string PostersDirectory,
    string ThumbnailsDirectory,
    string FanartDirectory,
    string PreviewsDirectory,
    string ScreenshotsDirectory,
    string WallCropsDirectory,
    string GifDirectory,
    string NfoDirectory,
    string MovieFolderTemplate,
    string FileNameTemplate,
    bool UsingFallbackDefault = false);

public sealed record UnifiedSettingsDto(
    MetaTubeSettingsDto MetaTube,
    NfoSettingsDto Nfo,
    PlaybackSettingsDto Playback,
    RatingRetentionSettingsDto RatingRetention,
    AppearanceSettingsDto Appearance,
    MediaStorageSettingsDto MediaStorage,
    MovieWallDisplaySettingsDto MovieWallDisplay,
    SystemSettingsDto System);

public sealed record UnifiedSettingsSaveResult(UnifiedSettingsDto Settings, IReadOnlyList<string> ChangedFields, string Message);

public sealed class SettingsSaveCoordinator(
    string databasePath,
    string installRoot,
    MetadataProviderSettingsService metadata,
    NfoService nfo,
    PlaybackSettingsService playback,
    RatingHistoryService ratings)
{
    private static readonly Regex TemplateTokenRegex = new("\\{([^}]+)\\}", RegexOptions.Compiled);

    public async Task<UnifiedSettingsDto> ReadAsync(CancellationToken token = default)
    {
        AppearanceSettingsDto appearance = await ReadAppearanceAsync(token);
        MovieWallDisplaySettingsDto movieWallDisplay = await ReadMovieWallDisplayAsync(token);
        MediaStorageSettingsDto mediaStorage = await ReadMediaStorageAsync(token);
        SystemSettingsDto system = await ReadSystemAsync(token);
        return new(
            await metadata.ReadMetaTubeAsync(),
            await nfo.ReadSettingsAsync(token),
            await playback.ReadAsync(token),
            await ratings.ReadSettingsAsync(token),
            appearance,
            mediaStorage,
            movieWallDisplay,
            system);
    }

    public UnifiedSettingsDto DefaultSettings() => SettingsDefaults.UnifiedForEnvironment(installRoot, databasePath);
    public static UnifiedSettingsDto Defaults(string? databasePath = null, string? installRoot = null) => SettingsDefaults.UnifiedForEnvironment(installRoot, databasePath);

    public async Task<UnifiedSettingsSaveResult> SaveAsync(UnifiedSettingsDto input, bool createMissingMediaStorageRoot = false, CancellationToken token = default)
    {
        UnifiedSettingsDto clean = Normalize(input, createMissingMediaStorageRoot);
        UnifiedSettingsDto before = await ReadAsync(token);
        IReadOnlyList<string> changed = ChangedFields(before, clean);
        if (changed.Count == 0) return new(clean, [], "设置没有变化。");

        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await StoreAsync(connection, transaction, "metadata.metatube.enabled", clean.MetaTube.Enabled, "boolean", token);
        await StoreAsync(connection, transaction, "metadata.metatube.baseUrl", clean.MetaTube.BaseUrl, "string", token);
        await StoreAsync(connection, transaction, "metadata.metatube.timeoutSeconds", clean.MetaTube.TimeoutSeconds, "integer", token);
        await StoreAsync(connection, transaction, "metadata.metatube.downloadImages", clean.MetaTube.DownloadImages, "boolean", token);
        await StoreAsync(connection, transaction, "metadata.metatube.writeNfo", clean.MetaTube.WriteNfo, "boolean", token);
        await StoreAsync(connection, transaction, "metadata.metatube.autoExecute", clean.MetaTube.AutoExecute, "boolean", token);
        await StoreAsync(connection, transaction, "metadata.metatube.nonDestructive", true, "boolean", token);
        await StoreAsync(connection, transaction, "nfo.export.policy", clean.Nfo.ExportPolicy, "string", token);
        await StoreAsync(connection, transaction, "nfo.export.outputDirectory", clean.Nfo.OutputDirectory, "string", token);
        await StoreAsync(connection, transaction, "nfo.import.fillEmptyOnly", true, "boolean", token);
        await StoreAsync(connection, transaction, "nfo.export.includeImages", clean.Nfo.IncludeImages, "boolean", token);
        await StoreAsync(connection, transaction, "playback.playerPath", clean.Playback.UseSystemDefault ? "" : clean.Playback.PlayerPath, "string", token);
        await StoreAsync(connection, transaction, "ratingHistory.enabled", clean.RatingRetention.Enabled, "boolean", token);
        await StoreAsync(connection, transaction, "appearance.themeMode", clean.Appearance.ThemeMode, "string", token);
        await StoreAsync(connection, transaction, "movieWall.posterOrientation", clean.MovieWallDisplay.PosterOrientation, "string", token);
        await StoreAsync(connection, transaction, "movieWall.posterSize", clean.MovieWallDisplay.PosterSize, "string", token);
        await StoreAsync(connection, transaction, "system.language", clean.System.Language, "string", token);
        await StoreAsync(connection, transaction, "system.closeBehavior", clean.System.CloseBehavior, "string", token);
        await StoreAsync(connection, transaction, "system.startMinimizedToTray", clean.System.StartMinimizedToTray, "boolean", token);
        await StoreAsync(connection, transaction, "system.logRetentionDays", clean.System.LogRetentionDays, "integer", token);
        await StoreAsync(connection, transaction, "system.globalShortcutsEnabled", clean.System.GlobalShortcutsEnabled, "boolean", token);
        await StoreAsync(connection, transaction, "system.autoCheckUpdates", clean.System.AutoCheckUpdates, "boolean", token);
        await StoreAsync(connection, transaction, "mediaStorage.rootPath", clean.MediaStorage.RootPath, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.posters", clean.MediaStorage.PostersDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.thumbnails", clean.MediaStorage.ThumbnailsDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.fanart", clean.MediaStorage.FanartDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.previews", clean.MediaStorage.PreviewsDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.screenshots", clean.MediaStorage.ScreenshotsDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.wallCrops", clean.MediaStorage.WallCropsDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.gif", clean.MediaStorage.GifDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.nfo", clean.MediaStorage.NfoDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.template.movieFolder", clean.MediaStorage.MovieFolderTemplate, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.template.fileName", clean.MediaStorage.FileNameTemplate, "string", token);
        await transaction.CommitAsync(token);
        return new(clean, changed, "设置已保存。");
    }

    private UnifiedSettingsDto Normalize(UnifiedSettingsDto input, bool createMissingMediaStorageRoot)
    {
        if (!Uri.TryCreate(input.MetaTube.BaseUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("MetaTube 地址必须是有效的 HTTP 或 HTTPS URL。", nameof(input.MetaTube.BaseUrl));
        string nfoPolicy = input.Nfo.ExportPolicy is "SkipExisting" or "SeparateFile" ? input.Nfo.ExportPolicy : "SkipExisting";
        string nfoOutput = NormalizeDirectory(input.Nfo.OutputDirectory, "NFO 输出目录");
        string player = "";
        bool useSystemDefault = input.Playback.UseSystemDefault || string.IsNullOrWhiteSpace(input.Playback.PlayerPath);
        if (!useSystemDefault)
        {
            player = Path.GetFullPath(input.Playback.PlayerPath.Trim());
            if (!File.Exists(player) || !player.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("播放器路径必须指向现有的 Windows 可执行文件。", nameof(input.Playback.PlayerPath));
        }
        string theme = string.Equals(input.Appearance.ThemeMode, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        MovieWallDisplaySettingsDto movieWallInput = input.MovieWallDisplay ?? SettingsDefaults.Unified.MovieWallDisplay;
        string posterOrientation = string.Equals(movieWallInput.PosterOrientation, "landscape", StringComparison.OrdinalIgnoreCase) ? "landscape" : "portrait";
        string posterSize = movieWallInput.PosterSize?.ToLowerInvariant() is "small" or "large" ? movieWallInput.PosterSize.ToLowerInvariant() : "medium";
        MediaStorageSettingsDto mediaStorage = NormalizeMediaStorage(input.MediaStorage, createMissingMediaStorageRoot);
        return new(
            input.MetaTube with
            {
                BaseUrl = uri.ToString().Trim().TrimEnd('/') + "/",
                TimeoutSeconds = Math.Clamp(input.MetaTube.TimeoutSeconds, 15, 180),
                NonDestructive = true,
            },
            new(nfoPolicy, nfoOutput, true, input.Nfo.IncludeImages),
            new(player, useSystemDefault),
            new(input.RatingRetention.Enabled),
            new(theme),
            mediaStorage,
            new(posterOrientation, posterSize),
            NormalizeSystem(input.System));
    }

    private static string NormalizeDirectory(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string path = Path.GetFullPath(value.Trim());
        string? parent = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new ArgumentException($"{label}的上级目录不存在，无法创建或写入。", label);
        return path;
    }

    private async Task<AppearanceSettingsDto> ReadAppearanceAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        string? raw = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='appearance.themeMode'", token);
        string theme = SettingsDefaults.Unified.Appearance.ThemeMode;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try { theme = JsonSerializer.Deserialize<string>(raw) ?? theme; }
            catch (JsonException) { }
        }
        return new(string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark");
    }

    private async Task<MovieWallDisplaySettingsDto> ReadMovieWallDisplayAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        string orientation = SettingsDefaults.Unified.MovieWallDisplay.PosterOrientation;
        string size = SettingsDefaults.Unified.MovieWallDisplay.PosterSize;
        string? rawOrientation = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='movieWall.posterOrientation'", token);
        string? rawSize = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='movieWall.posterSize'", token);
        if (!string.IsNullOrWhiteSpace(rawOrientation))
        {
            try { orientation = JsonSerializer.Deserialize<string>(rawOrientation) ?? orientation; }
            catch (JsonException) { }
        }
        if (!string.IsNullOrWhiteSpace(rawSize))
        {
            try { size = JsonSerializer.Deserialize<string>(rawSize) ?? size; }
            catch (JsonException) { }
        }
        orientation = string.Equals(orientation, "landscape", StringComparison.OrdinalIgnoreCase) ? "landscape" : "portrait";
        size = size.ToLowerInvariant() is "small" or "large" ? size.ToLowerInvariant() : "medium";
        return new(orientation, size);
    }

    private async Task<MediaStorageSettingsDto> ReadMediaStorageAsync(CancellationToken token)
    {
        MediaStorageSettingsDto defaults = SettingsDefaults.MediaStorageForEnvironment(installRoot, databasePath);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE 'mediaStorage.%'";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync()) values[reader.GetString(0)] = reader.GetString(1);
        string root = TextSetting(values, "mediaStorage.rootPath", "");
        bool usesRuntimeDefault = string.IsNullOrWhiteSpace(root);
        if (usesRuntimeDefault) root = defaults.RootPath;
        return new(
            root,
            TextSetting(values, "mediaStorage.directory.posters", defaults.PostersDirectory),
            TextSetting(values, "mediaStorage.directory.thumbnails", defaults.ThumbnailsDirectory),
            TextSetting(values, "mediaStorage.directory.fanart", defaults.FanartDirectory),
            TextSetting(values, "mediaStorage.directory.previews", defaults.PreviewsDirectory),
            TextSetting(values, "mediaStorage.directory.screenshots", defaults.ScreenshotsDirectory),
            TextSetting(values, "mediaStorage.directory.wallCrops", defaults.WallCropsDirectory),
            TextSetting(values, "mediaStorage.directory.gif", defaults.GifDirectory),
            TextSetting(values, "mediaStorage.directory.nfo", defaults.NfoDirectory),
            TextSetting(values, "mediaStorage.template.movieFolder", defaults.MovieFolderTemplate),
            TextSetting(values, "mediaStorage.template.fileName", defaults.FileNameTemplate),
            usesRuntimeDefault && defaults.UsingFallbackDefault);
    }

    private MediaStorageSettingsDto NormalizeMediaStorage(MediaStorageSettingsDto input, bool createMissingRoot)
    {
        string root = NormalizeMediaStorageRoot(input.RootPath, createMissingRoot);
        string[] directories = [
            NormalizeRelativeDirectory(input.WallCropsDirectory, "Wall crop directory"),
            NormalizeRelativeDirectory(input.PostersDirectory, "海报目录"),
            NormalizeRelativeDirectory(input.ThumbnailsDirectory, "缩略图目录"),
            NormalizeRelativeDirectory(input.FanartDirectory, "背景图目录"),
            NormalizeRelativeDirectory(input.PreviewsDirectory, "预览图目录"),
            NormalizeRelativeDirectory(input.ScreenshotsDirectory, "截图目录"),
            NormalizeRelativeDirectory(input.GifDirectory, "GIF 目录"),
            NormalizeRelativeDirectory(input.NfoDirectory, "NFO 目录"),
        ];
        if (directories.Distinct(StringComparer.OrdinalIgnoreCase).Count() != directories.Length)
            throw new ArgumentException("媒体存储资源目录不能重复。", nameof(input.PostersDirectory));
        string movieFolder = NormalizeTemplate(input.MovieFolderTemplate, "影片资源文件夹规则");
        string fileName = NormalizeTemplate(input.FileNameTemplate, "文件名规则");
        return new(root, directories[1], directories[2], directories[3], directories[4], directories[5], directories[0], directories[6], directories[7], movieFolder, fileName);
    }

    private static string NormalizeMediaStorageRoot(string value, bool createMissingRoot)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("媒体存储根目录不能为空。", nameof(value));
        string root;
        try { root = Path.GetFullPath(value.Trim()); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) {
            throw new ArgumentException("媒体存储根目录必须是合法 Windows 路径。", nameof(value));
        }
        if (!Path.IsPathRooted(root)) throw new ArgumentException("媒体存储根目录必须是绝对路径。", nameof(value));
        if (ContainsInvalidWindowsPathSegment(root)) throw new ArgumentException("媒体存储根目录必须是合法 Windows 路径。", nameof(value));
        if (File.Exists(root)) throw new ArgumentException("媒体存储根目录不能指向单个文件。", nameof(value));
        if (IsBlockedApplicationPath(root)) throw new ArgumentException("媒体存储根目录不能位于程序安装目录、resources 或 Web assets 内。", nameof(value));
        if (!Directory.Exists(root))
        {
            if (!createMissingRoot) throw new ArgumentException("媒体存储根目录不存在，需要确认创建。", nameof(value));
            Directory.CreateDirectory(root);
        }
        VerifyWritable(root);
        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeRelativeDirectory(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{label}不能为空。", label);
        string clean = value.Trim();
        if (Path.IsPathRooted(clean) || clean.Contains(':') || clean.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"{label}只能填写相对目录名，不能包含绝对路径、盘符或 ..。", label);
        if (clean.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || clean.Contains('/') || clean.Contains('\\'))
            throw new ArgumentException($"{label}包含非法 Windows 文件名字符。", label);
        if (IsReservedDeviceName(clean)) throw new ArgumentException($"{label}不能使用 Windows 保留设备名。", label);
        return clean;
    }

    private static string NormalizeTemplate(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{label}不能为空。", label);
        string clean = value.Trim();
        if (!clean.Contains("{MovieCode}", StringComparison.Ordinal) && !clean.Contains("{MovieTitle}", StringComparison.Ordinal))
            throw new ArgumentException($"{label}必须包含 {{MovieCode}} 或 {{MovieTitle}}。", label);
        foreach (Match match in TemplateTokenRegex.Matches(clean))
        {
            string token = match.Groups[1].Value;
            if (token is not ("MovieCode" or "MovieTitle")) throw new ArgumentException($"{label}包含不支持的变量：{{{token}}}。", label);
        }
        string sample = clean.Replace("{MovieCode}", "ABC-123", StringComparison.Ordinal).Replace("{MovieTitle}", "Example Movie", StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(sample) || IsReservedDeviceName(sample)) throw new ArgumentException($"{label}会生成空名称或 Windows 保留设备名。", label);
        return clean;
    }

    private static void VerifyWritable(string root)
    {
        string probe = Path.Combine(root, $".lmm-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "ok");
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw new ArgumentException("媒体存储根目录不可写，设置没有保存。", nameof(root));
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
        }
    }

    private static bool IsBlockedApplicationPath(string root)
    {
        string appBase = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (IsSameOrChild(root, appBase)) return true;
        string[] segments = root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment.Equals("resources", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("dist", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("assets", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsInvalidWindowsPathSegment(string path)
    {
        string root = Path.GetPathRoot(path) ?? "";
        string rest = path[root.Length..];
        return rest.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(segment => segment.Length > 0)
            .Any(segment => segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsReservedDeviceName(segment));
    }

    private static bool IsSameOrChild(string child, string parent)
    {
        string childFull = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return childFull.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || childFull.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReservedDeviceName(string value)
    {
        string name = Path.GetFileNameWithoutExtension(value.Trim());
        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && name[3] >= '1' && name[3] <= '9')
            return true;
        return false;
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
        }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }

    private static async Task StoreAsync(SqliteConnection connection, SqliteTransaction transaction, string key, object value, string type, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt)
            VALUES($key,$value,$type,$at)
            ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType=excluded.ValueType,UpdatedAt=excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value));
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync(token))?.ToString();
    }

    private static IReadOnlyList<string> ChangedFields(UnifiedSettingsDto before, UnifiedSettingsDto after)
    {
        var changed = new List<string>();
        if (before.MetaTube != after.MetaTube) changed.Add("metaTube");
        if (before.Nfo != after.Nfo) changed.Add("nfo");
        if (before.Playback != after.Playback) changed.Add("playback");
        if (before.RatingRetention != after.RatingRetention) changed.Add("ratingRetention");
        if (before.Appearance != after.Appearance) changed.Add("appearance");
        if (before.MediaStorage != after.MediaStorage) changed.Add("mediaStorage");
        if (before.MovieWallDisplay != after.MovieWallDisplay) changed.Add("movieWallDisplay");
        if (before.System != after.System) changed.Add("system");
        return changed;
    }

    private static string TextSetting(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        if (!values.TryGetValue(key, out string? raw)) return fallback;
        try { return JsonSerializer.Deserialize<string>(raw) ?? fallback; }
        catch (JsonException) { return fallback; }
    }

    private async Task<SystemSettingsDto> ReadSystemAsync(CancellationToken token)
    {
        SystemSettingsDto defaults = SettingsDefaults.Unified.System;
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE 'system.%' OR Key LIKE 'updates.%'";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) values[reader.GetString(0)] = reader.GetString(1);
        return NormalizeSystem(new(
            TextSetting(values, "system.language", defaults.Language),
            TextSetting(values, "system.closeBehavior", defaults.CloseBehavior),
            BoolSetting(values, "system.startMinimizedToTray", defaults.StartMinimizedToTray),
            IntSetting(values, "system.logRetentionDays", defaults.LogRetentionDays),
            BoolSetting(values, "system.globalShortcutsEnabled", defaults.GlobalShortcutsEnabled),
            BoolSetting(values, "system.autoCheckUpdates", defaults.AutoCheckUpdates),
            TextSetting(values, "updates.lastCheckedAt", defaults.LastUpdateCheckAt ?? "")));
    }

    private static SystemSettingsDto NormalizeSystem(SystemSettingsDto? input)
    {
        SystemSettingsDto defaults = SettingsDefaults.Unified.System;
        if (input is null) return defaults;
        string language = input.Language is "system" or "zh-CN" ? input.Language : defaults.Language;
        string closeBehavior = input.CloseBehavior is "exit" or "minimizeToTray" ? input.CloseBehavior : defaults.CloseBehavior;
        return new(language, closeBehavior, input.StartMinimizedToTray,
            LogMaintenanceService.NormalizeRetentionDays(input.LogRetentionDays),
            input.GlobalShortcutsEnabled, input.AutoCheckUpdates,
            string.IsNullOrWhiteSpace(input.LastUpdateCheckAt) ? null : input.LastUpdateCheckAt);
    }

    private static bool BoolSetting(IReadOnlyDictionary<string, string> values, string key, bool fallback)
    {
        if (!values.TryGetValue(key, out string? raw)) return fallback;
        try { return JsonSerializer.Deserialize<bool>(raw); }
        catch (JsonException) { return fallback; }
    }

    private static int IntSetting(IReadOnlyDictionary<string, string> values, string key, int fallback)
    {
        if (!values.TryGetValue(key, out string? raw)) return fallback;
        try { return JsonSerializer.Deserialize<int>(raw); }
        catch (JsonException) { return fallback; }
    }
}

public static class SettingsDefaults
{
    public static UnifiedSettingsDto Unified => UnifiedForEnvironment(null, null);

    public static UnifiedSettingsDto UnifiedForEnvironment(string? installRoot, string? databasePath) => new(
        new(true, "http://127.0.0.1:8080/", 30, true, false, true, true),
        new("SkipExisting", "", true, true),
        new("", true),
        new(true),
        new("dark"),
        MediaStorageForEnvironment(installRoot, databasePath),
        new("portrait", "medium"),
        new("system", "exit", false, 30, true, false, null));

    public static MediaStorageSettingsDto MediaStorageForEnvironment(
        string? installRoot,
        string? databasePath,
        string? documentsRoot = null,
        Func<string, bool>? canUseBesideDataRoot = null)
    {
        MediaStorageDefaultRoot root = ResolveMediaStorageDefaultRoot(installRoot, databasePath, documentsRoot, canUseBesideDataRoot);
        return new(
            root.RootPath,
            "Posters",
            "Thumbnails",
            "Fanart",
            "Previews",
            "Screenshots",
            "WallCrops",
            "GIF",
            "NFO",
            "{MovieCode}",
            "{MovieCode}",
            root.UsingFallback);
    }

    private static MediaStorageDefaultRoot ResolveMediaStorageDefaultRoot(
        string? installRoot,
        string? databasePath,
        string? documentsRoot,
        Func<string, bool>? canUseBesideDataRoot)
    {
        string? cleanInstallRoot = NormalizeOptionalFullPath(installRoot);
        if (!string.IsNullOrWhiteSpace(cleanInstallRoot))
        {
            string dataRoot = cleanInstallRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + " Data";
            bool protectedInstall = IsProtectedInstallRoot(cleanInstallRoot);
            bool canUseBeside = !protectedInstall && (canUseBesideDataRoot ?? CanUseBesideDataRoot)(dataRoot);
            if (canUseBeside)
            {
                return new(Path.Combine(dataRoot, "MediaStorage"), false);
            }

            Console.Error.WriteLine("Media storage default path fallback: install root is protected or beside data root is not writable.");
        }
        else if (!string.IsNullOrWhiteSpace(databasePath))
        {
            string? dataRoot = DataRootFromDatabasePath(databasePath);
            if (!string.IsNullOrWhiteSpace(dataRoot))
            {
                return new(Path.Combine(dataRoot, "MediaStorage"), false);
            }
        }

        string docs = string.IsNullOrWhiteSpace(documentsRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : documentsRoot;
        if (string.IsNullOrWhiteSpace(docs))
        {
            docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        return new(Path.Combine(docs, "Local Media Manager", "MediaStorage"), true);
    }

    private static string? NormalizeOptionalFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path.Trim()); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static string? DataRootFromDatabasePath(string? databasePath)
    {
        string? databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath ?? ""));
        if (string.IsNullOrWhiteSpace(databaseDirectory)) return null;
        return string.Equals(Path.GetFileName(databaseDirectory), "data", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(databaseDirectory)
            : databaseDirectory;
    }

    private static bool CanUseBesideDataRoot(string dataRoot)
    {
        try
        {
            string fullDataRoot = Path.GetFullPath(dataRoot);
            if (Directory.Exists(fullDataRoot))
            {
                string probe = Path.Combine(fullDataRoot, $".lmm-write-test-{Guid.NewGuid():N}.tmp");
                try
                {
                    File.WriteAllText(probe, "ok");
                    return true;
                }
                finally
                {
                    try { if (File.Exists(probe)) File.Delete(probe); } catch { }
                }
            }

            string? parent = Path.GetDirectoryName(fullDataRoot);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) return false;
            string probeDirectory = Path.Combine(parent, $".lmm-media-storage-probe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(probeDirectory);
            Directory.Delete(probeDirectory);
            return true;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsProtectedInstallRoot(string installRoot)
    {
        string root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return IsSameOrChild(root, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
            || IsSameOrChild(root, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86))
            || IsSameOrChild(root, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
    }

    private static bool IsSameOrChild(string child, string? parent)
    {
        if (string.IsNullOrWhiteSpace(parent)) return false;
        string childFull = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parentFull = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return childFull.Equals(parentFull, StringComparison.OrdinalIgnoreCase)
            || childFull.StartsWith(parentFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record MediaStorageDefaultRoot(string RootPath, bool UsingFallback);
}
