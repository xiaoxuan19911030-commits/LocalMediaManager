using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record MediaStorageMovie(long Id, string Code, string? Title);
public sealed record MediaStorageResourcePath(string ResourceType, string Directory, string MovieFolder, string FileName, string FullPath);

public sealed class MediaStoragePathResolver(string databasePath, string installRoot)
{
    public async Task<MediaStorageResourcePath> ResolveForMovieAsync(
        long movieId,
        string resourceType,
        string extension,
        int? index = null,
        string? uniqueSuffix = null,
        CancellationToken cancellationToken = default)
    {
        MediaStorageMovie movie = await ReadMovieAsync(movieId, cancellationToken);
        return await ResolveForMovieAsync(movie, resourceType, extension, index, uniqueSuffix, cancellationToken);
    }

    public async Task<MediaStorageResourcePath> ResolveForMovieAsync(
        MediaStorageMovie movie,
        string resourceType,
        string extension,
        int? index = null,
        string? uniqueSuffix = null,
        CancellationToken cancellationToken = default)
    {
        MediaStorageSettingsDto settings = await ReadSettingsAsync(cancellationToken);
        string normalizedType = NormalizeResourceType(resourceType);
        string resourceDirectory = ResourceDirectory(settings, normalizedType);
        string movieFolder = SafePathSegment(RenderTemplate(settings.MovieFolderTemplate, movie));
        string baseFileName = SafePathSegment(RenderTemplate(settings.FileNameTemplate, movie));
        string suffix = index.HasValue ? $"_{index.Value:000}" : string.IsNullOrWhiteSpace(uniqueSuffix) ? "" : "_" + SafePathSegment(uniqueSuffix);
        string fileName = baseFileName + suffix + NormalizeExtension(extension);
        string fullPath = Path.Combine(settings.RootPath, resourceDirectory, movieFolder, fileName);
        return new(normalizedType, resourceDirectory, movieFolder, fileName, fullPath);
    }

    public void EnsureDirectoryForWrite(string resourcePath)
    {
        string? directory = Path.GetDirectoryName(resourcePath);
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Media resource target directory is invalid.", nameof(resourcePath));
        Directory.CreateDirectory(directory);
    }

    public async Task<string> TemporaryRootAsync(CancellationToken cancellationToken = default)
    {
        MediaStorageSettingsDto settings = await ReadSettingsAsync(cancellationToken);
        string path = Path.Combine(settings.RootPath, ".lmm-temp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public async Task<bool> IsInsideMediaStorageAsync(string path, CancellationToken cancellationToken = default)
    {
        MediaStorageSettingsDto settings = await ReadSettingsAsync(cancellationToken);
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string root = Path.GetFullPath(settings.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<MediaStorageMovie> ReadMovieAsync(long movieId, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id,COALESCE(Code,''),Title FROM Movies WHERE Id=$id";
        command.Parameters.AddWithValue("$id", movieId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Movie does not exist.");
        return new(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private async Task<MediaStorageSettingsDto> ReadSettingsAsync(CancellationToken token)
    {
        MediaStorageSettingsDto defaults = SettingsDefaults.MediaStorageForEnvironment(installRoot, databasePath);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE 'mediaStorage.%'";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) values[reader.GetString(0)] = reader.GetString(1);
        string root = TextSetting(values, "mediaStorage.rootPath", "");
        if (string.IsNullOrWhiteSpace(root)) root = defaults.RootPath;
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
            TextSetting(values, "mediaStorage.template.fileName", defaults.FileNameTemplate));
    }

    private static string ResourceDirectory(MediaStorageSettingsDto settings, string resourceType) => resourceType switch
    {
        "Thumbnail" => settings.ThumbnailsDirectory,
        "Fanart" => settings.FanartDirectory,
        "Preview" => settings.PreviewsDirectory,
        "Screenshot" => settings.ScreenshotsDirectory,
        "GeneratedCard" => settings.WallCropsDirectory,
        "GIF" => settings.GifDirectory,
        "NFO" => settings.NfoDirectory,
        _ => settings.PostersDirectory,
    };

    public static string NormalizeResourceType(string type) => (type ?? "").Trim().ToLowerInvariant() switch
    {
        "thumb" or "thumbnail" or "smallpic" => "Thumbnail",
        "bigpic" or "fanart" or "background" => "Fanart",
        "extrapic" or "preview" => "Preview",
        "generatedcard" or "cardcover" or "cardcovers" or "wallcrop" or "wallcrops" => "GeneratedCard",
        "gif" => "GIF",
        "nfo" => "NFO",
        "screenshot" or "screen" => "Screenshot",
        _ => "Poster",
    };

    private static string RenderTemplate(string template, MediaStorageMovie movie)
    {
        string code = string.IsNullOrWhiteSpace(movie.Code) ? $"movie-{movie.Id}" : movie.Code.Trim();
        string title = string.IsNullOrWhiteSpace(movie.Title) ? code : movie.Title.Trim();
        return template
            .Replace("{MovieCode}", code, StringComparison.Ordinal)
            .Replace("{MovieTitle}", title, StringComparison.Ordinal)
            .Trim();
    }

    private static string SafePathSegment(string value)
    {
        string clean = string.Concat((value ?? "").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
        while (clean.EndsWith('.') || clean.EndsWith(' ')) clean = clean[..^1];
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }

    private static string NormalizeExtension(string extension)
    {
        string clean = string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension.Trim();
        return clean.StartsWith('.') ? clean : "." + clean;
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }

    private static string TextSetting(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        if (!values.TryGetValue(key, out string? raw)) return fallback;
        try { return JsonSerializer.Deserialize<string>(raw) ?? fallback; }
        catch (JsonException) { return fallback; }
    }
}
