using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record AppearanceSettingsDto(string ThemeMode);

public sealed record MediaStorageSettingsDto(
    string RootPath,
    string PostersDirectory,
    string ThumbnailsDirectory,
    string FanartDirectory,
    string PreviewsDirectory,
    string ScreenshotsDirectory,
    string GifDirectory,
    string NfoDirectory,
    string MovieFolderTemplate,
    string FileNameTemplate);

public sealed record UnifiedSettingsDto(
    MetaTubeSettingsDto MetaTube,
    NfoSettingsDto Nfo,
    PlaybackSettingsDto Playback,
    RatingRetentionSettingsDto RatingRetention,
    AppearanceSettingsDto Appearance,
    MediaStorageSettingsDto MediaStorage);

public sealed record UnifiedSettingsSaveResult(UnifiedSettingsDto Settings, IReadOnlyList<string> ChangedFields, string Message);

public sealed class SettingsSaveCoordinator(
    string databasePath,
    MetadataProviderSettingsService metadata,
    NfoService nfo,
    PlaybackSettingsService playback,
    RatingHistoryService ratings)
{
    private static readonly Regex TemplateTokenRegex = new("\\{([^}]+)\\}", RegexOptions.Compiled);

    public async Task<UnifiedSettingsDto> ReadAsync(CancellationToken token = default)
    {
        AppearanceSettingsDto appearance = await ReadAppearanceAsync(token);
        MediaStorageSettingsDto mediaStorage = await ReadMediaStorageAsync(token);
        return new(
            await metadata.ReadMetaTubeAsync(),
            await nfo.ReadSettingsAsync(token),
            await playback.ReadAsync(token),
            await ratings.ReadSettingsAsync(token),
            appearance,
            mediaStorage);
    }

    public UnifiedSettingsDto DefaultSettings() => SettingsDefaults.UnifiedForDatabase(databasePath);
    public static UnifiedSettingsDto Defaults(string? databasePath = null) => SettingsDefaults.UnifiedForDatabase(databasePath);

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
        await StoreAsync(connection, transaction, "mediaStorage.rootPath", clean.MediaStorage.RootPath, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.posters", clean.MediaStorage.PostersDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.thumbnails", clean.MediaStorage.ThumbnailsDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.fanart", clean.MediaStorage.FanartDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.previews", clean.MediaStorage.PreviewsDirectory, "string", token);
        await StoreAsync(connection, transaction, "mediaStorage.directory.screenshots", clean.MediaStorage.ScreenshotsDirectory, "string", token);
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
            mediaStorage);
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

    private async Task<MediaStorageSettingsDto> ReadMediaStorageAsync(CancellationToken token)
    {
        MediaStorageSettingsDto defaults = SettingsDefaults.MediaStorageForDatabase(databasePath);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE 'mediaStorage.%'";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync()) values[reader.GetString(0)] = reader.GetString(1);
        string root = TextSetting(values, "mediaStorage.rootPath", "");
        if (string.IsNullOrWhiteSpace(root)) root = defaults.RootPath;
        return new(
            root,
            TextSetting(values, "mediaStorage.directory.posters", defaults.PostersDirectory),
            TextSetting(values, "mediaStorage.directory.thumbnails", defaults.ThumbnailsDirectory),
            TextSetting(values, "mediaStorage.directory.fanart", defaults.FanartDirectory),
            TextSetting(values, "mediaStorage.directory.previews", defaults.PreviewsDirectory),
            TextSetting(values, "mediaStorage.directory.screenshots", defaults.ScreenshotsDirectory),
            TextSetting(values, "mediaStorage.directory.gif", defaults.GifDirectory),
            TextSetting(values, "mediaStorage.directory.nfo", defaults.NfoDirectory),
            TextSetting(values, "mediaStorage.template.movieFolder", defaults.MovieFolderTemplate),
            TextSetting(values, "mediaStorage.template.fileName", defaults.FileNameTemplate));
    }

    private MediaStorageSettingsDto NormalizeMediaStorage(MediaStorageSettingsDto input, bool createMissingRoot)
    {
        string root = NormalizeMediaStorageRoot(input.RootPath, createMissingRoot);
        string[] directories = [
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
        return new(root, directories[0], directories[1], directories[2], directories[3], directories[4], directories[5], directories[6], movieFolder, fileName);
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
        return changed;
    }

    private static string TextSetting(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        if (!values.TryGetValue(key, out string? raw)) return fallback;
        try { return JsonSerializer.Deserialize<string>(raw) ?? fallback; }
        catch (JsonException) { return fallback; }
    }
}

public static class SettingsDefaults
{
    public static UnifiedSettingsDto Unified => UnifiedForDatabase(null);

    public static UnifiedSettingsDto UnifiedForDatabase(string? databasePath) => new(
        new(true, "http://127.0.0.1:8080/", 30, true, false, true, true),
        new("SkipExisting", "", true, true),
        new("", true),
        new(true),
        new("dark"),
        MediaStorageForDatabase(databasePath));

    public static MediaStorageSettingsDto MediaStorageForDatabase(string? databasePath)
    {
        string dataRoot = DefaultDataRoot(databasePath);
        return new(
            Path.Combine(dataRoot, "MediaStorage"),
            "Posters",
            "Thumbnails",
            "Fanart",
            "Previews",
            "Screenshots",
            "GIF",
            "NFO",
            "{MovieCode}",
            "{MovieCode}");
    }

    private static string DefaultDataRoot(string? databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) return @"D:\Local Media Manager Next Data";
        string databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? @"D:\Local Media Manager Next Data";
        return string.Equals(Path.GetFileName(databaseDirectory), "data", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(databaseDirectory) ?? databaseDirectory
            : databaseDirectory;
    }
}
