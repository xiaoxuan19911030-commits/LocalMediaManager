using System.Diagnostics;
using System.Text.Json;
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
string? sessionToken = Environment.GetEnvironmentVariable("LMM_BRIDGE_TOKEN");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(bridgeUrl);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton(new ProductWriter(databasePath));
builder.Services.AddSingleton(new LibraryWorkflowService(databasePath));

var app = builder.Build();
app.UseCors();
app.Use(async (context, next) => {
    try {
        if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS")) {
            if (string.IsNullOrWhiteSpace(sessionToken)) {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new { code = "WRITE_SESSION_UNAVAILABLE", message = "Bridge 未由受信任的桌面会话启动，写入已禁用。" });
                return;
            }
            if (!context.Request.Headers.TryGetValue("X-LMM-Session", out var supplied) || supplied != sessionToken) {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { code = "INVALID_SESSION", message = "Bridge 会话凭据无效。" });
                return;
            }
        }
        await next();
    } catch (KeyNotFoundException error) {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { code = "NOT_FOUND", message = error.Message });
    } catch (UnauthorizedAccessException error) {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { code = "CONFIRMATION_REQUIRED", message = error.Message });
    } catch (ArgumentException error) {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { code = "INVALID_INPUT", message = error.Message });
    } catch (InvalidOperationException error) {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { code = "CONFLICT", message = error.Message });
    } catch (Exception error) {
        Console.Error.WriteLine(error);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "操作失败，数据库未提交更改。" });
    }
});

app.MapGet("/health", () => Results.Ok(new {
    product = "Local Media Manager",
    abbreviation = "LMM",
    version = "0.4.1",
    status = "ok",
    databaseAvailable = File.Exists(databasePath),
    databasePath,
    configDatabaseAvailable = File.Exists(configDatabasePath),
    configDatabasePath,
    readOnly = false,
    writeEnabled = !string.IsNullOrWhiteSpace(sessionToken),
    sessionAuthentication = "X-LMM-Session",
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

app.MapPost("/api/libraries", async (LibraryCommand command, LibraryWorkflowService service) =>
    Results.Ok(await service.CreateLibraryAsync(command)));
app.MapPut("/api/libraries/{libraryId:long}", async (long libraryId, LibraryCommand command, LibraryWorkflowService service) =>
    Results.Ok(await service.UpdateLibraryAsync(libraryId, command)));
app.MapGet("/api/libraries/{libraryId:long}/delete-preview", async (long libraryId, LibraryWorkflowService service) =>
    Results.Ok(await service.PreviewDeleteLibraryAsync(libraryId)));
app.MapPost("/api/libraries/{libraryId:long}/delete", async (long libraryId, ConfirmCommand command, LibraryWorkflowService service) =>
    Results.Ok(await service.DeleteLibraryAsync(libraryId, command)));
app.MapPost("/api/libraries/{libraryId:long}/scan", async (long libraryId, ScanLibraryCommand command, LibraryWorkflowService service) =>
    Results.Ok(await service.StartScanAsync(libraryId, command)));

app.MapGet("/api/tasks", async (int? limit) => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadTasksAsync(databasePath, Math.Clamp(limit ?? 50, 1, 200)))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));
app.MapGet("/api/tasks/{taskId:long}/logs", async (long taskId, int? limit) => File.Exists(databasePath)
    ? Results.Ok(await ProductReader.ReadTaskLogsAsync(databasePath, taskId, Math.Clamp(limit ?? 200, 1, 1000)))
    : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));
app.MapPost("/api/tasks/{taskId:long}/pause", async (long taskId, LibraryWorkflowService service) =>
    Results.Ok(await service.PauseTaskAsync(taskId)));
app.MapPost("/api/tasks/{taskId:long}/resume", async (long taskId, LibraryWorkflowService service) =>
    Results.Ok(await service.ResumeTaskAsync(taskId)));
app.MapPost("/api/tasks/{taskId:long}/cancel", async (long taskId, LibraryWorkflowService service) =>
    Results.Ok(await service.CancelTaskAsync(taskId)));
app.MapPost("/api/tasks/{taskId:long}/retry", async (long taskId, LibraryWorkflowService service) =>
    Results.Ok(await service.RetryTaskAsync(taskId)));

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

app.MapGet("/api/videos/{movieId:long}/neighbors", async (long movieId, string? search, string? sort) =>
    File.Exists(databasePath)
        ? Results.Ok(await ProductReader.ReadNeighborsAsync(databasePath, movieId, search ?? "", sort ?? "newest"))
        : Results.Problem($"找不到数据库：{databasePath}", statusCode: 503));

app.MapPatch("/api/videos/{movieId:long}/state", async (long movieId, UserStateCommand command, ProductWriter writer) =>
    Results.Ok(await writer.SetUserStateAsync(movieId, command)));

app.MapPost("/api/videos/batch/favorite", async (BatchFavoriteCommand command, ProductWriter writer) =>
    Results.Ok(await writer.SetFavoritesAsync(command)));

app.MapPost("/api/tags", async (TagCommand command, ProductWriter writer) => {
    var created = await writer.CreateTagAsync(command);
    return Results.Ok(new { id = created.Id, created.Result.Changed, created.Result.AuditId, created.Result.Message });
});
app.MapPut("/api/tags/{tagId:long}", async (long tagId, TagCommand command, ProductWriter writer) =>
    Results.Ok(await writer.UpdateTagAsync(tagId, command)));
app.MapGet("/api/tags/{tagId:long}/delete-preview", async (long tagId, ProductWriter writer) =>
    Results.Ok(await writer.PreviewDeleteTagAsync(tagId)));
app.MapPost("/api/tags/{tagId:long}/delete", async (long tagId, ConfirmCommand command, ProductWriter writer) =>
    Results.Ok(await writer.DeleteTagAsync(tagId, command)));
app.MapPost("/api/operations/{auditId:long}/rollback", async (long auditId, ProductWriter writer) =>
    Results.Ok(await writer.RollbackAsync(auditId)));
app.MapPatch("/api/videos/{movieId:long}/tags", async (long movieId, MovieTagsCommand command, ProductWriter writer) =>
    Results.Ok(await writer.UpdateMovieTagsAsync(movieId, command)));
app.MapPost("/api/videos/batch/tags", async (BatchTagsCommand command, ProductWriter writer) =>
    Results.Ok(await writer.UpdateBatchTagsAsync(command)));

app.MapPut("/api/actors/{actorId:long}", async (long actorId, ActorCommand command, ProductWriter writer) =>
    Results.Ok(await writer.UpdateActorAsync(actorId, command)));
app.MapPut("/api/videos/{movieId:long}/actors", async (long movieId, MovieActorsCommand command, ProductWriter writer) =>
    Results.Ok(await writer.SetMovieActorsAsync(movieId, command)));
app.MapGet("/api/actors/repair-preview", async (ProductWriter writer) => Results.Ok(await writer.PreviewActorRepairAsync()));
app.MapPost("/api/actors/repair", async (ConfirmCommand command, ProductWriter writer) => Results.Ok(await writer.ApplyActorRepairAsync(command)));

app.MapPost("/api/videos/{movieId:long}/remember-rating", async (long movieId, ProductWriter writer) =>
    Results.Ok(new { remembered = await writer.RememberDeletedRatingAsync(movieId) }));
app.MapPost("/api/videos/{movieId:long}/restore-rating", async (long movieId, ProductWriter writer) =>
    Results.Ok(new { restored = await writer.RestoreDeletedRatingAsync(movieId) }));
app.MapGet("/api/videos/{movieId:long}/delete-preview", async (long movieId, ProductWriter writer) =>
    Results.Ok(await writer.PreviewDeleteMovieAsync(movieId)));
app.MapPost("/api/videos/{movieId:long}/delete", async (long movieId, ConfirmCommand command, ProductWriter writer) =>
    Results.Ok(await writer.DeleteMovieAsync(movieId, command)));

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

app.MapGet("/api/actors/{actorId:long}", async (long actorId) => {
    if (!File.Exists(databasePath)) return Results.NotFound();
    ActorDetailDto? actor = await ProductReader.ReadActorAsync(databasePath, actorId);
    return actor is null ? Results.NotFound() : Results.Ok(actor);
});

app.MapPost("/api/videos/{dataId:long}/play", async (long dataId, ProductWriter writer) => {
    if (!File.Exists(databasePath))
        return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);

    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id,COALESCE(FilePath, '') FROM MediaFiles WHERE MovieId=$id AND IsPrimary=1 AND MediaType='Video' LIMIT 1";
    command.Parameters.AddWithValue("$id", dataId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound("影片没有可播放的主文件。");
    long mediaFileId = reader.GetInt64(0);
    string path = reader.GetString(1);
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
    DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    Process? player = Process.Start(startInfo);
    if (player is null) return Results.Problem("播放器未能启动。", statusCode: 502);
    _ = Task.Run(async () => {
        try {
            await player.WaitForExitAsync();
            if (player.ExitCode == 0)
                await writer.RecordPlaybackAsync(dataId, mediaFileId, Path.GetFileName(startInfo.FileName), startedAt, DateTimeOffset.UtcNow);
        } catch (Exception error) { Console.Error.WriteLine($"Playback tracking failed: {error}"); }
        finally { player.Dispose(); }
    });
    return Results.Ok(new { started = true, path, trackingWritten = false, trackingMode = "on-normal-exit" });
});

Console.WriteLine($"Local Media Manager Bridge: {bridgeUrl}");
Console.WriteLine($"Database (read/write via authenticated commands): {databasePath}");
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
