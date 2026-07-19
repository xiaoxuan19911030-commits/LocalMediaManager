using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualBasic.FileIO;

namespace LocalMediaManager.Bridge;

public sealed record SafeDeletePreviewCommand(IReadOnlyList<long> MovieIds, string Mode, bool DeleteDatabaseInfo = true);
public sealed record SafeDeleteExecuteCommand(string ConfirmationToken, bool ConfirmOriginalMedia = false, int? ConfirmCount = null, bool AllowPermanentDelete = false);
public sealed record SafeDeleteExecuteRequest(IReadOnlyList<long> MovieIds, string Mode, bool DeleteDatabaseInfo,
    string ConfirmationToken, bool ConfirmOriginalMedia = false, int? ConfirmCount = null, bool AllowPermanentDelete = false);
public sealed record SafeDeleteLaunchResult(long TaskId, string Status, int TotalItems, string Message);
public sealed record SafeDeletePreview(string Mode, bool DeleteDatabaseInfo, int MovieCount, long EstimatedBytes,
    bool DeletesOriginalMedia, bool RequiresStrongConfirmation, string ConfirmationToken,
    IReadOnlyList<string> Warnings, IReadOnlyList<SafeDeleteMoviePreview> Items);
public sealed record SafeDeleteMoviePreview(long MovieId, string Code, string Title, string? PrimaryPath,
    bool PrimaryExists, long EstimatedBytes, IReadOnlyList<string> DatabaseInfo,
    IReadOnlyList<SafeDeletePathPreview> Files, IReadOnlyList<string> Warnings);
public sealed record SafeDeletePathPreview(string Kind, string Path, bool Exists, long Size, bool WillDelete, string Status, string? Reason);
public sealed record SafeDeleteTaskPayload(string Mode, bool DeleteDatabaseInfo, IReadOnlyList<long> MovieIds,
    IReadOnlyList<SafeDeleteMoviePreview> Items);
public sealed record SafeDeleteMovieResult(long MovieId, string Code, string Status, string Message,
    IReadOnlyList<SafeDeleteFileResult> Files, long? AuditId);
public sealed record SafeDeleteFileResult(string Path, string Kind, string Status, string Message);
public sealed record SafeDeleteTaskResult(int Total, int Success, int Failed, IReadOnlyList<SafeDeleteMovieResult> Items);

public sealed class SafeDeleteWorkflowService(
    string databasePath,
    ProductWriter writer,
    TaskLogService logs,
    RatingHistoryService ratingHistory) : BackgroundService
{
    private static readonly HashSet<string> Modes = new(StringComparer.OrdinalIgnoreCase) { "metadata", "media" };
    private readonly ConcurrentDictionary<long, CancellationTokenSource> cancellations = new();

    public async Task<SafeDeletePreview> PreviewAsync(SafeDeletePreviewCommand command, CancellationToken token = default)
    {
        string mode = NormalizeMode(command.Mode);
        long[] ids = command.MovieIds.Distinct().Where(id => id > 0).Take(500).ToArray();
        if (ids.Length == 0) throw new ArgumentException("请选择至少 1 部影片。");
        if (command.MovieIds.Count > 500) throw new ArgumentException("单次最多处理 500 部影片。");

        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        var items = new List<SafeDeleteMoviePreview>();
        foreach (long id in ids)
            items.Add(await BuildMoviePreviewAsync(connection, id, mode, command.DeleteDatabaseInfo, token));

        long bytes = items.Sum(item => item.Files.Where(file => file.WillDelete).Sum(file => file.Size));
        bool deletesOriginal = mode == "media";
        var warnings = new List<string> {
            mode == "metadata"
                ? "删除信息只移除数据库中的影片记录与关联关系，不删除原始媒体文件。"
                : "此操作将删除原始影片文件，默认移动到系统回收站。",
            "只删除数据库已登记且可明确归属当前影片的文件；无法确认归属的附近文件不会被删除。"
        };
        if (mode == "media" && command.DeleteDatabaseInfo)
            warnings.Add("只有影片文件成功移入回收站后，才会删除对应数据库记录；失败项会保留记录。");

        return new(mode, command.DeleteDatabaseInfo, items.Count, bytes, deletesOriginal,
            deletesOriginal, Token(mode, command.DeleteDatabaseInfo, items), warnings, items);
    }

    public async Task<SafeDeleteLaunchResult> ExecuteAsync(SafeDeletePreviewCommand previewCommand,
        SafeDeleteExecuteCommand executeCommand, CancellationToken token = default)
    {
        SafeDeletePreview preview = await PreviewAsync(previewCommand, token);
        VerifyToken(preview, executeCommand.ConfirmationToken);
        if (preview.DeletesOriginalMedia && !executeCommand.ConfirmOriginalMedia)
            throw new UnauthorizedAccessException("删除原始影片文件需要额外确认。");
        if (preview.MovieCount > 1 && preview.DeletesOriginalMedia && executeCommand.ConfirmCount != preview.MovieCount)
            throw new UnauthorizedAccessException("批量删除影片需要输入正确数量确认。");

        string taskType = preview.Mode == "media" ? "DeleteMedia" : "DeleteMetadata";
        string payload = JsonSerializer.Serialize(new SafeDeleteTaskPayload(preview.Mode, preview.DeleteDatabaseInfo,
            preview.Items.Select(item => item.MovieId).ToArray(), preview.Items));
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Tasks(TaskType,Status,Stage,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt)
            VALUES($type,'Pending','Pending',0,$total,0,$payload,$at,$at);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$type", taskType);
        command.Parameters.AddWithValue("$total", preview.MovieCount);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$at", Now());
        long taskId = Convert.ToInt64(await command.ExecuteScalarAsync(token));
        await logs.WriteAsync(taskId, "Info", $"{TaskLabel(taskType)}已创建，等待执行。", token);
        return new(taskId, "Pending", preview.MovieCount, $"{TaskLabel(taskType)}已进入任务中心。");
    }

    public Task<SafeDeleteLaunchResult> ExecuteAsync(SafeDeleteExecuteRequest request, CancellationToken token = default) =>
        ExecuteAsync(new SafeDeletePreviewCommand(request.MovieIds, request.Mode, request.DeleteDatabaseInfo),
            new SafeDeleteExecuteCommand(request.ConfirmationToken, request.ConfirmOriginalMedia, request.ConfirmCount, request.AllowPermanentDelete), token);

    public Task<TaskMutationResult> PauseAsync(long id) => UpdateCommandAsync(id, "Paused", "Paused", "删除任务已暂停。", false);
    public Task<TaskMutationResult> ResumeAsync(long id) => UpdateCommandAsync(id, "Pending", "Pending", "删除任务已继续。", false);

    public async Task<TaskMutationResult> CancelAsync(long id)
    {
        if (cancellations.TryGetValue(id, out CancellationTokenSource? source)) source.Cancel();
        return await UpdateCommandAsync(id, "Cancelled", "Cancelled", "删除任务已取消；已成功处理的项目不会回滚。", true);
    }

    public async Task<SafeDeleteLaunchResult> RetryAsync(long id)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        TaskSnapshot snapshot = await ReadSnapshotAsync(connection, id);
        if (!IsDeleteTask(snapshot.Type)) throw new KeyNotFoundException("删除任务不存在。");
        if (snapshot.Status is not ("Failed" or "Cancelled" or "CompletedWithErrors"))
            throw new InvalidOperationException("只有失败、部分失败或已取消的删除任务可以重试。");
        await ExecuteAsync(connection,
            "UPDATE Tasks SET Status='Pending',Stage='Pending',Progress=0,ErrorMessage=NULL,CompletedAt=NULL,CancellationRequested=0,RetryCount=RetryCount+1,UpdatedAt=$at WHERE Id=$id",
            CancellationToken.None, ("$at", Now()), ("$id", id));
        await logs.WriteAsync(id, "Info", "删除任务已重新进入队列；已成功项目不会重复执行。");
        return new(id, "Pending", (int)snapshot.TotalItems, "删除任务已重新进入队列。");
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
            catch (Exception error) { Console.Error.WriteLine($"Safe delete runner: {error}"); await Task.Delay(1000, stoppingToken); }
        }
    }

    private async Task RunAsync(long taskId, CancellationToken token)
    {
        SafeDeleteTaskPayload payload = await ReadPayloadAsync(taskId, token);
        SafeDeleteTaskResult? previous = await ReadPreviousResultAsync(taskId, token);
        HashSet<long> alreadySucceeded = previous?.Items.Where(item => item.Status == "Success").Select(item => item.MovieId).ToHashSet() ?? [];
        var results = previous?.Items.Where(item => item.Status == "Success").ToList() ?? [];
        var pendingItems = payload.Items.Where(item => !alreadySucceeded.Contains(item.MovieId)).ToList();
        int completed = alreadySucceeded.Count;
        try {
            await logs.WriteAsync(taskId, "Info", $"开始{(payload.Mode == "media" ? "删除影片" : "删除信息")}，待处理 {pendingItems.Count} 项。", token);
            foreach (SafeDeleteMoviePreview item in pendingItems) {
                token.ThrowIfCancellationRequested();
                await EnsureRunnableAsync(taskId, token);
                SafeDeleteMovieResult result = payload.Mode == "media"
                    ? await DeleteMediaItemAsync(item, payload.DeleteDatabaseInfo, token)
                    : await DeleteMetadataItemAsync(item, token);
                results.Add(result);
                completed++;
                await PersistProgressAsync(taskId, payload.Items.Count, completed, results, token);
                await logs.WriteAsync(taskId, result.Status == "Success" ? "Info" : "Error",
                    $"{item.Code}: {result.Message}", token);
            }
            await CompleteTaskAsync(taskId, payload.Items.Count, results, token);
        }
        catch (OperationCanceledException) {
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
            await ExecuteAsync(connection, "UPDATE Tasks SET Status='Cancelled',Stage='Cancelled',CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",
                CancellationToken.None, ("$at", Now()), ("$id", taskId));
        }
        catch (Exception error) {
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
            await ExecuteAsync(connection, "UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,ResultSummary='删除任务失败，未完成项保持原状。',CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",
                CancellationToken.None, ("$error", error.Message), ("$at", Now()), ("$id", taskId));
            await logs.WriteAsync(taskId, "Error", error.Message);
        }
    }

    private async Task<SafeDeleteMovieResult> DeleteMetadataItemAsync(SafeDeleteMoviePreview item, CancellationToken token)
    {
        try {
            MovieDeletePreview preview = await writer.PreviewDeleteMovieAsync(item.MovieId);
            MutationResult result = await writer.DeleteMovieAsync(item.MovieId, new(preview.ConfirmationToken));
            return new(item.MovieId, item.Code, "Success", result.Message, [], result.AuditId);
        }
        catch (Exception error) {
            return new(item.MovieId, item.Code, "DbUpdateFailed", error.Message, [], null);
        }
    }

    private async Task<SafeDeleteMovieResult> DeleteMediaItemAsync(SafeDeleteMoviePreview item, bool deleteDatabaseInfo, CancellationToken token)
    {
        await ratingHistory.RememberAsync(item.MovieId, token);
        var fileResults = new List<SafeDeleteFileResult>();
        foreach (SafeDeletePathPreview file in item.Files.Where(file => file.WillDelete)) {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(file.Path)) {
                fileResults.Add(new(file.Path, file.Kind, file.Kind == "Video" ? "FileMissing" : "Skipped", "文件不存在。"));
                continue;
            }
            try {
                FileSystem.DeleteFile(file.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                fileResults.Add(new(file.Path, file.Kind, "Success", "已移入系统回收站。"));
            }
            catch (IOException error) {
                fileResults.Add(new(file.Path, file.Kind, "InUse", error.Message));
            }
            catch (UnauthorizedAccessException error) {
                fileResults.Add(new(file.Path, file.Kind, "AccessDenied", error.Message));
            }
            catch (Exception error) {
                fileResults.Add(new(file.Path, file.Kind, "DeleteFailed", error.Message));
            }
        }

        bool originalFailed = fileResults.Any(result => result.Kind == "Video" && result.Status != "Success");
        bool anyFailed = fileResults.Any(result => result.Status is "AccessDenied" or "InUse" or "DeleteFailed");
        if (originalFailed || anyFailed)
            return new(item.MovieId, item.Code, originalFailed ? "DeleteFailed" : "CompletedWithErrors",
                "部分文件未删除，数据库记录已保留。", fileResults, null);

        long? auditId = null;
        string message = "影片文件已移入回收站。";
        if (deleteDatabaseInfo) {
            MovieDeletePreview preview = await writer.PreviewDeleteMovieAsync(item.MovieId);
            MutationResult deleted = await writer.DeleteMovieAsync(item.MovieId, new(preview.ConfirmationToken));
            auditId = deleted.AuditId;
            message = "影片文件已移入回收站，数据库记录已删除。";
        }
        return new(item.MovieId, item.Code, "Success", message, fileResults, auditId);
    }

    private async Task<SafeDeleteMoviePreview> BuildMoviePreviewAsync(SqliteConnection connection, long movieId,
        string mode, bool deleteDatabaseInfo, CancellationToken token)
    {
        await using SqliteCommand movie = connection.CreateCommand();
        movie.CommandText = "SELECT Id,COALESCE(Code,''),COALESCE(Title,''),COALESCE(NfoPath,'') FROM Movies WHERE Id=$id";
        movie.Parameters.AddWithValue("$id", movieId);
        await using SqliteDataReader reader = await movie.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new KeyNotFoundException($"影片 {movieId} 不存在。");
        string code = string.IsNullOrWhiteSpace(reader.GetString(1)) ? $"#{movieId}" : reader.GetString(1);
        string title = reader.GetString(2);
        string? nfo = string.IsNullOrWhiteSpace(reader.GetString(3)) ? null : reader.GetString(3);
        await reader.DisposeAsync();

        var files = new List<SafeDeletePathPreview>();
        await using (SqliteCommand media = connection.CreateCommand()) {
            media.CommandText = "SELECT FilePath,COALESCE(FileSize,0),MediaType,IsPrimary FROM MediaFiles WHERE MovieId=$id ORDER BY IsPrimary DESC,Id";
            media.Parameters.AddWithValue("$id", movieId);
            await using SqliteDataReader rows = await media.ExecuteReaderAsync(token);
            while (await rows.ReadAsync(token)) {
                string path = rows.GetString(0);
                long size = rows.GetInt64(1);
                string kind = rows.GetString(2);
                bool exists = File.Exists(path);
                files.Add(new(kind, path, exists, exists ? SafeSize(path, size) : size, mode == "media", exists ? "Ready" : "Missing",
                    exists ? null : "文件不存在，删除媒体时将保留数据库记录。"));
            }
        }
        await using (SqliteCommand images = connection.CreateCommand()) {
            images.CommandText = "SELECT ImageType,FilePath,COALESCE(FileSize,0) FROM Images WHERE MovieId=$id AND FilePath IS NOT NULL ORDER BY ImageType,Id";
            images.Parameters.AddWithValue("$id", movieId);
            await using SqliteDataReader rows = await images.ExecuteReaderAsync(token);
            while (await rows.ReadAsync(token)) {
                string path = rows.GetString(1);
                bool exists = File.Exists(path);
                files.Add(new(rows.GetString(0), path, exists, exists ? SafeSize(path, rows.GetInt64(2)) : rows.GetInt64(2),
                    mode == "media", exists ? "Ready" : "Missing", exists ? null : "图片文件不存在，仅删除登记。"));
            }
        }
        if (!string.IsNullOrWhiteSpace(nfo)) {
            bool exists = File.Exists(nfo);
            files.Add(new("NFO", nfo, exists, exists ? SafeSize(nfo, 0) : 0, mode == "media", exists ? "Ready" : "Missing",
                exists ? null : "NFO 文件不存在。"));
        }

        string? primary = files.FirstOrDefault(file => file.Kind == "Video")?.Path;
        bool primaryExists = primary is not null && File.Exists(primary);
        var dbInfo = deleteDatabaseInfo
            ? new[] { "影片基本信息", "媒体文件登记", "演员/导演/标签/类型/系列/厂商关系", "评分/收藏/播放历史", "图片登记", "任务关联与外部 ID" }
            : Array.Empty<string>();
        var warnings = new List<string>();
        if (mode == "media" && !primaryExists) warnings.Add("原始影片文件不存在，执行时不会删除数据库记录。");
        if (mode == "metadata") warnings.Add("媒体文件、图片文件和 NFO 均不会被删除。");
        return new(movieId, code, title, primary, primaryExists, files.Where(file => file.WillDelete).Sum(file => file.Size),
            dbInfo, files, warnings);
    }

    private async Task PersistProgressAsync(long taskId, int total, int completed, List<SafeDeleteMovieResult> results, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        string result = JsonSerializer.Serialize(new SafeDeleteTaskResult(total, results.Count(item => item.Status == "Success"),
            results.Count(item => item.Status != "Success"), results));
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Running',Stage='Running',Progress=$progress,CompletedItems=$done,ResultJson=$result,UpdatedAt=$at WHERE Id=$id",
            token, ("$progress", total == 0 ? 0 : Math.Round(completed * 100d / total, 2)), ("$done", completed),
            ("$result", result), ("$at", Now()), ("$id", taskId));
    }

    private async Task CompleteTaskAsync(long taskId, int total, List<SafeDeleteMovieResult> results, CancellationToken token)
    {
        int failed = results.Count(item => item.Status != "Success");
        string status = failed == 0 ? "Completed" : results.Any(item => item.Status == "Success") ? "CompletedWithErrors" : "Failed";
        string summary = failed == 0 ? $"删除任务完成：{total} 项成功。" : $"删除任务完成：{results.Count - failed} 项成功，{failed} 项失败。";
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteAsync(connection, "UPDATE Tasks SET Status=$status,Stage=$status,Progress=100,ResultSummary=$summary,ErrorMessage=$error,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",
            token, ("$status", status), ("$summary", summary), ("$error", failed == 0 ? null : summary), ("$at", Now()), ("$id", taskId));
        await logs.WriteAsync(taskId, failed == 0 ? "Info" : "Warning", summary, token);
    }

    private async Task<long?> ClaimAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        long id = await ScalarLongAsync(connection, "SELECT COALESCE(MIN(Id),0) FROM Tasks WHERE TaskType IN ('DeleteMetadata','DeleteMedia') AND Status='Pending'", token);
        if (id == 0) return null;
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Running',Stage='Running',StartedAt=COALESCE(StartedAt,$at),UpdatedAt=$at WHERE Id=$id AND Status='Pending'",
            token, ("$at", Now()), ("$id", id));
        return id;
    }

    private async Task RecoverInterruptedAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Pending',Stage='Pending',ErrorMessage='上次运行异常中断，已恢复到队列。',UpdatedAt=$at WHERE TaskType IN ('DeleteMetadata','DeleteMedia') AND Status='Running'",
            token, ("$at", Now()));
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

    private async Task<SafeDeleteTaskPayload> ReadPayloadAsync(long taskId, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        string? json = await ScalarTextAsync(connection, "SELECT PayloadJson FROM Tasks WHERE Id=$id", token, ("$id", taskId));
        return JsonSerializer.Deserialize<SafeDeleteTaskPayload>(json ?? "") ?? throw new InvalidDataException("删除任务缺少执行计划。");
    }

    private async Task<SafeDeleteTaskResult?> ReadPreviousResultAsync(long taskId, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        string? json = await ScalarTextAsync(connection, "SELECT ResultJson FROM Tasks WHERE Id=$id", token, ("$id", taskId));
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<SafeDeleteTaskResult>(json);
    }

    private async Task<TaskMutationResult> UpdateCommandAsync(long id, string status, string stage, string message, bool cancel)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        TaskSnapshot snapshot = await ReadSnapshotAsync(connection, id);
        if (!IsDeleteTask(snapshot.Type)) throw new KeyNotFoundException("删除任务不存在。");
        await ExecuteAsync(connection, "UPDATE Tasks SET Status=$status,Stage=$stage,CancellationRequested=$cancel,CompletedAt=CASE WHEN $status='Cancelled' THEN $at ELSE CompletedAt END,UpdatedAt=$at WHERE Id=$id",
            CancellationToken.None, ("$status", status), ("$stage", stage), ("$cancel", cancel ? 1 : 0), ("$at", Now()), ("$id", id));
        await logs.WriteAsync(id, status == "Cancelled" ? "Warning" : "Info", message);
        return new(id, status, message);
    }

    private async Task<TaskSnapshot> ReadSnapshotAsync(SqliteConnection connection, long id)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TaskType,Status,TotalItems FROM Tasks WHERE Id=$id";
        command.Parameters.AddWithValue("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new KeyNotFoundException("任务不存在。");
        return new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2));
    }

    private static string NormalizeMode(string mode)
    {
        string value = (mode ?? "").Trim().ToLowerInvariant();
        if (!Modes.Contains(value)) throw new ArgumentException("删除模式必须是 metadata 或 media。");
        return value;
    }

    private static void VerifyToken(SafeDeletePreview preview, string token)
    {
        string expected = Token(preview.Mode, preview.DeleteDatabaseInfo, preview.Items);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(token ?? "")))
            throw new UnauthorizedAccessException("确认令牌无效或影响范围已变化，请重新预览。");
    }

    private static string Token(string mode, bool deleteDatabaseInfo, IReadOnlyList<SafeDeleteMoviePreview> items)
    {
        string state = JsonSerializer.Serialize(new {
            mode,
            deleteDatabaseInfo,
            items = items.Select(item => new {
                item.MovieId,
                item.Code,
                files = item.Files.Select(file => new { file.Kind, Path = Path.GetFullPath(file.Path), file.Exists, file.Size, file.WillDelete })
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("lmm-safe-delete|" + state))).ToLowerInvariant();
    }

    private static bool IsDeleteTask(string type) => type is "DeleteMetadata" or "DeleteMedia";
    private static string TaskLabel(string type) => type == "DeleteMedia" ? "删除影片任务" : "删除信息任务";
    private static long SafeSize(string path, long fallback) { try { return new FileInfo(path).Length; } catch { return fallback; } }
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token = default)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Private }.ToString());
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

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (await command.ExecuteScalarAsync(token))?.ToString();
    }

    private sealed record TaskSnapshot(string Type, string Status, long TotalItems);
}
