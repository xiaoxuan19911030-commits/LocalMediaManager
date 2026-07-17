using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace LocalMediaManager.Bridge;

#pragma warning disable CS0162 // Legacy scan control branches are bypassed by persistent Task state handling.

public sealed record LibraryFolderCommand(
    string Path,
    bool IncludeSubfolders = true,
    bool Enabled = true,
    string ScanMode = "normal",
    IReadOnlyList<string>? ExcludePatterns = null);

public sealed record LibraryCommand(
    string Name,
    string? Description,
    bool Enabled,
    IReadOnlyList<LibraryFolderCommand> Folders);

public sealed record LibraryMutationResult(long Id, bool Changed, long AuditId, string Message);
public sealed record LibraryDeletePreview(
    long LibraryId,
    string Name,
    long FolderCount,
    long LinkedFiles,
    string ConfirmationToken,
    IReadOnlyList<string> Warnings);
public sealed record ScanLibraryCommand(bool FullScan = false, bool AutoSync = true);
public sealed record ScanLaunchResult(long TaskId, string Status, string Message);
public sealed record TaskMutationResult(long TaskId, string Status, string Message);

public sealed class LibraryWorkflowService(string databasePath) : BackgroundService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".ts", ".m2ts", ".flv", ".webm",
        ".vob", ".mpg", ".mpeg", ".iso"
    };
    private readonly ConcurrentDictionary<string, PreviewGrant> grants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, ScanControl> scanControls = new();
    private readonly SemaphoreSlim scanLock = new(1, 1);
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedScansAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested) {
            try {
                long? taskId = await ClaimScanAsync(stoppingToken);
                if (taskId is null) {
                    await Task.Delay(750, stoppingToken);
                    continue;
                }
                await RunClaimedScanAsync(taskId.Value, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception error) {
                Console.Error.WriteLine($"Library scan runner: {error}");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    public async Task<LibraryMutationResult> CreateLibraryAsync(LibraryCommand input)
    {
        LibraryCommand clean = Validate(input);
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        long id = await InsertLibraryAsync(connection, transaction, clean);
        await ReplaceFoldersAsync(connection, transaction, id, clean.Folders);
        long auditId = await AuditAsync(connection, transaction, "LibraryCreate", "Library", id, null, clean);
        await transaction.CommitAsync();
        return new(id, true, auditId, "媒体库已创建。");
    }

    public async Task<LibraryMutationResult> UpdateLibraryAsync(long libraryId, LibraryCommand input)
    {
        LibraryCommand clean = Validate(input);
        await using var connection = await OpenAsync();
        object before = await ReadLibrarySnapshotAsync(connection, libraryId)
            ?? throw new KeyNotFoundException("媒体库不存在。");
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, transaction, """
            UPDATE Libraries
               SET Name=$name,Description=$description,IsEnabled=$enabled,UpdatedAt=$at
             WHERE Id=$id
            """, ("$name", clean.Name), ("$description", clean.Description),
            ("$enabled", clean.Enabled ? 1 : 0), ("$at", Now()), ("$id", libraryId));
        await ReplaceFoldersAsync(connection, transaction, libraryId, clean.Folders);
        long auditId = await AuditAsync(connection, transaction, "LibraryUpdate", "Library", libraryId, before, clean);
        await transaction.CommitAsync();
        return new(libraryId, true, auditId, "媒体库已保存。");
    }

    public async Task<LibraryDeletePreview> PreviewDeleteLibraryAsync(long libraryId)
    {
        await using var connection = await OpenAsync();
        string? name = await ScalarTextAsync(connection, null, "SELECT Name FROM Libraries WHERE Id=$id", ("$id", libraryId));
        if (name is null) throw new KeyNotFoundException("媒体库不存在。");
        long folders = await ScalarLongAsync(connection, null, "SELECT COUNT(*) FROM LibraryFolders WHERE LibraryId=$id", ("$id", libraryId));
        long files = await ScalarLongAsync(connection, null, "SELECT COUNT(*) FROM MediaFiles WHERE LibraryId=$id", ("$id", libraryId));
        string token = Grant("LibraryDelete", libraryId);
        return new(libraryId, name, folders, files, token, [
            "只删除媒体库定义和来源文件夹，不删除影片记录或媒体文件。",
            files > 0 ? $"{files} 个媒体文件将解除媒体库关联，影片仍保留在数据库中。" : "当前没有关联媒体文件。",
            "执行前会创建完整数据库备份。"
        ]);
    }

    public async Task<LibraryMutationResult> DeleteLibraryAsync(long libraryId, ConfirmCommand input)
    {
        Consume(input.ConfirmationToken, "LibraryDelete", libraryId);
        string backupPath = await BackupDatabaseAsync("library-delete");
        await using var connection = await OpenAsync();
        object before = await ReadLibrarySnapshotAsync(connection, libraryId)
            ?? throw new KeyNotFoundException("媒体库不存在。");
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, transaction, "DELETE FROM Libraries WHERE Id=$id", ("$id", libraryId));
        long auditId = await AuditAsync(connection, transaction, "LibraryDelete", "Library", libraryId, before, new { BackupPath = backupPath });
        await transaction.CommitAsync();
        return new(libraryId, true, auditId, "媒体库定义已删除；影片记录和媒体文件未删除。");
    }

    public async Task<ScanLaunchResult> StartScanAsync(long libraryId, ScanLibraryCommand input)
    {
        await using var connection = await OpenAsync();
        long exists = await ScalarLongAsync(connection, null,
            "SELECT COUNT(*) FROM Libraries WHERE Id=$id AND IsEnabled=1", ("$id", libraryId));
        if (exists == 0) throw new KeyNotFoundException("媒体库不存在或已停用。");
        string at = Now();
        long taskId = await InsertIdAsync(connection, null, """
            INSERT INTO Tasks(TaskType,Status,Stage,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt)
            VALUES('Scan','Pending','Pending',0,0,0,$payload,$at,$at);
            SELECT last_insert_rowid();
            """, ("$payload", JsonSerializer.Serialize(new { LibraryId = libraryId, input.FullScan, input.AutoSync })), ("$at", at));
        await LogAsync(connection, null, taskId, "Info", "扫描任务已进入队列。");
        return new(taskId, "Pending", "扫描任务已创建，可在任务中心查看进度。");
    }

    public async Task<TaskMutationResult> PauseTaskAsync(long taskId)
    {
        await using var persistentConnection = await OpenAsync();
        if (await ScalarLongAsync(persistentConnection, null, "SELECT COUNT(*) FROM Tasks WHERE Id=$id AND TaskType='Scan'", ("$id", taskId)) == 0)
            throw new KeyNotFoundException("任务不存在。");
        if (scanControls.TryGetValue(taskId, out ScanControl? persistentControl)) persistentControl.Paused = true;
        await ExecuteAsync(persistentConnection, null, """
            UPDATE Tasks
               SET Status='Paused',Stage='Paused',UpdatedAt=$at
             WHERE Id=$id AND Status IN ('Pending','Retrying','Preparing','Running')
            """, ("$at", Now()), ("$id", taskId));
        await LogAsync(persistentConnection, null, taskId, "Info", "任务已暂停。");
        return new(taskId, "Paused", "任务已暂停。");

        if (!scanControls.TryGetValue(taskId, out ScanControl? control))
            throw new InvalidOperationException("该任务不在当前会话中运行，无法暂停。");
        control.Paused = true;
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, null, "UPDATE Tasks SET Status='Paused' WHERE Id=$id AND Status IN ('Pending','Running')", ("$id", taskId));
        await LogAsync(connection, null, taskId, "Info", "任务已暂停。");
        return new(taskId, "Paused", "任务已暂停。");
    }

    public async Task<TaskMutationResult> ResumeTaskAsync(long taskId)
    {
        await using var persistentConnection = await OpenAsync();
        if (await ScalarLongAsync(persistentConnection, null, "SELECT COUNT(*) FROM Tasks WHERE Id=$id AND TaskType='Scan'", ("$id", taskId)) == 0)
            throw new KeyNotFoundException("任务不存在。");
        if (scanControls.TryGetValue(taskId, out ScanControl? persistentControl)) persistentControl.Paused = false;
        await ExecuteAsync(persistentConnection, null, """
            UPDATE Tasks
               SET Status='Pending',Stage='Pending',UpdatedAt=$at
             WHERE Id=$id AND Status='Paused'
            """, ("$at", Now()), ("$id", taskId));
        await LogAsync(persistentConnection, null, taskId, "Info", "任务已继续。");
        return new(taskId, "Pending", "任务已继续。");

        if (!scanControls.TryGetValue(taskId, out ScanControl? control))
            throw new InvalidOperationException("该任务不在当前会话中运行，无法继续。");
        control.Paused = false;
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, null, "UPDATE Tasks SET Status='Running' WHERE Id=$id AND Status='Paused'", ("$id", taskId));
        await LogAsync(connection, null, taskId, "Info", "任务已继续。");
        return new(taskId, "Running", "任务已继续。");
    }

    public async Task<TaskMutationResult> CancelTaskAsync(long taskId)
    {
        await using var persistentConnection = await OpenAsync();
        string? persistentStatus = await ScalarTextAsync(persistentConnection, null, "SELECT Status FROM Tasks WHERE Id=$id AND TaskType='Scan'", ("$id", taskId));
        if (persistentStatus is null) throw new KeyNotFoundException("任务不存在。");
        if (persistentStatus is "Completed" or "Failed" or "Cancelled") throw new InvalidOperationException("该任务已经结束。");
        if (scanControls.TryGetValue(taskId, out ScanControl? persistentControl)) persistentControl.Cancellation.Cancel();
        await ExecuteAsync(persistentConnection, null, """
            UPDATE Tasks
               SET Status='Cancelled',Stage='Cancelled',CancellationRequested=1,CompletedAt=$at,UpdatedAt=$at
             WHERE Id=$id
            """, ("$at", Now()), ("$id", taskId));
        await LogAsync(persistentConnection, null, taskId, "Warning", "任务已由用户取消。");
        return new(taskId, "Cancelled", "任务已取消；已经提交的单项导入不会回滚。");

        await using var connection = await OpenAsync();
        string? status = await ScalarTextAsync(connection, null, "SELECT Status FROM Tasks WHERE Id=$id", ("$id", taskId));
        if (status is null) throw new KeyNotFoundException("任务不存在。");
        if (status is "Completed" or "Failed" or "Cancelled") throw new InvalidOperationException("该任务已经结束。");
        if (scanControls.TryGetValue(taskId, out ScanControl? control)) control.Cancellation.Cancel();
        await ExecuteAsync(connection, null, "UPDATE Tasks SET Status='Cancelled',CompletedAt=$at WHERE Id=$id", ("$at", Now()), ("$id", taskId));
        await LogAsync(connection, null, taskId, "Warning", "任务已由用户取消。");
        return new(taskId, "Cancelled", "任务已取消；已经提交的单项导入不会回滚。");
    }

    public async Task<ScanLaunchResult> RetryTaskAsync(long taskId)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TaskType,Status,PayloadJson FROM Tasks WHERE Id=$id";
        command.Parameters.AddWithValue("$id", taskId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new KeyNotFoundException("任务不存在。");
        string type = reader.GetString(0);
        string status = reader.GetString(1);
        string payload = reader.IsDBNull(2) ? "" : reader.GetString(2);
        if (type != "Scan") throw new InvalidOperationException("当前仅扫描任务支持重试。");
        if (status is not ("Failed" or "Cancelled")) throw new InvalidOperationException("只有失败或已取消的任务可以重试。");
        _ = payload;
        await ExecuteAsync(connection, null, """
            UPDATE Tasks
               SET Status='Retrying',Stage='Retrying',Progress=0,CompletedItems=0,ErrorMessage=NULL,
                   CompletedAt=NULL,CancellationRequested=0,RetryCount=RetryCount+1,UpdatedAt=$at
             WHERE Id=$id
            """, ("$at", Now()), ("$id", taskId));
        await LogAsync(connection, null, taskId, "Info", "扫描任务已进入重试队列。");
        return new(taskId, "Retrying", "扫描任务已进入重试队列。");
    }

    public async Task<bool> RunQueuedScanForTestsAsync(long taskId, CancellationToken cancellationToken = default)
    {
        if (!await ClaimSpecificScanAsync(taskId, cancellationToken)) return false;
        await RunClaimedScanAsync(taskId, cancellationToken);
        return true;
    }

    public Task RecoverInterruptedScansForTestsAsync(CancellationToken cancellationToken = default) =>
        RecoverInterruptedScansAsync(cancellationToken);

    private async Task RunClaimedScanAsync(long taskId, CancellationToken cancellationToken)
    {
        ScanTaskPayload payload = await ReadScanTaskPayloadAsync(taskId, cancellationToken);
        scanControls[taskId] = new ScanControl();
        using CancellationTokenRegistration registration = cancellationToken.Register(() => {
            if (scanControls.TryGetValue(taskId, out ScanControl? control)) control.Cancellation.Cancel();
        });
        await ExecuteScanAsync(taskId, payload.LibraryId, new(payload.FullScan, payload.AutoSync));
    }

    private async Task RecoverInterruptedScansAsync(CancellationToken token)
    {
        await using var connection = await OpenAsync();
        var recovered = new List<long>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText = "SELECT Id FROM Tasks WHERE TaskType='Scan' AND Status IN ('Preparing','Running')";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) recovered.Add(reader.GetInt64(0));
        }
        await ExecuteAsync(connection, null, """
            UPDATE Tasks
               SET Status='Retrying',Stage='Retrying',RetryCount=RetryCount+1,
                   ErrorMessage='上次扫描异常中断，已恢复到可重试队列。',UpdatedAt=$at
             WHERE TaskType='Scan' AND Status IN ('Preparing','Running')
            """, ("$at", Now()));
        foreach (long id in recovered)
            await LogAsync(connection, null, id, "Warning", "上次扫描异常中断，任务已恢复到可重试队列。");
    }

    private async Task<long?> ClaimScanAsync(CancellationToken token)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Tasks
               SET Status='Preparing',Stage='Preparing',StartedAt=COALESCE(StartedAt,$at),
                   UpdatedAt=$at,CancellationRequested=0
             WHERE Id=(
                SELECT Id FROM Tasks
                 WHERE TaskType='Scan' AND Status IN ('Pending','Retrying')
                 ORDER BY Id
                 LIMIT 1
             )
             RETURNING Id
            """;
        command.Parameters.AddWithValue("$at", Now());
        object? result = await command.ExecuteScalarAsync(token);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private async Task<bool> ClaimSpecificScanAsync(long taskId, CancellationToken token)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Tasks
               SET Status='Preparing',Stage='Preparing',StartedAt=COALESCE(StartedAt,$at),
                   UpdatedAt=$at,CancellationRequested=0
             WHERE Id=$id AND TaskType='Scan' AND Status IN ('Pending','Retrying')
             RETURNING Id
            """;
        command.Parameters.AddWithValue("$at", Now());
        command.Parameters.AddWithValue("$id", taskId);
        object? result = await command.ExecuteScalarAsync(token);
        return result is not null and not DBNull;
    }

    private async Task<ScanTaskPayload> ReadScanTaskPayloadAsync(long taskId, CancellationToken token)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PayloadJson FROM Tasks WHERE Id=$id AND TaskType='Scan'";
        command.Parameters.AddWithValue("$id", taskId);
        string payload = Convert.ToString(await command.ExecuteScalarAsync(token)) ?? "{}";
        using JsonDocument json = JsonDocument.Parse(payload);
        long libraryId = json.RootElement.GetProperty("LibraryId").GetInt64();
        bool fullScan = json.RootElement.TryGetProperty("FullScan", out JsonElement full) && full.GetBoolean();
        bool autoSync = !json.RootElement.TryGetProperty("AutoSync", out JsonElement sync) || sync.GetBoolean();
        return new(libraryId, fullScan, autoSync);
    }

    private async Task ExecuteScanAsync(long taskId, long libraryId, ScanLibraryCommand input)
    {
        if (!scanControls.TryGetValue(taskId, out ScanControl? control)) return;
        bool lockAcquired = false;
        try {
            await scanLock.WaitAsync(control.Cancellation.Token);
            lockAcquired = true;
            await using var connection = await OpenAsync();
            await ExecuteAsync(connection, null,
                "UPDATE Tasks SET Status='Running',Stage='Running',StartedAt=COALESCE(StartedAt,$at),UpdatedAt=$at WHERE Id=$id", ("$at", Now()), ("$id", taskId));
            await LogAsync(connection, null, taskId, "Info", input.FullScan ? "开始全量扫描。" : "开始增量扫描。");

            IReadOnlyList<ScanFolder> folders = await ReadScanFoldersAsync(connection, libraryId);
            if (folders.Count == 0) throw new InvalidOperationException("媒体库没有启用的来源文件夹。");

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folderErrors = new List<string>();
            foreach (ScanFolder folder in folders) {
                await WaitIfPausedAsync(taskId, control);
                control.Cancellation.Token.ThrowIfCancellationRequested();
                if (!Directory.Exists(folder.Path)) {
                    string error = $"来源文件夹不存在：{folder.Path}";
                    folderErrors.Add(error);
                    await LogAsync(connection, null, taskId, "Warning", error);
                    continue;
                }
                try {
                    var options = new EnumerationOptions {
                        RecurseSubdirectories = folder.IncludeSubfolders,
                        IgnoreInaccessible = true,
                        ReturnSpecialDirectories = false,
                        AttributesToSkip = FileAttributes.System
                    };
                    foreach (string file in Directory.EnumerateFiles(folder.Path, "*", options)) {
                        if (!VideoExtensions.Contains(Path.GetExtension(file))) continue;
                        if (IsExcluded(folder.Path, file, folder.ExcludePatterns)) continue;
                        candidates.Add(Path.GetFullPath(file));
                    }
                } catch (Exception error) {
                    string message = $"扫描来源失败：{folder.Path}；{error.Message}";
                    folderErrors.Add(message);
                    await LogAsync(connection, null, taskId, "Error", message);
                }
            }

            await ExecuteAsync(connection, null, "UPDATE Tasks SET TotalItems=$total WHERE Id=$id",
                ("$total", candidates.Count), ("$id", taskId));
            int imported = 0, skipped = 0, restored = 0, failed = 0, completed = 0;
            foreach (string path in candidates) {
                await WaitIfPausedAsync(taskId, control);
                control.Cancellation.Token.ThrowIfCancellationRequested();
                try {
                    ImportOutcome outcome = await ImportFileAsync(connection, libraryId, path, input.AutoSync);
                    imported += outcome.Imported ? 1 : 0;
                    skipped += outcome.Imported ? 0 : 1;
                    restored += outcome.RatingRestored ? 1 : 0;
                } catch (Exception error) {
                    failed++;
                    await LogAsync(connection, null, taskId, "Error", $"导入失败：{Path.GetFileName(path)}；{error.Message}");
                }
                completed++;
                double progress = candidates.Count == 0 ? 100 : completed * 100d / candidates.Count;
                await ExecuteAsync(connection, null,
                    "UPDATE Tasks SET Progress=$progress,CompletedItems=$completed,UpdatedAt=$at WHERE Id=$id",
                    ("$progress", progress), ("$completed", completed), ("$at", Now()), ("$id", taskId));
            }

            int missing = 0;
            if (input.FullScan && folderErrors.Count == 0)
                missing = await RefreshMissingStatesAsync(connection, libraryId);

            string finished = Now();
            foreach (ScanFolder folder in folders)
                await ExecuteAsync(connection, null, "UPDATE LibraryFolders SET LastScannedAt=$at,UpdatedAt=$at WHERE Id=$id",
                    ("$at", finished), ("$id", folder.Id));
            string result = JsonSerializer.Serialize(new { Imported = imported, Skipped = skipped, Failed = failed, RatingRestored = restored, Missing = missing, FolderErrors = folderErrors.Count });
            string status = failed > 0 || folderErrors.Count > 0 ? "Failed" : "Completed";
            string? errorMessage = status == "Failed" ? $"{failed + folderErrors.Count} 项失败；其余项目已安全完成。" : null;
            await ExecuteAsync(connection, null, """
                UPDATE Tasks SET Status=$status,Stage=$status,Progress=100,CompletedItems=$completed,ResultJson=$result,
                                 ErrorMessage=$error,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id
                """, ("$status", status), ("$completed", completed), ("$result", result),
                ("$error", errorMessage), ("$at", finished), ("$id", taskId));
            await LogAsync(connection, null, taskId, status == "Completed" ? "Info" : "Warning",
                $"扫描结束：新增 {imported}，跳过 {skipped}，失败 {failed}，恢复评分 {restored}。");
        } catch (OperationCanceledException) {
            try {
                await using var connection = await OpenAsync();
                await ExecuteAsync(connection, null, "UPDATE Tasks SET Status='Cancelled',Stage='Cancelled',CompletedAt=COALESCE(CompletedAt,$at),UpdatedAt=$at WHERE Id=$id",
                    ("$at", Now()), ("$id", taskId));
            } catch (Exception persistenceError) { Console.Error.WriteLine($"Could not persist cancellation for task {taskId}: {persistenceError}"); }
        } catch (Exception error) {
            try {
                await using var connection = await OpenAsync();
                await ExecuteAsync(connection, null, "UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",
                    ("$error", error.Message), ("$at", Now()), ("$id", taskId));
                await LogAsync(connection, null, taskId, "Error", error.Message);
            } catch (Exception persistenceError) {
                Console.Error.WriteLine($"Scan task {taskId} failed and could not persist its error: {persistenceError}");
            }
        } finally {
            if (lockAcquired) scanLock.Release();
            scanControls.TryRemove(taskId, out _);
            control.Cancellation.Dispose();
        }
    }

    private async Task WaitIfPausedAsync(long taskId, ScanControl control)
    {
        while (true) {
            control.Cancellation.Token.ThrowIfCancellationRequested();
            await using var connection = await OpenAsync();
            string? status = await ScalarTextAsync(connection, null, "SELECT Status FROM Tasks WHERE Id=$id", ("$id", taskId));
            if (status == "Cancelled") throw new OperationCanceledException(control.Cancellation.Token);
            if (status != "Paused" && !control.Paused) return;
            await Task.Delay(100, control.Cancellation.Token);
        }
    }

    private static async Task<ImportOutcome> ImportFileAsync(SqliteConnection connection, long libraryId, string path, bool autoSync)
    {
        string normalized = NormalizePath(path);
        await using var transaction = await connection.BeginTransactionAsync();
        long existing = await ScalarLongAsync(connection, transaction,
            "SELECT COALESCE(MAX(MovieId),0) FROM MediaFiles WHERE NormalizedPath=$path", ("$path", normalized));
        if (existing > 0) {
            await ExecuteAsync(connection, transaction,
                "UPDATE MediaFiles SET ExistsState='Present',LastSeenAt=$at,UpdatedAt=$at WHERE NormalizedPath=$path",
                ("$at", Now()), ("$path", normalized));
            await transaction.CommitAsync();
            return new(false, false);
        }

        var file = new FileInfo(path);
        string baseName = Path.GetFileNameWithoutExtension(path).Trim();
        string code = string.IsNullOrWhiteSpace(baseName) ? file.Name : baseName;
        string at = Now();
        long movieId = await InsertIdAsync(connection, transaction, """
            INSERT INTO Movies(Code,Title,SortTitle,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt,ImportedAt)
            VALUES($code,$title,$title,0,0,'pending','LMM.Scan',$at,$at,$at);
            SELECT last_insert_rowid();
            """, ("$code", code), ("$title", code), ("$at", at));
        string sourceType = path.StartsWith("\\\\", StringComparison.Ordinal) ? "NAS" : "Local";
        await ExecuteAsync(connection, transaction, """
            INSERT INTO MediaFiles(MovieId,LibraryId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,DurationSeconds,LastSeenAt,CreatedAt,UpdatedAt)
            VALUES($movie,$library,$path,$normalized,$name,$extension,$size,'Video',$source,1,'Present',0,$at,$at,$at)
            """, ("$movie", movieId), ("$library", libraryId), ("$path", path), ("$normalized", normalized),
            ("$name", file.Name), ("$extension", file.Extension.ToLowerInvariant()), ("$size", Math.Max(0, file.Length)),
            ("$source", sourceType), ("$at", at));

        bool restored = await RatingHistoryService.RestoreForImportedMovieAsync(connection, transaction, movieId, code, at);
        if (autoSync) {
            long syncTaskId = await InsertIdAsync(connection, transaction, """
                INSERT INTO Tasks(TaskType,Status,Stage,Provider,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt,CurrentMovieId)
                VALUES('Sync','Pending','Pending','MetaTube',0,1,0,$payload,$at,$at,$movie); SELECT last_insert_rowid();
                """, ("$payload", JsonSerializer.Serialize(new { MovieId = movieId, Trigger = "ScanImport" })), ("$at", at), ("$movie", movieId));
            await LogAsync(connection, transaction, syncTaskId, "Info", "影片导入完成，等待元数据同步执行器处理。");
        }
        await transaction.CommitAsync();
        return new(true, restored);
    }

    private static async Task<int> RefreshMissingStatesAsync(SqliteConnection connection, long libraryId)
    {
        var files = new List<(long Id, string Path)>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText = "SELECT Id,FilePath FROM MediaFiles WHERE LibraryId=$library AND MediaType='Video'";
            command.Parameters.AddWithValue("$library", libraryId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) files.Add((reader.GetInt64(0), reader.GetString(1)));
        }
        int missing = 0;
        foreach ((long id, string path) in files) {
            bool exists = File.Exists(path);
            if (!exists) missing++;
            await ExecuteAsync(connection, null,
                "UPDATE MediaFiles SET ExistsState=$state,UpdatedAt=$at WHERE Id=$id",
                ("$state", exists ? "Present" : "Missing"), ("$at", Now()), ("$id", id));
        }
        return missing;
    }

    private static async Task<IReadOnlyList<ScanFolder>> ReadScanFoldersAsync(SqliteConnection connection, long libraryId)
    {
        var result = new List<ScanFolder>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,FolderPath,IncludeSubfolders,ExcludePatternsJson
              FROM LibraryFolders WHERE LibraryId=$library AND IsEnabled=1 ORDER BY Id
            """;
        command.Parameters.AddWithValue("$library", libraryId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            string json = reader.IsDBNull(3) ? "[]" : reader.GetString(3);
            IReadOnlyList<string> patterns;
            try { patterns = JsonSerializer.Deserialize<string[]>(json) ?? []; }
            catch (JsonException) { patterns = []; }
            result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2) == 1, patterns));
        }
        return result;
    }

    private static bool IsExcluded(string root, string path, IReadOnlyList<string> patterns)
    {
        string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        string[] segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (string raw in patterns) {
            string pattern = raw.Trim().Replace('\\', '/');
            if (pattern.Length == 0) continue;
            if (FileSystemName.MatchesSimpleExpression(pattern, relative, ignoreCase: true)) return true;
            if (!pattern.Contains('/') && segments.Any(segment => FileSystemName.MatchesSimpleExpression(pattern, segment, ignoreCase: true))) return true;
        }
        return false;
    }

    private static LibraryCommand Validate(LibraryCommand input)
    {
        string name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 100) throw new ArgumentException("媒体库名称长度必须为 1 到 100 个字符。");
        if (input.Folders is null || input.Folders.Count is 0) throw new ArgumentException("媒体库至少需要一个来源文件夹。");
        if (input.Folders.Count > 64) throw new ArgumentException("单个媒体库最多支持 64 个来源文件夹。");
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new List<LibraryFolderCommand>();
        foreach (LibraryFolderCommand folder in input.Folders) {
            string path = folder.Path?.Trim() ?? string.Empty;
            if (path.Length == 0 || !Path.IsPathFullyQualified(path)) throw new ArgumentException($"来源文件夹必须是绝对路径：{path}");
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!normalized.Add(NormalizePath(full))) throw new ArgumentException($"来源文件夹重复：{full}");
            string scanMode = folder.ScanMode?.Trim().ToLowerInvariant() ?? "normal";
            if (scanMode is not ("normal" or "watch" or "manual")) throw new ArgumentException("扫描模式只支持 normal、watch 或 manual。");
            string[] exclusions = (folder.ExcludePatterns ?? []).Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
            folders.Add(new(full, folder.IncludeSubfolders, folder.Enabled, scanMode, exclusions));
        }
        return new(name, input.Description?.Trim(), input.Enabled, folders);
    }

    private static async Task<long> InsertLibraryAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, LibraryCommand input)
    {
        string at = Now();
        return await InsertIdAsync(connection, transaction, """
            INSERT INTO Libraries(Name,Description,IsEnabled,SortOrder,CreatedAt,UpdatedAt)
            VALUES($name,$description,$enabled,COALESCE((SELECT MAX(SortOrder)+1 FROM Libraries),0),$at,$at);
            SELECT last_insert_rowid();
            """, ("$name", input.Name), ("$description", input.Description), ("$enabled", input.Enabled ? 1 : 0), ("$at", at));
    }

    private static async Task ReplaceFoldersAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction,
        long libraryId, IReadOnlyList<LibraryFolderCommand> folders)
    {
        var existing = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using (var read = connection.CreateCommand()) {
            read.Transaction = (SqliteTransaction)transaction;
            read.CommandText = "SELECT Id,NormalizedPath FROM LibraryFolders WHERE LibraryId=$library";
            read.Parameters.AddWithValue("$library", libraryId);
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync()) existing[reader.GetString(1)] = reader.GetInt64(0);
        }
        foreach (LibraryFolderCommand folder in folders) {
            string at = Now();
            string normalized = NormalizePath(folder.Path);
            try {
                if (existing.Remove(normalized, out long folderId))
                    await ExecuteAsync(connection, transaction, """
                        UPDATE LibraryFolders SET FolderPath=$path,IncludeSubfolders=$subfolders,IsEnabled=$enabled,
                            ScanMode=$mode,ExcludePatternsJson=$exclude,UpdatedAt=$at WHERE Id=$id
                        """, ("$path", folder.Path), ("$subfolders", folder.IncludeSubfolders ? 1 : 0),
                        ("$enabled", folder.Enabled ? 1 : 0), ("$mode", folder.ScanMode),
                        ("$exclude", JsonSerializer.Serialize(folder.ExcludePatterns ?? [])), ("$at", at), ("$id", folderId));
                else
                    await ExecuteAsync(connection, transaction, """
                        INSERT INTO LibraryFolders(LibraryId,FolderPath,NormalizedPath,IncludeSubfolders,IsEnabled,ScanMode,ExcludePatternsJson,CreatedAt,UpdatedAt)
                        VALUES($library,$path,$normalized,$subfolders,$enabled,$mode,$exclude,$at,$at)
                        """, ("$library", libraryId), ("$path", folder.Path), ("$normalized", normalized),
                        ("$subfolders", folder.IncludeSubfolders ? 1 : 0), ("$enabled", folder.Enabled ? 1 : 0),
                        ("$mode", folder.ScanMode), ("$exclude", JsonSerializer.Serialize(folder.ExcludePatterns ?? [])), ("$at", at));
            } catch (SqliteException error) when (error.SqliteErrorCode == 19) {
                throw new InvalidOperationException($"来源文件夹已被其他媒体库使用：{folder.Path}");
            }
        }
        foreach (long removedId in existing.Values)
            await ExecuteAsync(connection, transaction, "DELETE FROM LibraryFolders WHERE Id=$id", ("$id", removedId));
    }

    private static async Task<object?> ReadLibrarySnapshotAsync(SqliteConnection connection, long libraryId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name,Description,IsEnabled FROM Libraries WHERE Id=$id";
        command.Parameters.AddWithValue("$id", libraryId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new { Name = reader.GetString(0), Description = reader.IsDBNull(1) ? null : reader.GetString(1), Enabled = reader.GetInt64(2) == 1 };
    }

    private async Task<string> BackupDatabaseAsync(string operation)
    {
        string directory = Path.GetDirectoryName(databasePath) ?? throw new InvalidOperationException("无法确定数据库目录。");
        string backupDirectory = Path.Combine(directory, "backups", "operations", DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff"));
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(backupDirectory, $"{operation}-LocalMediaManager.db");
        await using var source = await OpenAsync();
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        await destination.OpenAsync();
        source.BackupDatabase(destination);
        return backupPath;
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Cache = SqliteCacheMode.Private
        }.ToString());
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        return connection;
    }

    private string Grant(string operation, long entityId)
    {
        string token = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        grants[token] = new(operation, entityId, DateTimeOffset.UtcNow.AddMinutes(5));
        return token;
    }

    private void Consume(string token, string operation, long entityId)
    {
        if (!grants.TryRemove(token, out PreviewGrant? grant) || grant.Operation != operation || grant.EntityId != entityId || grant.ExpiresAt < DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("确认令牌无效或已过期，请重新预览影响范围。");
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
    private static async Task LogAsync(SqliteConnection connection, System.Data.Common.DbTransaction? transaction, long taskId, string level, string message) =>
        await ExecuteAsync(connection, transaction, "INSERT INTO TaskLogs(TaskId,Level,Message,CreatedAt) VALUES($task,$level,$message,$at)",
            ("$task", taskId), ("$level", level), ("$message", message), ("$at", Now()));
    private static async Task<long> AuditAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string operation, string entity, long? id, object? before, object? after) =>
        await InsertIdAsync(connection, transaction, """
            INSERT INTO OperationAudit(OperationType,EntityType,EntityId,BeforeJson,AfterJson,CreatedAt)
            VALUES($operation,$entity,$id,$before,$after,$at); SELECT last_insert_rowid();
            """, ("$operation", operation), ("$entity", entity), ("$id", id),
            ("$before", before is null ? null : JsonSerializer.Serialize(before)), ("$after", after is null ? null : JsonSerializer.Serialize(after)), ("$at", Now()));
    private static async Task ExecuteAsync(SqliteConnection connection, System.Data.Common.DbTransaction? transaction, string sql, params (string Name, object? Value)[] values)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction as SqliteTransaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<long> InsertIdAsync(SqliteConnection connection, System.Data.Common.DbTransaction? transaction, string sql, params (string Name, object? Value)[] values)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction as SqliteTransaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
    private static async Task<long> ScalarLongAsync(SqliteConnection connection, System.Data.Common.DbTransaction? transaction, string sql, params (string Name, object? Value)[] values)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction as SqliteTransaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
    }
    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, System.Data.Common.DbTransaction? transaction, string sql, params (string Name, object? Value)[] values)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction as SqliteTransaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private sealed record PreviewGrant(string Operation, long EntityId, DateTimeOffset ExpiresAt);
    private sealed record ScanFolder(long Id, string Path, bool IncludeSubfolders, IReadOnlyList<string> ExcludePatterns);
    private sealed record ScanTaskPayload(long LibraryId, bool FullScan, bool AutoSync);
    private sealed record ImportOutcome(bool Imported, bool RatingRestored);
    private sealed class ScanControl
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public volatile bool Paused;
    }
}
