using System.Diagnostics;
using Microsoft.Data.Sqlite;

const string bridgeUrl = "http://127.0.0.1:47831";
string installedRoot = Environment.GetEnvironmentVariable("LMM_LEGACY_ROOT")
    ?? @"D:\Jvedio\Jvedio5.0";
string databasePath = Environment.GetEnvironmentVariable("LMM_DATABASE_PATH")
    ?? Path.Combine(installedRoot, "data", Environment.UserName, "app_datas.sqlite");
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
    readOnly = true,
}));

app.MapGet("/api/library/summary", async () => {
    if (!File.Exists(databasePath))
        return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);

    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM metadata WHERE DataType = 0";
    long count = (long)(await command.ExecuteScalarAsync() ?? 0L);
    return Results.Ok(new { videoCount = count, databasePath, readOnly = true });
});

app.MapGet("/api/videos", async (int? limit) => {
    if (!File.Exists(databasePath))
        return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);

    int take = Math.Clamp(limit ?? 24, 1, 96);
    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT m.DataID,
               COALESCE(NULLIF(v.VID, ''), NULLIF(m.Title, ''), CAST(m.DataID AS TEXT)) AS Code,
               COALESCE(m.Title, '') AS Title,
               COALESCE(m.Path, '') AS Path,
               COALESCE(m.Grade, 0) AS Grade,
               CASE WHEN COALESCE(m.FavoriteCount, 0) > 0 THEN 1 ELSE 0 END AS Favorite,
               COALESCE(m.ReleaseDate, '') AS ReleaseDate,
               COALESCE(m.CreateDate, '') AS ImportedAt
          FROM metadata m
          LEFT JOIN (
              SELECT DataID, MIN(VID) AS VID
                FROM metadata_video
               GROUP BY DataID
         ) v ON v.DataID = m.DataID
         WHERE m.DataType = 0
           AND (
               lower(m.Path) LIKE '%.mp4' OR lower(m.Path) LIKE '%.mkv' OR
               lower(m.Path) LIKE '%.avi' OR lower(m.Path) LIKE '%.wmv' OR
               lower(m.Path) LIKE '%.mov' OR lower(m.Path) LIKE '%.ts' OR
               lower(m.Path) LIKE '%.m2ts' OR lower(m.Path) LIKE '%.flv' OR
               lower(m.Path) LIKE '%.webm'
           )
         ORDER BY m.DataID DESC
         LIMIT $limit
        """;
    command.Parameters.AddWithValue("$limit", take);
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
            coverUrl = FindCover(imageRoot, code) is null
                ? null
                : $"{bridgeUrl}/api/covers/{Uri.EscapeDataString(code)}",
        });
    }
    return Results.Ok(items);
});

app.MapGet("/api/covers/{code}", (string code) => {
    string? path = FindCover(imageRoot, code);
    return path is null
        ? Results.NotFound()
        : Results.File(path, ContentType(path), enableRangeProcessing: true);
});

app.MapPost("/api/videos/{dataId:long}/play", async (long dataId) => {
    if (!File.Exists(databasePath))
        return Results.Problem($"找不到数据库：{databasePath}", statusCode: 503);

    await using var connection = await OpenReadOnlyAsync(databasePath);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COALESCE(Path, '') FROM metadata WHERE DataID = $id AND DataType = 0";
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
    ".mp4" or ".mkv" or ".avi" or ".wmv" or ".mov" or ".ts" or ".m2ts" or ".flv" or ".webm";
