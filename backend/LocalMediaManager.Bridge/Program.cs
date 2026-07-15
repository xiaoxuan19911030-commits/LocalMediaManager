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
    product = "Local Media Manager",
    abbreviation = "LMM",
    version = "0.4.0",
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

app.MapGet("/api/dashboard", async () => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadDashboardAsync(databasePath, bridgeUrl))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/search", async (string? q, int? limit) => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.SearchAsync(databasePath, bridgeUrl, q ?? string.Empty, Math.Clamp(limit ?? 12, 1, 48)))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/libraries", async () => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadLibrariesAsync(databasePath))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/tasks", async (int? limit) => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadTasksAsync(databasePath, Math.Clamp(limit ?? 50, 1, 200)))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/entities/{entityType}", async (string entityType, string? search, string? sort, int? limit, int? offset) => {
    if (!File.Exists(databasePath)) return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);
    if (entityType is not ("actors" or "tags")) return Results.BadRequest("仅支持 actors 或 tags。");
    return Results.Ok(await ProductReader.ReadEntitiesPageAsync(databasePath, bridgeUrl, entityType, search ?? "", sort ?? "count", Math.Clamp(limit ?? 48, 1, 96), Math.Max(offset ?? 0, 0)));
});

app.MapGet("/api/entities/{entityType}/{entityId:long}/movies", async (string entityType, long entityId, int? limit, int? offset) => {
    if (!File.Exists(databasePath)) return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);
    if (entityType is not ("actors" or "tags")) return Results.BadRequest("仅支持 actors 或 tags。");
    return Results.Ok(await ProductReader.ReadEntityMoviesAsync(databasePath, bridgeUrl, entityType, entityId, Math.Clamp(limit ?? 48, 1, 96), Math.Max(offset ?? 0, 0)));
});

app.MapGet("/api/collections/{kind}", async (string kind, int? limit, int? offset) => {
    if (!File.Exists(databasePath)) return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);
    if (kind is not ("favorites" or "history")) return Results.BadRequest("仅支持 favorites 或 history。");
    return Results.Ok(await ProductReader.ReadCollectionAsync(databasePath, bridgeUrl, kind, Math.Clamp(limit ?? 48, 1, 96), Math.Max(offset ?? 0, 0)));
});

app.MapGet("/api/search/advanced", async (string? q, long? actorId, long? tagId, bool? favorite, double? ratingMin,
    string? metadata, string? fileStatus, long? libraryId, string? sort, int? limit, int? offset) => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.AdvancedSearchAsync(databasePath, bridgeUrl, q ?? "", actorId, tagId, favorite,
        Math.Clamp(ratingMin ?? 0, 0, 5), metadata ?? "all", fileStatus ?? "all", libraryId, sort ?? "newest",
        Math.Clamp(limit ?? 48, 1, 96), Math.Max(offset ?? 0, 0)))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/metadata/overview", async () => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadMetadataOverviewAsync(databasePath))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapGet("/api/diagnostics", async () => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadDiagnosticsAsync(databasePath))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

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
    MovieDetailDto? detail = await ProductReader.ReadMovieAsync(databasePath, bridgeUrl, movieId);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
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

app.MapGet("/api/actors/{actorId:long}/image", async (long actorId) => {
    if (!File.Exists(databasePath)) return Results.NotFound();
    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT FilePath FROM Images WHERE ActorId=$id AND FilePath IS NOT NULL ORDER BY IsPrimary DESC,Id LIMIT 1";
    command.Parameters.AddWithValue("$id", actorId);
    string? path = (string?)await command.ExecuteScalarAsync();
    return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? Results.NotFound() : Results.File(path, ContentType(path), enableRangeProcessing: true);
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
