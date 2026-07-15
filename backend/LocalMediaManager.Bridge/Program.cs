using System.Diagnostics;
using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;

const string bridgeUrl = "http://127.0.0.1:47831";
string installedRoot = Environment.GetEnvironmentVariable("LMM_LEGACY_ROOT")
    ?? @"D:\Jvedio\Jvedio5.0";
string databasePath = Environment.GetEnvironmentVariable("LMM_DATABASE_PATH")
    ?? @"D:\Local Media Manager Next Data\data\LocalMediaManager.db";
string configDatabasePath = Environment.GetEnvironmentVariable("LMM_CONFIG_DATABASE_PATH")
    ?? Path.Combine(installedRoot, "data", Environment.UserName, "app_configs.sqlite");
string imageRoot = Environment.GetEnvironmentVariable("LMM_IMAGE_ROOT")
    ?? (Directory.Exists(@"Z:\bcbcbcbc\ca-ES\JVDIO")
        ? @"Z:\bcbcbcbc\ca-ES\JVDIO"
        : Path.Combine(installedRoot, "data", Environment.UserName, "pic"));

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(bridgeUrl);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/health", () => Results.Ok(new {
    status = "ok",
    databaseAvailable = File.Exists(databasePath),
    databasePath,
    configDatabaseAvailable = File.Exists(configDatabasePath),
    configDatabasePath,
    readOnly = true,
    dataSeparated = true,
    legacyDatabaseUsedForRuntime = false,
}));

app.MapGet("/api/settings", async () => Results.Ok(await SettingsReader.ReadAsync(configDatabasePath)));

app.MapGet("/api/library/summary", async () => {
    if (!File.Exists(databasePath))
        return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);

    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM Movies";
    long count = (long)(await command.ExecuteScalarAsync() ?? 0L);
    return Results.Ok(new { videoCount = count, databasePath, readOnly = true });
});

app.MapGet("/api/videos", async (int? limit, int? offset, string? search, string? sort) => {
    if (!File.Exists(databasePath))
        return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);

    int take = Math.Clamp(limit ?? 24, 1, 96);
    int skip = Math.Max(0, offset ?? 0);
    string query = (search ?? string.Empty).Trim();
    string orderBy = sort?.ToLowerInvariant() switch {
        "code" => "m.Code COLLATE NOCASE, m.Id",
        "title" => "m.Title COLLATE NOCASE, m.Id",
        "release" => "m.ReleaseDate DESC, m.Id DESC",
        "rating" => "s.UserRating DESC, m.Id DESC",
        _ => "m.Id DESC",
    };
    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var countCommand = connection.CreateCommand();
    countCommand.CommandText = """
        SELECT COUNT(*) FROM Movies m
         WHERE $search='' OR m.Code LIKE $like ESCAPE '\' OR m.Title LIKE $like ESCAPE '\'
        """;
    countCommand.Parameters.AddWithValue("$search", query);
    countCommand.Parameters.AddWithValue("$like", $"%{EscapeLike(query)}%");
    long total = (long)(await countCommand.ExecuteScalarAsync() ?? 0L);
    await using var command = connection.CreateCommand();
    command.CommandText = $"""
        SELECT m.Id,
               COALESCE(NULLIF(m.Code, ''), NULLIF(m.Title, ''), CAST(m.Id AS TEXT)) AS Code,
               COALESCE(m.Title, '') AS Title,
               COALESCE(f.FilePath, '') AS Path,
               COALESCE(s.UserRating, 0) AS Grade,
               COALESCE(s.IsFavorite, 0) AS Favorite,
               COALESCE(m.ReleaseDate, '') AS ReleaseDate,
               COALESCE(m.ImportedAt, m.CreatedAt, '') AS ImportedAt,
               CASE WHEN EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id) THEN 1 ELSE 0 END AS HasCover
          FROM Movies m
          JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
          LEFT JOIN UserMovieState s ON s.MovieId=m.Id
         WHERE $search='' OR m.Code LIKE $like ESCAPE '\' OR m.Title LIKE $like ESCAPE '\'
         ORDER BY {orderBy}
         LIMIT $limit OFFSET $offset
        """;
    command.Parameters.AddWithValue("$search", query);
    command.Parameters.AddWithValue("$like", $"%{EscapeLike(query)}%");
    command.Parameters.AddWithValue("$limit", take);
    command.Parameters.AddWithValue("$offset", skip);
    var items = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) {
        string code = reader.GetString(1);
        items.Add(new {
            dataId = reader.GetInt64(0),
            code,
            title = reader.GetString(2),
            path = reader.GetString(3),
            grade = reader.GetDouble(4),
            favorite = reader.GetInt64(5) == 1,
            releaseDate = reader.GetString(6),
            importedAt = reader.GetString(7),
            coverUrl = reader.GetInt64(8) == 1
                ? $"{bridgeUrl}/api/images/{reader.GetInt64(0)}/primary"
                : null,
        });
    }
    return Results.Ok(new { items, total, limit = take, offset = skip });
});

app.MapGet("/api/videos/{movieId:long}", async (long movieId) => {
    if (!File.Exists(databasePath)) return Results.NotFound();
    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT m.Id,m.Code,m.Title,m.OriginalTitle,m.ReleaseDate,m.DurationSeconds,m.Description,
               m.ProviderRating,m.IsScraped,m.ScrapeStatus,m.NfoPath,m.ImportedAt,m.UpdatedAt,
               s.IsFavorite,s.UserRating,s.PlayCount,s.LastPlayedAt,s.LastPositionSeconds,s.Notes,
               f.Id,f.FilePath,f.FileName,f.Extension,f.FileSize,f.SourceType,f.ExistsState
          FROM Movies m
          LEFT JOIN UserMovieState s ON s.MovieId=m.Id
          LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1
         WHERE m.Id=$id LIMIT 1
        """;
    command.Parameters.AddWithValue("$id", movieId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    var detail = new {
        id = reader.GetInt64(0), code = Text(reader, 1), title = Text(reader, 2), originalTitle = Text(reader, 3),
        releaseDate = Text(reader, 4), durationSeconds = Number(reader, 5), description = Text(reader, 6),
        providerRating = DecimalNumber(reader, 7), scraped = Number(reader, 8) == 1, scrapeStatus = Text(reader, 9),
        nfoPath = Text(reader, 10), importedAt = Text(reader, 11), updatedAt = Text(reader, 12),
        favorite = Number(reader, 13) == 1, userRating = DecimalNumber(reader, 14), playCount = Number(reader, 15),
        lastPlayedAt = Text(reader, 16), lastPositionSeconds = Number(reader, 17), notes = Text(reader, 18),
        mediaFile = reader.IsDBNull(19) ? null : new { id = reader.GetInt64(19), path = Text(reader, 20), fileName = Text(reader, 21),
            extension = Text(reader, 22), fileSize = Number(reader, 23), sourceType = Text(reader, 24), existsState = Text(reader, 25) },
        coverUrl = $"{bridgeUrl}/api/images/{movieId}/primary",
    };
    return Results.Ok(detail);
});

app.MapGet("/api/covers/{code}", (string code) => {
    string? path = FindCover(imageRoot, code);
    return path is null
        ? Results.NotFound()
        : Results.File(path, ContentType(path), enableRangeProcessing: true);
});

app.MapGet("/api/images/{movieId:long}/primary", async (long movieId) => {
    if (!File.Exists(databasePath)) return Results.NotFound();
    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT FilePath FROM Images
         WHERE MovieId=$id AND FilePath IS NOT NULL
         ORDER BY IsPrimary DESC,
                  CASE ImageType WHEN 'GeneratedCard' THEN 0 WHEN 'Poster' THEN 1 WHEN 'Fanart' THEN 2 ELSE 3 END,
                  Id
         LIMIT 1
        """;
    command.Parameters.AddWithValue("$id", movieId);
    string? path = (string?)(await command.ExecuteScalarAsync());
    return string.IsNullOrWhiteSpace(path) || !File.Exists(path)
        ? Results.NotFound()
        : Results.File(path, ContentType(path), enableRangeProcessing: true);
});

app.MapPost("/api/videos/{dataId:long}/play", async (long dataId) => {
    if (!File.Exists(databasePath))
        return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);

    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COALESCE(FilePath, '') FROM MediaFiles WHERE MovieId=$id AND IsPrimary=1 AND MediaType='Video' LIMIT 1";
    command.Parameters.AddWithValue("$id", dataId);
    string path = (string?)(await command.ExecuteScalarAsync()) ?? string.Empty;
    if (!File.Exists(path))
        return Results.NotFound($"影片文件不存在：{path}");
    if (!IsVideoFile(path))
        return Results.BadRequest($"该记录不是可播放的影片文件：{path}");

    string? configuredPlayer = Environment.GetEnvironmentVariable("LMM_PLAYER_PATH");
    var startInfo = new ProcessStartInfo { UseShellExecute = true };
    if (!string.IsNullOrWhiteSpace(configuredPlayer) && File.Exists(configuredPlayer)) {
        startInfo.FileName = configuredPlayer;
        startInfo.ArgumentList.Add(path);
    } else {
        startInfo.FileName = path;
    }
    Process.Start(startInfo);
    return Results.Ok(new { started = true, path, trackingWritten = false });
});

Console.WriteLine($"Local Media Manager Bridge: {bridgeUrl}");
Console.WriteLine($"Database (read-only): {databasePath}");
await app.RunAsync();

static async Task<SqliteConnection> OpenReadOnlyAsync(string databasePath)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadOnly,
        Cache = SqliteCacheMode.Shared,
    }.ToString());
    await connection.OpenAsync();
    return connection;
}

static string? FindCover(string imageRoot, string code)
{
    if (string.IsNullOrWhiteSpace(code) || Path.GetFileName(code) != code)
        return null;
    foreach (string folder in new[] { "CardCovers", "SmallPic" })
        foreach (string extension in new[] { ".jpg", ".jpeg", ".png", ".webp" }) {
            string candidate = Path.Combine(imageRoot, folder, code + extension);
            if (File.Exists(candidate))
                return candidate;
        }
    return null;
}

static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch {
    ".png" => "image/png",
    ".webp" => "image/webp",
    _ => "image/jpeg",
};

static bool IsVideoFile(string path) => Path.GetExtension(path).ToLowerInvariant() is
    ".mp4" or ".mkv" or ".avi" or ".wmv" or ".mov" or ".ts" or ".m2ts" or ".flv" or ".webm"
    or ".vob" or ".mpg" or ".mpeg";

static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
static long Number(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? 0 : reader.GetInt64(ordinal);
static double DecimalNumber(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? 0 : reader.GetDouble(ordinal);
