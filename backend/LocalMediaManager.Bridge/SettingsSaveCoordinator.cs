using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record AppearanceSettingsDto(string ThemeMode);
public sealed record MovieWallDisplaySettingsDto(string PosterOrientation, string PosterSize,
    string WallImageSource = "poster", string DetailImageSource = "fanart", string DefaultViewMode = "grid", string CoverCropMode = "AutoFace");
public sealed record ScanSettingsDto(double MinFileSizeMb);
public sealed record SearchSettingsDto(string DefaultSort, string DefaultFilter);
public sealed record DataBackupSettingsDto(bool Enabled, int FrequencyDays, int RetentionCount);
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
    MdcNgSettingsDto MdcNg,
    MetaTubeSettingsDto MetaTube,
    JavBusSettingsDto JavBus,
    NfoSettingsDto Nfo,
    PlaybackSettingsDto Playback,
    RatingRetentionSettingsDto RatingRetention,
    AppearanceSettingsDto Appearance,
    MediaStorageSettingsDto MediaStorage,
    MovieWallDisplaySettingsDto MovieWallDisplay,
    SearchSettingsDto Search,
    DataBackupSettingsDto DataBackup,
    ScanSettingsDto Scan,
    SystemSettingsDto System,
    WebMetadataSettingsDto? Dmm = null,
    WebMetadataSettingsDto? JavDb = null,
    WebMetadataSettingsDto? Minnano = null,
    WebMetadataSettingsDto? WikipediaJp = null,
    ProviderNetworkSettingsDto? ProviderNetwork = null,
    RenameSettingsDto? Rename = null);

public sealed record UnifiedSettingsSaveResult(UnifiedSettingsDto Settings, IReadOnlyList<string> ChangedFields, string Message);

public sealed class SettingsSaveCoordinator(
    string databasePath,
    string installRoot,
    string legacyConfigDatabasePath,
    MetadataProviderSettingsService metadata,
    NfoService nfo,
    PlaybackSettingsService playback,
    RatingHistoryService ratings)
{
    private static readonly Regex TemplateTokenRegex = new("\\{([^}]+)\\}", RegexOptions.Compiled);

    public async Task<UnifiedSettingsDto> ReadAsync(CancellationToken token = default)
    {
        await MigrateLegacySettingsOnceAsync(token);
        AppearanceSettingsDto appearance = await ReadAppearanceAsync(token);
        MovieWallDisplaySettingsDto movieWallDisplay = await ReadMovieWallDisplayAsync(token);
        MediaStorageSettingsDto mediaStorage = await ReadMediaStorageAsync(token);
        SearchSettingsDto search = await ReadSearchAsync(token);
        DataBackupSettingsDto dataBackup = await ReadDataBackupAsync(token);
        ScanSettingsDto scan = await ReadScanAsync(token);
        SystemSettingsDto system = await ReadSystemAsync(token);
        return new(
            await metadata.ReadMdcNgAsync(),
            await metadata.ReadMetaTubeAsync(),
            await metadata.ReadJavBusAsync(),
            await nfo.ReadSettingsAsync(token),
            await playback.ReadAsync(token),
            await ratings.ReadSettingsAsync(token),
            appearance,
            mediaStorage,
            movieWallDisplay,
            search,
            dataBackup,
            scan,
            system,
            await metadata.ReadDmmAsync(),
            await metadata.ReadJavDbAsync(),
            await metadata.ReadMinnanoAsync(),
            await metadata.ReadWikipediaJpAsync(),
            await metadata.ReadNetworkAsync(),
            await ReadRenameAsync(token));
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
        await MetadataProviderSettingsService.StoreMdcNgAsync(connection, transaction, clean.MdcNg);
        await MetadataProviderSettingsService.StoreJavBusAsync(connection, transaction, clean.JavBus);
        await MetadataProviderSettingsService.StoreWebAsync(connection, transaction, "dmm", clean.Dmm!);
        await MetadataProviderSettingsService.StoreWebAsync(connection, transaction, "javdb", clean.JavDb!);
        await MetadataProviderSettingsService.StoreWebAsync(connection, transaction, "minnano", clean.Minnano!);
        await MetadataProviderSettingsService.StoreWebAsync(connection, transaction, "wikipediaJp", clean.WikipediaJp!);
        await MetadataProviderSettingsService.StoreNetworkAsync(connection, transaction, clean.ProviderNetwork ?? ProviderNetworkSettingsDto.Default);
        await StoreAsync(connection, transaction, "nfo.export.policy", clean.Nfo.ExportPolicy, "string", token);
        await StoreAsync(connection, transaction, "nfo.export.outputDirectory", clean.Nfo.OutputDirectory, "string", token);
        await StoreAsync(connection, transaction, "nfo.import.fillEmptyOnly", true, "boolean", token);
        await StoreAsync(connection, transaction, "nfo.export.includeImages", clean.Nfo.IncludeImages, "boolean", token);
        await StoreAsync(connection, transaction, "playback.playerPath", clean.Playback.UseSystemDefault ? "" : clean.Playback.PlayerPath, "string", token);
        await StoreAsync(connection, transaction, "ratingHistory.enabled", clean.RatingRetention.Enabled, "boolean", token);
        await StoreAsync(connection, transaction, "appearance.themeMode", clean.Appearance.ThemeMode, "string", token);
        await StoreAsync(connection, transaction, "movieWall.posterOrientation", clean.MovieWallDisplay.PosterOrientation, "string", token);
        await StoreAsync(connection, transaction, "movieWall.posterSize", clean.MovieWallDisplay.PosterSize, "string", token);
        await StoreAsync(connection, transaction, "movieWall.wallImageSource", clean.MovieWallDisplay.WallImageSource, "string", token);
        await StoreAsync(connection, transaction, "movieWall.detailImageSource", clean.MovieWallDisplay.DetailImageSource, "string", token);
        await StoreAsync(connection, transaction, "movieWall.defaultViewMode", clean.MovieWallDisplay.DefaultViewMode, "string", token);
        await StoreAsync(connection, transaction, "movieWall.coverCropMode", clean.MovieWallDisplay.CoverCropMode, "string", token);
        await StoreAsync(connection, transaction, "search.defaultSort", clean.Search.DefaultSort, "string", token);
        await StoreAsync(connection, transaction, "search.defaultFilter", clean.Search.DefaultFilter, "string", token);
        await StoreAsync(connection, transaction, "dataBackup.enabled", clean.DataBackup.Enabled, "boolean", token);
        await StoreAsync(connection, transaction, "dataBackup.frequencyDays", clean.DataBackup.FrequencyDays, "integer", token);
        await StoreAsync(connection, transaction, "dataBackup.retentionCount", clean.DataBackup.RetentionCount, "integer", token);
        await StoreAsync(connection, transaction, "scan.minFileSizeMb", clean.Scan.MinFileSizeMb, "number", token);
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
        await StoreAsync(connection, transaction, "rename.settings", clean.Rename!, "json", token);
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
            MetadataProviderSettingsService.NormalizeMdcNg(input.MdcNg ?? SettingsDefaults.MdcNg),
            input.MetaTube with
            {
                BaseUrl = uri.ToString().Trim().TrimEnd('/') + "/",
                TimeoutSeconds = Math.Clamp(input.MetaTube.TimeoutSeconds, 15, 180),
                WriteNfo = true,
                NonDestructive = true,
            },
            MetadataProviderSettingsService.NormalizeJavBus(input.JavBus ?? SettingsDefaults.JavBus),
            new(nfoPolicy, nfoOutput, true, true),
            new(player, useSystemDefault),
            new(input.RatingRetention.Enabled),
            new(theme),
            mediaStorage,
            NormalizeMovieWallDisplay(new(posterOrientation, posterSize, movieWallInput.WallImageSource, movieWallInput.DetailImageSource, movieWallInput.DefaultViewMode, movieWallInput.CoverCropMode)),
            NormalizeSearch(input.Search),
            NormalizeDataBackup(input.DataBackup),
            NormalizeScan(input.Scan),
            NormalizeSystem(input.System),
            MetadataProviderSettingsService.NormalizeWeb(input.Dmm ?? SettingsDefaults.Dmm, "DMM"),
            MetadataProviderSettingsService.NormalizeWeb(input.JavDb ?? SettingsDefaults.JavDb, "JavDB"),
            MetadataProviderSettingsService.NormalizeWeb(input.Minnano ?? SettingsDefaults.Minnano, "Minnano"),
            MetadataProviderSettingsService.NormalizeWeb(input.WikipediaJp ?? SettingsDefaults.WikipediaJp, "Wikipedia JP"),
            MetadataProviderSettingsService.NormalizeNetwork(input.ProviderNetwork ?? ProviderNetworkSettingsDto.Default),
            RenameSettingsService.Normalize(input.Rename ?? RenameSettingsDto.Default));
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
        string wallImageSource = SettingsDefaults.Unified.MovieWallDisplay.WallImageSource;
        string detailImageSource = SettingsDefaults.Unified.MovieWallDisplay.DetailImageSource;
        string defaultViewMode = SettingsDefaults.Unified.MovieWallDisplay.DefaultViewMode;
        string cropMode = SettingsDefaults.Unified.MovieWallDisplay.CoverCropMode;
        string? rawOrientation = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='movieWall.posterOrientation'", token);
        string? rawSize = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='movieWall.posterSize'", token);
        string? rawWallImageSource = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='movieWall.wallImageSource'", token);
        string? rawDetailImageSource = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='movieWall.detailImageSource'", token);
        string? rawDefaultViewMode = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='movieWall.defaultViewMode'", token);
        string? rawCropMode = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='movieWall.coverCropMode'", token);
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
        if (!string.IsNullOrWhiteSpace(rawWallImageSource))
        {
            try { wallImageSource = JsonSerializer.Deserialize<string>(rawWallImageSource) ?? wallImageSource; }
            catch (JsonException) { }
        }
        if (!string.IsNullOrWhiteSpace(rawDetailImageSource))
        {
            try { detailImageSource = JsonSerializer.Deserialize<string>(rawDetailImageSource) ?? detailImageSource; }
            catch (JsonException) { }
        }
        if (!string.IsNullOrWhiteSpace(rawDefaultViewMode))
        {
            try { defaultViewMode = JsonSerializer.Deserialize<string>(rawDefaultViewMode) ?? defaultViewMode; }
            catch (JsonException) { }
        }
        if (!string.IsNullOrWhiteSpace(rawCropMode)) { try { cropMode = JsonSerializer.Deserialize<string>(rawCropMode) ?? cropMode; } catch (JsonException) { } }
        return NormalizeMovieWallDisplay(new(orientation, size, wallImageSource, detailImageSource, defaultViewMode, cropMode));
    }

    private async Task<SearchSettingsDto> ReadSearchAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        string defaultSort = SettingsDefaults.Unified.Search.DefaultSort;
        string defaultFilter = SettingsDefaults.Unified.Search.DefaultFilter;
        string? rawSort = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='search.defaultSort'", token);
        string? rawFilter = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='search.defaultFilter'", token);
        if (!string.IsNullOrWhiteSpace(rawSort))
        {
            try { defaultSort = JsonSerializer.Deserialize<string>(rawSort) ?? defaultSort; }
            catch (JsonException) { }
        }
        if (!string.IsNullOrWhiteSpace(rawFilter))
        {
            try { defaultFilter = JsonSerializer.Deserialize<string>(rawFilter) ?? defaultFilter; }
            catch (JsonException) { }
        }
        return NormalizeSearch(new(defaultSort, defaultFilter));
    }

    private async Task<DataBackupSettingsDto> ReadDataBackupAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        DataBackupSettingsDto defaults = SettingsDefaults.Unified.DataBackup;
        bool enabled = defaults.Enabled;
        int frequencyDays = defaults.FrequencyDays;
        int retentionCount = defaults.RetentionCount;
        string? rawEnabled = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='dataBackup.enabled'", token);
        string? rawFrequency = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='dataBackup.frequencyDays'", token);
        string? rawRetention = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='dataBackup.retentionCount'", token);
        if (!string.IsNullOrWhiteSpace(rawEnabled))
        {
            try { enabled = JsonSerializer.Deserialize<bool>(rawEnabled); }
            catch (JsonException) { }
        }
        if (!string.IsNullOrWhiteSpace(rawFrequency))
        {
            try { frequencyDays = JsonSerializer.Deserialize<int>(rawFrequency); }
            catch (JsonException) { }
        }
        if (!string.IsNullOrWhiteSpace(rawRetention))
        {
            try { retentionCount = JsonSerializer.Deserialize<int>(rawRetention); }
            catch (JsonException) { }
        }
        return NormalizeDataBackup(new(enabled, frequencyDays, retentionCount));
    }

    private async Task<ScanSettingsDto> ReadScanAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        double fallback = SettingsDefaults.Unified.Scan.MinFileSizeMb;
        string? raw = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='scan.minFileSizeMb'", token);
        if (!string.IsNullOrWhiteSpace(raw)) {
            try { return NormalizeScan(new(JsonSerializer.Deserialize<double>(raw))); }
            catch (JsonException) { }
        }
        return new(fallback);
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

    private static MovieWallDisplaySettingsDto NormalizeMovieWallDisplay(MovieWallDisplaySettingsDto? input)
    {
        input ??= SettingsDefaults.Unified.MovieWallDisplay;
        string orientation = string.Equals(input.PosterOrientation, "landscape", StringComparison.OrdinalIgnoreCase) ? "landscape" : "portrait";
        string size = input.PosterSize?.ToLowerInvariant() is "small" or "large" ? input.PosterSize.ToLowerInvariant() : "medium";
        string wallSource = NormalizeImageSource(input.WallImageSource, "poster");
        string detailSource = NormalizeImageSource(input.DetailImageSource, "fanart");
        string viewMode = string.Equals(input.DefaultViewMode, "list", StringComparison.OrdinalIgnoreCase) ? "list" : "grid";
        string cropMode = input.CoverCropMode is "Left" or "Center" or "Right" ? input.CoverCropMode : "AutoFace";
        return new(orientation, size, wallSource, detailSource, viewMode, cropMode);
    }

    private static SearchSettingsDto NormalizeSearch(SearchSettingsDto? input)
    {
        input ??= SettingsDefaults.Unified.Search;
        string sort = input.DefaultSort?.ToLowerInvariant() switch {
            "code" or "title" or "release" or "rating" or "oldest" => input.DefaultSort.ToLowerInvariant(),
            _ => "newest"
        };
        return new(sort, "all");
    }

    private static DataBackupSettingsDto NormalizeDataBackup(DataBackupSettingsDto? input)
    {
        input ??= SettingsDefaults.Unified.DataBackup;
        int frequency = input.FrequencyDays is 1 or 7 ? input.FrequencyDays : 3;
        int retention = input.RetentionCount is 5 or 20 ? input.RetentionCount : 10;
        return new(input.Enabled, frequency, retention);
    }

    private static string NormalizeImageSource(string? value, string fallback) => value?.ToLowerInvariant() switch {
        "poster" or "thumbnail" or "fanart" => value.ToLowerInvariant(),
        _ => fallback
    };

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

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token, params (string Name, object? Value)[] values)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (await command.ExecuteScalarAsync(token))?.ToString();
    }

    private static IReadOnlyList<string> ChangedFields(UnifiedSettingsDto before, UnifiedSettingsDto after)
    {
        var changed = new List<string>();
        if (before.MdcNg != after.MdcNg) changed.Add("mdcNg");
        if (before.MetaTube != after.MetaTube) changed.Add("metaTube");
        if (JavBusChanged(before.JavBus, after.JavBus)) changed.Add("javBus");
        if (WebProviderChanged(before.Dmm, after.Dmm)) changed.Add("dmm");
        if (WebProviderChanged(before.JavDb, after.JavDb)) changed.Add("javDb");
        if (WebProviderChanged(before.Minnano, after.Minnano)) changed.Add("minnano");
        if (WebProviderChanged(before.WikipediaJp, after.WikipediaJp)) changed.Add("wikipediaJp");
        if (before.ProviderNetwork != after.ProviderNetwork) changed.Add("providerNetwork");
        if (before.Nfo != after.Nfo) changed.Add("nfo");
        if (before.Playback != after.Playback) changed.Add("playback");
        if (before.RatingRetention != after.RatingRetention) changed.Add("ratingRetention");
        if (before.Appearance != after.Appearance) changed.Add("appearance");
        if (before.MediaStorage != after.MediaStorage) changed.Add("mediaStorage");
        if (before.MovieWallDisplay != after.MovieWallDisplay) changed.Add("movieWallDisplay");
        if (before.Search != after.Search) changed.Add("search");
        if (before.DataBackup != after.DataBackup) changed.Add("dataBackup");
        if (before.Scan != after.Scan) changed.Add("scan");
        if (before.System != after.System) changed.Add("system");
        if (before.Rename != after.Rename) changed.Add("rename");
        return changed;
    }

    private async Task<RenameSettingsDto> ReadRenameAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        string? raw = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='rename.settings'", token);
        try
        {
            return RenameSettingsService.Normalize(string.IsNullOrWhiteSpace(raw)
                ? RenameSettingsDto.Default
                : JsonSerializer.Deserialize<RenameSettingsDto>(raw) ?? RenameSettingsDto.Default);
        }
        catch (JsonException)
        {
            return RenameSettingsDto.Default;
        }
    }

    private static bool JavBusChanged(JavBusSettingsDto before, JavBusSettingsDto after) =>
        before.Enabled != after.Enabled ||
        before.Priority != after.Priority ||
        before.BaseUrl != after.BaseUrl ||
        before.TimeoutSeconds != after.TimeoutSeconds ||
        before.RetryCount != after.RetryCount ||
        before.Cookie != after.Cookie ||
        before.DownloadImages != after.DownloadImages ||
        before.FillMissingOnly != after.FillMissingOnly ||
        !SequenceEqual(before.MirrorUrls, after.MirrorUrls);

    private static bool WebProviderChanged(WebMetadataSettingsDto? before, WebMetadataSettingsDto? after)
    {
        if (before is null || after is null) return before != after;
        return before.Enabled != after.Enabled ||
            before.Priority != after.Priority ||
            before.BaseUrl != after.BaseUrl ||
            before.TimeoutSeconds != after.TimeoutSeconds ||
            before.RetryCount != after.RetryCount ||
            before.Cookie != after.Cookie ||
            before.DownloadImages != after.DownloadImages ||
            before.FillMissingOnly != after.FillMissingOnly ||
            !SequenceEqual(before.MirrorUrls, after.MirrorUrls);
    }

    private static bool SequenceEqual(IReadOnlyList<string>? before, IReadOnlyList<string>? after) =>
        (before ?? []).SequenceEqual(after ?? [], StringComparer.OrdinalIgnoreCase);

    private async Task MigrateLegacySettingsOnceAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        string? migrated = await ScalarTextAsync(connection, "SELECT ValueJson FROM AppSettings WHERE Key='legacySettings.migration.completedAt'", token);
        if (!string.IsNullOrWhiteSpace(migrated)) return;

        var migratedKeys = new List<string>();
        try {
            Dictionary<string, JsonObject> legacy = await ReadLegacyConfigObjectsAsync(token);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
            await StoreIfMissingOrDefaultAsync(connection, transaction, "scan.minFileSizeMb", ReadLegacyDouble(legacy, "ScanConfig", "MinFileSize", SettingsDefaults.Unified.Scan.MinFileSizeMb), SettingsDefaults.Unified.Scan.MinFileSizeMb, "number", migratedKeys, token);
            await StoreIfMissingOrDefaultAsync(connection, transaction, "system.language", ReadLegacyLanguage(legacy), SettingsDefaults.Unified.System.Language, "string", migratedKeys, token);
            await StoreIfMissingOrDefaultAsync(connection, transaction, "system.closeBehavior", ReadLegacyBool(legacy, "WindowConfig.Settings", "CloseToTaskBar", false) ? "minimizeToTray" : "exit", SettingsDefaults.Unified.System.CloseBehavior, "string", migratedKeys, token);
            await StoreIfMissingOrDefaultAsync(connection, transaction, "metadata.metatube.timeoutSeconds", Math.Clamp((int)Math.Round(ReadLegacyDouble(legacy, "ProxyConfig", "HttpTimeout", SettingsDefaults.Unified.MetaTube.TimeoutSeconds)), 15, 180), SettingsDefaults.Unified.MetaTube.TimeoutSeconds, "integer", migratedKeys, token);
            await StoreIfMissingOrDefaultAsync(connection, transaction, "metadata.metatube.writeNfo", ReadLegacyBool(legacy, "WindowConfig.Settings", "SaveInfoToNFO", SettingsDefaults.Unified.MetaTube.WriteNfo), SettingsDefaults.Unified.MetaTube.WriteNfo, "boolean", migratedKeys, token);
            await StoreIfMissingOrDefaultAsync(connection, transaction, "metadata.metatube.autoExecute", ReadLegacyBool(legacy, "DownloadConfig", "AutoDownloadAfterScan", SettingsDefaults.Unified.MetaTube.AutoExecute), SettingsDefaults.Unified.MetaTube.AutoExecute, "boolean", migratedKeys, token);
            await StoreIfMissingOrDefaultAsync(connection, transaction, "metadata.javbus.baseUrl", JavBusProvider.DefaultBaseUrl, SettingsDefaults.JavBus.BaseUrl, "string", migratedKeys, token);
            string legacyPlayer = ReadLegacyText(legacy, "WindowConfig.Settings", "VideoPlayerPath", "");
            if (!string.IsNullOrWhiteSpace(legacyPlayer) && File.Exists(legacyPlayer))
                await StoreIfMissingOrDefaultAsync(connection, transaction, "playback.playerPath", Path.GetFullPath(legacyPlayer), "", "string", migratedKeys, token);
            await StoreAsync(connection, transaction, "legacySettings.migration.completedAt", DateTimeOffset.UtcNow.ToString("O"), "string", token);
            await StoreAsync(connection, transaction, "legacySettings.migration.keys", migratedKeys.ToArray(), "json", token);
            await transaction.CommitAsync(token);
        } catch (Exception error) {
            Console.Error.WriteLine($"Legacy settings migration skipped: {error.Message}");
            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
            await StoreAsync(connection, transaction, "legacySettings.migration.completedAt", DateTimeOffset.UtcNow.ToString("O"), "string", token);
            await StoreAsync(connection, transaction, "legacySettings.migration.error", error.Message, "string", token);
            await transaction.CommitAsync(token);
        }
    }

    private async Task<Dictionary<string, JsonObject>> ReadLegacyConfigObjectsAsync(CancellationToken token)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(legacyConfigDatabasePath) || !File.Exists(legacyConfigDatabasePath)) return result;
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = legacyConfigDatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        await connection.OpenAsync(token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ConfigName, ConfigValue FROM app_configs";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) {
            string name = reader.GetString(0);
            string json = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (string.IsNullOrWhiteSpace(json)) continue;
            try {
                if (JsonNode.Parse(json) is JsonObject obj) result[name] = obj;
            } catch (JsonException error) {
                Console.Error.WriteLine($"Legacy settings migration ignored invalid config {name}: {error.Message}");
            }
        }
        return result;
    }

    private static async Task StoreIfMissingOrDefaultAsync(SqliteConnection connection, SqliteTransaction transaction, string key, object value, object defaultValue, string type, List<string> migratedKeys, CancellationToken token)
    {
        string? existing = await ScalarTextAsync(connection, transaction, "SELECT ValueJson FROM AppSettings WHERE Key=$key", token, ("$key", key));
        if (!string.IsNullOrWhiteSpace(existing) && !JsonEquivalent(existing, JsonSerializer.Serialize(defaultValue))) return;
        await StoreAsync(connection, transaction, key, value, type, token);
        migratedKeys.Add(key);
    }

    private static bool JsonEquivalent(string left, string right)
    {
        try {
            using JsonDocument leftDoc = JsonDocument.Parse(left);
            using JsonDocument rightDoc = JsonDocument.Parse(right);
            return JsonElementEquality(leftDoc.RootElement, rightDoc.RootElement);
        } catch (JsonException) {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }

    private static bool JsonElementEquality(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        return left.ValueKind switch {
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.GetDouble().Equals(right.GetDouble()),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null => true,
            _ => left.GetRawText() == right.GetRawText(),
        };
    }

    private static bool ReadLegacyBool(IReadOnlyDictionary<string, JsonObject> values, string config, string property, bool fallback) =>
        values.TryGetValue(config, out JsonObject? obj) && TryGetLegacyProperty(obj, property, out JsonNode? node) && bool.TryParse(node?.ToString(), out bool value) ? value : fallback;

    private static double ReadLegacyDouble(IReadOnlyDictionary<string, JsonObject> values, string config, string property, double fallback) =>
        values.TryGetValue(config, out JsonObject? obj) && TryGetLegacyProperty(obj, property, out JsonNode? node) && double.TryParse(node?.ToString(), out double value) ? value : fallback;

    private static string ReadLegacyText(IReadOnlyDictionary<string, JsonObject> values, string config, string property, string fallback) =>
        values.TryGetValue(config, out JsonObject? obj) && TryGetLegacyProperty(obj, property, out JsonNode? node) ? node?.ToString() ?? fallback : fallback;

    private static string ReadLegacyLanguage(IReadOnlyDictionary<string, JsonObject> values)
    {
        string language = ReadLegacyText(values, "WindowConfig.Settings", "CurrentLanguage", "");
        return language.Contains("zh", StringComparison.OrdinalIgnoreCase) || language.Contains("中文", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : SettingsDefaults.Unified.System.Language;
    }

    private static bool TryGetLegacyProperty(JsonObject source, string name, out JsonNode? value)
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

    private static ScanSettingsDto NormalizeScan(ScanSettingsDto? input)
    {
        double value = input?.MinFileSizeMb ?? SettingsDefaults.Unified.Scan.MinFileSizeMb;
        if (double.IsNaN(value) || double.IsInfinity(value)) value = SettingsDefaults.Unified.Scan.MinFileSizeMb;
        return new(Math.Clamp(value, 0, 1024 * 1024));
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token, params (string Name, object? Value)[] values)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L);
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
    public static MdcNgSettingsDto MdcNg => new(false, "http://127.0.0.1:5800/", "http://127.0.0.1:9207/", 120, "", true);
    public static JavBusSettingsDto JavBus => new(true, 2, JavBusProvider.DefaultBaseUrl, 30, 1, "", true, true);
    public static WebMetadataSettingsDto Dmm => new(false, 3, DmmProvider.DefaultBaseUrl, 30, 1, "", true, true);
    public static WebMetadataSettingsDto JavDb => new(false, 4, JavDbProvider.DefaultBaseUrl, 30, 1, "", true, true);
    public static WebMetadataSettingsDto Minnano => new(false, 1, MinnanoActorProfileProvider.DefaultBaseUrl, 30, 1, "", true, true);
    public static WebMetadataSettingsDto WikipediaJp => new(false, 2, WikipediaJpActorProfileProvider.DefaultBaseUrl, 30, 1, "", false, true);
    public static ProviderNetworkSettingsDto ProviderNetwork => ProviderNetworkSettingsDto.Default;
    public static UnifiedSettingsDto Unified => UnifiedForEnvironment(null, null);

    public static UnifiedSettingsDto UnifiedForEnvironment(string? installRoot, string? databasePath) => new(
        MdcNg,
        new(true, "http://127.0.0.1:8080/", 30, true, true, true, true),
        JavBus,
        new("SkipExisting", "", true, true),
        new("", true),
        new(true),
        new("dark"),
        MediaStorageForEnvironment(installRoot, databasePath),
        new("portrait", "medium", "poster", "fanart", "grid"),
        new("newest", "all"),
        new(true, 3, 10),
        new(0),
        new("system", "exit", false, 30, true, false, null),
        Dmm,
        JavDb,
        Minnano,
        WikipediaJp,
        ProviderNetwork,
        RenameSettingsDto.Default);

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
