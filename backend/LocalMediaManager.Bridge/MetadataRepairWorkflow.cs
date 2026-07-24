using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace LocalMediaManager.Bridge;

public sealed record MetadataRepairScanCommand(
    bool Poster = true,
    bool Fanart = true,
    bool Preview = true,
    bool Screenshot = true,
    bool Nfo = true,
    bool RepairInvalidPaths = true,
    bool RegisterUnregistered = true);

public sealed record MetadataRepairLaunchResult(long TaskId, string Status, string Message);
public sealed record MetadataRepairExecuteCommand(string ConfirmationToken);
public sealed record MetadataRepairExportResult(string MarkdownPath, string CsvPath, long Items, string Message);

public sealed record MetadataRepairCandidate(
    string ItemId,
    long MovieId,
    string Number,
    string VideoPath,
    string ResourceType,
    long? ExistingRecordId,
    string? CurrentDatabasePath,
    string? CandidatePath,
    string Evidence,
    int Confidence,
    string Action,
    string Status,
    string Reason,
    bool SafeToApply,
    bool WillOverwrite,
    string? FileFingerprint);

public sealed record MetadataRepairCounts(
    long ScannedStandardMovies,
    long EligibleMovies,
    long ExcludedMissingMedia,
    long ExcludedLowConfidence,
    long ExcludedCodeMismatch,
    long UnregisteredPoster,
    long UnregisteredFanart,
    long UnregisteredPreview,
    long UnregisteredScreenshot,
    long UnregisteredNfo,
    long SafeRepairs,
    long Conflicts,
    long LowConfidence,
    long ExistingValidSkipped,
    long InvalidDatabaseRecords,
    long MissingPhysicalFiles,
    long UnmatchedResources,
    long Applied,
    long Skipped,
    long Failed);

public sealed record MetadataRepairProjection(
    long CompleteMovies,
    long PosterMovies,
    long FanartMovies,
    long PreviewMovies,
    long ScreenshotMovies,
    long NfoMovies,
    long InvalidResourceRecords,
    long UnregisteredResources);

public sealed record MetadataRepairPreview(
    long TaskId,
    string Status,
    string Stage,
    double Progress,
    string ConfirmationToken,
    MetadataRepairCounts Counts,
    MetadataRepairProjection Before,
    MetadataRepairProjection Projected,
    MetadataRepairProjection? After,
    IReadOnlyList<MetadataRepairCandidate> Items,
    IReadOnlyList<string> Warnings,
    string CreatedAt,
    string? CompletedAt,
    bool CanExecute,
    bool CanRollback,
    bool DryRun);

public sealed class MetadataRepairWorkflow(
    string databasePath,
    MediaStoragePathResolver pathResolver,
    MetadataHealthAnalysisService health,
    IMovieNumberExtractor movieNumberExtractor,
    TaskLogService logs) : BackgroundService
{
    private const string TaskType = "MetadataRepair";
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] ResourceTypes = ["Poster", "Fanart", "Preview", "Screenshot", "NFO"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<long, CancellationTokenSource> cancellations = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested) {
            try {
                long? taskId = await ClaimNextAsync(stoppingToken);
                if (taskId is null) { await Task.Delay(500, stoppingToken); continue; }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cancellations[taskId.Value] = linked;
                try { await ProcessClaimedTaskAsync(taskId.Value, linked.Token); }
                finally { cancellations.TryRemove(taskId.Value, out _); }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) {
                Console.Error.WriteLine($"Metadata repair worker: {error}");
                await Task.Delay(750, stoppingToken);
            }
        }
    }

    public async Task<MetadataRepairLaunchResult> StartDryRunAsync(
        MetadataRepairScanCommand input,
        CancellationToken cancellationToken = default)
    {
        Validate(input);
        string at = Now();
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        long existing = await ScalarLongAsync(connection, null,
            $"SELECT COALESCE(MAX(Id),0) FROM Tasks WHERE TaskType='{TaskType}' AND Status IN ('Pending','Running')",
            cancellationToken);
        if (existing > 0) throw new InvalidOperationException($"已有元数据修复扫描正在运行（任务 #{existing}）。");
        long id = await InsertIdAsync(connection, null, """
            INSERT INTO Tasks(TaskType,Status,Stage,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt)
            VALUES('MetadataRepair','Pending','Scan',0,0,0,$payload,$at,$at);
            SELECT last_insert_rowid();
            """, cancellationToken, ("$payload", JsonSerializer.Serialize(input, JsonOptions)), ("$at", at));
        await logs.WriteAsync(id, "Info", "元数据修复 Dry Run 已排队；本阶段只扫描文件并生成计划，不修改影片元数据。", cancellationToken);
        return new(id, "Pending", "离线元数据扫描已进入任务中心。");
    }

    public async Task<MetadataRepairPreview> GetAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        TaskRow task = await ReadTaskAsync(connection, taskId, cancellationToken);
        RepairPlanDocument? plan = DeserializePlan(task.ResultJson);
        if (plan is null) {
            return new(taskId, task.Status, task.Stage, task.Progress, string.Empty, EmptyCounts(), EmptyProjection(), EmptyProjection(), null, [],
                task.ErrorMessage is null ? [] : [task.ErrorMessage], task.CreatedAt, task.CompletedAt, false, false, true);
        }
        string token = ConfirmationToken(taskId, plan);
        bool canExecute = task.Status == "PreviewReady" && plan.Items.Any(item => item.SafeToApply);
        bool canRollback = task.Status is "Completed" or "CompletedWithErrors" && !string.IsNullOrWhiteSpace(plan.RollbackToken)
            && await ScalarLongAsync(connection, null,
                "SELECT COUNT(*) FROM OperationAudit WHERE OperationType='MetadataRepair' AND RollbackToken=$token AND RevertedAt IS NULL",
                cancellationToken, ("$token", plan.RollbackToken)) > 0;
        return new(taskId, task.Status, task.Stage, task.Progress, token, plan.Counts, plan.Before, plan.Projected,
            plan.After, plan.Items, plan.Warnings, task.CreatedAt, task.CompletedAt, canExecute, canRollback, plan.DryRun);
    }

    public async Task<MetadataRepairLaunchResult> ExecuteConfirmedAsync(
        long taskId,
        string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        MetadataRepairPreview preview = await GetAsync(taskId, cancellationToken);
        VerifyToken(preview.ConfirmationToken, confirmationToken);
        if (!preview.CanExecute) throw new InvalidOperationException("修复计划当前没有可执行的安全项目。");
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        int changed = await ExecuteAsync(connection, null, """
            UPDATE Tasks
               SET Status='Pending',Stage='Apply',Progress=0,CompletedItems=0,ErrorMessage=NULL,
                   CompletedAt=NULL,CancellationRequested=0,UpdatedAt=$at
             WHERE Id=$id AND TaskType='MetadataRepair' AND Status='PreviewReady'
            """, cancellationToken, ("$at", Now()), ("$id", taskId));
        if (changed != 1) throw new InvalidOperationException("修复计划不在可执行状态，请重新扫描。");
        await logs.WriteAsync(taskId, "Info", "用户已确认 Dry Run 计划，安全修复进入执行队列。", cancellationToken);
        return new(taskId, "Pending", "安全修复已进入任务中心。");
    }

    public async Task<TaskMutationResult> CancelAsync(long taskId)
    {
        if (cancellations.TryGetValue(taskId, out CancellationTokenSource? source)) source.Cancel();
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, CancellationToken.None);
        int changed = await ExecuteAsync(connection, null, """
            UPDATE Tasks SET Status='Cancelled',Stage='Cancelled',CancellationRequested=1,
                CompletedAt=$at,UpdatedAt=$at
             WHERE Id=$id AND TaskType='MetadataRepair' AND Status IN ('Pending','Running')
            """, CancellationToken.None, ("$at", Now()), ("$id", taskId));
        if (changed == 0) throw new InvalidOperationException("只有等待中或运行中的元数据修复任务可以取消。");
        await logs.WriteAsync(taskId, "Warning", "元数据修复任务已取消；扫描计划之外的数据未被修改。");
        return new(taskId, "Cancelled", "元数据修复任务已取消。");
    }

    public async Task<MetadataRepairLaunchResult> RollbackAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        TaskRow task = await ReadTaskAsync(connection, taskId, cancellationToken);
        RepairPlanDocument plan = DeserializePlan(task.ResultJson) ?? throw new InvalidOperationException("修复会话没有可回滚计划。");
        if (task.Status is not ("Completed" or "CompletedWithErrors") || string.IsNullOrWhiteSpace(plan.RollbackToken))
            throw new InvalidOperationException("只有已完成且尚未回滚的修复会话可以回滚。");
        List<AuditRow> audits = await ReadAuditsAsync(connection, plan.RollbackToken, cancellationToken);
        if (audits.Count == 0) throw new InvalidOperationException("本次修复没有尚未回滚的写入项。");

        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (AuditRow audit in audits.OrderByDescending(value => value.Id)) {
            MutationSnapshot before = DeserializeSnapshot(audit.BeforeJson);
            MutationSnapshot after = DeserializeSnapshot(audit.AfterJson);
            await RestoreSnapshotAsync(connection, transaction, before, after, cancellationToken);
            await ExecuteAsync(connection, transaction,
                "UPDATE OperationAudit SET RevertedAt=$at WHERE Id=$id AND RevertedAt IS NULL",
                cancellationToken, ("$at", Now()), ("$id", audit.Id));
        }
        string at = Now();
        await ExecuteAsync(connection, transaction, """
            INSERT INTO OperationAudit(OperationType,EntityType,EntityId,BeforeJson,AfterJson,RollbackToken,CreatedAt)
            VALUES('MetadataRepairRollback','MetadataRepairSession',$id,$before,$after,$token,$at)
            """, cancellationToken, ("$id", taskId), ("$before", JsonSerializer.Serialize(new { Applied = audits.Count }, JsonOptions)),
            ("$after", JsonSerializer.Serialize(new { Reverted = audits.Count }, JsonOptions)),
            ("$token", plan.RollbackToken), ("$at", at));
        await ExecuteAsync(connection, transaction, """
            UPDATE Tasks SET Stage='RolledBack',ResultSummary=$summary,UpdatedAt=$at WHERE Id=$id
            """, cancellationToken, ("$summary", $"已回滚 {audits.Count} 个元数据修复项目。"), ("$at", at), ("$id", taskId));
        await transaction.CommitAsync(cancellationToken);
        health.Invalidate();
        await logs.WriteAsync(taskId, "Warning", $"元数据修复会话已回滚：{audits.Count} 项。", cancellationToken);
        return new(taskId, "Completed", $"已回滚 {audits.Count} 个修复项目。");
    }

    public async Task<MetadataRepairExportResult> ExportAsync(long taskId, CancellationToken cancellationToken = default)
    {
        MetadataRepairPreview preview = await GetAsync(taskId, cancellationToken);
        if (preview.Items.Count == 0) throw new InvalidOperationException("扫描尚未完成，没有可导出的修复计划。");
        string dataRoot = Directory.GetParent(Path.GetDirectoryName(databasePath) ?? string.Empty)?.FullName
            ?? Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory;
        string reportDirectory = Path.Combine(dataRoot, "reports");
        Directory.CreateDirectory(reportDirectory);
        string stem = $"metadata-repair-{taskId}-{DateTime.Now:yyyyMMdd-HHmmss}";
        string markdownPath = Path.Combine(reportDirectory, stem + ".md");
        string csvPath = Path.Combine(reportDirectory, stem + ".csv");
        await File.WriteAllTextAsync(markdownPath, RenderMarkdown(preview), new UTF8Encoding(true), cancellationToken);
        await File.WriteAllTextAsync(csvPath, RenderCsv(preview), new UTF8Encoding(true), cancellationToken);
        return new(markdownPath, csvPath, preview.Items.Count, "元数据修复 Dry Run 报告已导出。");
    }

    internal async Task ProcessPendingOnceAsync(CancellationToken cancellationToken = default)
    {
        long? taskId = await ClaimNextAsync(cancellationToken);
        if (taskId is not null) await ProcessClaimedTaskAsync(taskId.Value, cancellationToken);
    }

    private async Task ProcessClaimedTaskAsync(long taskId, CancellationToken token)
    {
        try {
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
            TaskRow task = await ReadTaskAsync(connection, taskId, token);
            if (task.Stage == "Scan") await RunScanAsync(taskId, task, token);
            else if (task.Stage == "Apply") await RunApplyAsync(taskId, task, token);
            else throw new InvalidOperationException($"未知元数据修复阶段：{task.Stage}");
        } catch (OperationCanceledException) {
            await MarkCancelledAsync(taskId);
        } catch (Exception error) {
            await MarkFailedAsync(taskId, error);
        }
    }

    private async Task RunScanAsync(long taskId, TaskRow task, CancellationToken token)
    {
        MetadataRepairScanCommand input = JsonSerializer.Deserialize<MetadataRepairScanCommand>(task.PayloadJson ?? "{}", JsonOptions)
            ?? new();
        MediaStorageSettingsDto storage = await pathResolver.GetSettingsAsync(token);
        await UpdateTaskAsync(taskId, "Running", "IndexingStorage", 5, 0, 0, "正在建立本次扫描目录缓存", token);
        var warnings = new ConcurrentBag<string>();
        DirectoryIndex storageIndex = await Task.Run(() => BuildStorageIndex(storage, warnings, token), token);
        await UpdateTaskAsync(taskId, "Running", "AnalyzingCurrentHealth", 8, 0, 0, "正在按统一口径读取 Standard 元数据状态", token);
        MetadataHealthSummary beforeHealth = await MetadataHealthReader.ReadWithInventoryAsync(
            databasePath, storage, null, token, true, storageIndex.HealthInventory);
        RepairPlanDocument plan = await BuildPlanAsync(taskId, input, beforeHealth, storageIndex, warnings, token);
        string summary = $"Dry Run：扫描 {plan.Counts.ScannedStandardMovies} 部 Standard，安全修复 {plan.Counts.SafeRepairs} 项，冲突 {plan.Counts.Conflicts} 项，低置信度 {plan.Counts.LowConfidence} 项。";
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteAsync(connection, null, """
            UPDATE Tasks SET Status='PreviewReady',Stage='PreviewReady',Progress=100,
                TotalItems=$total,CompletedItems=$completed,ResultJson=$result,ResultSummary=$summary,
                CompletedAt=$at,UpdatedAt=$at,CurrentMovieId=NULL
             WHERE Id=$id AND TaskType='MetadataRepair'
            """, token, ("$total", plan.Items.Count), ("$completed", plan.Items.Count),
            ("$result", JsonSerializer.Serialize(plan, JsonOptions)), ("$summary", summary), ("$at", Now()), ("$id", taskId));
        await logs.WriteAsync(taskId, "Info", summary + " 尚未修改资源关联。", token);
    }

    private async Task RunApplyAsync(long taskId, TaskRow task, CancellationToken token)
    {
        RepairPlanDocument plan = DeserializePlan(task.ResultJson) ?? throw new InvalidOperationException("修复计划已丢失，请重新扫描。");
        MetadataRepairCandidate[] safe = plan.Items.Where(item => item.SafeToApply).ToArray();
        MetadataRepairCandidate[] finalizedItems = plan.Items.ToArray();
        Dictionary<string, int> itemIndexes = finalizedItems.Select((item, index) => (item.ItemId, index))
            .ToDictionary(value => value.ItemId, value => value.index, StringComparer.Ordinal);
        string rollbackToken = $"metadata-repair:{taskId}:{Guid.NewGuid():N}";
        int applied = 0, skipped = 0;
        await UpdateTaskAsync(taskId, "Running", "Applying", 0, safe.Length, 0, "正在重新校验修复计划", token);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        RepairPlanDocument committedPlan;
        string summary;
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        try {
            foreach (MetadataRepairCandidate item in safe) {
                token.ThrowIfCancellationRequested();
                if (!await IsMovieStillEligibleAsync(connection, transaction, item, token))
                    throw new InvalidOperationException($"影片 {item.Number} 在预览后发生变化，请重新 Dry Run。");
                ApplyResult result = await ApplyItemAsync(connection, transaction, taskId, rollbackToken, item, token);
                int itemIndex = itemIndexes[item.ItemId];
                if (result.Applied) {
                    applied++;
                    finalizedItems[itemIndex] = item with {
                        Status = "Applied", SafeToApply = false,
                        Reason = "已按确认的 Dry Run 计划写入关联，并记录 Before / After 审计。",
                    };
                } else {
                    skipped++;
                    finalizedItems[itemIndex] = item with {
                        Status = "Skipped", SafeToApply = false,
                        Reason = "执行前复核发现当前状态已有效，未覆盖现有资源。",
                    };
                }
            }
            MetadataRepairCounts counts = plan.Counts with { Applied = applied, Skipped = skipped, Failed = 0 };
            committedPlan = plan with { Counts = counts, Items = finalizedItems, RollbackToken = rollbackToken, DryRun = false };
            string at = Now();
            summary = $"安全修复完成：{applied} 项写入，{skipped} 项因当前状态已有效而跳过。";
            await ExecuteAsync(connection, transaction, """
                UPDATE Tasks SET Status='Running',Stage='Recalculating',Progress=99,CompletedItems=$done,
                    ResultJson=$result,ResultSummary=$summary,CompletedAt=$at,UpdatedAt=$at,CurrentMovieId=NULL
                 WHERE Id=$id
                """, token, ("$done", applied + skipped), ("$result", JsonSerializer.Serialize(committedPlan, JsonOptions)),
                ("$summary", summary), ("$at", at), ("$id", taskId));
            await transaction.CommitAsync(token);
        } catch {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        health.Invalidate();
        MetadataRepairProjection? after = null;
        string? analysisError = null;
        try { after = Projection(await RunFullHealthAnalysisAsync()); }
        catch (Exception error) { analysisError = error.Message; }
        RepairPlanDocument finalPlan = committedPlan with { After = after };
        await using SqliteConnection finish = await OpenAsync(SqliteOpenMode.ReadWrite, CancellationToken.None);
        await ExecuteAsync(finish, null, """
            UPDATE Tasks SET Status=$status,Stage=$stage,Progress=100,ResultJson=$result,ResultSummary=$summary,
                ErrorMessage=$error,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id
            """, CancellationToken.None,
            ("$status", analysisError is null ? "Completed" : "CompletedWithErrors"),
            ("$stage", analysisError is null ? "Completed" : "HealthRefreshFailed"),
            ("$result", JsonSerializer.Serialize(finalPlan, JsonOptions)),
            ("$summary", analysisError is null ? summary : summary + " 健康统计刷新失败。"),
            ("$error", analysisError), ("$at", Now()), ("$id", taskId));
        await logs.WriteAsync(taskId, analysisError is null ? "Info" : "Warning",
            analysisError is null ? summary : $"{summary} 健康统计刷新失败：{analysisError}", CancellationToken.None);
    }

    private async Task<MetadataHealthSummary> RunFullHealthAnalysisAsync()
    {
        MetadataHealthAnalysisState state = health.Start();
        while (state.Running) {
            await Task.Delay(200, CancellationToken.None);
            state = health.GetState();
        }
        if (state.Result is null) throw new InvalidOperationException(state.Error ?? "Metadata Health 重新统计没有返回结果。");
        return state.Result;
    }

    private async Task<RepairPlanDocument> BuildPlanAsync(
        long taskId,
        MetadataRepairScanCommand input,
        MetadataHealthSummary beforeHealth,
        DirectoryIndex storageIndex,
        ConcurrentBag<string> warnings,
        CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        MetadataHealthDefinition.RegisterFileFunctions(connection);
        List<MovieRow> movies = await ReadStandardMoviesAsync(connection, token);
        Dictionary<string, RegisteredPath> registered = await ReadRegisteredPathsAsync(connection, token);
        var items = new List<MetadataRepairCandidate>();
        var directoryCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var directoryAvailabilityCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool DirectoryAvailableFor(string? path) {
            string? directory;
            try { directory = Path.GetDirectoryName(Path.GetFullPath(path ?? string.Empty)); }
            catch (Exception) { return false; }
            if (string.IsNullOrWhiteSpace(directory)) return false;
            if (!directoryAvailabilityCache.TryGetValue(directory, out bool available))
                directoryAvailabilityCache[directory] = available = DirectoryAvailable(directory);
            return available;
        }
        int missingMedia = 0, lowConfidenceMovies = 0, codeMismatch = 0, eligible = 0;
        long existingValid = 0, invalidRecords = 0, missingPhysical = 0, unmatched = 0;
        var projectedResources = new Dictionary<long, HashSet<string>>();

        for (int index = 0; index < movies.Count; index++) {
            token.ThrowIfCancellationRequested();
            MovieRow movie = movies[index];
            if (!File.Exists(movie.VideoPath)) { missingMedia++; continue; }
            MovieNumberExtractionResult extraction = movieNumberExtractor.Extract(movie.FileName);
            if (string.IsNullOrWhiteSpace(extraction.NormalizedNumber)
                || extraction.Confidence < movieNumberExtractor.MinimumAutoSyncConfidence) { lowConfidenceMovies++; continue; }
            string currentNumber = MetadataNamingPolicy.NormalizeMovieNumber(movie.Number, movie.Id);
            if (!currentNumber.Equals(extraction.NormalizedNumber, StringComparison.OrdinalIgnoreCase)) { codeMismatch++; continue; }
            eligible++;
            MovieResources existing = await ReadMovieResourcesAsync(connection, movie.Id, token);
            IReadOnlyList<string> localFiles = ReadRelatedFiles(movie.VideoPath, directoryCache, warnings, token);

            foreach (string resourceType in ResourceTypes.Where(input.Includes)) {
                IReadOnlyList<ResourceRecord> records = existing.For(resourceType);
                ResourceRecord[] validRecords = records.Where(record => FileExists(record.Path)).ToArray();
                bool singleton = resourceType is "Poster" or "Fanart" or "NFO";
                if (validRecords.Length > 0) {
                    existingValid++;
                    if (singleton) continue;
                }
                ResourceRecord[] missingRecords = records.Where(record => !FileExists(record.Path)).ToArray();
                invalidRecords += missingRecords.Length;
                missingPhysical += missingRecords.Length;
                bool dedicatedMediaDirectory = localFiles.Count(IsVideoFile) <= 1;
                List<ScoredPath> candidates = FindCandidates(movie, resourceType, storageIndex, localFiles, registered, dedicatedMediaDirectory);
                if (candidates.Count == 0) {
                    if (missingRecords.Length > 0 && resourceType != "NFO" && input.RepairInvalidPaths) {
                        foreach (ResourceRecord record in missingRecords) {
                            bool rootAvailable = DirectoryAvailableFor(record.Path);
                            items.Add(CreateItem(movie, resourceType, record.Id, record.Path, null,
                                "数据库记录指向的实体文件不存在", rootAvailable ? 100 : 0,
                                "MarkMissing", rootAvailable ? "Safe" : "Skipped",
                                rootAvailable ? "将 ValidationStatus 标记为 Missing，不删除记录。" : "磁盘或 NAS 当前不可访问，保留历史状态。",
                                rootAvailable, false));
                        }
                    } else if (validRecords.Length == 0) unmatched++;
                    continue;
                }

                ScoredPath[] high = candidates.Where(candidate => candidate.Score >= 80).ToArray();
                ScoredPath[] low = candidates.Where(candidate => candidate.Score < 80).ToArray();
                if (resourceType is "Poster" or "Fanart" or "NFO") {
                    if (high.Length > 1) {
                        foreach (ScoredPath candidate in high)
                            items.Add(CreateItem(movie, resourceType, missingRecords.FirstOrDefault()?.Id,
                                missingRecords.FirstOrDefault()?.Path, candidate.Path, candidate.Evidence, candidate.Score,
                                "ManualReview", "Conflict", "存在多个高置信度候选，禁止自动选择。", false, false));
                    } else if (high.Length == 1) {
                        if (AddSafeCandidate(items, movie, resourceType, missingRecords, high[0], input))
                            projectedResources.GetOrAdd(movie.Id).Add(resourceType);
                    } else {
                        foreach (ScoredPath candidate in low)
                            items.Add(CreateItem(movie, resourceType, missingRecords.FirstOrDefault()?.Id,
                                missingRecords.FirstOrDefault()?.Path, candidate.Path, candidate.Evidence, candidate.Score,
                                "ManualReview", "LowConfidence", "置信度低于 80，仅供人工核对。", false, false));
                    }
                } else {
                    foreach (ScoredPath candidate in high) {
                        if (AddSafeCandidate(items, movie, resourceType, [], candidate, input) && validRecords.Length == 0)
                            projectedResources.GetOrAdd(movie.Id).Add(resourceType);
                    }
                    foreach (ScoredPath candidate in low)
                        items.Add(CreateItem(movie, resourceType, null, null, candidate.Path, candidate.Evidence, candidate.Score,
                            "ManualReview", "LowConfidence", "置信度低于 80，仅供人工核对。", false, false));
                }
                if (resourceType != "NFO" && input.RepairInvalidPaths) {
                    int usedRecords = resourceType is "Poster" or "Fanart" && high.Length == 1 ? 1 : 0;
                    foreach (ResourceRecord record in missingRecords.Skip(usedRecords)) {
                        bool rootAvailable = DirectoryAvailableFor(record.Path);
                        items.Add(CreateItem(movie, resourceType, record.Id, record.Path, null,
                            "数据库记录指向的实体文件不存在", rootAvailable ? 100 : 0,
                            "MarkMissing", rootAvailable ? "Safe" : "Skipped",
                            rootAvailable ? "将 ValidationStatus 标记为 Missing，不删除记录。" : "磁盘或 NAS 当前不可访问，保留历史状态。",
                            rootAvailable, false));
                    }
                }
            }
            if (index % 20 == 0 || index == movies.Count - 1) {
                double progress = movies.Count == 0 ? 100 : 10 + (index + 1) * 90d / movies.Count;
                await UpdateTaskAsync(taskId, "Running", "Scanning", progress, movies.Count, index + 1,
                    $"正在扫描 {movie.Number}", token, movie.Id);
            }
        }

        HashSet<string> sharedPaths = items.Where(item => item.SafeToApply && item.CandidatePath is not null)
            .GroupBy(item => FullPath(item.CandidatePath!), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.MovieId).Distinct().Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sharedPaths.Count > 0) {
            for (int index = 0; index < items.Count; index++) {
                MetadataRepairCandidate item = items[index];
                if (item.CandidatePath is null || !sharedPaths.Contains(FullPath(item.CandidatePath))) continue;
                items[index] = item with {
                    Action = "ManualReview", Status = "Conflict", SafeToApply = false,
                    Reason = "同一实体文件同时匹配多部影片，禁止自动建立共享关联。",
                };
            }
        }
        projectedResources.Clear();
        foreach (MetadataRepairCandidate item in items.Where(item => item.SafeToApply && item.CandidatePath is not null && item.Action != "MarkMissing"))
            projectedResources.GetOrAdd(item.MovieId).Add(item.ResourceType);

        MetadataRepairProjection before = Projection(beforeHealth);
        MetadataRepairProjection projected = await ProjectAsync(connection, beforeHealth, projectedResources, items, token);
        projected = projected with {
            UnregisteredResources = Math.Max(0, before.UnregisteredResources
                - items.LongCount(item => item.SafeToApply && item.CandidatePath is not null && item.Action is "RegisterImage" or "LinkNfo")),
        };
        long safe = items.LongCount(item => item.SafeToApply);
        long conflicts = items.LongCount(item => item.Status == "Conflict");
        long lowConfidence = items.LongCount(item => item.Status == "LowConfidence");
        long CountUnregistered(string type) => items.LongCount(item => item.ResourceType == type
            && item.CandidatePath is not null && item.Action is "RegisterImage" or "LinkNfo");
        var counts = new MetadataRepairCounts(movies.Count, eligible, missingMedia, lowConfidenceMovies, codeMismatch,
            CountUnregistered("Poster"), CountUnregistered("Fanart"), CountUnregistered("Preview"),
            CountUnregistered("Screenshot"), CountUnregistered("NFO"), safe, conflicts, lowConfidence,
            existingValid, invalidRecords, missingPhysical, unmatched, 0, 0, 0);
        warnings.Add("Dry Run 只生成候选计划；未修改 Images、Movies.NfoPath、NfoDocuments 或任何媒体文件。");
        warnings.Add("低于 80 分和多候选冲突不会进入安全修复。");
        return new(input, counts, before, projected, null, items.OrderBy(item => item.MovieId).ThenBy(item => item.ResourceType).ThenBy(item => item.CandidatePath).ToArray(),
            warnings.Distinct().ToArray(), true, null);
    }

    private static bool AddSafeCandidate(
        ICollection<MetadataRepairCandidate> items,
        MovieRow movie,
        string type,
        IReadOnlyList<ResourceRecord> missingRecords,
        ScoredPath candidate,
        MetadataRepairScanCommand input)
    {
        ResourceRecord? existing = missingRecords.FirstOrDefault();
        string action = type == "NFO"
            ? existing is null ? "LinkNfo" : "RepairNfoPath"
            : existing is not null && input.RepairInvalidPaths ? "RepairImagePath" : "RegisterImage";
        bool enabled = action switch {
            "RegisterImage" or "LinkNfo" => input.RegisterUnregistered,
            _ => input.RepairInvalidPaths,
        };
        items.Add(CreateItem(movie, type, existing?.Id, existing?.Path, candidate.Path, candidate.Evidence,
            candidate.Score, action, enabled ? "Safe" : "Skipped",
            enabled ? "唯一高置信度实体，可在确认后建立或修正关联。" : "当前修复类型未启用。",
            enabled, false));
        return enabled;
    }

    private static MetadataRepairCandidate CreateItem(
        MovieRow movie,
        string type,
        long? recordId,
        string? before,
        string? candidate,
        string evidence,
        int confidence,
        string action,
        string status,
        string reason,
        bool safe,
        bool overwrite)
    {
        string state = $"{movie.Id}|{type}|{recordId}|{before}|{candidate}|{action}";
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state))).ToLowerInvariant()[..16];
        return new(id, movie.Id, movie.Number, movie.VideoPath, type, recordId, before, candidate, evidence,
            confidence, action, status, reason, safe, overwrite, candidate is null ? null : Fingerprint(candidate));
    }

    private static List<ScoredPath> FindCandidates(
        MovieRow movie,
        string resourceType,
        DirectoryIndex storage,
        IReadOnlyList<string> localFiles,
        IReadOnlyDictionary<string, RegisteredPath> registered,
        bool dedicatedMediaDirectory)
    {
        string code = MetadataNamingPolicy.NormalizeMovieNumber(movie.Number, movie.Id);
        string videoStem = Path.GetFileNameWithoutExtension(movie.FileName);
        var paths = new HashSet<string>(storage.For(resourceType), StringComparer.OrdinalIgnoreCase);
        foreach (string path in localFiles.Where(path => ClassifyLocal(path, movie.VideoPath, code) == resourceType)) paths.Add(path);
        var result = new List<ScoredPath>();
        foreach (string path in paths) {
            // Both sources are directory snapshots from this scan. Rechecking every
            // candidate over NAS turns the movie/resource loop into millions of
            // remote File.Exists calls; execution validates the selected file again.
            if (!MatchesExtension(resourceType, path)) continue;
            string normalized = FullPath(path);
            if (registered.TryGetValue(normalized, out RegisteredPath? owner)) {
                if (owner.MovieId != movie.Id && LooseMatch(path, code, videoStem))
                    result.Add(new(path, 70, $"实体已登记到 MovieId {owner.MovieId}，禁止自动复用"));
                continue;
            }
            (int score, string evidence) = Score(path, resourceType, code, videoStem, movie.VideoPath, dedicatedMediaDirectory);
            if (score > 0) result.Add(new(path, score, evidence));
        }
        return result.GroupBy(value => FullPath(value.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(value => value.Score).First())
            .OrderByDescending(value => value.Score).ThenBy(value => value.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (int Score, string Evidence) Score(
        string path,
        string resourceType,
        string code,
        string videoStem,
        string videoPath,
        bool dedicatedMediaDirectory)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        string parent = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty;
        bool sameDirectory = string.Equals(Path.GetDirectoryName(path), Path.GetDirectoryName(videoPath), StringComparison.OrdinalIgnoreCase);
        if (stem.Equals(videoStem, StringComparison.OrdinalIgnoreCase)) return (100, "文件名与当前视频文件名完全一致");
        if (stem.Equals(code, StringComparison.OrdinalIgnoreCase)) return (90, "文件名与标准化番号完全一致");
        if (parent.Equals(code, StringComparison.OrdinalIgnoreCase)) return (90, "位于标准化番号同名资源目录");
        string lower = stem.ToLowerInvariant();
        if (lower.StartsWith(code.ToLowerInvariant() + "-fanart") && resourceType == "Fanart") return (90, "番号相关 Fanart 命名");
        if (lower.StartsWith(code.ToLowerInvariant() + "-") && resourceType is "Preview" or "Screenshot") return (90, "番号相关序列资源命名");
        if (sameDirectory && dedicatedMediaDirectory && IsStandardName(lower, resourceType)) return (80, "唯一视频目录内的标准资源名称");
        if (LooseMatch(path, code, videoStem)) return (70, "文件名包含番号，但证据不足以唯一确认");
        return (0, string.Empty);
    }

    private static bool LooseMatch(string path, string code, string videoStem)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        return stem.Contains(code, StringComparison.OrdinalIgnoreCase)
            || stem.Contains(videoStem, StringComparison.OrdinalIgnoreCase)
            || (Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty).Equals(code, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStandardName(string stem, string type) => type switch {
        "Poster" => stem is "poster" or "cover",
        "Fanart" => stem is "fanart" or "backdrop" or "background",
        "Preview" => stem.StartsWith("preview") || stem.StartsWith("thumb"),
        "Screenshot" => stem.StartsWith("screenshot") || stem.StartsWith("screen"),
        "NFO" => stem == "movie",
        _ => false,
    };

    private static string? ClassifyLocal(string path, string videoPath, string code)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".nfo") return "NFO";
        if (!ImageExtensions.Contains(extension)) return null;
        string stem = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        string directory = (Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty).ToLowerInvariant();
        if (directory is "extrafanart" or "extrapic" or "previews" or "preview") return "Preview";
        if (directory is "screenshots" or "screenshot") return "Screenshot";
        if (stem.Contains("fanart") || stem is "backdrop" or "background") return "Fanart";
        if (stem.StartsWith("preview") || stem.StartsWith("thumb")) return "Preview";
        if (stem.StartsWith("screenshot") || stem.StartsWith("screen")) return "Screenshot";
        if (stem is "poster" or "cover"
            || stem.Equals(code, StringComparison.OrdinalIgnoreCase)
            || stem.Equals(Path.GetFileNameWithoutExtension(videoPath), StringComparison.OrdinalIgnoreCase)) return "Poster";
        return null;
    }

    private static IReadOnlyList<string> ReadRelatedFiles(
        string videoPath,
        IDictionary<string, IReadOnlyList<string>> cache,
        ConcurrentBag<string> warnings,
        CancellationToken token)
    {
        string directory = Path.GetDirectoryName(videoPath) ?? string.Empty;
        if (cache.TryGetValue(directory, out IReadOnlyList<string>? existing)) return existing;
        var files = new List<string>();
        try {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(directory)) {
                files.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly));
                foreach (string child in Directory.EnumerateDirectories(directory).Where(IsHistoricalResourceDirectory))
                    files.AddRange(EnumerateFilesSafe(child, token, warnings));
            }
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            warnings.Add($"目录扫描失败，已跳过：{directory}；{error.Message}");
        }
        cache[directory] = files;
        return files;
    }

    private static bool IsHistoricalResourceDirectory(string path)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();
        return name is "extrafanart" or "extrapic" or "previews" or "preview" or "screenshots" or "screenshot";
    }

    private static DirectoryIndex BuildStorageIndex(MediaStorageSettingsDto settings, ConcurrentBag<string> warnings, CancellationToken token)
    {
        var values = ResourceTypes.ToDictionary(type => type, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var healthValues = ResourceTypes.ToDictionary(type => type, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var configured = new List<(string Type, string Directory)> {
            ("Poster", settings.PostersDirectory), ("Fanart", settings.FanartDirectory),
            ("Preview", settings.PreviewsDirectory), ("Screenshot", settings.ScreenshotsDirectory), ("NFO", settings.NfoDirectory),
        };
        var historical = new List<(string Type, string Directory)> {
            ("Poster", "Covers"), ("Poster", "Posters"), ("Fanart", "Fanart"), ("Fanart", "BigPic"),
            ("Preview", "Previews"), ("Preview", "ExtraPic"), ("Screenshot", "Screenshots"), ("NFO", "NFO"),
        };
        var rootCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> ReadRoot(string directory) {
            token.ThrowIfCancellationRequested();
            string root;
            try { root = Path.GetFullPath(Path.Combine(settings.RootPath, directory)); }
            catch (Exception) { return []; }
            if (!rootCache.TryGetValue(root, out IReadOnlyList<string>? files))
                rootCache[root] = files = EnumerateFilesSafe(root, token, warnings).ToArray();
            return files;
        }
        foreach ((string type, string directory) in configured.Distinct()) {
            foreach (string file in ReadRoot(directory)) {
                healthValues[type].Add(file);
                if (MatchesExtension(type, file)) values[type].Add(file);
            }
        }
        foreach ((string type, string directory) in historical.Distinct())
            foreach (string file in ReadRoot(directory))
                if (MatchesExtension(type, file)) values[type].Add(file);

        string storageRoot;
        try { storageRoot = Path.GetFullPath(settings.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception) { storageRoot = string.Empty; }
        var healthInventory = new MetadataStorageInventory(storageRoot, healthValues);
        return new(values.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase), healthInventory);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, CancellationToken token, ConcurrentBag<string> warnings)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0) {
            token.ThrowIfCancellationRequested();
            string current = pending.Pop();
            IEnumerable<string> files;
            IEnumerable<string> directories;
            try { files = Directory.EnumerateFiles(current).ToArray(); directories = Directory.EnumerateDirectories(current).ToArray(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                warnings.Add($"目录不可访问，已跳过：{current}；{error.Message}");
                continue;
            }
            foreach (string file in files) yield return file;
            foreach (string directory in directories) pending.Push(directory);
        }
    }

    private static bool MatchesExtension(string type, string path) => type == "NFO"
        ? Path.GetExtension(path).Equals(".nfo", StringComparison.OrdinalIgnoreCase)
        : ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsVideoFile(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".mp4" or ".mkv" or ".avi" or ".wmv" or ".mov" or ".flv" or ".webm" or ".m4v" or ".ts" or ".m2ts";

    private static async Task<List<MovieRow>> ReadStandardMoviesAsync(SqliteConnection connection, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.Id,COALESCE(m.Code,''),f.Id,f.FilePath,f.FileName
              FROM Movies m
              JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
              JOIN Libraries l ON l.Id=f.LibraryId AND l.IsEnabled=1 AND l.LibraryType='Standard'
             WHERE COALESCE(f.ExistsState,'')<>'Missing'
               AND f.Id=(SELECT f2.Id FROM MediaFiles f2 WHERE f2.MovieId=m.Id AND f2.IsPrimary=1 AND f2.MediaType='Video' ORDER BY f2.Id LIMIT 1)
             ORDER BY m.Id
            """;
        var rows = new List<MovieRow>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    private static async Task<MovieResources> ReadMovieResourcesAsync(SqliteConnection connection, long movieId, CancellationToken token)
    {
        var values = ResourceTypes.ToDictionary(type => type, _ => new List<ResourceRecord>(), StringComparer.OrdinalIgnoreCase);
        await using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = "SELECT Id,ImageType,FilePath FROM Images WHERE MovieId=$movie AND ImageType IN ('Poster','Fanart','BigPic','Preview','ExtraPic','Screenshot') ORDER BY Id";
            command.Parameters.AddWithValue("$movie", movieId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) {
                string type = MediaStoragePathResolver.NormalizeResourceType(reader.GetString(1));
                values[type].Add(new(reader.GetInt64(0), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }
        await using (SqliteCommand movie = connection.CreateCommand()) {
            movie.CommandText = "SELECT NfoPath FROM Movies WHERE Id=$movie";
            movie.Parameters.AddWithValue("$movie", movieId);
            string? path = (await movie.ExecuteScalarAsync(token))?.ToString();
            if (!string.IsNullOrWhiteSpace(path)) values["NFO"].Add(new(null, path));
        }
        await using (SqliteCommand documents = connection.CreateCommand()) {
            documents.CommandText = "SELECT Id,FilePath FROM NfoDocuments WHERE MovieId=$movie ORDER BY Id";
            documents.Parameters.AddWithValue("$movie", movieId);
            await using SqliteDataReader reader = await documents.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) values["NFO"].Add(new(reader.GetInt64(0), reader.GetString(1)));
        }
        foreach (string key in values.Keys.ToArray())
            values[key] = values[key].GroupBy(value => value.Path ?? string.Empty, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        return new(values);
    }

    private static async Task<Dictionary<string, RegisteredPath>> ReadRegisteredPathsAsync(SqliteConnection connection, CancellationToken token)
    {
        var result = new Dictionary<string, RegisteredPath>(StringComparer.OrdinalIgnoreCase);
        await using (SqliteCommand images = connection.CreateCommand()) {
            images.CommandText = "SELECT MovieId,ImageType,FilePath FROM Images WHERE MovieId IS NOT NULL AND trim(COALESCE(FilePath,''))<>''";
            await using SqliteDataReader reader = await images.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) result[FullPath(reader.GetString(2))] = new(reader.GetInt64(0), MediaStoragePathResolver.NormalizeResourceType(reader.GetString(1)));
        }
        await using (SqliteCommand nfo = connection.CreateCommand()) {
            nfo.CommandText = "SELECT Id,NfoPath FROM Movies WHERE trim(COALESCE(NfoPath,''))<>'' UNION ALL SELECT MovieId,FilePath FROM NfoDocuments WHERE trim(COALESCE(FilePath,''))<>''";
            await using SqliteDataReader reader = await nfo.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) result[FullPath(reader.GetString(1))] = new(reader.GetInt64(0), "NFO");
        }
        return result;
    }

    private static async Task<MetadataRepairProjection> ProjectAsync(
        SqliteConnection connection,
        MetadataHealthSummary health,
        IReadOnlyDictionary<long, HashSet<string>> projectedResources,
        IReadOnlyList<MetadataRepairCandidate> items,
        CancellationToken token)
    {
        long completeGain = 0, posterGain = 0, fanartGain = 0, previewGain = 0, screenshotGain = 0, nfoGain = 0;
        foreach ((long movieId, HashSet<string> resources) in projectedResources) {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT CASE WHEN {MetadataHealthDefinition.Number("m")} AND {MetadataHealthDefinition.Title("m")}
                    AND {MetadataHealthDefinition.ReleaseDate("m")} AND {MetadataHealthDefinition.Studios("m")}
                    AND {MetadataHealthDefinition.Actors("m")} AND {MetadataHealthDefinition.ProviderTags("m")}
                    THEN 1 ELSE 0 END,
                    CASE WHEN {MetadataHealthDefinition.ImagePhysical("m", "Poster")} THEN 1 ELSE 0 END,
                    CASE WHEN {MetadataHealthDefinition.ImagePhysical("m", "Fanart")} THEN 1 ELSE 0 END,
                    CASE WHEN {MetadataHealthDefinition.NfoPhysical("m", true)} THEN 1 ELSE 0 END,
                    CASE WHEN {MetadataHealthDefinition.ImagePhysical("m", "Preview")} THEN 1 ELSE 0 END,
                    CASE WHEN {MetadataHealthDefinition.ImagePhysical("m", "Screenshot")} THEN 1 ELSE 0 END
                  FROM Movies m WHERE m.Id=$id
                """;
            command.Parameters.AddWithValue("$id", movieId);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) continue;
            bool existingPoster = reader.GetInt64(1) == 1, existingFanart = reader.GetInt64(2) == 1, existingNfo = reader.GetInt64(3) == 1;
            bool existingPreview = reader.GetInt64(4) == 1, existingScreenshot = reader.GetInt64(5) == 1;
            bool poster = existingPoster || resources.Contains("Poster");
            bool fanart = existingFanart || resources.Contains("Fanart");
            bool nfo = existingNfo || resources.Contains("NFO");
            if (!existingPoster && resources.Contains("Poster")) posterGain++;
            if (!existingFanart && resources.Contains("Fanart")) fanartGain++;
            if (!existingNfo && resources.Contains("NFO")) nfoGain++;
            if (!existingPreview && resources.Contains("Preview")) previewGain++;
            if (!existingScreenshot && resources.Contains("Screenshot")) screenshotGain++;
            if (reader.GetInt64(0) == 1 && poster && fanart && nfo && !(existingPoster && existingFanart && existingNfo)) completeGain++;
        }
        return new(health.CompleteMovies + completeGain,
            Math.Min(health.TotalMovies, health.Coverage.Poster.PhysicalMovies + posterGain),
            Math.Min(health.TotalMovies, health.Coverage.Fanart.PhysicalMovies + fanartGain),
            Math.Min(health.TotalMovies, health.Coverage.Preview.PhysicalMovies + previewGain),
            Math.Min(health.TotalMovies, health.Coverage.Screenshot.PhysicalMovies + screenshotGain),
            Math.Min(health.TotalMovies, health.Coverage.Nfo.PhysicalMovies + nfoGain),
            health.Coverage.InvalidResourceRecords,
            Math.Max(0, health.Coverage.UnregisteredResources - projectedResources.Sum(pair => pair.Value.Count)));
    }

    private async Task<ApplyResult> ApplyItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long taskId,
        string rollbackToken,
        MetadataRepairCandidate item,
        CancellationToken token)
    {
        if (item.CandidatePath is not null) {
            if (!FileExists(item.CandidatePath) || Fingerprint(item.CandidatePath) != item.FileFingerprint)
                throw new IOException($"候选资源在预览后变化：{item.CandidatePath}");
        }
        MutationSnapshot before = await CaptureSnapshotAsync(connection, transaction, item, token);
        bool applied = item.Action switch {
            "RegisterImage" => await RegisterImageAsync(connection, transaction, item, token),
            "RepairImagePath" => await RepairImagePathAsync(connection, transaction, item, token),
            "MarkMissing" => await MarkMissingAsync(connection, transaction, item, token),
            "LinkNfo" or "RepairNfoPath" => await LinkNfoAsync(connection, transaction, item, token),
            _ => false,
        };
        if (!applied) return new(false);
        MutationSnapshot after = await CaptureSnapshotAsync(connection, transaction, item, token);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO OperationAudit(OperationType,EntityType,EntityId,BeforeJson,AfterJson,RollbackToken,CreatedAt)
            VALUES('MetadataRepair','MetadataResource',$movie,$before,$after,$token,$at)
            """, token, ("$movie", item.MovieId), ("$before", JsonSerializer.Serialize(before, JsonOptions)),
            ("$after", JsonSerializer.Serialize(after, JsonOptions)), ("$token", rollbackToken), ("$at", Now()));
        return new(true);
    }

    private static async Task<bool> RegisterImageAsync(SqliteConnection connection, SqliteTransaction transaction, MetadataRepairCandidate item, CancellationToken token)
    {
        if (item.CandidatePath is null) return false;
        if (item.ResourceType is "Poster" or "Fanart"
            && await HasValidResourceAsync(connection, transaction, item.MovieId, item.ResourceType, token)) return false;
        if (await ExactImagePathExistsAsync(connection, transaction, item.MovieId, item.ResourceType, item.CandidatePath, token)) return false;
        if (await PathOwnedByOtherMovieAsync(connection, transaction, item.MovieId, item.CandidatePath, token))
            throw new InvalidOperationException($"候选图片已关联其他影片：{item.CandidatePath}");
        var file = new FileInfo(item.CandidatePath);
        return await ExecuteAsync(connection, transaction, """
            INSERT INTO Images(MovieId,ActorId,ImageType,FilePath,FileSize,IsPrimary,SourceProvider,
                Ownership,IsLocked,IsDerived,ValidationStatus,ValidatedAt,CreatedAt,UpdatedAt)
            VALUES($movie,NULL,$type,$path,$size,$primary,'OfflineRepair','Legacy',0,0,'Valid',$at,$at,$at)
            """, token, ("$movie", item.MovieId), ("$type", item.ResourceType), ("$path", item.CandidatePath),
            ("$size", file.Length), ("$primary", item.ResourceType is "Poster" or "Fanart" ? 1 : 0), ("$at", Now())) == 1;
    }

    private static async Task<bool> RepairImagePathAsync(SqliteConnection connection, SqliteTransaction transaction, MetadataRepairCandidate item, CancellationToken token)
    {
        if (item.ExistingRecordId is null || item.CandidatePath is null) return false;
        if (await HasValidResourceAsync(connection, transaction, item.MovieId, item.ResourceType, token)) return false;
        if (await PathOwnedByOtherMovieAsync(connection, transaction, item.MovieId, item.CandidatePath, token))
            throw new InvalidOperationException($"候选图片已关联其他影片：{item.CandidatePath}");
        var file = new FileInfo(item.CandidatePath);
        return await ExecuteAsync(connection, transaction, """
            UPDATE Images SET FilePath=$path,FileSize=$size,ValidationStatus='Valid',ValidatedAt=$at,UpdatedAt=$at
             WHERE Id=$id AND MovieId=$movie AND (FilePath=$before OR (FilePath IS NULL AND $before IS NULL))
               AND lmm_file_exists(FilePath)=0
            """, token, ("$path", item.CandidatePath), ("$size", file.Length), ("$at", Now()),
            ("$id", item.ExistingRecordId), ("$movie", item.MovieId), ("$before", item.CurrentDatabasePath)) == 1;
    }

    private static async Task<bool> MarkMissingAsync(SqliteConnection connection, SqliteTransaction transaction, MetadataRepairCandidate item, CancellationToken token)
    {
        if (item.ExistingRecordId is null || !DirectoryAvailable(Path.GetDirectoryName(FullPath(item.CurrentDatabasePath ?? string.Empty)))) return false;
        return await ExecuteAsync(connection, transaction, """
            UPDATE Images SET ValidationStatus='Missing',ValidatedAt=$at,UpdatedAt=$at
             WHERE Id=$id AND MovieId=$movie AND (FilePath=$before OR (FilePath IS NULL AND $before IS NULL))
               AND lmm_file_exists(FilePath)=0 AND ValidationStatus<>'Missing'
            """, token, ("$at", Now()), ("$id", item.ExistingRecordId), ("$movie", item.MovieId),
            ("$before", item.CurrentDatabasePath)) == 1;
    }

    private static async Task<bool> LinkNfoAsync(SqliteConnection connection, SqliteTransaction transaction, MetadataRepairCandidate item, CancellationToken token)
    {
        if (item.CandidatePath is null) return false;
        await using SqliteCommand current = connection.CreateCommand();
        current.Transaction = transaction;
        current.CommandText = "SELECT NfoPath FROM Movies WHERE Id=$movie";
        current.Parameters.AddWithValue("$movie", item.MovieId);
        string? existing = (await current.ExecuteScalarAsync(token))?.ToString();
        if (MetadataHealthDefinition.FileExists(existing)) return false;
        string at = Now();
        await ExecuteAsync(connection, transaction, "UPDATE Movies SET NfoPath=$path,UpdatedAt=$at WHERE Id=$movie",
            token, ("$path", item.CandidatePath), ("$at", at), ("$movie", item.MovieId));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO NfoDocuments(MovieId,FilePath,Ownership,IsLocked,SourceProvider,LastReadAt,CreatedAt,UpdatedAt)
            VALUES($movie,$path,'User',1,'OfflineRepair',$at,$at,$at)
            ON CONFLICT(MovieId,FilePath) DO UPDATE SET UpdatedAt=excluded.UpdatedAt
            """, token, ("$movie", item.MovieId), ("$path", item.CandidatePath), ("$at", at));
        return true;
    }

    private static async Task<bool> IsMovieStillEligibleAsync(SqliteConnection connection, SqliteTransaction transaction, MetadataRepairCandidate item, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM Movies m
             WHERE m.Id=$movie AND upper(trim(COALESCE(m.Code,'')))=$code
               AND EXISTS(SELECT 1 FROM MediaFiles f JOIN Libraries l ON l.Id=f.LibraryId
                   WHERE f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video' AND l.IsEnabled=1
                     AND l.LibraryType='Standard' AND COALESCE(f.ExistsState,'')<>'Missing')
            """;
        command.Parameters.AddWithValue("$movie", item.MovieId);
        command.Parameters.AddWithValue("$code", item.Number);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L) == 1 && FileExists(item.VideoPath);
    }

    private static async Task<bool> HasValidResourceAsync(SqliteConnection connection, SqliteTransaction transaction, long movieId, string type, CancellationToken token)
    {
        string condition = type switch {
            "Fanart" => "ImageType IN ('Fanart','BigPic')",
            "Preview" => "ImageType IN ('Preview','ExtraPic')",
            _ => "ImageType=$type",
        };
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM Images WHERE MovieId=$movie AND {condition} AND lmm_file_exists(FilePath)=1";
        command.Parameters.AddWithValue("$movie", movieId);
        command.Parameters.AddWithValue("$type", type);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L) > 0;
    }

    private static async Task<bool> ExactImagePathExistsAsync(SqliteConnection connection, SqliteTransaction transaction, long movieId, string type, string path, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Images WHERE MovieId=$movie AND ImageType=$type AND lower(FilePath)=lower($path)";
        command.Parameters.AddWithValue("$movie", movieId);
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$path", path);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L) > 0;
    }

    private static async Task<bool> PathOwnedByOtherMovieAsync(SqliteConnection connection, SqliteTransaction transaction, long movieId, string path, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Images WHERE MovieId<>$movie AND lower(FilePath)=lower($path)";
        command.Parameters.AddWithValue("$movie", movieId);
        command.Parameters.AddWithValue("$path", path);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L) > 0;
    }

    private static async Task<MutationSnapshot> CaptureSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction, MetadataRepairCandidate item, CancellationToken token)
    {
        ImageSnapshot? image = null;
        if (item.ResourceType != "NFO") {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = item.ExistingRecordId is not null
                ? "SELECT Id,MovieId,ImageType,FilePath,FileSize,IsPrimary,Ownership,IsLocked,ValidationStatus,ValidatedAt,UpdatedAt FROM Images WHERE Id=$id"
                : "SELECT Id,MovieId,ImageType,FilePath,FileSize,IsPrimary,Ownership,IsLocked,ValidationStatus,ValidatedAt,UpdatedAt FROM Images WHERE MovieId=$movie AND ImageType=$type AND lower(FilePath)=lower($path) ORDER BY Id DESC LIMIT 1";
            command.Parameters.AddWithValue("$id", item.ExistingRecordId ?? 0);
            command.Parameters.AddWithValue("$movie", item.MovieId);
            command.Parameters.AddWithValue("$type", item.ResourceType);
            command.Parameters.AddWithValue("$path", item.CandidatePath ?? string.Empty);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token)) image = new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.GetInt64(5),
                reader.GetString(6), reader.GetInt64(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetString(10));
        }
        string? movieNfo = null;
        NfoSnapshot? nfo = null;
        if (item.ResourceType == "NFO") {
            await using SqliteCommand movie = connection.CreateCommand();
            movie.Transaction = transaction;
            movie.CommandText = "SELECT NfoPath FROM Movies WHERE Id=$movie";
            movie.Parameters.AddWithValue("$movie", item.MovieId);
            movieNfo = (await movie.ExecuteScalarAsync(token))?.ToString();
            await using SqliteCommand document = connection.CreateCommand();
            document.Transaction = transaction;
            document.CommandText = "SELECT Id,MovieId,FilePath,Ownership,IsLocked,SourceProvider,LastReadAt,UpdatedAt FROM NfoDocuments WHERE MovieId=$movie AND lower(FilePath)=lower($path) LIMIT 1";
            document.Parameters.AddWithValue("$movie", item.MovieId);
            document.Parameters.AddWithValue("$path", item.CandidatePath ?? string.Empty);
            await using SqliteDataReader reader = await document.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token)) nfo = new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7));
        }
        return new(item.ItemId, item.Action, item.MovieId, image, movieNfo, nfo);
    }

    private static async Task RestoreSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction, MutationSnapshot before, MutationSnapshot after, CancellationToken token)
    {
        if (before.Image is null && after.Image is not null) {
            int changed = await ExecuteAsync(connection, transaction,
                "DELETE FROM Images WHERE Id=$id AND MovieId=$movie AND FilePath=$path AND UpdatedAt=$updated",
                token, ("$id", after.Image.Id), ("$movie", after.Image.MovieId), ("$path", after.Image.FilePath), ("$updated", after.Image.UpdatedAt));
            if (changed != 1) throw new InvalidOperationException("图片记录在修复后又被修改，拒绝回滚覆盖用户数据。");
        } else if (before.Image is not null && after.Image is not null) {
            int changed = await ExecuteAsync(connection, transaction, """
                UPDATE Images SET ImageType=$type,FilePath=$path,FileSize=$size,IsPrimary=$primary,Ownership=$owner,
                    IsLocked=$locked,ValidationStatus=$status,ValidatedAt=$validated,UpdatedAt=$updated
                 WHERE Id=$id AND MovieId=$movie AND UpdatedAt=$afterUpdated
                """, token, ("$type", before.Image.ImageType), ("$path", before.Image.FilePath), ("$size", before.Image.FileSize),
                ("$primary", before.Image.IsPrimary), ("$owner", before.Image.Ownership), ("$locked", before.Image.IsLocked),
                ("$status", before.Image.ValidationStatus), ("$validated", before.Image.ValidatedAt), ("$updated", before.Image.UpdatedAt),
                ("$id", before.Image.Id), ("$movie", before.Image.MovieId), ("$afterUpdated", after.Image.UpdatedAt));
            if (changed != 1) throw new InvalidOperationException("图片记录在修复后又被修改，拒绝回滚覆盖用户数据。");
        }
        if (before.Action is "LinkNfo" or "RepairNfoPath") {
            int changed = await ExecuteAsync(connection, transaction,
                "UPDATE Movies SET NfoPath=$before,UpdatedAt=$at WHERE Id=$movie AND (NfoPath=$after OR (NfoPath IS NULL AND $after IS NULL))",
                token, ("$before", before.MovieNfoPath), ("$after", after.MovieNfoPath), ("$at", Now()), ("$movie", before.MovieId));
            if (changed != 1) throw new InvalidOperationException("NFO 关联在修复后又被修改，拒绝回滚覆盖用户数据。");
            if (before.Nfo is null && after.Nfo is not null) {
                await ExecuteAsync(connection, transaction,
                    "DELETE FROM NfoDocuments WHERE Id=$id AND MovieId=$movie AND FilePath=$path AND UpdatedAt=$updated",
                    token, ("$id", after.Nfo.Id), ("$movie", after.Nfo.MovieId), ("$path", after.Nfo.FilePath), ("$updated", after.Nfo.UpdatedAt));
            } else if (before.Nfo is not null && after.Nfo is not null) {
                await ExecuteAsync(connection, transaction, """
                    UPDATE NfoDocuments SET FilePath=$path,Ownership=$owner,IsLocked=$locked,SourceProvider=$provider,
                        LastReadAt=$read,UpdatedAt=$updated WHERE Id=$id AND UpdatedAt=$afterUpdated
                    """, token, ("$path", before.Nfo.FilePath), ("$owner", before.Nfo.Ownership), ("$locked", before.Nfo.IsLocked),
                    ("$provider", before.Nfo.SourceProvider), ("$read", before.Nfo.LastReadAt), ("$updated", before.Nfo.UpdatedAt),
                    ("$id", before.Nfo.Id), ("$afterUpdated", after.Nfo.UpdatedAt));
            }
        }
    }

    private async Task<long?> ClaimNextAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        long id = await ScalarLongAsync(connection, null,
            "SELECT COALESCE(MIN(Id),0) FROM Tasks WHERE TaskType='MetadataRepair' AND Status='Pending' AND Stage IN ('Scan','Apply')",
            token);
        if (id == 0) return null;
        int changed = await ExecuteAsync(connection, null,
            "UPDATE Tasks SET Status='Running',UpdatedAt=$at WHERE Id=$id AND Status='Pending'",
            token, ("$at", Now()), ("$id", id));
        return changed == 1 ? id : null;
    }

    private async Task UpdateTaskAsync(long taskId, string status, string stage, double progress, long total, long completed, string summary,
        CancellationToken token, long? movieId = null)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteAsync(connection, null, """
            UPDATE Tasks SET Status=$status,Stage=$stage,Progress=$progress,TotalItems=$total,CompletedItems=$completed,
                ResultSummary=$summary,CurrentMovieId=$movie,StartedAt=COALESCE(StartedAt,$at),UpdatedAt=$at
             WHERE Id=$id AND TaskType='MetadataRepair'
            """, token, ("$status", status), ("$stage", stage), ("$progress", progress), ("$total", total),
            ("$completed", completed), ("$summary", summary), ("$movie", movieId), ("$at", Now()), ("$id", taskId));
    }

    private async Task MarkCancelledAsync(long taskId)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, CancellationToken.None);
        await ExecuteAsync(connection, null, """
            UPDATE Tasks SET Status='Cancelled',Stage='Cancelled',CancellationRequested=1,
                ResultSummary='扫描或修复已取消；未提交当前事务。',CompletedAt=$at,UpdatedAt=$at,CurrentMovieId=NULL
             WHERE Id=$id AND TaskType='MetadataRepair' AND Status<>'Completed'
            """, CancellationToken.None, ("$at", Now()), ("$id", taskId));
    }

    private async Task MarkFailedAsync(long taskId, Exception error)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, CancellationToken.None);
        await ExecuteAsync(connection, null, """
            UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,
                ResultSummary='元数据修复失败，当前事务未提交。',CompletedAt=$at,UpdatedAt=$at,CurrentMovieId=NULL
             WHERE Id=$id AND TaskType='MetadataRepair'
            """, CancellationToken.None, ("$error", error.Message), ("$at", Now()), ("$id", taskId));
        await logs.WriteAsync(taskId, "Error", error.Message);
    }

    private async Task RecoverInterruptedAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteAsync(connection, null, """
            UPDATE Tasks SET Status='Failed',Stage='Interrupted',ErrorMessage='上次元数据修复异常中断；事务已由 SQLite 回滚，请重新 Dry Run。',
                ResultSummary='异常中断，未确认的计划不会自动继续。',CompletedAt=$at,UpdatedAt=$at
             WHERE TaskType='MetadataRepair' AND Status='Running'
            """, token, ("$at", Now()));
    }

    private static async Task<TaskRow> ReadTaskAsync(SqliteConnection connection, long id, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Status,COALESCE(Stage,''),Progress,PayloadJson,ResultJson,ErrorMessage,CreatedAt,CompletedAt FROM Tasks WHERE Id=$id AND TaskType='MetadataRepair'";
        command.Parameters.AddWithValue("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("元数据修复任务不存在。");
        return new(reader.GetString(0), reader.GetString(1), reader.GetDouble(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    private static async Task<List<AuditRow>> ReadAuditsAsync(SqliteConnection connection, string tokenValue, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id,BeforeJson,AfterJson FROM OperationAudit WHERE OperationType='MetadataRepair' AND RollbackToken=$token AND RevertedAt IS NULL ORDER BY Id";
        command.Parameters.AddWithValue("$token", tokenValue);
        var values = new List<AuditRow>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) values.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return values;
    }

    private static string ConfirmationToken(long taskId, RepairPlanDocument plan)
    {
        string state = JsonSerializer.Serialize(plan with { RollbackToken = null }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"metadata-repair|{taskId}|{state}"))).ToLowerInvariant();
    }

    private static void VerifyToken(string expected, string supplied)
    {
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected ?? string.Empty);
        byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied ?? string.Empty);
        if (expectedBytes.Length != suppliedBytes.Length || !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
            throw new UnauthorizedAccessException("修复计划已变化，请重新 Dry Run 后确认。");
    }

    private static void Validate(MetadataRepairScanCommand input)
    {
        if (!ResourceTypes.Any(input.Includes)) throw new ArgumentException("至少选择一种要扫描的资源类型。");
        if (!input.RepairInvalidPaths && !input.RegisterUnregistered)
            throw new ArgumentException("至少启用“修正失效路径”或“登记未登记资源”中的一项。");
    }

    private static MetadataRepairProjection Projection(MetadataHealthSummary value) => new(value.CompleteMovies,
        value.Coverage.Poster.PhysicalMovies, value.Coverage.Fanart.PhysicalMovies,
        value.Coverage.Preview.PhysicalMovies, value.Coverage.Screenshot.PhysicalMovies,
        value.Coverage.Nfo.PhysicalMovies, value.Coverage.InvalidResourceRecords, value.Coverage.UnregisteredResources);

    private static MetadataRepairCounts EmptyCounts() => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static MetadataRepairProjection EmptyProjection() => new(0, 0, 0, 0, 0, 0, 0, 0);
    private static RepairPlanDocument? DeserializePlan(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : JsonSerializer.Deserialize<RepairPlanDocument>(value, JsonOptions);
    private static MutationSnapshot DeserializeSnapshot(string value) => JsonSerializer.Deserialize<MutationSnapshot>(value, JsonOptions)
        ?? throw new InvalidOperationException("修复审计快照损坏，无法安全回滚。");
    private static bool FileExists(string? path) => MetadataHealthDefinition.FileExists(path);
    private static string FullPath(string path) { try { return Path.GetFullPath(path); } catch { return path; } }
    private static string Fingerprint(string path) { var file = new FileInfo(path); return FileExists(path) ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : "missing"; }
    private static bool DirectoryAvailable(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return false;
        try {
            using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
            _ = entries.MoveNext();
            return true;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Private }.ToString());
        await connection.OpenAsync(token);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(token);
        MetadataHealthDefinition.RegisterFileFunctions(connection);
        return connection;
    }

    private static async Task<int> ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<long> InsertIdAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token));
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L);
    }

    private static string RenderMarkdown(MetadataRepairPreview preview)
    {
        var text = new StringBuilder();
        text.AppendLine("# Metadata Repair Dry Run").AppendLine();
        text.AppendLine($"- TaskId: {preview.TaskId}");
        text.AppendLine($"- Status: {preview.Status}");
        text.AppendLine($"- Standard scanned: {preview.Counts.ScannedStandardMovies}");
        text.AppendLine($"- Safe repairs: {preview.Counts.SafeRepairs}");
        text.AppendLine($"- Conflicts: {preview.Counts.Conflicts}");
        text.AppendLine($"- Low confidence: {preview.Counts.LowConfidence}");
        text.AppendLine($"- Complete: {preview.Before.CompleteMovies} -> {preview.Projected.CompleteMovies}");
        text.AppendLine($"- Poster: {preview.Before.PosterMovies} -> {preview.Projected.PosterMovies}");
        text.AppendLine($"- Fanart: {preview.Before.FanartMovies} -> {preview.Projected.FanartMovies}");
        text.AppendLine($"- NFO: {preview.Before.NfoMovies} -> {preview.Projected.NfoMovies}").AppendLine();
        text.AppendLine("| MovieId | Number | Type | Action | Confidence | Status | Candidate |");
        text.AppendLine("|---:|---|---|---|---:|---|---|");
        foreach (MetadataRepairCandidate item in preview.Items)
            text.AppendLine($"| {item.MovieId} | {EscapeMarkdown(item.Number)} | {item.ResourceType} | {item.Action} | {item.Confidence} | {item.Status} | {EscapeMarkdown(item.CandidatePath ?? string.Empty)} |");
        return text.ToString();
    }

    private static string RenderCsv(MetadataRepairPreview preview)
    {
        var text = new StringBuilder();
        text.AppendLine("TaskId,MovieId,Number,VideoPath,ResourceType,ExistingRecordId,CurrentDatabasePath,CandidatePath,Evidence,Confidence,Action,Status,Reason,SafeToApply,WillOverwrite");
        foreach (MetadataRepairCandidate item in preview.Items) text.AppendLine(string.Join(',', new[] {
            preview.TaskId.ToString(), item.MovieId.ToString(), item.Number, item.VideoPath, item.ResourceType,
            item.ExistingRecordId?.ToString() ?? string.Empty, item.CurrentDatabasePath ?? string.Empty,
            item.CandidatePath ?? string.Empty, item.Evidence, item.Confidence.ToString(), item.Action,
            item.Status, item.Reason, item.SafeToApply.ToString(), item.WillOverwrite.ToString(),
        }.Select(Csv)));
        return text.ToString();
    }

    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
    private static string EscapeMarkdown(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private sealed record RepairPlanDocument(
        MetadataRepairScanCommand Request,
        MetadataRepairCounts Counts,
        MetadataRepairProjection Before,
        MetadataRepairProjection Projected,
        MetadataRepairProjection? After,
        IReadOnlyList<MetadataRepairCandidate> Items,
        IReadOnlyList<string> Warnings,
        bool DryRun,
        string? RollbackToken);
    private sealed record TaskRow(string Status, string Stage, double Progress, string? PayloadJson, string? ResultJson, string? ErrorMessage, string CreatedAt, string? CompletedAt);
    private sealed record MovieRow(long Id, string Number, long MediaFileId, string VideoPath, string FileName);
    private sealed record ResourceRecord(long? Id, string? Path);
    private sealed record RegisteredPath(long MovieId, string Type);
    private sealed record ScoredPath(string Path, int Score, string Evidence);
    private sealed record ApplyResult(bool Applied);
    private sealed record AuditRow(long Id, string BeforeJson, string AfterJson);
    private sealed record ImageSnapshot(long Id, long MovieId, string ImageType, string? FilePath, long? FileSize, long IsPrimary,
        string Ownership, long IsLocked, string ValidationStatus, string? ValidatedAt, string UpdatedAt);
    private sealed record NfoSnapshot(long Id, long MovieId, string FilePath, string Ownership, long IsLocked,
        string? SourceProvider, string? LastReadAt, string UpdatedAt);
    private sealed record MutationSnapshot(string ItemId, string Action, long MovieId, ImageSnapshot? Image, string? MovieNfoPath, NfoSnapshot? Nfo);
    private sealed record DirectoryIndex(
        IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
        MetadataStorageInventory HealthInventory)
    {
        public IReadOnlyList<string> For(string type) => Values.GetValueOrDefault(type) ?? [];
    }
    private sealed record MovieResources(IReadOnlyDictionary<string, List<ResourceRecord>> Values)
    {
        public IReadOnlyList<ResourceRecord> For(string type) => Values.GetValueOrDefault(type) ?? [];
    }
}

internal static class MetadataRepairExtensions
{
    public static bool Includes(this MetadataRepairScanCommand input, string type) => type switch {
        "Poster" => input.Poster,
        "Fanart" => input.Fanart,
        "Preview" => input.Preview,
        "Screenshot" => input.Screenshot,
        "NFO" => input.Nfo,
        _ => false,
    };

    public static HashSet<string> GetOrAdd(this IDictionary<long, HashSet<string>> values, long movieId)
    {
        if (!values.TryGetValue(movieId, out HashSet<string>? result)) values[movieId] = result = new(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}
