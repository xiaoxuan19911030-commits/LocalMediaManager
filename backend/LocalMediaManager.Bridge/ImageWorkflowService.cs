using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace LocalMediaManager.Bridge;

public sealed record ImageReplaceCommand(string Path);
public sealed record ImageDeletePreview(long ImageId, string Type, string FileName, string? Path, bool FileWillBeDeleted,
    string ConfirmationToken, IReadOnlyList<string> Warnings);
public sealed record ImageDeleteCommand(string ConfirmationToken);
public sealed record ImageTaskLaunchResult(long TaskId, string Status, string Type, string Message);

public sealed class ImageWorkflowService(string databasePath, string imageRoot)
{
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Poster", "Thumbnail", "Fanart", "Preview", "Screenshot", "GIF" };
    private string CacheRoot => Path.GetFullPath(Path.Combine(imageRoot, ".lmm-cache", "thumbnails"));

    public async Task<ImageMutationResult> ReplaceAsync(long movieId, string type, string sourcePath,
        CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeType(type);
        if (!SupportedTypes.Contains(normalized)) throw new ArgumentException("不支持的图片类型。");
        string fullSource = Path.GetFullPath(sourcePath ?? "");
        if (!File.Exists(fullSource)) throw new KeyNotFoundException($"图片文件不存在：{fullSource}");

        ImageValidationResult validation = await ImageFileValidator.ValidateAsync(fullSource, null, cancellationToken);
        if (!validation.Valid) throw new InvalidDataException(validation.Error ?? "图片校验失败。");

        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        if (await ScalarLongAsync(connection, "SELECT COUNT(*) FROM Movies WHERE Id=$movie", cancellationToken, ("$movie", movieId)) == 0)
            throw new KeyNotFoundException("影片不存在。");
        if (await ScalarLongAsync(connection, "SELECT COUNT(*) FROM Images WHERE MovieId=$movie AND ImageType=$type AND IsLocked=1",
                cancellationToken, ("$movie", movieId), ("$type", normalized)) > 0)
            throw new InvalidOperationException("该类型图片已锁定，请先解除锁定再替换。");

        string targetDirectory = Path.Combine(imageRoot, "LMM", "Movies", movieId.ToString(), normalized);
        Directory.CreateDirectory(targetDirectory);
        string target = Path.Combine(targetDirectory, $"{normalized}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{Extension(validation.ContentType)}");
        File.Copy(fullSource, target, false);
        await RegisterImageAsync(connection, movieId, normalized, target, validation, "User", false,
            normalized is "Poster" or "Thumbnail" or "Fanart" or "Preview", "LocalReplace", cancellationToken);
        await InvalidateMovieCacheAsync(connection, movieId, cancellationToken);
        return new(true, $"{Label(normalized)} 已替换并锁定，缓存已刷新。");
    }

    public async Task<ImageDeletePreview> PreviewDeleteAsync(long imageId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        ImageRow row = await ReadImageRowAsync(connection, imageId, cancellationToken);
        string fileName = row.Path is null ? row.Type : Path.GetFileName(row.Path);
        bool fileWillBeDeleted = row.Path is not null && File.Exists(row.Path) && IsInsideImageRoot(row.Path);
        var warnings = new List<string> { "将删除图片登记并刷新缓存。" };
        warnings.Add(fileWillBeDeleted ? "图片文件位于受控图片目录内，会一并删除。" : "图片文件不在受控图片目录内或不存在，只会删除数据库登记。");
        if (row.Locked) warnings.Add("该图片处于锁定状态，本次确认后仍会删除。");
        return new(row.Id, row.Type, fileName, row.Path, fileWillBeDeleted, DeleteToken(row, fileWillBeDeleted), warnings);
    }

    public async Task<ImageMutationResult> DeleteAsync(long imageId, string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        ImageDeletePreview preview = await PreviewDeleteAsync(imageId, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(preview.ConfirmationToken),
                Encoding.UTF8.GetBytes(confirmationToken ?? "")))
            throw new UnauthorizedAccessException("图片内容已变化，请重新预览后确认。");

        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        ImageRow row = await ReadImageRowAsync(connection, imageId, cancellationToken);
        await ExecuteAsync(connection, "DELETE FROM Images WHERE Id=$id", cancellationToken, ("$id", imageId));
        if (preview.FileWillBeDeleted && row.Path is not null && File.Exists(row.Path)) File.Delete(row.Path);
        if (row.MovieId.HasValue) await InvalidateMovieCacheAsync(connection, row.MovieId.Value, cancellationToken);
        return new(true, $"{Label(row.Type)} 已删除，缓存已刷新。");
    }

    public async Task<PlatformOpenResult> RevealAsync(long imageId, PlatformCommandService platform,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        ImageRow row = await ReadImageRowAsync(connection, imageId, cancellationToken);
        if (string.IsNullOrWhiteSpace(row.Path)) throw new KeyNotFoundException("图片文件不存在。");
        return platform.RevealFile(row.Path);
    }

    public async Task<PlatformOpenResult> OpenDirectoryAsync(long imageId, PlatformCommandService platform,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        ImageRow row = await ReadImageRowAsync(connection, imageId, cancellationToken);
        if (string.IsNullOrWhiteSpace(row.Path)) throw new KeyNotFoundException("图片目录不存在。");
        return platform.OpenDirectory(row.Path);
    }

    public async Task RegisterGeneratedAsync(long movieId, string type, string path, CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeType(type);
        ImageValidationResult validation = await ImageFileValidator.ValidateAsync(path, null, cancellationToken);
        if (!validation.Valid) throw new InvalidDataException(validation.Error ?? "生成图片校验失败。");
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await RegisterImageAsync(connection, movieId, normalized, path, validation, "Generated", true,
            normalized is "Poster" or "Preview", "FFmpeg", cancellationToken);
        await InvalidateMovieCacheAsync(connection, movieId, cancellationToken);
    }

    public async Task InvalidateMovieCacheAsync(long movieId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await InvalidateMovieCacheAsync(connection, movieId, cancellationToken);
    }

    private async Task RegisterImageAsync(SqliteConnection connection, long movieId, string type, string path,
        ImageValidationResult validation, string ownership, bool derived, bool primary, string provider,
        CancellationToken cancellationToken)
    {
        string at = Now();
        if (primary) await ExecuteAsync(connection, "UPDATE Images SET IsPrimary=0,UpdatedAt=$at WHERE MovieId=$movie AND ImageType=$type AND IsLocked=0",
            cancellationToken, ("$at", at), ("$movie", movieId), ("$type", type));
        await ExecuteAsync(connection, """
            INSERT INTO Images(MovieId,ActorId,ImageType,FilePath,Width,Height,FileSize,FileHash,IsPrimary,SourceProvider,
                DownloadedAt,CreatedAt,UpdatedAt,Ownership,IsLocked,IsDerived,ContentType,ValidationStatus,ValidatedAt)
            VALUES($movie,NULL,$type,$path,$width,$height,$size,$hash,$primary,$provider,$at,$at,$at,$ownership,
                CASE WHEN $ownership='User' THEN 1 ELSE 0 END,$derived,$content,'Valid',$at)
            """, cancellationToken, ("$movie", movieId), ("$type", type), ("$path", path), ("$width", validation.Width),
            ("$height", validation.Height), ("$size", validation.FileSize), ("$hash", validation.Sha256),
            ("$primary", primary ? 1 : 0), ("$provider", provider), ("$at", at), ("$ownership", ownership),
            ("$derived", derived ? 1 : 0), ("$content", validation.ContentType));
    }

    private async Task InvalidateMovieCacheAsync(SqliteConnection connection, long movieId, CancellationToken cancellationToken)
    {
        var rows = new List<(long Id, string Path)>();
        await using (SqliteCommand query = connection.CreateCommand()) {
            query.CommandText = "SELECT Id,CachePath FROM ImageCacheEntries WHERE MovieId=$movie";
            query.Parameters.AddWithValue("$movie", movieId);
            await using SqliteDataReader reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetInt64(0), reader.GetString(1)));
        }
        foreach ((long id, string path) in rows) {
            if (IsInsideCache(path) && File.Exists(path)) File.Delete(path);
            await ExecuteAsync(connection, "DELETE FROM ImageCacheEntries WHERE Id=$id", cancellationToken, ("$id", id));
        }
    }

    private async Task<ImageRow> ReadImageRowAsync(SqliteConnection connection, long imageId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id,MovieId,ImageType,FilePath,IsLocked,COALESCE(UpdatedAt,''),COALESCE(FileSize,0) FROM Images WHERE Id=$id";
        command.Parameters.AddWithValue("$id", imageId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("图片不存在。");
        return new(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), NormalizeType(reader.GetString(2)),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt64(4) == 1, reader.GetString(5), reader.GetInt64(6));
    }

    private bool IsInsideImageRoot(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetFullPath(imageRoot);
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInsideCache(string path)
    {
        string full = Path.GetFullPath(path);
        return full.StartsWith(CacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string DeleteToken(ImageRow row, bool fileWillBeDeleted)
    {
        string state = $"{row.Id}|{row.Type}|{Path.GetFullPath(row.Path ?? "")}|{row.UpdatedAt}|{row.FileSize}|{row.Locked}|{fileWillBeDeleted}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("lmm-image-delete|" + state))).ToLowerInvariant();
    }

    internal static string NormalizeType(string type) => type.Trim().ToLowerInvariant() switch {
        "thumb" or "thumbnail" or "smallpic" => "Thumbnail",
        "bigpic" or "fanart" or "background" => "Fanart",
        "extrapic" or "preview" => "Preview",
        "gif" => "GIF",
        "screenshot" or "screen" => "Screenshot",
        _ => "Poster",
    };

    internal static string Label(string type) => NormalizeType(type) switch {
        "Thumbnail" => "缩略图",
        "Fanart" => "背景图",
        "Preview" => "预览图",
        "Screenshot" => "截图",
        "GIF" => "GIF",
        _ => "封面",
    };

    private static string Extension(string? contentType) => contentType switch {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".jpg",
    };

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Shared }.ToString());
        await connection.OpenAsync(token);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", token);
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L);
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    private sealed record ImageRow(long Id, long? MovieId, string Type, string? Path, bool Locked, string UpdatedAt, long FileSize);
}

public sealed class ImageGenerationTaskService(
    string databasePath,
    string imageRoot,
    ImageWorkflowService images,
    TaskLogService logs,
    FfmpegLocator ffmpegLocator) : BackgroundService
{
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase) { "Poster", "Preview", "Screenshot", "GIF" };
    private readonly ConcurrentDictionary<long, CancellationTokenSource> cancellations = new();

    public async Task<ImageTaskLaunchResult> EnqueueAsync(long movieId, string type, CancellationToken cancellationToken = default)
    {
        string normalized = ImageWorkflowService.NormalizeType(type);
        if (!Types.Contains(normalized)) throw new ArgumentException("该图片类型不支持生成任务。");
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        if (await ScalarLongAsync(connection, "SELECT COUNT(*) FROM Movies WHERE Id=$movie", cancellationToken, ("$movie", movieId)) == 0)
            throw new KeyNotFoundException("影片不存在。");
        long active = await ScalarLongAsync(connection,
            "SELECT COALESCE(MAX(Id),0) FROM Tasks WHERE TaskType=$type AND CurrentMovieId=$movie AND Status NOT IN ('Completed','Failed','Cancelled')",
            cancellationToken, ("$type", normalized), ("$movie", movieId));
        if (active > 0) return new(active, "Pending", normalized, $"{ImageWorkflowService.Label(normalized)} 生成任务已在队列中。");

        string payload = JsonSerializer.Serialize(new { MovieId = movieId, Type = normalized });
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Tasks(TaskType,Status,Stage,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt,CurrentMovieId)
            VALUES($type,'Pending','Pending',0,1,0,$payload,$at,$at,$movie); SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$type", normalized);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$at", Now());
        command.Parameters.AddWithValue("$movie", movieId);
        long id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        await logs.WriteAsync(id, "Info", $"{ImageWorkflowService.Label(normalized)} 生成任务已创建。", cancellationToken);
        return new(id, "Pending", normalized, $"{ImageWorkflowService.Label(normalized)} 任务已进入任务中心。");
    }

    public Task<TaskMutationResult> PauseAsync(long id) => UpdateCommandAsync(id, "Paused", "Paused", "图片生成任务已暂停。", false);
    public Task<TaskMutationResult> ResumeAsync(long id) => UpdateCommandAsync(id, "Pending", "Pending", "图片生成任务已继续。", false);

    public async Task<TaskMutationResult> CancelAsync(long id)
    {
        if (cancellations.TryGetValue(id, out CancellationTokenSource? source)) source.Cancel();
        return await UpdateCommandAsync(id, "Cancelled", "Cancelled", "图片生成任务已取消。", true);
    }

    public async Task<ImageTaskLaunchResult> RetryAsync(long id)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        string? type = await ScalarTextAsync(connection, "SELECT TaskType FROM Tasks WHERE Id=$id", CancellationToken.None, ("$id", id));
        string? status = await ScalarTextAsync(connection, "SELECT Status FROM Tasks WHERE Id=$id", CancellationToken.None, ("$id", id));
        if (type is null || !Types.Contains(type)) throw new KeyNotFoundException("图片生成任务不存在。");
        if (status is not ("Failed" or "Cancelled")) throw new InvalidOperationException("只有失败或已取消的图片生成任务可以重试。");
        await ExecuteAsync(connection,
            "UPDATE Tasks SET Status='Pending',Stage='Pending',Progress=0,CompletedItems=0,ErrorMessage=NULL,CompletedAt=NULL,CancellationRequested=0,RetryCount=RetryCount+1,UpdatedAt=$at WHERE Id=$id",
            CancellationToken.None, ("$at", Now()), ("$id", id));
        await logs.WriteAsync(id, "Info", "图片生成任务已重试。");
        return new(id, "Pending", type, "图片生成任务已重试。");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested) {
            try {
                long? taskId = await ClaimAsync(stoppingToken);
                if (taskId is null) { await Task.Delay(750, stoppingToken); continue; }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cancellations[taskId.Value] = linked;
                try { await RunAsync(taskId.Value, linked.Token); }
                finally { cancellations.TryRemove(taskId.Value, out _); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { Console.Error.WriteLine($"Image generation runner: {error}"); await Task.Delay(1000, stoppingToken); }
        }
    }

    private async Task RunAsync(long taskId, CancellationToken token)
    {
        try {
            TaskInput input = await ReadInputAsync(taskId, token);
            await EnsureRunnableAsync(taskId, token);
            string? videoPath = await ReadPrimaryVideoAsync(input.MovieId, token);
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
                throw new FileNotFoundException("影片文件不存在，无法生成图片。", videoPath);
            FfmpegLookupResult lookup = ffmpegLocator.Locate();
            if (!lookup.Found || string.IsNullOrWhiteSpace(lookup.Path))
                throw new FileNotFoundException(lookup.Message);
            string ffmpeg = lookup.Path;
            string target = TargetPath(input.MovieId, input.Type);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await logs.WriteAsync(taskId, "Info", $"使用 FFmpeg 生成 {ImageWorkflowService.Label(input.Type)}（{lookup.Source}）。", token);
            await ExecuteAsync(await OpenAsync(SqliteOpenMode.ReadWrite, token),
                "UPDATE Tasks SET Status='Running',Stage='Running',Progress=25,StartedAt=COALESCE(StartedAt,$at),UpdatedAt=$at WHERE Id=$id",
                token, ("$at", Now()), ("$id", taskId));
            await RunFfmpegAsync(ffmpeg, videoPath, target, input.Type, token);
            await images.RegisterGeneratedAsync(input.MovieId, input.Type, target, token);
            await CompleteAsync(taskId, input, token);
        }
        catch (OperationCanceledException) {
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
            await ExecuteAsync(connection, "UPDATE Tasks SET Status='Cancelled',Stage='Cancelled',CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",
                CancellationToken.None, ("$at", Now()), ("$id", taskId));
        }
        catch (Exception error) {
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
            await ExecuteAsync(connection,
                "UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,ResultSummary=$summary,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",
                CancellationToken.None, ("$error", error.Message), ("$summary", $"图片生成失败：{error.Message}"), ("$at", Now()), ("$id", taskId));
            await logs.WriteAsync(taskId, "Error", error.Message);
        }
    }

    private async Task<TaskInput> ReadInputAsync(long taskId, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TaskType,CurrentMovieId FROM Tasks WHERE Id=$id";
        command.Parameters.AddWithValue("$id", taskId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("图片生成任务不存在。");
        return new(reader.GetInt64(1), reader.GetString(0));
    }

    private async Task<string?> ReadPrimaryVideoAsync(long movieId, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        return await ScalarTextAsync(connection,
            "SELECT FilePath FROM MediaFiles WHERE MovieId=$movie AND MediaType='Video' ORDER BY IsPrimary DESC,Id LIMIT 1",
            token, ("$movie", movieId));
    }

    private string TargetPath(long movieId, string type)
    {
        string extension = type.Equals("GIF", StringComparison.OrdinalIgnoreCase) ? ".gif" : ".jpg";
        return Path.Combine(imageRoot, "LMM", "Movies", movieId.ToString(), type, $"{type}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{extension}");
    }

    private static async Task RunFfmpegAsync(string ffmpeg, string videoPath, string target, string type, CancellationToken token)
    {
        List<string> args = type.Equals("GIF", StringComparison.OrdinalIgnoreCase)
            ? ["-y", "-ss", "00:00:05", "-t", "3", "-i", videoPath, "-vf", "fps=8,scale=480:-1:flags=lanczos", target]
            : ["-y", "-ss", "00:00:05", "-i", videoPath, "-frames:v", "1", target];
        using Process process = new() {
            StartInfo = new ProcessStartInfo {
                FileName = ffmpeg,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
        foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        string stderr = await process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0 || !File.Exists(target))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "FFmpeg 生成失败。" : stderr.Trim().Split('\n').Last().Trim());
    }

    private async Task CompleteAsync(long taskId, TaskInput input, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        string summary = $"{ImageWorkflowService.Label(input.Type)} 已生成，详情页图片缓存已刷新。";
        await ExecuteAsync(connection,
            "UPDATE Tasks SET Status='Completed',Stage='Completed',Progress=100,CompletedItems=1,ResultSummary=$summary,ErrorMessage=NULL,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",
            token, ("$summary", summary), ("$at", Now()), ("$id", taskId));
        await logs.WriteAsync(taskId, "Info", summary, token);
    }

    private async Task<long?> ClaimAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        string placeholders = string.Join(",", Types.Select((_, index) => $"$t{index}"));
        var parameters = Types.Select((value, index) => ($"$t{index}", (object?)value)).ToList();
        long id = await ScalarLongAsync(connection, $"SELECT COALESCE(MIN(Id),0) FROM Tasks WHERE TaskType IN ({placeholders}) AND Status='Pending'",
            token, parameters.ToArray());
        if (id == 0) return null;
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Preparing',Stage='Preparing',UpdatedAt=$at WHERE Id=$id AND Status='Pending'",
            token, ("$at", Now()), ("$id", id));
        return id;
    }

    private async Task RecoverInterruptedAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        string placeholders = string.Join(",", Types.Select((_, index) => $"$t{index}"));
        var parameters = Types.Select((value, index) => ($"$t{index}", (object?)value)).ToList();
        parameters.Add(("$at", Now()));
        await ExecuteAsync(connection, $"UPDATE Tasks SET Status='Pending',Stage='Pending',ErrorMessage='上次运行异常中断，已恢复到队列。',UpdatedAt=$at WHERE TaskType IN ({placeholders}) AND Status IN ('Preparing','Running')",
            token, parameters.ToArray());
    }

    private async Task EnsureRunnableAsync(long id, CancellationToken token)
    {
        while (true) {
            token.ThrowIfCancellationRequested();
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
            string? status = await ScalarTextAsync(connection, "SELECT Status FROM Tasks WHERE Id=$id", token, ("$id", id));
            if (status == "Cancelled") throw new OperationCanceledException(token);
            if (status != "Paused") return;
            await Task.Delay(250, token);
        }
    }

    private async Task<TaskMutationResult> UpdateCommandAsync(long id, string status, string stage, string message, bool cancel)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        string? type = await ScalarTextAsync(connection, "SELECT TaskType FROM Tasks WHERE Id=$id", CancellationToken.None, ("$id", id));
        if (type is null || !Types.Contains(type)) throw new KeyNotFoundException("图片生成任务不存在。");
        await ExecuteAsync(connection,
            "UPDATE Tasks SET Status=$status,Stage=$stage,CancellationRequested=$cancel,CompletedAt=CASE WHEN $status='Cancelled' THEN $at ELSE CompletedAt END,UpdatedAt=$at WHERE Id=$id",
            CancellationToken.None, ("$status", status), ("$stage", stage), ("$cancel", cancel ? 1 : 0), ("$at", Now()), ("$id", id));
        await logs.WriteAsync(id, status == "Cancelled" ? "Warning" : "Info", message);
        return new(id, status, message);
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token = default)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Shared }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L);
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (await command.ExecuteScalarAsync(token))?.ToString();
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    private sealed record TaskInput(long MovieId, string Type);
}
