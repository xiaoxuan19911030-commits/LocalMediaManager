using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace LocalMediaManager.Bridge;

public sealed record MetadataCompletionScanCommand(
    bool Actors = true,
    bool Genres = true,
    bool Poster = true,
    bool Fanart = true,
    bool Nfo = true,
    bool Description = true,
    bool Series = true,
    bool Director = true,
    bool Studio = true,
    bool ReleaseDate = true,
    int Concurrency = 4,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ProviderPriorities = null);

public sealed record MetadataCompletionLaunchResult(long TaskId, string Status, string Message);
public sealed record MetadataCompletionExecuteCommand(string ConfirmationToken);
public sealed record MetadataCompletionExportResult(string MarkdownPath, string CsvPath, long Items, string Message);
public sealed record MetadataCompletionProviderCapability(string Provider, IReadOnlyList<string> Fields, int MinimumDelayMilliseconds);

public sealed record MetadataCompletionCounts(
    long ScannedStandardMovies,
    long IncompleteMovies,
    long EligibleMovies,
    long PlannedNetworkMovies,
    long ExcludedMissingMedia,
    long ExcludedLowConfidence,
    long ExcludedMultipleNumbers,
    long ExcludedCodeConflict,
    long ExcludedLocked,
    IReadOnlyDictionary<string, long> MissingByField,
    IReadOnlyDictionary<string, long> ProviderRequests,
    long Completed,
    long Partial,
    long Skipped,
    long Failed,
    long NoResult,
    long Conflict);

public sealed record MetadataCompletionProjection(
    long StandardMovies,
    long CompleteBefore,
    long CompleteProjected,
    long? CompleteAfter,
    long EstimatedSeconds);

public sealed record MetadataCompletionItem(
    string ItemId,
    long MovieId,
    string Number,
    string VideoPath,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> RequiredMissingFields,
    IReadOnlyList<string> ProtectedFields,
    IReadOnlyList<string> ProviderPlan,
    string Status,
    string Reason,
    string? FailureCategory,
    int Attempts,
    long ElapsedMilliseconds,
    IReadOnlyList<string> AddedFields,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ProviderContributions);

public sealed record MetadataCompletionPreview(
    long TaskId,
    string Status,
    string Stage,
    double Progress,
    string ConfirmationToken,
    MetadataCompletionScanCommand Options,
    IReadOnlyList<MetadataCompletionProviderCapability> Capabilities,
    MetadataCompletionCounts Counts,
    MetadataCompletionProjection Projection,
    IReadOnlyList<MetadataCompletionItem> Items,
    IReadOnlyList<string> Warnings,
    string CreatedAt,
    string? CompletedAt,
    bool CanExecute,
    bool CanResume,
    bool CanRollback,
    bool DryRun);

public interface IMetadataCompletionProviderClient
{
    Task<ProviderMetadata?> GetMetadataAsync(
        string provider,
        string code,
        string moviePath,
        MetadataProviderContext context,
        CancellationToken cancellationToken);
}

public sealed class MetadataCompletionProviderClient(
    MdcNgProvider mdcNg,
    MetaTubeProvider metaTube,
    JavBusProvider javBus) : IMetadataCompletionProviderClient
{
    public async Task<ProviderMetadata?> GetMetadataAsync(
        string provider,
        string code,
        string moviePath,
        MetadataProviderContext context,
        CancellationToken cancellationToken)
    {
        IMetadataProvider source = provider switch {
            "MDC-NG" => mdcNg,
            "MetaTube" => metaTube,
            "JavBus" => javBus,
            _ => throw new ArgumentException($"Unknown metadata completion provider: {provider}", nameof(provider)),
        };
        MetadataProviderContext scoped = context with {
            PreferredSource = provider,
            CurrentMoviePath = moviePath,
        };
        IReadOnlyList<MetadataSearchResult> results = await source.SearchAsync(code, scoped, cancellationToken);
        foreach (MetadataSearchResult result in results.Take(3)) {
            ProviderMetadata? metadata = await source.GetMetadataAsync(result, scoped, cancellationToken);
            if (metadata is null) continue;
            if (JavBusCode.Normalize(metadata.Code).Equals(JavBusCode.Normalize(code), StringComparison.OrdinalIgnoreCase))
                return metadata;
        }
        return null;
    }
}

public sealed class MetadataCompletionWorkflow : BackgroundService
{
    private const string TaskType = "MetadataCompletion";
    private const int MaximumAttempts = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] KnownFields = [
        "Actors", "Genres", "Poster", "Fanart", "NFO", "Description", "Series", "Director", "Studio", "ReleaseDate",
    ];
    private static readonly string[] RequiredFields = ["ReleaseDate", "Studio", "Actors", "Genres", "Poster", "Fanart", "NFO"];
    private static readonly IReadOnlyList<MetadataCompletionProviderCapability> CapabilityCatalog = [
        new("JavBus", ["Actors", "Genres", "ReleaseDate", "Description", "Director", "Studio", "Series", "Poster", "Fanart", "NFO"], 900),
        new("MetaTube", ["Actors", "Genres", "ReleaseDate", "Description", "Director", "Studio", "Series", "Poster", "Fanart", "NFO"], 150),
        new("MDC-NG", ["Actors", "Genres", "ReleaseDate", "Description", "Director", "Studio", "Series", "Poster", "Fanart", "NFO"], 250),
    ];
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultPriorities =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["Actors"] = ["JavBus", "MetaTube", "MDC-NG"],
            ["Genres"] = ["JavBus", "MetaTube", "MDC-NG"],
            ["Poster"] = ["MetaTube", "JavBus", "MDC-NG"],
            ["Fanart"] = ["MetaTube", "MDC-NG", "JavBus"],
            ["NFO"] = ["MetaTube", "MDC-NG", "JavBus"],
            ["Description"] = ["JavBus", "MDC-NG", "MetaTube"],
            ["Series"] = ["MetaTube", "MDC-NG", "JavBus"],
            ["Director"] = ["JavBus", "MetaTube", "MDC-NG"],
            ["Studio"] = ["JavBus", "MetaTube", "MDC-NG"],
            ["ReleaseDate"] = ["JavBus", "MetaTube", "MDC-NG"],
        };

    private readonly string databasePath;
    private readonly MediaStoragePathResolver pathResolver;
    private readonly MetadataProviderSettingsService settingsService;
    private readonly IMetadataCompletionProviderClient providerClient;
    private readonly MetadataWriteService writer;
    private readonly ImageDownloadService images;
    private readonly NfoService nfo;
    private readonly MetadataHealthAnalysisService health;
    private readonly IMovieNumberExtractor movieNumberExtractor;
    private readonly TaskLogService logs;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly ConcurrentDictionary<long, CancellationTokenSource> cancellations = new();
    private readonly ConcurrentDictionary<string, ProviderThrottle> throttles = new(StringComparer.OrdinalIgnoreCase);

    public MetadataCompletionWorkflow(
        string databasePath,
        MediaStoragePathResolver pathResolver,
        MetadataProviderSettingsService settingsService,
        IMetadataCompletionProviderClient providerClient,
        MetadataWriteService writer,
        ImageDownloadService images,
        NfoService nfo,
        MetadataHealthAnalysisService health,
        IMovieNumberExtractor movieNumberExtractor,
        TaskLogService logs,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.databasePath = databasePath;
        this.pathResolver = pathResolver;
        this.settingsService = settingsService;
        this.providerClient = providerClient;
        this.writer = writer;
        this.images = images;
        this.nfo = nfo;
        this.health = health;
        this.movieNumberExtractor = movieNumberExtractor;
        this.logs = logs;
        this.delay = delay ?? Task.Delay;
    }

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
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) {
                Console.Error.WriteLine($"Metadata completion worker: {error}");
                await Task.Delay(750, stoppingToken);
            }
        }
    }

    public async Task<MetadataCompletionLaunchResult> StartDryRunAsync(
        MetadataCompletionScanCommand input,
        CancellationToken cancellationToken = default)
    {
        MetadataCompletionScanCommand normalized = Normalize(input);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        long existing = await ScalarLongAsync(connection, null,
            $"SELECT COALESCE(MAX(Id),0) FROM Tasks WHERE TaskType='{TaskType}' AND Status IN ('Pending','Running')",
            cancellationToken);
        if (existing > 0) throw new InvalidOperationException($"已有定向补全分析正在运行（任务 #{existing}）。");
        string at = Now();
        long id = await InsertIdAsync(connection, null, """
            INSERT INTO Tasks(TaskType,Status,Stage,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt)
            VALUES('MetadataCompletion','Pending','Analyze',0,0,0,$payload,$at,$at);
            SELECT last_insert_rowid();
            """, cancellationToken, ("$payload", JsonSerializer.Serialize(normalized, JsonOptions)), ("$at", at));
        await logs.WriteAsync(id, "Info", "Metadata Completion Dry Run 已排队；分析阶段不会调用任何 Provider。", cancellationToken);
        return new(id, "Pending", "定向补全分析已进入任务中心。");
    }

    public async Task<MetadataCompletionPreview> GetAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        TaskRow task = await ReadTaskAsync(connection, taskId, cancellationToken);
        CompletionPlanDocument? plan = DeserializePlan(task.ResultJson);
        if (plan is null) {
            MetadataCompletionScanCommand options = DeserializeOptions(task.PayloadJson);
            return new(taskId, task.Status, task.Stage, task.Progress, string.Empty, options, CapabilityCatalog,
                EmptyCounts(), new(0, 0, 0, null, 0), [], task.ErrorMessage is null ? [] : [task.ErrorMessage],
                task.CreatedAt, task.CompletedAt, false, task.Status == "Paused", false, true);
        }
        string token = ConfirmationToken(taskId, plan);
        bool canExecute = task.Status == "PreviewReady" && plan.Items.Any(item => item.Status == "Pending");
        bool canRollback = task.Status is "Completed" or "CompletedWithErrors" or "Failed"
            && !string.IsNullOrWhiteSpace(plan.RollbackToken)
            && await ScalarLongAsync(connection, null, """
                SELECT COUNT(*) FROM OperationAudit
                 WHERE OperationType='MetadataCompletion' AND RollbackToken=$token AND RevertedAt IS NULL
                """, cancellationToken, ("$token", plan.RollbackToken)) > 0;
        return new(taskId, task.Status, task.Stage, task.Progress, token, plan.Options, CapabilityCatalog,
            plan.Counts, plan.Projection, plan.Items, plan.Warnings, task.CreatedAt, task.CompletedAt,
            canExecute, task.Status == "Paused", canRollback, plan.DryRun);
    }

    public async Task<MetadataCompletionLaunchResult> ExecuteConfirmedAsync(
        long taskId,
        string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        MetadataCompletionPreview preview = await GetAsync(taskId, cancellationToken);
        VerifyToken(preview.ConfirmationToken, confirmationToken);
        if (!preview.CanExecute) throw new InvalidOperationException("定向补全计划当前没有可执行项目。");
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        int changed = await ExecuteSqlAsync(connection, null, """
            UPDATE Tasks SET Status='Pending',Stage='Execute',Progress=0,CompletedItems=0,
                ErrorMessage=NULL,CompletedAt=NULL,CancellationRequested=0,UpdatedAt=$at
             WHERE Id=$id AND TaskType='MetadataCompletion' AND Status='PreviewReady'
            """, cancellationToken, ("$at", Now()), ("$id", taskId));
        if (changed != 1) throw new InvalidOperationException("补全计划状态已变化，请重新生成 Dry Run。");
        await logs.WriteAsync(taskId, "Info", "用户已确认定向补全计划；执行将只请求计划中的 Provider 和缺失字段。", cancellationToken);
        return new(taskId, "Pending", "定向补全已进入执行队列。");
    }

    public async Task<MetadataCompletionLaunchResult> PauseAsync(long taskId)
    {
        if (cancellations.TryGetValue(taskId, out CancellationTokenSource? source)) source.Cancel();
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, CancellationToken.None);
        int changed = await ExecuteSqlAsync(connection, null, """
            UPDATE Tasks SET Status='Paused',Stage=CASE WHEN Stage='Analyze' THEN 'Analyze' ELSE 'Execute' END,
                CancellationRequested=1,UpdatedAt=$at
             WHERE Id=$id AND TaskType='MetadataCompletion' AND Status IN ('Pending','Running')
            """, CancellationToken.None, ("$at", Now()), ("$id", taskId));
        if (changed != 1) throw new InvalidOperationException("只有等待中或运行中的定向补全任务可以暂停。");
        await logs.WriteAsync(taskId, "Warning", "定向补全已暂停，已完成项目作为断点保留。", CancellationToken.None);
        return new(taskId, "Paused", "任务已暂停，可从断点继续。");
    }

    public async Task<MetadataCompletionLaunchResult> ResumeAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        int changed = await ExecuteSqlAsync(connection, null, """
            UPDATE Tasks SET Status='Pending',CancellationRequested=0,ErrorMessage=NULL,CompletedAt=NULL,UpdatedAt=$at
             WHERE Id=$id AND TaskType='MetadataCompletion' AND Status='Paused'
            """, cancellationToken, ("$at", Now()), ("$id", taskId));
        if (changed != 1) throw new InvalidOperationException("只有已暂停的定向补全任务可以继续。");
        await logs.WriteAsync(taskId, "Info", "定向补全从已保存断点继续。", cancellationToken);
        return new(taskId, "Pending", "任务已恢复执行。");
    }

    public async Task<MetadataCompletionLaunchResult> RollbackAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        TaskRow task = await ReadTaskAsync(connection, taskId, cancellationToken);
        CompletionPlanDocument plan = DeserializePlan(task.ResultJson)
            ?? throw new InvalidOperationException("补全会话没有可回滚的执行计划。");
        if (task.Status is not ("Completed" or "CompletedWithErrors" or "Failed") || string.IsNullOrWhiteSpace(plan.RollbackToken))
            throw new InvalidOperationException("只有已完成且尚未回滚的补全会话可以回滚。");
        List<AuditRow> audits = await ReadAuditsAsync(connection, plan.RollbackToken, cancellationToken);
        if (audits.Count == 0) throw new InvalidOperationException("本会话没有尚未回滚的数据库变更。");

        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (AuditRow audit in audits.OrderByDescending(value => value.Id)) {
            CompletionSnapshot before = DeserializeSnapshot(audit.BeforeJson);
            CompletionSnapshot after = DeserializeSnapshot(audit.AfterJson);
            await RestoreSnapshotAsync(connection, transaction, before, after, cancellationToken);
            await ExecuteSqlAsync(connection, transaction,
                "UPDATE OperationAudit SET RevertedAt=$at WHERE Id=$id AND RevertedAt IS NULL",
                cancellationToken, ("$at", Now()), ("$id", audit.Id));
        }
        await ExecuteSqlAsync(connection, transaction, """
            UPDATE MetadataSyncSnapshots SET RolledBackAt=$at
             WHERE TaskId=$task AND RolledBackAt IS NULL
            """, cancellationToken, ("$at", Now()), ("$task", taskId));
        await ExecuteSqlAsync(connection, transaction, """
            INSERT INTO OperationAudit(OperationType,EntityType,EntityId,BeforeJson,AfterJson,RollbackToken,CreatedAt)
            VALUES('MetadataCompletionRollback','MetadataCompletionSession',$id,$before,$after,$token,$at)
            """, cancellationToken, ("$id", taskId),
            ("$before", JsonSerializer.Serialize(new { Applied = audits.Count }, JsonOptions)),
            ("$after", JsonSerializer.Serialize(new { Reverted = audits.Count, PhysicalFilesRetained = true }, JsonOptions)),
            ("$token", plan.RollbackToken), ("$at", Now()));
        await ExecuteSqlAsync(connection, transaction,
            "UPDATE Tasks SET Stage='RolledBack',ResultSummary=$summary,UpdatedAt=$at WHERE Id=$id",
            cancellationToken, ("$summary", $"已回滚 {audits.Count} 部影片的数据库变更；下载文件按安全规则保留。"),
            ("$at", Now()), ("$id", taskId));
        await transaction.CommitAsync(cancellationToken);
        health.Invalidate();
        await logs.WriteAsync(taskId, "Warning", $"补全会话已回滚 {audits.Count} 项数据库变更；未删除磁盘文件。", cancellationToken);
        return new(taskId, "Completed", $"已回滚 {audits.Count} 项数据库变更。");
    }

    public async Task<MetadataCompletionExportResult> ExportAsync(long taskId, CancellationToken cancellationToken = default)
    {
        MetadataCompletionPreview preview = await GetAsync(taskId, cancellationToken);
        if (preview.Items.Count == 0) throw new InvalidOperationException("分析尚未完成，没有可导出的补全计划。");
        string dataRoot = Directory.GetParent(Path.GetDirectoryName(databasePath) ?? string.Empty)?.FullName
            ?? Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory;
        string reportDirectory = Path.Combine(dataRoot, "reports");
        Directory.CreateDirectory(reportDirectory);
        string stem = $"metadata-completion-{taskId}-{DateTime.Now:yyyyMMdd-HHmmss}";
        string markdownPath = Path.Combine(reportDirectory, stem + ".md");
        string csvPath = Path.Combine(reportDirectory, stem + ".csv");
        await File.WriteAllTextAsync(markdownPath, RenderMarkdown(preview), new UTF8Encoding(true), cancellationToken);
        await File.WriteAllTextAsync(csvPath, RenderCsv(preview), new UTF8Encoding(true), cancellationToken);
        return new(markdownPath, csvPath, preview.Items.Count, "Metadata Completion 报告已导出。");
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
            if (task.Stage == "Analyze") await RunAnalyzeAsync(taskId, task, token);
            else if (task.Stage == "Execute") await RunExecuteAsync(taskId, task, token);
            else throw new InvalidOperationException($"未知补全阶段：{task.Stage}");
        }
        catch (OperationCanceledException) { await MarkPausedAsync(taskId); }
        catch (Exception error) { await MarkFailedAsync(taskId, error); }
    }

    private async Task RunAnalyzeAsync(long taskId, TaskRow task, CancellationToken token)
    {
        MetadataCompletionScanCommand options = DeserializeOptions(task.PayloadJson);
        await UpdateTaskAsync(taskId, "Running", "Analyzing", 5, 0, 0, "读取 Standard 影片元数据状态", token);
        MediaStorageSettingsDto storage = await pathResolver.GetSettingsAsync(token);
        MetadataHealthSummary baseline = await MetadataHealthReader.ReadAsync(
            databasePath, storage, cancellationToken: token, includeStorageInventory: false);
        MetadataProviderContext providerSettings = await ReadProviderContextAsync();
        CompletionPlanDocument plan = await BuildPlanAsync(options, baseline, providerSettings, token);
        string summary = $"Dry Run：扫描 {plan.Counts.ScannedStandardMovies} 部 Standard；预计联网 {plan.Counts.PlannedNetworkMovies} 部；" +
            $"当前完整 {plan.Projection.CompleteBefore}，乐观预计 {plan.Projection.CompleteProjected}。";
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteSqlAsync(connection, null, """
            UPDATE Tasks SET Status='PreviewReady',Stage='PreviewReady',Progress=100,
                TotalItems=$total,CompletedItems=$completed,ResultJson=$result,ResultSummary=$summary,
                CompletedAt=$at,UpdatedAt=$at,CurrentMovieId=NULL
             WHERE Id=$id AND TaskType='MetadataCompletion'
            """, token, ("$total", plan.Items.Count), ("$completed", plan.Items.Count),
            ("$result", JsonSerializer.Serialize(plan, JsonOptions)), ("$summary", summary),
            ("$at", Now()), ("$id", taskId));
        await logs.WriteAsync(taskId, "Info", summary + " Dry Run 未调用 Provider，也未写入影片元数据。", token);
    }

    private async Task RunExecuteAsync(long taskId, TaskRow task, CancellationToken token)
    {
        CompletionPlanDocument initial = DeserializePlan(task.ResultJson)
            ?? throw new InvalidOperationException("补全计划丢失，请重新 Dry Run。");
        MetadataProviderContext context = await ReadProviderContextAsync();
        MetadataCompletionItem[] items = initial.Items.ToArray();
        Dictionary<string, int> indexes = items.Select((item, index) => (item.ItemId, index))
            .ToDictionary(value => value.ItemId, value => value.index, StringComparer.Ordinal);
        object planGate = new();
        var persistenceGate = new SemaphoreSlim(1, 1);
        var writeGate = new SemaphoreSlim(1, 1);
        string rollbackToken = initial.RollbackToken ?? $"metadata-completion:{taskId}:{Guid.NewGuid():N}";
        int processed = items.Count(item => IsTerminal(item.Status));
        MetadataCompletionItem[] pending = items.Where(item => item.Status is "Pending" or "Running").ToArray();
        await UpdateTaskAsync(taskId, "Running", "Executing", Percent(processed, items.Length), items.Length, processed,
            $"按字段执行定向补全（并发 {initial.Options.Concurrency}）", token);

        async Task PersistAsync(MetadataCompletionItem next)
        {
            lock (planGate) items[indexes[next.ItemId]] = next;
            int completed = items.Count(item => IsTerminal(item.Status));
            CompletionPlanDocument checkpoint = initial with {
                Items = items.ToArray(),
                Counts = CountExecution(initial.Counts, items),
                RollbackToken = rollbackToken,
                DryRun = false,
            };
            await persistenceGate.WaitAsync(token);
            try {
                await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
                await ExecuteSqlAsync(connection, null, """
                    UPDATE Tasks SET ResultJson=$result,CompletedItems=$completed,Progress=$progress,
                        CurrentMovieId=$movie,UpdatedAt=$at WHERE Id=$id
                    """, token, ("$result", JsonSerializer.Serialize(checkpoint, JsonOptions)),
                    ("$completed", completed), ("$progress", Percent(completed, items.Length)),
                    ("$movie", next.MovieId), ("$at", Now()), ("$id", taskId));
            }
            finally { persistenceGate.Release(); }
        }

        await Parallel.ForEachAsync(pending,
            new ParallelOptions { MaxDegreeOfParallelism = initial.Options.Concurrency, CancellationToken = token },
            async (item, itemToken) => {
                MetadataCompletionItem running = item with { Status = "Running", Reason = "正在按 Provider 能力补全缺失字段。" };
                await PersistAsync(running);
                MetadataCompletionItem result = await ProcessItemAsync(taskId, running, initial.Options, context,
                    rollbackToken, writeGate, itemToken);
                await PersistAsync(result);
            });

        health.Invalidate();
        MetadataHealthSummary after = await health.GetAsync(token);
        CompletionPlanDocument completedPlan = initial with {
            Items = items,
            Counts = CountExecution(initial.Counts, items),
            Projection = initial.Projection with { CompleteAfter = after.CompleteMovies },
            RollbackToken = rollbackToken,
            DryRun = false,
        };
        bool errors = completedPlan.Counts.Failed > 0 || completedPlan.Counts.Partial > 0;
        string status = errors ? "CompletedWithErrors" : "Completed";
        string summary = $"定向补全结束：完成 {completedPlan.Counts.Completed}，部分 {completedPlan.Counts.Partial}，" +
            $"无结果 {completedPlan.Counts.NoResult}，失败 {completedPlan.Counts.Failed}；完整影片 " +
            $"{completedPlan.Projection.CompleteBefore} -> {after.CompleteMovies}。";
        await using SqliteConnection finalConnection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteSqlAsync(finalConnection, null, """
            UPDATE Tasks SET Status=$status,Stage=$status,Progress=100,CompletedItems=$completed,
                ResultJson=$result,ResultSummary=$summary,ErrorMessage=$error,CompletedAt=$at,UpdatedAt=$at,CurrentMovieId=NULL
             WHERE Id=$id
            """, token, ("$status", status), ("$completed", items.Length),
            ("$result", JsonSerializer.Serialize(completedPlan, JsonOptions)), ("$summary", summary),
            ("$error", errors ? "部分影片未补齐，详情见会话项目。" : null), ("$at", Now()), ("$id", taskId));
        await logs.WriteAsync(taskId, errors ? "Warning" : "Info", summary, token);
    }

    private async Task<MetadataCompletionItem> ProcessItemAsync(
        long taskId,
        MetadataCompletionItem item,
        MetadataCompletionScanCommand options,
        MetadataProviderContext baseContext,
        string rollbackToken,
        SemaphoreSlim writeGate,
        CancellationToken token)
    {
        Stopwatch timer = Stopwatch.StartNew();
        var attempts = 0;
        var contributions = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        try {
            CompletionMovieState current = await ReadMovieStateAsync(item.MovieId, token);
            if (!File.Exists(current.VideoPath))
                return Finish(item, "Skipped", "媒体文件不存在或当前存储不可访问。", "MissingMedia", attempts, timer, [], contributions);
            MovieNumberExtractionResult extraction = movieNumberExtractor.Extract(current.FileName);
            if (string.IsNullOrWhiteSpace(extraction.NormalizedNumber)
                || extraction.Confidence < movieNumberExtractor.MinimumAutoSyncConfidence)
                return Finish(item, "Skipped", "番号识别置信度不足，未发起 Provider 请求。", "CodeInvalid", attempts, timer, [], contributions);
            if (!extraction.NormalizedNumber.Equals(item.Number, StringComparison.OrdinalIgnoreCase))
                return Finish(item, "Conflict", "番号在 Dry Run 后发生变化，禁止执行。", "CodeConflict", attempts, timer, [], contributions);

            HashSet<string> remaining = MissingFields(current, options).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string protectedField in ProtectedFields(current)) remaining.Remove(protectedField);
            if (remaining.Count == 0)
                return Finish(item, "Skipped", "缺失字段已被其他任务补齐，或当前字段受用户保护。", "ExistingProtected", attempts, timer, [], contributions);

            CompletionSnapshot before = await CaptureSnapshotAsync(item.MovieId, token);
            ProviderMetadata? aggregate = null;
            ProviderMetadata? nfoSource = null;
            var savedImages = new List<SavedImage>();
            string? nfoPath = null;
            var createdPaths = new List<string>();
            string? lastProvider = null;

            foreach (string provider in item.ProviderPlan) {
                token.ThrowIfCancellationRequested();
                string[] providerFields = remaining.Where(field => Supports(provider, field)).ToArray();
                if (providerFields.Length == 0) continue;
                ProviderFetchResult fetched = await FetchWithRetryAsync(taskId, provider, item.Number,
                    current.VideoPath, baseContext, token);
                attempts += fetched.Attempts;
                if (fetched.Metadata is null) {
                    errors.Add($"{provider}:{fetched.FailureCategory ?? "NoResult"}");
                    contributions[provider] = [];
                    continue;
                }
                ProviderMetadata metadata = fetched.Metadata;
                nfoSource ??= metadata;
                var provided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string field in providerFields.Where(field => field is not ("Poster" or "Fanart" or "NFO"))) {
                    if (HasValue(metadata, field)) provided.Add(field);
                }

                string[] imageTargets = providerFields.Where(field => field is "Poster" or "Fanart" && HasValue(metadata, field)).ToArray();
                if (imageTargets.Length > 0 && baseContext.DownloadImages(provider)) {
                    try {
                        IReadOnlyList<MetadataImage> selected = metadata.Images
                            .Where(image => imageTargets.Contains(MediaStoragePathResolver.NormalizeResourceType(image.Type), StringComparer.OrdinalIgnoreCase))
                            .ToArray();
                        IReadOnlyList<SavedImage> downloaded = await images.DownloadAsync(pathResolver,
                            new MediaStorageMovie(current.MovieId, current.Code, current.Title), selected,
                            baseContext.TimeoutSeconds(provider), false, token, JavBusImageOptions(provider, baseContext.JavBus));
                        savedImages.AddRange(downloaded);
                        createdPaths.AddRange(downloaded.Where(value => value.Created).Select(value => value.Path));
                        foreach (string field in imageTargets.Where(field => downloaded.Any(image => image.Type.Equals(field, StringComparison.OrdinalIgnoreCase))))
                            provided.Add(field);
                    }
                    catch (Exception error) when (error is not OperationCanceledException) {
                        errors.Add($"{provider}:Image:{Classify(error)}");
                    }
                }
                if (providerFields.Contains("NFO", StringComparer.OrdinalIgnoreCase)) provided.Add("NFO");

                ProviderMetadata restricted = Restrict(metadata, providerFields);
                aggregate = aggregate is null ? restricted : Merge(aggregate, restricted);
                remaining.ExceptWith(provided);
                contributions[provider] = provided.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                lastProvider = provider;
                if (remaining.Count == 0) break;
            }

            if (item.MissingFields.Contains("NFO", StringComparer.OrdinalIgnoreCase)
                && current.NfoMissing && !current.NfoProtected && nfoSource is not null) {
                try {
                    (nfoPath, bool created) = await nfo.WriteAsync(
                        new SyncMovie(current.MovieId, current.Code, current.Title, current.Description,
                            current.ReleaseDate, current.DurationSeconds, current.VideoPath, current.NfoPath),
                        nfoSource, token);
                    if (created && nfoPath is not null) createdPaths.Add(nfoPath);
                    if (nfoPath is not null && File.Exists(nfoPath)) remaining.Remove("NFO");
                }
                catch (Exception error) when (error is not OperationCanceledException) {
                    errors.Add($"{lastProvider ?? "Provider"}:NFO:{Classify(error)}");
                }
            }

            if (aggregate is null && savedImages.Count == 0 && nfoPath is null)
                return Finish(item, "NoResult", "所有计划 Provider 均未返回可写入的缺失字段。",
                    errors.FirstOrDefault()?.Split(':').LastOrDefault() ?? "NoData", attempts, timer, [], contributions);

            aggregate ??= EmptyMetadata(lastProvider ?? "MetadataCompletion", item.Number);
            await writeGate.WaitAsync(token);
            CompletionSnapshot after;
            try {
                SyncMovie movie = new(current.MovieId, current.Code, current.Title, current.Description,
                    current.ReleaseDate, current.DurationSeconds, current.VideoPath, current.NfoPath);
                await writer.ApplyAsync(taskId, movie, aggregate, new(savedImages, nfoPath, createdPaths), false, token);
                after = await CaptureSnapshotAsync(item.MovieId, token);
                try { await WriteAuditAsync(taskId, rollbackToken, before, after, token); }
                catch {
                    await CompensateMissingAuditAsync(taskId, before, after, token);
                    throw;
                }
            }
            finally { writeGate.Release(); }

            IReadOnlyList<string> added = AddedFields(before, after);
            CompletionMovieState finalState = await ReadMovieStateAsync(item.MovieId, token);
            HashSet<string> stillMissing = MissingFields(finalState, options).ToHashSet(StringComparer.OrdinalIgnoreCase);
            stillMissing.IntersectWith(item.MissingFields);
            string status = stillMissing.Count == 0 ? "Completed" : added.Count > 0 ? "Partial" : "Skipped";
            string? category = stillMissing.Count == 0 ? null : added.Count > 0 ? "ProviderEmpty" : "MergeSkipped";
            string reason = stillMissing.Count == 0
                ? $"已仅补入空字段：{string.Join("、", added)}。"
                : $"仍缺少：{string.Join("、", stillMissing.Order())}；已写入：{string.Join("、", added)}。";
            if (errors.Count > 0) reason += $" Provider 记录：{string.Join(" | ", errors)}。";
            return Finish(item, status, reason, category, attempts, timer, added, contributions);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) {
            return Finish(item, "Failed", error.Message, Classify(error), attempts, timer, [], contributions);
        }
    }

    private async Task<ProviderFetchResult> FetchWithRetryAsync(
        long taskId,
        string provider,
        string code,
        string moviePath,
        MetadataProviderContext baseContext,
        CancellationToken token)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= MaximumAttempts; attempt++) {
            try {
                ProviderMetadata? metadata = await WithThrottleAsync(provider,
                    ct => providerClient.GetMetadataAsync(provider, code, moviePath,
                        baseContext with {
                            ProviderLog = (source, message, logToken) => logs.WriteAsync(taskId, "Info", $"[{source}] {message}", logToken),
                            ProviderDebugLog = (source, message, logToken) => logs.WriteAsync(taskId, "Debug", $"[{source}] {message}", logToken),
                        }, ct), token);
                return new(metadata, attempt, metadata is null ? "NoData" : null);
            }
            catch (Exception error) when (error is not OperationCanceledException) {
                last = error;
                string category = Classify(error);
                await logs.WriteAsync(taskId, "Warning", $"[{provider}] attempt {attempt}/{MaximumAttempts}: {category}: {error.Message}", token);
                if (attempt < MaximumAttempts)
                    await delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1)), token);
            }
        }
        return new(null, MaximumAttempts, last is null ? "NoData" : Classify(last));
    }

    private async Task<T> WithThrottleAsync<T>(string provider, Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        MetadataCompletionProviderCapability capability = CapabilityCatalog.Single(value => value.Provider == provider);
        ProviderThrottle throttle = throttles.GetOrAdd(provider, _ => new());
        await throttle.Gate.WaitAsync(token);
        try {
            TimeSpan remaining = throttle.LastCompletedUtc.AddMilliseconds(capability.MinimumDelayMilliseconds) - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero) await delay(remaining, token);
            return await action(token);
        }
        finally {
            throttle.LastCompletedUtc = DateTimeOffset.UtcNow;
            throttle.Gate.Release();
        }
    }

    private async Task<CompletionPlanDocument> BuildPlanAsync(
        MetadataCompletionScanCommand options,
        MetadataHealthSummary baseline,
        MetadataProviderContext settings,
        CancellationToken token)
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> priorities = EffectivePriorities(options);
        HashSet<string> enabledProviders = EnabledProviders(settings);
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token, registerFileFunctions: true);
        bool hasDirectors = await TableExistsAsync(connection, "MovieDirectors", token)
            && await TableExistsAsync(connection, "Directors", token);
        List<CompletionMovieState> movies = await ReadMovieStatesAsync(connection, hasDirectors, token);
        var items = new List<MetadataCompletionItem>();
        Dictionary<string, long> missing = KnownFields.ToDictionary(field => field, _ => 0L, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, long> requests = CapabilityCatalog.ToDictionary(value => value.Provider, _ => 0L, StringComparer.OrdinalIgnoreCase);
        long incomplete = 0, eligible = 0, missingMedia = 0, lowConfidence = 0, multiple = 0, conflicts = 0, locked = 0;
        long projectedGain = 0;

        foreach (CompletionMovieState movie in movies) {
            token.ThrowIfCancellationRequested();
            if (!movie.Complete) incomplete++;
            IReadOnlyList<string> selectedMissing = MissingFields(movie, options);
            foreach (string field in selectedMissing) missing[field]++;
            if (movie.Complete || selectedMissing.Count == 0) continue;
            string number = MovieCodeNormalizer.Normalize(movie.Code) ?? movie.Code.Trim().ToUpperInvariant();
            IReadOnlyList<string> requiredMissing = RequiredMissingFields(movie);
            IReadOnlyList<string> protectedFields = ProtectedFields(movie).Where(selectedMissing.Contains).ToArray();

            string status = "Pending";
            string reason = "只请求当前缺失字段；执行前会再次校验。";
            string? failure = null;
            IReadOnlyList<string> providerPlan = [];
            if (!File.Exists(movie.VideoPath)) {
                missingMedia++; status = "Skipped"; failure = "MissingMedia"; reason = "媒体文件不存在或存储当前不可访问。";
            }
            else {
                MovieNumberExtractionResult extraction = movieNumberExtractor.Extract(movie.FileName);
                bool hasMultiple = extraction.Warnings.Any(value => value.StartsWith("MultipleCandidates", StringComparison.OrdinalIgnoreCase));
                if (hasMultiple) {
                    multiple++; status = "Conflict"; failure = "MultipleNumbers"; reason = "文件名存在多个番号候选，禁止自动补全。";
                }
                else if (string.IsNullOrWhiteSpace(extraction.NormalizedNumber)
                    || extraction.Confidence < movieNumberExtractor.MinimumAutoSyncConfidence) {
                    lowConfidence++; status = "Skipped"; failure = "CodeInvalid"; reason = "番号识别置信度低于自动联网阈值。";
                }
                else if (!extraction.NormalizedNumber.Equals(number, StringComparison.OrdinalIgnoreCase)) {
                    conflicts++; status = "Conflict"; failure = "CodeConflict"; reason = $"数据库番号 {number} 与文件重识别 {extraction.NormalizedNumber} 不一致。";
                }
                else {
                    string[] actionable = selectedMissing.Except(protectedFields, StringComparer.OrdinalIgnoreCase).ToArray();
                    if (protectedFields.Count > 0) locked++;
                    providerPlan = BuildProviderPlan(actionable, priorities, enabledProviders);
                    if (actionable.Length == 0) {
                        status = "Skipped"; failure = "Locked"; reason = "所有缺失字段均受用户上传或锁定状态保护。";
                    }
                    else if (providerPlan.Count == 0) {
                        status = "Skipped"; failure = "ProviderDisabled"; reason = "没有已启用且具备相应能力的 Provider。";
                    }
                    else {
                        eligible++;
                        foreach (string provider in BuildPrimaryProviderPlan(actionable, priorities, enabledProviders)) requests[provider]++;
                        bool canComplete = requiredMissing.Count > 0
                            && requiredMissing.All(field => actionable.Contains(field, StringComparer.OrdinalIgnoreCase))
                            && requiredMissing.All(field => providerPlan.Any(provider => Supports(provider, field)));
                        if (canComplete) projectedGain++;
                    }
                }
            }
            items.Add(new(Guid.NewGuid().ToString("N"), movie.MovieId, number, movie.VideoPath,
                selectedMissing, requiredMissing, protectedFields, providerPlan, status, reason, failure, 0, 0, [],
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)));
        }

        long estimatedSeconds = (long)Math.Ceiling(requests.Values.Sum() * 2.5 / Math.Max(1, options.Concurrency));
        MetadataCompletionCounts counts = new(movies.Count, incomplete, eligible, eligible, missingMedia, lowConfidence,
            multiple, conflicts, locked, missing, requests, 0, 0,
            items.LongCount(item => item.Status == "Skipped"), 0, 0, items.LongCount(item => item.Status == "Conflict"));
        MetadataCompletionProjection projection = new(baseline.Scope.StandardMovies, baseline.CompleteMovies,
            Math.Min(baseline.Scope.StandardMovies, baseline.CompleteMovies + projectedGain), null, estimatedSeconds);
        var warnings = new List<string> {
            "Dry Run 不调用 Provider，不下载图片，不写 NFO，也不修改影片元数据。",
            "Provider 远端接口不能按单个 JSON 字段裁剪；系统只调用有能力贡献缺失字段的 Provider，并在 Merge 前丢弃非目标字段。",
            "完整影片提升为乐观估计，只有 Provider 实际返回所需字段且文件落盘成功后才会计入最终统计。",
            "回滚只恢复数据库状态；为遵守文件安全规则，不自动删除执行期间下载或生成的实体文件。",
        };
        if (baseline.Coverage.ResourceInventoryComplete is false)
            warnings.Add("当前健康统计未完成完整 MediaStorage 盘点，图片/NFO 预计值将在执行后重新统计。");
        return new(options, counts, projection, items, warnings, true, null);
    }

    private static IReadOnlyList<string> MissingFields(CompletionMovieState movie, MetadataCompletionScanCommand options)
    {
        var result = new List<string>();
        void Add(bool selected, bool missing, string field) { if (selected && missing) result.Add(field); }
        Add(options.Actors, !movie.Actors, "Actors");
        Add(options.Genres, !movie.Genres, "Genres");
        Add(options.Poster, !movie.Poster, "Poster");
        Add(options.Fanart, !movie.Fanart, "Fanart");
        Add(options.Nfo, movie.NfoMissing, "NFO");
        Add(options.Description, !movie.HasDescription, "Description");
        Add(options.Series, !movie.Series, "Series");
        Add(options.Director, !movie.Director, "Director");
        Add(options.Studio, !movie.Studio, "Studio");
        Add(options.ReleaseDate, !movie.HasReleaseDate, "ReleaseDate");
        return result;
    }

    private static IReadOnlyList<string> RequiredMissingFields(CompletionMovieState movie)
    {
        var result = new List<string>();
        if (!movie.HasNumber) result.Add("Code");
        if (!movie.HasTitle) result.Add("Title");
        if (!movie.HasReleaseDate) result.Add("ReleaseDate");
        if (!movie.Studio) result.Add("Studio");
        if (!movie.Actors) result.Add("Actors");
        if (!movie.Genres) result.Add("Genres");
        if (!movie.Poster) result.Add("Poster");
        if (!movie.Fanart) result.Add("Fanart");
        if (movie.NfoMissing) result.Add("NFO");
        return result;
    }

    private static IReadOnlyList<string> ProtectedFields(CompletionMovieState movie)
    {
        var result = new List<string>();
        if (movie.PosterProtected) result.Add("Poster");
        if (movie.FanartProtected) result.Add("Fanart");
        if (movie.NfoProtected) result.Add("NFO");
        return result;
    }

    private static IReadOnlyList<string> BuildProviderPlan(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, IReadOnlyList<string>> priorities,
        HashSet<string> enabled)
    {
        var result = new List<string>();
        int max = fields.Select(field => priorities.GetValueOrDefault(field)?.Count ?? 0).DefaultIfEmpty().Max();
        for (int rank = 0; rank < max; rank++) {
            foreach (string field in fields) {
                IReadOnlyList<string>? ordered = priorities.GetValueOrDefault(field);
                if (ordered is null || rank >= ordered.Count) continue;
                string provider = ordered[rank];
                if (enabled.Contains(provider) && Supports(provider, field)
                    && !result.Contains(provider, StringComparer.OrdinalIgnoreCase)) result.Add(provider);
            }
        }
        return result;
    }

    private static IReadOnlyList<string> BuildPrimaryProviderPlan(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, IReadOnlyList<string>> priorities,
        HashSet<string> enabled)
    {
        var result = new List<string>();
        foreach (string field in fields) {
            string? provider = priorities.GetValueOrDefault(field)?.FirstOrDefault(value => enabled.Contains(value) && Supports(value, field));
            if (provider is not null && !result.Contains(provider, StringComparer.OrdinalIgnoreCase)) result.Add(provider);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> EffectivePriorities(MetadataCompletionScanCommand options)
    {
        var result = DefaultPriorities.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (options.ProviderPriorities is null) return result;
        foreach ((string field, IReadOnlyList<string> providers) in options.ProviderPriorities) {
            if (!KnownFields.Contains(field, StringComparer.OrdinalIgnoreCase)) continue;
            string[] clean = providers.Where(provider => CapabilityCatalog.Any(value => value.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (clean.Length > 0) result[field] = clean;
        }
        return result;
    }

    private static HashSet<string> EnabledProviders(MetadataProviderContext settings)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (settings.JavBus.Enabled) result.Add("JavBus");
        if (settings.MetaTube.Enabled) result.Add("MetaTube");
        if (settings.MdcNg.Enabled) result.Add("MDC-NG");
        return result;
    }

    private static bool Supports(string provider, string field) => CapabilityCatalog.Any(value =>
        value.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)
        && value.Fields.Contains(field, StringComparer.OrdinalIgnoreCase));

    private static bool HasValue(ProviderMetadata metadata, string field) => field switch {
        "Actors" => metadata.Actors.Count > 0,
        "Genres" => metadata.Genres.Count > 0,
        "Poster" => metadata.Images.Any(image => MediaStoragePathResolver.NormalizeResourceType(image.Type) == "Poster"),
        "Fanart" => metadata.Images.Any(image => MediaStoragePathResolver.NormalizeResourceType(image.Type) == "Fanart"),
        "NFO" => true,
        "Description" => !string.IsNullOrWhiteSpace(metadata.Description),
        "Series" => !string.IsNullOrWhiteSpace(metadata.Series),
        "Director" => !string.IsNullOrWhiteSpace(metadata.Director),
        "Studio" => !string.IsNullOrWhiteSpace(metadata.Studio) || !string.IsNullOrWhiteSpace(metadata.Publisher),
        "ReleaseDate" => !string.IsNullOrWhiteSpace(metadata.ReleaseDate),
        _ => false,
    };

    private static ProviderMetadata Restrict(ProviderMetadata value, IReadOnlyList<string> fields)
    {
        var targets = fields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return value with {
            Title = null,
            OriginalTitle = null,
            Description = targets.Contains("Description") ? value.Description : null,
            Director = targets.Contains("Director") ? value.Director : null,
            Studio = targets.Contains("Studio") ? value.Studio : null,
            Publisher = targets.Contains("Studio") ? value.Publisher : null,
            Series = targets.Contains("Series") ? value.Series : null,
            DurationSeconds = null,
            ReleaseDate = targets.Contains("ReleaseDate") ? value.ReleaseDate : null,
            WebUrl = null,
            Genres = targets.Contains("Genres") ? value.Genres : [],
            Actors = targets.Contains("Actors") ? value.Actors : [],
            Images = value.Images.Where(image => targets.Contains(MediaStoragePathResolver.NormalizeResourceType(image.Type))).ToArray(),
            Rating = null,
            Country = null,
            ActorImages = [],
        };
    }

    private static ProviderMetadata Merge(ProviderMetadata primary, ProviderMetadata fallback) => primary with {
        Description = First(primary.Description, fallback.Description),
        Director = First(primary.Director, fallback.Director),
        Studio = First(primary.Studio, fallback.Studio),
        Publisher = First(primary.Publisher, fallback.Publisher),
        Series = First(primary.Series, fallback.Series),
        ReleaseDate = First(primary.ReleaseDate, fallback.ReleaseDate),
        Genres = MergeValues(primary.Genres, fallback.Genres),
        Actors = MergeValues(primary.Actors, fallback.Actors),
        Images = primary.Images.Concat(fallback.Images)
            .GroupBy(image => $"{MediaStoragePathResolver.NormalizeResourceType(image.Type)}|{image.Url}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray(),
    };

    private static ProviderMetadata EmptyMetadata(string provider, string code) =>
        new(provider, code, code, null, null, null, null, null, null, null, null, null, [], [], []);

    private static string? First(string? primary, string? fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static IReadOnlyList<string> MergeValues(IReadOnlyList<string> primary, IReadOnlyList<string> fallback) =>
        primary.Concat(fallback).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static ImageDownloadOptions? JavBusImageOptions(string provider, JavBusSettingsDto settings)
    {
        if (!provider.Equals("JavBus", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(settings.Cookie)
            || !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out Uri? uri)) return null;
        return new(settings.Cookie, uri.GetLeftPart(UriPartial.Authority) + "/", uri.Host);
    }

    private async Task<MetadataProviderContext> ReadProviderContextAsync() => new(
        await settingsService.ReadMetaTubeAsync(),
        await settingsService.ReadJavBusAsync(),
        null,
        await settingsService.ReadDmmAsync(),
        await settingsService.ReadJavDbAsync(),
        await settingsService.ReadNetworkAsync()) {
        MdcNg = await settingsService.ReadMdcNgAsync(),
    };

    private async Task<List<CompletionMovieState>> ReadMovieStatesAsync(
        SqliteConnection connection,
        bool hasDirectors,
        CancellationToken token,
        long? movieId = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        string director = hasDirectors ? MetadataHealthDefinition.Directors("m", true) : "0=1";
        command.CommandText = $"""
            SELECT m.Id,COALESCE(m.Code,''),m.Title,m.Description,m.ReleaseDate,COALESCE(m.DurationSeconds,0),m.NfoPath,
                   mf.FilePath,mf.FileName,
                   CASE WHEN {MetadataHealthDefinition.Number("m")} THEN 1 ELSE 0 END,
                   CASE WHEN {MetadataHealthDefinition.Title("m")} THEN 1 ELSE 0 END,
                   CASE WHEN {MetadataHealthDefinition.ReleaseDate("m")} THEN 1 ELSE 0 END,
                   CASE WHEN {MetadataHealthDefinition.Studios("m")} THEN 1 ELSE 0 END,
                   CASE WHEN {MetadataHealthDefinition.Actors("m")} THEN 1 ELSE 0 END,
                   CASE WHEN {MetadataHealthDefinition.ProviderTags("m")} THEN 1 ELSE 0 END,
                   CASE WHEN {MetadataHealthDefinition.ImagePhysical("m", "Poster")} THEN 1 ELSE 0 END,
                   CASE WHEN {MetadataHealthDefinition.ImagePhysical("m", "Fanart")} THEN 1 ELSE 0 END,
                   CASE WHEN {MetadataHealthDefinition.NfoPhysical("m", true)} THEN 1 ELSE 0 END,
                   CASE WHEN {MetadataHealthDefinition.Description("m")} THEN 1 ELSE 0 END,
                   CASE WHEN {MetadataHealthDefinition.Series("m")} THEN 1 ELSE 0 END,
                   CASE WHEN {director} THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND i.ImageType='Poster' AND (i.IsLocked=1 OR i.Ownership='User')) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM Images i WHERE i.MovieId=m.Id AND i.ImageType IN ('Fanart','BigPic') AND (i.IsLocked=1 OR i.Ownership='User')) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS(SELECT 1 FROM NfoDocuments n WHERE n.MovieId=m.Id AND (n.IsLocked=1 OR n.Ownership='User')) THEN 1 ELSE 0 END
              FROM Movies m
              JOIN MediaFiles mf ON mf.Id=(SELECT x.Id FROM MediaFiles x
                                            WHERE x.MovieId=m.Id AND x.IsPrimary=1 AND x.MediaType='Video'
                                            ORDER BY x.Id LIMIT 1)
             WHERE {MetadataHealthDefinition.StandardMovie("m")}
               AND ($movie IS NULL OR m.Id=$movie)
             ORDER BY m.Id
            """;
        command.Parameters.AddWithValue("$movie", (object?)movieId ?? DBNull.Value);
        var result = new List<CompletionMovieState>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(ReadState(reader));
        return result;
    }

    private async Task<CompletionMovieState> ReadMovieStateAsync(long movieId, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token, registerFileFunctions: true);
        bool hasDirectors = await TableExistsAsync(connection, "MovieDirectors", token)
            && await TableExistsAsync(connection, "Directors", token);
        List<CompletionMovieState> rows = await ReadMovieStatesAsync(connection, hasDirectors, token, movieId);
        return rows.SingleOrDefault(value => value.MovieId == movieId)
            ?? throw new KeyNotFoundException($"Standard 影片 {movieId} 不存在或已退出补全范围。");
    }

    private static CompletionMovieState ReadState(SqliteDataReader reader)
    {
        bool B(int index) => reader.GetInt64(index) == 1;
        return new(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(8),
            B(9), B(10), B(11), B(12), B(13), B(14), B(15), B(16), !B(17), B(18), B(19), B(20), B(21), B(22), B(23));
    }

    private async Task<CompletionSnapshot> CaptureSnapshotAsync(long movieId, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, token);
        await using SqliteCommand movie = connection.CreateCommand();
        movie.CommandText = """
            SELECT Id,Title,OriginalTitle,SortTitle,Description,ReleaseDate,DurationSeconds,ProviderRating,NfoPath,
                   IsScraped,ScrapeStatus,UpdatedAt FROM Movies WHERE Id=$id
            """;
        movie.Parameters.AddWithValue("$id", movieId);
        await using SqliteDataReader reader = await movie.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new KeyNotFoundException($"影片 {movieId} 不存在。");
        var scalar = new MovieScalarSnapshot(reader.GetInt64(0), Text(reader, 1), Text(reader, 2), Text(reader, 3),
            Text(reader, 4), Text(reader, 5), reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            Text(reader, 8), reader.GetInt64(9), reader.GetString(10), reader.GetString(11));
        await reader.DisposeAsync();
        long[] actors = await ReadIdsAsync(connection, "SELECT ActorId FROM MovieActors WHERE MovieId=$id", movieId, token);
        long[] genres = await ReadIdsAsync(connection, "SELECT GenreId FROM MovieGenres WHERE MovieId=$id", movieId, token);
        long[] directors = await TableExistsAsync(connection, "MovieDirectors", token)
            ? await ReadIdsAsync(connection, "SELECT DirectorId FROM MovieDirectors WHERE MovieId=$id", movieId, token) : [];
        long[] series = await ReadIdsAsync(connection, "SELECT SeriesId FROM MovieSeries WHERE MovieId=$id", movieId, token);
        string[] studios = await ReadStringsAsync(connection,
            "SELECT StudioId||'|'||RelationType FROM MovieStudios WHERE MovieId=$id", movieId, token);
        ImageSnapshotRef[] imageRefs = await ReadImageRefsAsync(connection, movieId, token);
        long[] nfoIds = await TableExistsAsync(connection, "NfoDocuments", token)
            ? await ReadIdsAsync(connection, "SELECT Id FROM NfoDocuments WHERE MovieId=$id", movieId, token) : [];
        return new(scalar, actors, genres, directors, studios, series, imageRefs, nfoIds);
    }

    private async Task WriteAuditAsync(long taskId, string rollbackToken, CompletionSnapshot before,
        CompletionSnapshot after, CancellationToken token)
    {
        if (JsonSerializer.Serialize(before, JsonOptions) == JsonSerializer.Serialize(after, JsonOptions)) return;
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteSqlAsync(connection, null, """
            INSERT INTO OperationAudit(OperationType,EntityType,EntityId,BeforeJson,AfterJson,RollbackToken,CreatedAt)
            VALUES('MetadataCompletion','Movie',$movie,$before,$after,$token,$at)
            """, token, ("$movie", before.Movie.Id), ("$before", JsonSerializer.Serialize(before, JsonOptions)),
            ("$after", JsonSerializer.Serialize(after, JsonOptions)), ("$token", rollbackToken), ("$at", Now()));
    }

    private async Task CompensateMissingAuditAsync(long taskId, CompletionSnapshot before,
        CompletionSnapshot after, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await RestoreSnapshotAsync(connection, transaction, before, after, token);
        await ExecuteSqlAsync(connection, transaction, """
            UPDATE MetadataSyncSnapshots SET RolledBackAt=$at
             WHERE Id=(SELECT MAX(Id) FROM MetadataSyncSnapshots WHERE TaskId=$task AND MovieId=$movie)
            """, token, ("$at", Now()), ("$task", taskId), ("$movie", before.Movie.Id));
        await transaction.CommitAsync(token);
    }

    private static IReadOnlyList<string> AddedFields(CompletionSnapshot before, CompletionSnapshot after)
    {
        var result = new List<string>();
        void TextAdded(string field, string? oldValue, string? newValue) {
            if (string.IsNullOrWhiteSpace(oldValue) && !string.IsNullOrWhiteSpace(newValue)) result.Add(field);
        }
        TextAdded("简介", before.Movie.Description, after.Movie.Description);
        TextAdded("日期", before.Movie.ReleaseDate, after.Movie.ReleaseDate);
        TextAdded("NFO", before.Movie.NfoPath, after.Movie.NfoPath);
        if (after.ActorIds.Except(before.ActorIds).Any()) result.Add("演员");
        if (after.GenreIds.Except(before.GenreIds).Any()) result.Add("Genre");
        if (after.DirectorIds.Except(before.DirectorIds).Any()) result.Add("导演");
        if (after.StudioRelations.Except(before.StudioRelations, StringComparer.OrdinalIgnoreCase).Any()) result.Add("厂商");
        if (after.SeriesIds.Except(before.SeriesIds).Any()) result.Add("系列");
        foreach (ImageSnapshotRef image in after.Images.Where(image => before.Images.All(old => old.Id != image.Id)))
            result.Add(MediaStoragePathResolver.NormalizeResourceType(image.Type));
        if (after.NfoDocumentIds.Except(before.NfoDocumentIds).Any() && !result.Contains("NFO")) result.Add("NFO");
        return result;
    }

    private static async Task RestoreSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction,
        CompletionSnapshot before, CompletionSnapshot after, CancellationToken token)
    {
        await ExecuteSqlAsync(connection, transaction, """
            UPDATE Movies SET Title=$title,OriginalTitle=$original,SortTitle=$sort,Description=$description,
                ReleaseDate=$release,DurationSeconds=$duration,ProviderRating=$rating,NfoPath=$nfo,
                IsScraped=$scraped,ScrapeStatus=$status,UpdatedAt=$updated WHERE Id=$id
            """, token, ("$title", before.Movie.Title), ("$original", before.Movie.OriginalTitle),
            ("$sort", before.Movie.SortTitle), ("$description", before.Movie.Description),
            ("$release", before.Movie.ReleaseDate), ("$duration", before.Movie.DurationSeconds),
            ("$rating", before.Movie.ProviderRating), ("$nfo", before.Movie.NfoPath),
            ("$scraped", before.Movie.IsScraped), ("$status", before.Movie.ScrapeStatus),
            ("$updated", before.Movie.UpdatedAt), ("$id", before.Movie.Id));
        await DeleteAddedIdsAsync(connection, transaction, "MovieActors", "ActorId", before.Movie.Id,
            after.ActorIds.Except(before.ActorIds), token);
        await DeleteAddedIdsAsync(connection, transaction, "MovieGenres", "GenreId", before.Movie.Id,
            after.GenreIds.Except(before.GenreIds), token);
        if (await TableExistsAsync(connection, "MovieDirectors", token))
            await DeleteAddedIdsAsync(connection, transaction, "MovieDirectors", "DirectorId", before.Movie.Id,
                after.DirectorIds.Except(before.DirectorIds), token);
        await DeleteAddedIdsAsync(connection, transaction, "MovieSeries", "SeriesId", before.Movie.Id,
            after.SeriesIds.Except(before.SeriesIds), token);
        foreach (string relation in after.StudioRelations.Except(before.StudioRelations, StringComparer.OrdinalIgnoreCase)) {
            string[] parts = relation.Split('|', 2);
            if (parts.Length == 2 && long.TryParse(parts[0], out long studioId))
                await ExecuteSqlAsync(connection, transaction,
                    "DELETE FROM MovieStudios WHERE MovieId=$movie AND StudioId=$id AND RelationType=$type",
                    token, ("$movie", before.Movie.Id), ("$id", studioId), ("$type", parts[1]));
        }
        await DeleteByPrimaryIdsAsync(connection, transaction, "Images",
            after.Images.Select(image => image.Id).Except(before.Images.Select(image => image.Id)), token);
        if (await TableExistsAsync(connection, "NfoDocuments", token))
            await DeleteByPrimaryIdsAsync(connection, transaction, "NfoDocuments", after.NfoDocumentIds.Except(before.NfoDocumentIds), token);
    }

    private static async Task DeleteAddedIdsAsync(SqliteConnection connection, SqliteTransaction transaction,
        string table, string column, long movieId, IEnumerable<long> ids, CancellationToken token)
    {
        foreach (long id in ids)
            await ExecuteSqlAsync(connection, transaction, $"DELETE FROM {table} WHERE MovieId=$movie AND {column}=$id",
                token, ("$movie", movieId), ("$id", id));
    }

    private static async Task DeleteByPrimaryIdsAsync(SqliteConnection connection, SqliteTransaction transaction,
        string table, IEnumerable<long> ids, CancellationToken token)
    {
        foreach (long id in ids)
            await ExecuteSqlAsync(connection, transaction, $"DELETE FROM {table} WHERE Id=$id", token, ("$id", id));
    }

    private async Task<List<AuditRow>> ReadAuditsAsync(SqliteConnection connection, string token, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,BeforeJson,AfterJson FROM OperationAudit
             WHERE OperationType='MetadataCompletion' AND RollbackToken=$token AND RevertedAt IS NULL ORDER BY Id
            """;
        command.Parameters.AddWithValue("$token", token);
        var result = new List<AuditRow>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    private static MetadataCompletionItem Finish(MetadataCompletionItem item, string status, string reason,
        string? category, int attempts, Stopwatch timer, IReadOnlyList<string> added,
        IReadOnlyDictionary<string, IReadOnlyList<string>> contributions)
    {
        timer.Stop();
        return item with { Status = status, Reason = reason, FailureCategory = category, Attempts = attempts,
            ElapsedMilliseconds = timer.ElapsedMilliseconds, AddedFields = added, ProviderContributions = contributions };
    }

    private static MetadataCompletionCounts CountExecution(MetadataCompletionCounts baseline, IReadOnlyList<MetadataCompletionItem> items) =>
        baseline with {
            Completed = items.LongCount(item => item.Status == "Completed"),
            Partial = items.LongCount(item => item.Status == "Partial"),
            Skipped = items.LongCount(item => item.Status == "Skipped"),
            Failed = items.LongCount(item => item.Status == "Failed"),
            NoResult = items.LongCount(item => item.Status == "NoResult"),
            Conflict = items.LongCount(item => item.Status == "Conflict"),
        };

    private static bool IsTerminal(string status) => status is "Completed" or "Partial" or "Skipped" or "Failed" or "NoResult" or "Conflict";
    private static double Percent(int complete, int total) => total == 0 ? 100 : Math.Round(complete * 100d / total, 2);

    private static string Classify(Exception error)
    {
        string message = error.Message;
        if (message.Contains("429", StringComparison.OrdinalIgnoreCase) || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)) return "RateLimit";
        if (error is TaskCanceledException or TimeoutException || message.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return "Timeout";
        if (error is HttpRequestException) return "Network";
        if (message.Contains("locked", StringComparison.OrdinalIgnoreCase) || message.Contains("锁定", StringComparison.OrdinalIgnoreCase)) return "Locked";
        if (message.Contains("conflict", StringComparison.OrdinalIgnoreCase) || message.Contains("冲突", StringComparison.OrdinalIgnoreCase)) return "ProviderConflict";
        return "ProviderError";
    }

    private static MetadataCompletionScanCommand Normalize(MetadataCompletionScanCommand input)
    {
        if (!KnownFields.Any(field => IsSelected(input, field))) throw new ArgumentException("至少选择一个补全字段。");
        return input with { Concurrency = Math.Clamp(input.Concurrency, 1, 8) };
    }

    private static bool IsSelected(MetadataCompletionScanCommand input, string field) => field switch {
        "Actors" => input.Actors, "Genres" => input.Genres, "Poster" => input.Poster, "Fanart" => input.Fanart,
        "NFO" => input.Nfo, "Description" => input.Description, "Series" => input.Series,
        "Director" => input.Director, "Studio" => input.Studio, "ReleaseDate" => input.ReleaseDate, _ => false,
    };

    private static MetadataCompletionScanCommand DeserializeOptions(string? json)
    {
        try { return Normalize(JsonSerializer.Deserialize<MetadataCompletionScanCommand>(json ?? "{}", JsonOptions) ?? new()); }
        catch (JsonException) { return new(); }
    }

    private static CompletionPlanDocument? DeserializePlan(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CompletionPlanDocument>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static CompletionSnapshot DeserializeSnapshot(string json) =>
        JsonSerializer.Deserialize<CompletionSnapshot>(json, JsonOptions)
        ?? throw new InvalidDataException("补全审计快照损坏，无法安全回滚。");

    private static string ConfirmationToken(long taskId, CompletionPlanDocument plan)
    {
        string material = $"{taskId}|{plan.Projection.CompleteBefore}|{string.Join(';', plan.Items.Select(item => $"{item.MovieId}:{string.Join(',', item.MissingFields)}:{item.Status}"))}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static void VerifyToken(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual)))
            throw new InvalidOperationException("确认令牌无效或补全计划已变化，请重新 Dry Run。");
    }

    private async Task<long?> ClaimNextAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        long id = await ScalarLongAsync(connection, null, $"""
            SELECT COALESCE(MIN(Id),0) FROM Tasks
             WHERE TaskType='{TaskType}' AND Status='Pending' AND CancellationRequested=0
            """, token);
        if (id == 0) return null;
        int changed = await ExecuteSqlAsync(connection, null, """
            UPDATE Tasks SET Status='Running',StartedAt=COALESCE(StartedAt,$at),UpdatedAt=$at
             WHERE Id=$id AND Status='Pending' AND CancellationRequested=0
            """, token, ("$at", Now()), ("$id", id));
        return changed == 1 ? id : null;
    }

    private async Task RecoverInterruptedAsync(CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteSqlAsync(connection, null, $"""
            UPDATE Tasks SET Status='Paused',CancellationRequested=1,
                ResultSummary='应用退出时任务中断；可从已保存断点继续。',UpdatedAt=$at
             WHERE TaskType='{TaskType}' AND Status='Running'
            """, token, ("$at", Now()));
    }

    private async Task MarkPausedAsync(long taskId)
    {
        try {
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, CancellationToken.None);
            await ExecuteSqlAsync(connection, null, """
                UPDATE Tasks SET Status='Paused',CancellationRequested=1,
                    ResultSummary='任务已暂停，断点已保留。',UpdatedAt=$at
                 WHERE Id=$id AND TaskType='MetadataCompletion' AND Status<>'Paused'
                """, CancellationToken.None, ("$at", Now()), ("$id", taskId));
        }
        catch (Exception error) { Console.Error.WriteLine(error); }
    }

    private async Task MarkFailedAsync(long taskId, Exception error)
    {
        try {
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, CancellationToken.None);
            await ExecuteSqlAsync(connection, null, """
                UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,
                    ResultSummary='定向补全失败，已完成项目保留并可按审计回滚。',CompletedAt=$at,UpdatedAt=$at
                 WHERE Id=$id
                """, CancellationToken.None, ("$error", error.Message), ("$at", Now()), ("$id", taskId));
            await logs.WriteAsync(taskId, "Error", error.Message, CancellationToken.None);
        }
        catch (Exception persistenceError) { Console.Error.WriteLine(persistenceError); }
    }

    private async Task UpdateTaskAsync(long id, string status, string stage, double progress, int total, int completed,
        string summary, CancellationToken token)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
        await ExecuteSqlAsync(connection, null, """
            UPDATE Tasks SET Status=$status,Stage=$stage,Progress=$progress,TotalItems=$total,
                CompletedItems=$completed,ResultSummary=$summary,UpdatedAt=$at WHERE Id=$id
            """, token, ("$status", status), ("$stage", stage), ("$progress", progress),
            ("$total", total), ("$completed", completed), ("$summary", summary), ("$at", Now()), ("$id", id));
    }

    private async Task<TaskRow> ReadTaskAsync(SqliteConnection connection, long taskId, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Status,COALESCE(Stage,''),Progress,PayloadJson,ResultJson,ErrorMessage,CreatedAt,CompletedAt
              FROM Tasks WHERE Id=$id AND TaskType='MetadataCompletion'
            """;
        command.Parameters.AddWithValue("$id", taskId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("定向补全任务不存在。");
        return new(reader.GetString(0), reader.GetString(1), reader.GetDouble(2), Text(reader, 3), Text(reader, 4),
            Text(reader, 5), reader.GetString(6), Text(reader, 7));
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode, CancellationToken token, bool registerFileFunctions = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = mode }.ToString());
        if (registerFileFunctions) MetadataHealthDefinition.RegisterFileFunctions(connection);
        await connection.OpenAsync(token);
        if (mode != SqliteOpenMode.ReadOnly) {
            await using SqliteCommand pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            await pragma.ExecuteNonQueryAsync(token);
        }
        return connection;
    }

    private static async Task<long[]> ReadIdsAsync(SqliteConnection connection, string sql, long movieId, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql; command.Parameters.AddWithValue("$id", movieId);
        var result = new List<long>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(reader.GetInt64(0));
        return result.Order().ToArray();
    }

    private static async Task<string[]> ReadStringsAsync(SqliteConnection connection, string sql, long movieId, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql; command.Parameters.AddWithValue("$id", movieId);
        var result = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(reader.GetString(0));
        return result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<ImageSnapshotRef[]> ReadImageRefsAsync(SqliteConnection connection, long movieId, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id,ImageType FROM Images WHERE MovieId=$id ORDER BY Id";
        command.Parameters.AddWithValue("$id", movieId);
        var result = new List<ImageSnapshotRef>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(new(reader.GetInt64(0), reader.GetString(1)));
        return result.ToArray();
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table, CancellationToken token)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L) > 0;
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction; command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token) ?? 0L);
    }

    private static async Task<long> InsertIdAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction; command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token));
    }

    private static async Task<int> ExecuteSqlAsync(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction; command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(token);
    }

    private static MetadataCompletionCounts EmptyCounts() => new(0, 0, 0, 0, 0, 0, 0, 0, 0,
        new Dictionary<string, long>(), new Dictionary<string, long>(), 0, 0, 0, 0, 0, 0);

    private static string RenderMarkdown(MetadataCompletionPreview preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Metadata Completion Report").AppendLine();
        builder.AppendLine($"- Task: {preview.TaskId}");
        builder.AppendLine($"- Status: {preview.Status}");
        builder.AppendLine($"- Standard scanned: {preview.Counts.ScannedStandardMovies}");
        builder.AppendLine($"- Planned network movies: {preview.Counts.PlannedNetworkMovies}");
        builder.AppendLine($"- Complete: {preview.Projection.CompleteBefore} -> {preview.Projection.CompleteAfter?.ToString() ?? preview.Projection.CompleteProjected.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Dry Run: {preview.DryRun}").AppendLine();
        builder.AppendLine("## Provider Requests");
        foreach ((string provider, long count) in preview.Counts.ProviderRequests) builder.AppendLine($"- {provider}: {count}");
        builder.AppendLine().AppendLine("## Items");
        foreach (MetadataCompletionItem item in preview.Items)
            builder.AppendLine($"- {item.Number} (MovieId {item.MovieId}): {item.Status}; missing={string.Join(',', item.MissingFields)}; providers={string.Join('>', item.ProviderPlan)}; {item.Reason}");
        return builder.ToString();
    }

    private static string RenderCsv(MetadataCompletionPreview preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MovieId,Number,VideoPath,MissingFields,ProtectedFields,ProviderPlan,Status,FailureCategory,Attempts,ElapsedMilliseconds,AddedFields,Reason");
        foreach (MetadataCompletionItem item in preview.Items) builder.AppendLine(string.Join(',', new[] {
            item.MovieId.ToString(CultureInfo.InvariantCulture), Csv(item.Number), Csv(item.VideoPath), Csv(string.Join('|', item.MissingFields)),
            Csv(string.Join('|', item.ProtectedFields)), Csv(string.Join('|', item.ProviderPlan)), Csv(item.Status), Csv(item.FailureCategory),
            item.Attempts.ToString(CultureInfo.InvariantCulture), item.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
            Csv(string.Join('|', item.AddedFields)), Csv(item.Reason),
        }));
        return builder.ToString();
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    private static string? Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    private sealed record CompletionPlanDocument(
        MetadataCompletionScanCommand Options,
        MetadataCompletionCounts Counts,
        MetadataCompletionProjection Projection,
        IReadOnlyList<MetadataCompletionItem> Items,
        IReadOnlyList<string> Warnings,
        bool DryRun,
        string? RollbackToken);

    private sealed record CompletionMovieState(
        long MovieId, string Code, string? Title, string? Description, string? ReleaseDate, int DurationSeconds,
        string? NfoPath, string VideoPath, string FileName, bool HasNumber, bool HasTitle, bool HasReleaseDate,
        bool Studio, bool Actors, bool Genres, bool Poster, bool Fanart, bool NfoMissing, bool HasDescription,
        bool Series, bool Director, bool PosterProtected, bool FanartProtected, bool NfoProtected)
    {
        public bool Complete => HasNumber && HasTitle && HasReleaseDate && Studio && Actors && Genres && Poster && Fanart && !NfoMissing;
    }

    private sealed record MovieScalarSnapshot(long Id, string? Title, string? OriginalTitle, string? SortTitle,
        string? Description, string? ReleaseDate, int DurationSeconds, decimal? ProviderRating, string? NfoPath,
        long IsScraped, string ScrapeStatus, string UpdatedAt);
    private sealed record CompletionSnapshot(MovieScalarSnapshot Movie, IReadOnlyList<long> ActorIds,
        IReadOnlyList<long> GenreIds, IReadOnlyList<long> DirectorIds, IReadOnlyList<string> StudioRelations,
        IReadOnlyList<long> SeriesIds, IReadOnlyList<ImageSnapshotRef> Images, IReadOnlyList<long> NfoDocumentIds);
    private sealed record ImageSnapshotRef(long Id, string Type);
    private sealed record ProviderFetchResult(ProviderMetadata? Metadata, int Attempts, string? FailureCategory);
    private sealed record TaskRow(string Status, string Stage, double Progress, string? PayloadJson, string? ResultJson,
        string? ErrorMessage, string CreatedAt, string? CompletedAt);
    private sealed record AuditRow(long Id, string BeforeJson, string AfterJson);
    private sealed class ProviderThrottle
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public DateTimeOffset LastCompletedUtc { get; set; } = DateTimeOffset.MinValue;
    }
}
