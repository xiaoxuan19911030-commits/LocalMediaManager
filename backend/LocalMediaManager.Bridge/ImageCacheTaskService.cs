using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace LocalMediaManager.Bridge;

public sealed class ImageCacheTaskService(
    string databasePath,
    ImageAssetService images,
    TaskLogService logs) : BackgroundService
{
    private readonly ConcurrentDictionary<long, CancellationTokenSource> cancellations = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await RecoverInterruptedAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception error) { Console.Error.WriteLine($"Image cache recovery skipped: {error}"); }
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
            catch (Exception error) { Console.Error.WriteLine($"Image cache runner: {error}"); await Task.Delay(1000, stoppingToken); }
        }
    }

    public async Task<ImageCacheRebuildLaunchResult> EnqueueAsync()
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        await using (SqliteCommand existing = connection.CreateCommand()) {
            existing.CommandText = "SELECT Id,Status,TotalItems FROM Tasks WHERE TaskType='ImageCacheRebuild' AND Status NOT IN ('Completed','Failed','Cancelled') ORDER BY Id DESC LIMIT 1";
            await using SqliteDataReader reader = await existing.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return new(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), "图片缓存重建任务已在队列中。");
        }
        long total = (await images.ReadThumbnailCandidatesAsync()).Count;
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Tasks(TaskType,Status,Stage,Progress,TotalItems,CompletedItems,CreatedAt,UpdatedAt) VALUES('ImageCacheRebuild','Pending','Pending',0,$total,0,$at,$at); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$total", total); command.Parameters.AddWithValue("$at", Now());
        long id = Convert.ToInt64(await command.ExecuteScalarAsync());
        await logs.WriteAsync(id, "Info", $"图片缓存重建任务已创建，共 {total} 部影片。");
        return new(id, "Pending", total, "图片缓存重建任务已进入任务中心。");
    }

    public Task<TaskMutationResult> PauseAsync(long id) =>
        UpdateCommandAsync(id, "Paused", "Paused", "图片缓存重建任务已暂停。", false);

    public Task<TaskMutationResult> ResumeAsync(long id) =>
        UpdateCommandAsync(id, "Pending", "Pending", "图片缓存重建任务已继续。", false);

    public async Task<TaskMutationResult> CancelAsync(long id)
    {
        if (cancellations.TryGetValue(id, out CancellationTokenSource? source)) source.Cancel();
        return await UpdateCommandAsync(id, "Cancelled", "Cancelled", "图片缓存重建任务已取消。", true);
    }

    public async Task<ImageCacheRebuildLaunchResult> RetryAsync(long id)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        string? status = await ScalarTextAsync(connection,
            "SELECT Status FROM Tasks WHERE Id=$id AND TaskType='ImageCacheRebuild'", ("$id", id));
        if (status is null) throw new KeyNotFoundException("图片缓存任务不存在。");
        if (status is not ("Failed" or "Cancelled"))
            throw new InvalidOperationException("只有失败或已取消的图片缓存任务可以重试。");
        await ExecuteAsync(connection,
            "UPDATE Tasks SET Status='Pending',Stage='Pending',Progress=0,CompletedItems=0,ErrorMessage=NULL,CompletedAt=NULL,CancellationRequested=0,RetryCount=RetryCount+1,UpdatedAt=$at WHERE Id=$id",
            ("$at", Now()), ("$id", id));
        long total = await ScalarLongAsync(connection, "SELECT TotalItems FROM Tasks WHERE Id=$id", ("$id", id));
        await logs.WriteAsync(id, "Info", "图片缓存重建任务已重新进入队列。");
        return new(id, "Pending", total, "图片缓存重建任务已重试。");
    }

    private async Task RunAsync(long taskId, CancellationToken token)
    {
        try {
            long actorImages = await images.ImportLegacyActorAssetsAsync(token);
            IReadOnlyList<long> movieIds = await images.ReadThumbnailCandidatesAsync(token);
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
            await ExecuteAsync(connection,
                "UPDATE Tasks SET Status='Running',Stage='RebuildingThumbnails',TotalItems=$total,StartedAt=COALESCE(StartedAt,$at),UpdatedAt=$at WHERE Id=$id",
                ("$total", movieIds.Count), ("$at", Now()), ("$id", taskId));
            await logs.WriteAsync(taskId, "Info", $"开始重建影片缩略图；已兼容导入 {actorImages} 张演员头像。");
            int completed = 0, failed = 0;
            foreach (long movieId in movieIds) {
                token.ThrowIfCancellationRequested();
                string? status = await ScalarTextAsync(connection, "SELECT Status FROM Tasks WHERE Id=$id", ("$id", taskId));
                if (status == "Paused") { await logs.WriteAsync(taskId, "Info", "任务暂停，剩余影片将在继续后处理。"); return; }
                if (status == "Cancelled") return;
                try { if (!await images.RebuildThumbnailAsync(movieId, token)) failed++; }
                catch (Exception error) {
                    failed++;
                    await logs.WriteAsync(taskId, "Warning", $"影片 {movieId} 缩略图重建失败：{error.Message}");
                }
                completed++;
                double progress = movieIds.Count == 0 ? 100 : completed * 100d / movieIds.Count;
                await ExecuteAsync(connection,
                    "UPDATE Tasks SET Progress=$progress,CompletedItems=$completed,CurrentMovieId=$movie,UpdatedAt=$at WHERE Id=$id",
                    ("$progress", progress), ("$completed", completed), ("$movie", movieId), ("$at", Now()), ("$id", taskId));
            }
            string summary = $"缩略图缓存重建完成：{completed - failed} 成功，{failed} 失败，演员头像导入 {actorImages} 张。";
            await ExecuteAsync(connection,
                "UPDATE Tasks SET Status='Completed',Stage='Completed',Progress=100,ResultSummary=$summary,CompletedAt=$at,UpdatedAt=$at,CurrentMovieId=NULL WHERE Id=$id",
                ("$summary", summary), ("$at", Now()), ("$id", taskId));
            await logs.WriteAsync(taskId, failed == 0 ? "Info" : "Warning", summary);
        }
        catch (OperationCanceledException) {
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
            await ExecuteAsync(connection,
                "UPDATE Tasks SET Status='Cancelled',Stage='Cancelled',CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",
                ("$at", Now()), ("$id", taskId));
        }
        catch (Exception error) {
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
            await ExecuteAsync(connection,
                "UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",
                ("$error", error.Message), ("$at", Now()), ("$id", taskId));
            await logs.WriteAsync(taskId, "Error", $"图片缓存重建失败：{error.Message}");
        }
    }

    private async Task<long?> ClaimAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        long id = await ScalarLongAsync(connection,
            "SELECT COALESCE(MIN(Id),0) FROM Tasks WHERE TaskType='ImageCacheRebuild' AND Status='Pending'");
        if (id == 0) return null;
        await ExecuteAsync(connection,
            "UPDATE Tasks SET Status='Preparing',Stage='Preparing',UpdatedAt=$at WHERE Id=$id AND Status='Pending'",
            ("$at", Now()), ("$id", id));
        return id;
    }

    private async Task RecoverInterruptedAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteAsync(connection,
            "UPDATE Tasks SET Status='Pending',Stage='Pending',ErrorMessage='上次运行异常中断，已恢复到队列。',UpdatedAt=$at WHERE TaskType='ImageCacheRebuild' AND Status IN ('Preparing','Running')",
            ("$at", Now()));
    }

    private async Task<TaskMutationResult> UpdateCommandAsync(long id, string status, string stage,
        string message, bool cancel)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        if (await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM Tasks WHERE Id=$id AND TaskType='ImageCacheRebuild'", ("$id", id)) == 0)
            throw new KeyNotFoundException("图片缓存任务不存在。");
        await ExecuteAsync(connection,
            "UPDATE Tasks SET Status=$status,Stage=$stage,CancellationRequested=$cancel,CompletedAt=CASE WHEN $status='Cancelled' THEN $at ELSE CompletedAt END,UpdatedAt=$at WHERE Id=$id",
            ("$status", status), ("$stage", stage), ("$cancel", cancel ? 1 : 0), ("$at", Now()), ("$id", id));
        await logs.WriteAsync(id, status == "Cancelled" ? "Warning" : "Info", message);
        return new(id, status, message);
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token = default)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Shared
        }.ToString());
        await connection.OpenAsync(token);
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
}
