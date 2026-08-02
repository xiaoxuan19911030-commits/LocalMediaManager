using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;

const string seed = "lmm-v0.7.8-production-validation-fixed-sample-v1";
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

if (args.Length == 0) return Usage();
string command = args[0].Trim().ToLowerInvariant();
return command switch {
    "prepare" => await PrepareAsync(args),
    "run" => await RunAsync(args),
    "audit" => await AuditAsync(args),
    "reverify" => await ReverifyAsync(args),
    "coverage" => await CoverageAsync(args),
    "manifest" => await ManifestAsync(args),
    "memory" => await MemoryAsync(args),
    "heapwalk" => await HeapWalkAsync(args),
    "growth-shape" => await GrowthShapeAsync(args),
    _ => Usage(),
};

int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  prepare <source-db> <validation-root> <repo-root>");
    Console.Error.WriteLine("  run <validation-root> <repo-root> <20|50|100> [port] [run-label]");
    Console.Error.WriteLine("  audit <database> <manifest>");
    Console.Error.WriteLine("  reverify <baseline-db> <run-root> <manifest>");
    Console.Error.WriteLine("  coverage <database> <repo-root> <output-root>");
    Console.Error.WriteLine("  manifest <source-manifest> <output-manifest>");
    Console.Error.WriteLine("  memory <validation-root> <repo-root> [port] [no-images]");
    Console.Error.WriteLine("  heapwalk <validation-root> <repo-root> <run-label> <Full|Disabled|DownloadAndDrainOnly|DownloadToBufferOnly|DownloadAndProbeOnly|DownloadAndDecodeOnly|DownloadDecodeAndHash|FullWithoutFileWrite|FullEarlyResponseDispose> [port]");
    Console.Error.WriteLine("  growth-shape <validation-root> <repo-root> <run-label> <same-process|fresh-process|disabled> [port]");
    return 2;
}

async Task<int> ManifestAsync(string[] input)
{
    if (input.Length != 3) return Usage();
    SampleManifest source = JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(input[1]), jsonOptions)
        ?? throw new InvalidDataException("Sample manifest could not be read.");
    var rows = source.Samples.Select((sample, index) => {
        string normalized = sample.Code.Trim().ToUpperInvariant();
        bool nonStandard = normalized.Equals("QQCM-002", StringComparison.OrdinalIgnoreCase);
        bool coverageGap = normalized is "MD-0221" or "IMG-0501";
        string category = nonStandard ? "KnownNonStandard"
            : coverageGap ? "KnownProviderCoverageGap"
            : normalized.StartsWith("FC2-PPV-", StringComparison.Ordinal) ? "FC2"
            : sample.LowercaseCode ? "Normalization"
            : sample.DigitCount >= 6 ? "LongNumericCode"
            : sample.ActorCount > 1 ? "MultipleActors"
            : sample.ProviderFieldCount < 8 ? "PartialMetadata"
            : "StandardExactMatch";
        bool protectedData = sample.Favorite || sample.HasUserRating || sample.UserTagCount > 0
            || sample.PlayHistoryCount > 0 || sample.LockedImageCount > 0 || sample.LockedNfoCount > 0;
        return new Stage3ManifestRow(index + 1, sample.MovieId, sample.Code, normalized, index < 50, index >= 50,
            category, $"Fixed deterministic sample; {category}", nonStandard ? "NonStandard" : "Standard",
            !nonStandard, nonStandard ? "None" : "MetaTube -> JavBus (fill-missing only)",
            !nonStandard && (sample.ProviderFieldCount < 8 || coverageGap), protectedData,
            coverageGap || normalized.StartsWith("FC2-PPV-", StringComparison.Ordinal) || normalized == "777YMYM-030",
            "Pending", coverageGap ? "ProviderCoverageGap" : nonStandard ? "NonStandard" : "Pending");
    }).ToArray();
    var manifest = new Stage3SampleManifest(source.Seed, source.Count, rows.Count(value => value.IsStage2Sample),
        rows.Count(value => value.IsNewStage3Sample), rows);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(input[2]))!);
    await WriteJsonAsync(Path.GetFullPath(input[2]), manifest);
    Console.WriteLine(JsonSerializer.Serialize(new { manifest.Count, manifest.Stage2Samples, manifest.NewStage3Samples }, jsonOptions));
    return 0;
}

async Task<int> CoverageAsync(string[] input)
{
    if (input.Length != 4) return Usage();
    string database = Path.GetFullPath(input[1]);
    string repo = Path.GetFullPath(input[2]);
    string outputRoot = Path.GetFullPath(input[3]);
    Directory.CreateDirectory(outputRoot);
    var extractor = new MovieNumberExtractor(Path.Combine(repo, "backend", "LocalMediaManager.Bridge", "movie-number-rules.json"));
    var rows = new List<CoverageRow>();
    await using SqliteConnection connection = await OpenAsync(database, true);
    await using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        SELECT m.Id,trim(COALESCE(m.Code,'')),COALESCE(f.FileName,''),COALESCE(f.FilePath,''),
               COALESCE((SELECT t.Status FROM Tasks t WHERE t.TaskType='Sync' AND t.CurrentMovieId=m.Id ORDER BY t.Id DESC LIMIT 1),''),
               EXISTS(
                   SELECT 1 FROM MetadataSyncSnapshots s
                   WHERE s.MovieId=m.Id AND s.AppliedJson IS NOT NULL AND s.AppliedJson LIKE '%MetaTube/%'),
               EXISTS(
                   SELECT 1 FROM Tasks t JOIN TaskLogs tl ON tl.TaskId=t.Id
                   WHERE t.TaskType='Sync' AND t.CurrentMovieId=m.Id
                     AND tl.Message='[MetaTube] 结束：已参与合并'),
               EXISTS(
                   SELECT 1 FROM MetadataSyncSnapshots s
                   WHERE s.MovieId=m.Id AND s.AppliedJson IS NOT NULL AND s.AppliedJson LIKE '%JavBus/%'),
               EXISTS(
                   SELECT 1 FROM Tasks t JOIN TaskLogs tl ON tl.TaskId=t.Id
                   WHERE t.TaskType='Sync' AND t.CurrentMovieId=m.Id
                     AND tl.Message='[JavBus] 结束：已参与合并')
          FROM Movies m
          JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
          JOIN Libraries l ON l.Id=f.LibraryId AND l.LibraryType='Standard' AND l.IsEnabled=1
         WHERE COALESCE(f.ExistsState,'')<>'Missing'
         ORDER BY m.Id
        """;
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) {
        long movieId = reader.GetInt64(0);
        string code = reader.GetString(1);
        string fileName = reader.GetString(2);
        string filePath = reader.GetString(3);
        string latestStatus = reader.GetString(4);
        bool metaTube = reader.GetInt64(5) == 1 || reader.GetInt64(6) == 1;
        bool javBus = reader.GetInt64(7) == 1 || reader.GetInt64(8) == 1;
        MovieNumberExtractionResult extraction = extractor.Extract(fileName);
        bool validNumber = !string.IsNullOrWhiteSpace(extraction.NormalizedNumber)
            && extraction.Confidence >= extractor.MinimumAutoSyncConfidence
            && extractor.AreEquivalent(code, extraction.NormalizedNumber);
        bool nonStandard = fileName.Contains("国产", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("國產", StringComparison.OrdinalIgnoreCase);
        string classification;
        string reason;
        if (nonStandard) {
            classification = "NonStandard";
            reason = "Filename contains an explicit domestic-content marker outside the current JAV Provider capability.";
        }
        else if (!validNumber) {
            classification = "Unknown";
            reason = "Number is missing, low-confidence, or inconsistent with the current file name.";
        }
        else if (metaTube) {
            classification = "MetaTubeCovered";
            reason = "A prior applied snapshot or task log records a MetaTube contribution.";
        }
        else if (javBus) {
            classification = "JavBusCovered";
            reason = "A prior applied snapshot or task log records a JavBus contribution.";
        }
        else if (latestStatus.Equals("NoResult", StringComparison.OrdinalIgnoreCase)) {
            classification = "ProviderCoverageGap";
            reason = "The latest real synchronization task ended as NoResult for a valid Standard number.";
        }
        else {
            classification = "Unknown";
            reason = "No read-only historical Provider evidence is available; no network request was made.";
        }
        rows.Add(new(movieId, code, fileName, filePath, classification, metaTube, javBus,
            latestStatus, extraction.Confidence, extraction.NormalizedNumber ?? "", reason));
    }

    var summary = new CoverageSummary(rows.Count,
        rows.Count(value => value.MetaTubeEvidence),
        rows.Count(value => value.JavBusSupplementEvidence),
        rows.Count(value => value.Classification == "ProviderCoverageGap"),
        rows.Count(value => value.Classification == "NonStandard"),
        rows.Count(value => value.Classification == "Unknown"),
        DateTimeOffset.Now.ToString("O"), "ReadOnlyHistoricalEvidence");
    await WriteJsonAsync(Path.Combine(outputRoot, "coverage-summary.json"), summary);
    await WriteJsonAsync(Path.Combine(outputRoot, "coverage-matrix.json"), rows);
    var csv = new StringBuilder("MovieId,Code,FileName,FilePath,Classification,MetaTubeEvidence,JavBusSupplementEvidence,LatestSyncStatus,Confidence,NormalizedNumber,Reason\r\n");
    foreach (CoverageRow row in rows) csv.AppendLine(string.Join(',', new[] {
        row.MovieId.ToString(CultureInfo.InvariantCulture), row.Code, row.FileName, row.FilePath, row.Classification,
        row.MetaTubeEvidence.ToString(), row.JavBusSupplementEvidence.ToString(), row.LatestSyncStatus,
        row.Confidence.ToString("0.00", CultureInfo.InvariantCulture), row.NormalizedNumber, row.Reason,
    }.Select(Csv)));
    await File.WriteAllTextAsync(Path.Combine(outputRoot, "coverage-matrix.csv"), csv.ToString(), new UTF8Encoding(false));
    Console.WriteLine(JsonSerializer.Serialize(summary, jsonOptions));
    return 0;
}

async Task<int> ReverifyAsync(string[] input)
{
    if (input.Length != 4) return Usage();
    string baseline = Path.GetFullPath(input[1]);
    string runRoot = Path.GetFullPath(input[2]);
    SampleManifest manifest = JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(input[3]), jsonOptions)
        ?? throw new InvalidDataException("Sample manifest could not be read.");
    string evidence = Path.Combine(runRoot, "evidence");
    string database = Path.Combine(runRoot, "data", "LocalMediaManager.db");
    IReadOnlyList<MovieSnapshot> source = JsonSerializer.Deserialize<List<MovieSnapshot>>(await File.ReadAllTextAsync(Path.Combine(evidence, "source-before-reset.json")), jsonOptions)!;
    IReadOnlyList<MovieSnapshot> before = JsonSerializer.Deserialize<List<MovieSnapshot>>(await File.ReadAllTextAsync(Path.Combine(evidence, "before.json")), jsonOptions)!;
    IReadOnlyList<MovieSnapshot> after = JsonSerializer.Deserialize<List<MovieSnapshot>>(await File.ReadAllTextAsync(Path.Combine(evidence, "after.json")), jsonOptions)!;
    IReadOnlyList<TaskRow> tasks = JsonSerializer.Deserialize<List<TaskRow>>(await File.ReadAllTextAsync(Path.Combine(evidence, "tasks.json")), jsonOptions)!;
    IReadOnlyList<TaskLogRow> logs = JsonSerializer.Deserialize<List<TaskLogRow>>(await File.ReadAllTextAsync(Path.Combine(evidence, "task-logs.json")), jsonOptions)!;
    BatchVerification previous = JsonSerializer.Deserialize<BatchVerification>(await File.ReadAllTextAsync(Path.Combine(evidence, "verification.json")), jsonOptions)!;
    string integrity = await ScalarTextAsync(database, "PRAGMA integrity_check") ?? "unknown";
    long foreignKeys = await ScalarLongAsync(database, "SELECT COUNT(*) FROM pragma_foreign_key_check");
    BatchVerification verification = await VerifyBatchAsync(baseline, database, Path.Combine(runRoot, "MediaStorage"), source, before, after,
        tasks, logs, TimeSpan.FromMilliseconds(previous.TotalElapsedMilliseconds), integrity, foreignKeys);
    long previousMaxTask = await ScalarLongAsync(baseline, "SELECT COALESCE(MAX(Id),0) FROM Tasks");
    ProtectedDataAudit protectedData = BuildProtectedDataAudit(source, after);
    DataIntegrityAudit dataIntegrity = await BuildDataIntegrityAuditAsync(database, Path.Combine(runRoot, "MediaStorage"),
        manifest.Samples, tasks, previousMaxTask, integrity, foreignKeys);
    HttpTrafficAudit httpTraffic = AnalyzeHttpTraffic(Path.Combine(runRoot, "logs", "bridge-stdout.log"), logs);
    await WriteJsonAsync(Path.Combine(evidence, "verification.json"), verification);
    await WriteJsonAsync(Path.Combine(evidence, "task-stage-metrics.json"), BuildTaskStageMetrics(tasks, logs));
    await WriteJsonAsync(Path.Combine(evidence, "protected-data-audit.json"), protectedData);
    await WriteJsonAsync(Path.Combine(evidence, "data-integrity-audit.json"), dataIntegrity);
    await WriteJsonAsync(Path.Combine(evidence, "http-traffic-audit.json"), httpTraffic);
    Console.WriteLine(JsonSerializer.Serialize(verification, jsonOptions));
    return verification.Passed ? 0 : 4;
}

async Task<int> AuditAsync(string[] input)
{
    if (input.Length != 3) return Usage();
    string database = Path.GetFullPath(input[1]);
    SampleManifest manifest = JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(input[2]), jsonOptions)
        ?? throw new InvalidDataException("Sample manifest could not be read.");
    string ids = string.Join(',', manifest.Samples.Select(value => value.MovieId));
    await using SqliteConnection connection = await OpenAsync(database, true);
    async Task<IReadOnlyList<string>> GroupsAsync(string sql)
    {
        var rows = new List<string>();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount)
            .Select(index => reader.IsDBNull(index) ? "<NULL>" : reader.GetValue(index).ToString())));
        return rows;
    }
    var result = new {
        database,
        sampleContext = await GroupsAsync($"""
            SELECT m.Id,COALESCE(m.Code,''),COALESCE(f.FileName,''),COALESCE(f.FilePath,''),
                   COALESCE(l.Name,''),COALESCE(l.LibraryType,'Standard')
              FROM Movies m
              LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
              LEFT JOIN Libraries l ON l.Id=f.LibraryId
             WHERE m.Id IN ({ids})
             ORDER BY m.Id
            """),
        duplicateActors = await GroupsAsync("SELECT NormalizedName,COUNT(*) FROM Actors WHERE trim(COALESCE(NormalizedName,''))<>'' GROUP BY NormalizedName HAVING COUNT(*)>1 ORDER BY NormalizedName"),
        duplicateGenres = await GroupsAsync("SELECT NormalizedName,COUNT(*) FROM Genres WHERE trim(COALESCE(NormalizedName,''))<>'' GROUP BY NormalizedName HAVING COUNT(*)>1 ORDER BY NormalizedName"),
        selectedDuplicateMovieActors = await GroupsAsync($"SELECT MovieId,ActorId,RoleName,COUNT(*) FROM MovieActors WHERE MovieId IN ({ids}) GROUP BY MovieId,ActorId,RoleName HAVING COUNT(*)>1"),
        selectedDuplicateMovieGenres = await GroupsAsync($"SELECT MovieId,GenreId,COUNT(*) FROM MovieGenres WHERE MovieId IN ({ids}) GROUP BY MovieId,GenreId HAVING COUNT(*)>1"),
        selectedDuplicateResources = await GroupsAsync($"SELECT MovieId,ImageType,lower(FilePath),COUNT(*) FROM Images WHERE MovieId IN ({ids}) GROUP BY MovieId,ImageType,lower(FilePath) HAVING COUNT(*)>1 ORDER BY MovieId,ImageType"),
    };
    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
    return 0;
}

async Task<int> PrepareAsync(string[] input)
{
    if (input.Length != 4) return Usage();
    string source = Path.GetFullPath(input[1]);
    string root = Path.GetFullPath(input[2]);
    string repo = Path.GetFullPath(input[3]);
    if (!File.Exists(source)) throw new FileNotFoundException("Production database was not found.", source);
    if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        throw new InvalidOperationException($"Validation root must be new and empty: {root}");

    Directory.CreateDirectory(Path.Combine(root, "baseline"));
    Directory.CreateDirectory(Path.Combine(root, "evidence"));
    string destination = Path.Combine(root, "baseline", "LocalMediaManager.db");

    FileInfo sourceBefore = new(source);
    string sourceHashBefore = HashFile(source);
    DateTimeOffset copiedAt = DateTimeOffset.Now;
    await BackupAsync(source, destination);
    string integrity = await ScalarTextAsync(destination, "PRAGMA integrity_check") ?? "unknown";
    long foreignKeys = await ScalarLongAsync(destination, "SELECT COUNT(*) FROM pragma_foreign_key_check");
    string destinationHash = HashFile(destination);
    FileInfo sourceAfter = new(source);
    string sourceHashAfter = HashFile(source);
    if (sourceHashBefore != sourceHashAfter || sourceBefore.LastWriteTimeUtc != sourceAfter.LastWriteTimeUtc || sourceBefore.Length != sourceAfter.Length)
        throw new InvalidOperationException("Production database changed while the read-only backup was created.");

    var extractor = new MovieNumberExtractor(Path.Combine(repo, "backend", "LocalMediaManager.Bridge", "movie-number-rules.json"));
    List<SampleCandidate> candidates = await ReadCandidatesAsync(destination, extractor);
    List<SampleCandidate> selected = SelectFixedSamples(candidates, 100);
    if (selected.Count < 100) throw new InvalidOperationException($"Only {selected.Count} eligible Standard samples were found.");

    var sourceEvidence = new SourceEvidence(
        source, sourceBefore.Length, sourceHashBefore, sourceBefore.LastWriteTimeUtc.ToString("O"),
        destination, new FileInfo(destination).Length, destinationHash, copiedAt.ToString("O"),
        integrity, foreignKeys, sourceHashAfter, sourceAfter.LastWriteTimeUtc.ToString("O"));
    await WriteJsonAsync(Path.Combine(root, "evidence", "database-copy.json"), sourceEvidence);
    await WriteJsonAsync(Path.Combine(root, "evidence", "eligible-candidates-summary.json"), new {
        eligible = candidates.Count,
        rejected = await CountStandardCandidatesAsync(destination) - candidates.Count,
        seed,
        selected = selected.Count,
    });
    await WriteManifestAsync(root, 20, selected.Take(20).ToArray());
    await WriteManifestAsync(root, 10, selected.Take(10).ToArray());
    await WriteManifestAsync(root, 25, selected.Take(25).ToArray());
    await WriteManifestAsync(root, 50, selected.Take(50).ToArray());
    await WriteManifestAsync(root, 100, selected);

    Console.WriteLine(JsonSerializer.Serialize(sourceEvidence, jsonOptions));
    Console.WriteLine($"MANIFEST_20={Path.Combine(root, "manifests", "sample-20.json")}");
    Console.WriteLine($"MANIFEST_50={Path.Combine(root, "manifests", "sample-50.json")}");
    Console.WriteLine($"MANIFEST_100={Path.Combine(root, "manifests", "sample-100.json")}");
    return integrity == "ok" && foreignKeys == 0 ? 0 : 3;
}

async Task<int> RunAsync(string[] input)
{
    if (input.Length is < 4 or > 6) return Usage();
    string root = Path.GetFullPath(input[1]);
    string repo = Path.GetFullPath(input[2]);
    if (!int.TryParse(input[3], out int size) || size is not (10 or 20 or 25 or 50 or 100)) return Usage();
    int port = input.Length >= 5 && int.TryParse(input[4], out int parsed) ? parsed : 47900 + size;
    string runLabel = input.Length == 6 ? input[5].Trim() : $"batch-{size}";
    if (string.IsNullOrWhiteSpace(runLabel) || runLabel.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        throw new ArgumentException("Run label is not a valid directory name.");
    string baseline = Path.Combine(root, "baseline", "LocalMediaManager.db");
    string manifestPath = Path.Combine(root, "manifests", $"sample-{size}.json");
    if (!File.Exists(baseline) || !File.Exists(manifestPath)) throw new InvalidOperationException("Run prepare first.");
    SampleManifest manifest = JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(manifestPath), jsonOptions)
        ?? throw new InvalidDataException("Sample manifest could not be read.");

    string runRoot = Path.Combine(root, runLabel);
    if (Directory.Exists(runRoot) && Directory.EnumerateFileSystemEntries(runRoot).Any())
        throw new InvalidOperationException($"Batch root already contains evidence: {runRoot}");
    string database = Path.Combine(runRoot, "data", "LocalMediaManager.db");
    string evidence = Path.Combine(runRoot, "evidence");
    string logs = Path.Combine(runRoot, "logs");
    string mediaStorage = Path.Combine(runRoot, "MediaStorage");
    Directory.CreateDirectory(Path.GetDirectoryName(database)!);
    Directory.CreateDirectory(evidence);
    Directory.CreateDirectory(logs);
    Directory.CreateDirectory(mediaStorage);
    await ManifestAsync(["manifest", manifestPath, Path.Combine(evidence, "stage3-manifest.json")]);
    await BackupAsync(baseline, database);

    long previousMaxTask = await ScalarLongAsync(database, "SELECT COALESCE(MAX(Id),0) FROM Tasks");
    await ConfigureIsolationAsync(database, mediaStorage);
    IReadOnlyList<MovieSnapshot> sourceSnapshots = await SnapshotAsync(database, manifest.Samples);
    await WriteJsonAsync(Path.Combine(evidence, "source-before-reset.json"), sourceSnapshots);
    await ResetProviderMetadataAsync(database, manifest.Samples);
    IReadOnlyList<MovieSnapshot> before = await SnapshotAsync(database, manifest.Samples);
    await WriteJsonAsync(Path.Combine(evidence, "before.json"), before);

    string bridgeExe = Path.Combine(repo, "backend", "LocalMediaManager.Bridge", "bin", "Release", "net8.0", "LocalMediaManager.Bridge.exe");
    if (!File.Exists(bridgeExe)) throw new FileNotFoundException("Build the Release Bridge before running validation.", bridgeExe);
    string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    string bridgeUrl = $"http://127.0.0.1:{port}";
    using Process bridge = StartBridge(bridgeExe, database, runRoot, mediaStorage, bridgeUrl, token, logs);
    using var client = new HttpClient { BaseAddress = new Uri(bridgeUrl), Timeout = TimeSpan.FromSeconds(90) };
    client.DefaultRequestHeaders.Add("X-LMM-Session", token);
    try {
        await WaitForBridgeAsync(client, bridge, TimeSpan.FromSeconds(30));
        JsonElement readiness = await GetJsonAsync(client, "/api/developer/providers");
        await WriteJsonAsync(Path.Combine(evidence, "provider-readiness.json"), readiness);

        Stopwatch batchTimer = Stopwatch.StartNew();
        JsonElement enqueue = await PostJsonAsync(client, "/api/videos/batch/sync", manifest.Samples.Select(value => value.MovieId).ToArray());
        await WriteJsonAsync(Path.Combine(evidence, "enqueue.json"), enqueue);
        TaskWaitResult wait = await WaitForTasksAsync(database, previousMaxTask, manifest.Samples.Count, TimeSpan.FromMinutes(90), bridge);
        IReadOnlyList<TaskRow> tasks = wait.Tasks;
        batchTimer.Stop();

        IReadOnlyList<MovieSnapshot> after = await SnapshotAsync(database, manifest.Samples);
        IReadOnlyList<TaskLogRow> taskLogs = await ReadTaskLogsAsync(database, previousMaxTask);
        JsonElement providerAfter = await GetJsonAsync(client, "/api/developer/providers");
        JsonElement health = await GetJsonAsync(client, "/api/metadata/health?refresh=true");
        string integrity = await ScalarTextAsync(database, "PRAGMA integrity_check") ?? "unknown";
        long foreignKeys = await ScalarLongAsync(database, "SELECT COUNT(*) FROM pragma_foreign_key_check");
        BatchVerification verification = await VerifyBatchAsync(baseline, database, mediaStorage, sourceSnapshots, before, after,
            tasks, taskLogs, batchTimer.Elapsed, integrity, foreignKeys);
        IReadOnlyList<TaskStageMetric> stageMetrics = BuildTaskStageMetrics(tasks, taskLogs);
        ProtectedDataAudit protectedData = BuildProtectedDataAudit(sourceSnapshots, after);
        DataIntegrityAudit dataIntegrity = await BuildDataIntegrityAuditAsync(database, mediaStorage, manifest.Samples,
            tasks, previousMaxTask, integrity, foreignKeys);
        await Task.Delay(250);
        HttpTrafficAudit httpTraffic = AnalyzeHttpTraffic(Path.Combine(logs, "bridge-stdout.log"), taskLogs);
        await WriteJsonAsync(Path.Combine(evidence, "tasks.json"), tasks);
        await WriteJsonAsync(Path.Combine(evidence, "task-logs.json"), taskLogs);
        await WriteJsonAsync(Path.Combine(evidence, "task-stage-metrics.json"), stageMetrics);
        await WriteJsonAsync(Path.Combine(evidence, "runtime-samples.json"), wait.RuntimeSamples);
        await WriteJsonAsync(Path.Combine(evidence, "provider-dashboard-after.json"), providerAfter);
        await WriteJsonAsync(Path.Combine(evidence, "protected-data-audit.json"), protectedData);
        await WriteJsonAsync(Path.Combine(evidence, "data-integrity-audit.json"), dataIntegrity);
        await WriteJsonAsync(Path.Combine(evidence, "http-traffic-audit.json"), httpTraffic);
        await WriteJsonAsync(Path.Combine(evidence, "after.json"), after);
        await WriteJsonAsync(Path.Combine(evidence, "metadata-health.json"), health);
        await WriteJsonAsync(Path.Combine(evidence, "verification.json"), verification);
        await File.WriteAllTextAsync(Path.Combine(runRoot, $"batch-{size}-summary.md"), RenderBatchSummary(size, database, verification), new UTF8Encoding(false));
        Console.WriteLine(JsonSerializer.Serialize(verification, jsonOptions));
        return verification.Passed ? 0 : 4;
    }
    finally {
        try {
            if (!bridge.HasExited) {
                bridge.CloseMainWindow();
                if (!bridge.WaitForExit(5000)) bridge.Kill(true);
            }
        }
        catch { }
    }
}

async Task<int> MemoryAsync(string[] input)
{
    if (input.Length is < 3 or > 5) return Usage();
    string root = Path.GetFullPath(input[1]);
    string repo = Path.GetFullPath(input[2]);
    int port = input.Length >= 4 && int.TryParse(input[3], out int parsed) ? parsed : 48020;
    bool downloadImages = input.Length < 5 || !input[4].Equals("no-images", StringComparison.OrdinalIgnoreCase);
    if (input.Length == 5 && downloadImages) return Usage();
    string baseline = Path.Combine(root, "baseline", "LocalMediaManager.db");
    string manifestPath = Path.Combine(root, "manifests", "sample-100.json");
    if (!File.Exists(baseline) || !File.Exists(manifestPath)) throw new InvalidOperationException("Run prepare first.");
    SampleManifest manifest = JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(manifestPath), jsonOptions)
        ?? throw new InvalidDataException("Sample manifest could not be read.");

    string runRoot = Path.Combine(root, downloadImages ? "memory-100x2" : "memory-100x2-no-images");
    if (Directory.Exists(runRoot) && Directory.EnumerateFileSystemEntries(runRoot).Any())
        throw new InvalidOperationException($"Memory validation root already contains evidence: {runRoot}");
    string database = Path.Combine(runRoot, "data", "LocalMediaManager.db");
    string evidence = Path.Combine(runRoot, "evidence");
    string logs = Path.Combine(runRoot, "logs");
    string mediaStorage = Path.Combine(runRoot, "MediaStorage");
    Directory.CreateDirectory(Path.GetDirectoryName(database)!);
    Directory.CreateDirectory(evidence);
    Directory.CreateDirectory(logs);
    Directory.CreateDirectory(mediaStorage);
    await BackupAsync(baseline, database);
    await ConfigureIsolationAsync(database, mediaStorage, downloadImages);
    IReadOnlyList<MovieSnapshot> source = await SnapshotAsync(database, manifest.Samples);
    await WriteJsonAsync(Path.Combine(evidence, "source-before-reset.json"), source);
    await ResetProviderMetadataAsync(database, manifest.Samples);

    string bridgeExe = Path.Combine(repo, "backend", "LocalMediaManager.Bridge", "bin", "Release", "net8.0", "LocalMediaManager.Bridge.exe");
    if (!File.Exists(bridgeExe)) throw new FileNotFoundException("Build the Release Bridge before running validation.", bridgeExe);
    string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    string bridgeUrl = $"http://127.0.0.1:{port}";
    using Process bridge = StartBridge(bridgeExe, database, runRoot, mediaStorage, bridgeUrl, token, logs, enableValidationDiagnostics: true);
    using var client = new HttpClient { BaseAddress = new Uri(bridgeUrl), Timeout = TimeSpan.FromSeconds(90) };
    client.DefaultRequestHeaders.Add("X-LMM-Session", token);
    try {
        await WaitForBridgeAsync(client, bridge, TimeSpan.FromSeconds(30));
        var snapshots = new List<MemoryRuntimeSnapshot>();
        await CaptureMemorySnapshotAsync(client, database, snapshots, "BridgeReady", 0, false);

        long firstPreviousTask = await ScalarLongAsync(database, "SELECT COALESCE(MAX(Id),0) FROM Tasks");
        IReadOnlyList<MovieSnapshot> firstBefore = await SnapshotAsync(database, manifest.Samples);
        await PostJsonAsync(client, "/api/videos/batch/sync", manifest.Samples.Select(value => value.MovieId).ToArray());
        await CaptureMemorySnapshotAsync(client, database, snapshots, "Batch1BeforeFirstMovie", 0, false);
        MemoryTaskWaitResult firstWait = await WaitForMemoryTasksAsync(database, firstPreviousTask, manifest.Samples.Count,
            TimeSpan.FromMinutes(90), bridge, client, snapshots, "Batch1");
        IReadOnlyList<MovieSnapshot> firstAfter = await SnapshotAsync(database, manifest.Samples);
        IReadOnlyList<TaskLogRow> firstLogs = await ReadTaskLogsAsync(database, firstPreviousTask);
        string firstIntegrity = await ScalarTextAsync(database, "PRAGMA integrity_check") ?? "unknown";
        long firstForeignKeys = await ScalarLongAsync(database, "SELECT COUNT(*) FROM pragma_foreign_key_check");
        BatchVerification firstVerification = await VerifyBatchAsync(baseline, database, mediaStorage, source, firstBefore,
            firstAfter, firstWait.Tasks, firstLogs, firstWait.Elapsed, firstIntegrity, firstForeignKeys);

        await ClearIsolatedResourcesAsync(mediaStorage, runRoot);
        await ResetProviderMetadataAsync(database, manifest.Samples);
        long secondPreviousTask = await ScalarLongAsync(database, "SELECT COALESCE(MAX(Id),0) FROM Tasks");
        IReadOnlyList<MovieSnapshot> secondBefore = await SnapshotAsync(database, manifest.Samples);
        await CaptureMemorySnapshotAsync(client, database, snapshots, "Batch2BeforeFirstMovie", 0, false);
        await PostJsonAsync(client, "/api/videos/batch/sync", manifest.Samples.Select(value => value.MovieId).ToArray());
        MemoryTaskWaitResult secondWait = await WaitForMemoryTasksAsync(database, secondPreviousTask, manifest.Samples.Count,
            TimeSpan.FromMinutes(90), bridge, client, snapshots, "Batch2");
        IReadOnlyList<MovieSnapshot> secondAfter = await SnapshotAsync(database, manifest.Samples);
        IReadOnlyList<TaskLogRow> secondLogs = await ReadTaskLogsAsync(database, secondPreviousTask);
        string secondIntegrity = await ScalarTextAsync(database, "PRAGMA integrity_check") ?? "unknown";
        long secondForeignKeys = await ScalarLongAsync(database, "SELECT COUNT(*) FROM pragma_foreign_key_check");
        BatchVerification secondVerification = await VerifyBatchAsync(baseline, database, mediaStorage, source, secondBefore,
            secondAfter, secondWait.Tasks, secondLogs, secondWait.Elapsed, secondIntegrity, secondForeignKeys);

        await WriteJsonAsync(Path.Combine(evidence, "memory-snapshots.json"), snapshots);
        await WriteJsonAsync(Path.Combine(evidence, "batch-1-verification.json"), firstVerification);
        await WriteJsonAsync(Path.Combine(evidence, "batch-2-verification.json"), secondVerification);
        await WriteJsonAsync(Path.Combine(evidence, "batch-1-tasks.json"), firstWait.Tasks);
        await WriteJsonAsync(Path.Combine(evidence, "batch-2-tasks.json"), secondWait.Tasks);
        Console.WriteLine(JsonSerializer.Serialize(new { firstVerification, secondVerification, snapshots = snapshots.Count }, jsonOptions));
        return firstVerification.Passed && secondVerification.Passed ? 0 : 4;
    }
    finally {
        try {
            if (!bridge.HasExited) {
                bridge.CloseMainWindow();
                if (!bridge.WaitForExit(5000)) bridge.Kill(true);
            }
        }
        catch { }
    }
}

async Task<int> GrowthShapeAsync(string[] input)
{
    if (input.Length is < 5 or > 6) return Usage();
    string root = Path.GetFullPath(input[1]);
    string repo = Path.GetFullPath(input[2]);
    string runLabel = input[3].Trim();
    string experiment = input[4].Trim().ToLowerInvariant();
    if (experiment is not ("same-process" or "fresh-process" or "disabled")) return Usage();
    int port = input.Length == 6 && int.TryParse(input[5], out int parsed) ? parsed : 48220;
    if (string.IsNullOrWhiteSpace(runLabel) || runLabel.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        throw new ArgumentException("Run label is not a valid directory name.");

    string baseline = Path.Combine(root, "baseline", "LocalMediaManager.db");
    string manifestPath = Path.Combine(root, "manifests", "sample-20.json");
    if (!File.Exists(baseline) || !File.Exists(manifestPath)) throw new InvalidOperationException("Run prepare first.");
    SampleManifest manifest = JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(manifestPath), jsonOptions)
        ?? throw new InvalidDataException("Sample manifest could not be read.");
    string runRoot = Path.Combine(root, runLabel);
    int[] batchSizes = experiment == "disabled" ? [20, 20, 20]
        : experiment == "same-process" ? [1, 5, 10, 20, 20, 20] : [1, 5, 10, 20];
    bool downloadImages = experiment != "disabled";
    if (Directory.Exists(runRoot) && Directory.EnumerateFileSystemEntries(runRoot).Any())
        throw new InvalidOperationException($"Growth-shape validation root already contains evidence: {runRoot}");
    if (downloadImages) await EnsureLocalMetaTubeProviderAsync();
    Directory.CreateDirectory(runRoot);
    Directory.CreateDirectory(Path.Combine(runRoot, "evidence"));
    var stages = new List<GrowthShapeStage>();
    if (experiment == "fresh-process") {
        for (int index = 0; index < batchSizes.Length; index++)
            stages.Add(await RunGrowthShapeProcessAsync(runRoot, repo, baseline, manifest, batchSizes[index], index + 1,
                port + index, downloadImages, startupOnly: false));
    }
    else {
        stages.AddRange(await RunGrowthShapeSameProcessAsync(runRoot, repo, baseline, manifest, batchSizes, port, downloadImages));
    }

    var result = new GrowthShapeRunResult(runLabel, experiment, manifest.Seed, manifest.Count, downloadImages, stages,
        DateTimeOffset.UtcNow.ToString("O"));
    await WriteJsonAsync(Path.Combine(runRoot, "evidence", "growth-shape-result.json"), result);
    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
    return stages.All(stage => stage.HeapWalkValid && stage.Failed == 0 && stage.Cancelled == 0) ? 0 : 4;
}

async Task<IReadOnlyList<GrowthShapeStage>> RunGrowthShapeSameProcessAsync(string runRoot, string repo, string baseline,
    SampleManifest manifest, IReadOnlyList<int> batchSizes, int port, bool downloadImages)
{
    string processRoot = Path.Combine(runRoot, "same-bridge");
    string database = Path.Combine(processRoot, "data", "LocalMediaManager.db");
    string mediaStorage = Path.Combine(processRoot, "MediaStorage");
    string logs = Path.Combine(processRoot, "logs");
    Directory.CreateDirectory(Path.GetDirectoryName(database)!);
    Directory.CreateDirectory(mediaStorage);
    Directory.CreateDirectory(logs);
    await BackupAsync(baseline, database);
    await ConfigureIsolationAsync(database, mediaStorage, downloadImages);
    await ResetProviderMetadataAsync(database, manifest.Samples);
    string bridgeExe = Path.Combine(repo, "backend", "LocalMediaManager.Bridge", "bin", "Release", "net8.0", "LocalMediaManager.Bridge.exe");
    if (!File.Exists(bridgeExe)) throw new FileNotFoundException("Build the Release Bridge before running validation.", bridgeExe);
    string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    string bridgeUrl = $"http://127.0.0.1:{port}";
    using Process bridge = StartBridge(bridgeExe, database, processRoot, mediaStorage, bridgeUrl, token, logs,
        enableValidationDiagnostics: true, validationImageMode: "Full");
    using var client = new HttpClient { BaseAddress = new Uri(bridgeUrl), Timeout = TimeSpan.FromSeconds(90) };
    client.DefaultRequestHeaders.Add("X-LMM-Session", token);
    try {
        await WaitForBridgeAsync(client, bridge, TimeSpan.FromSeconds(30));
        var result = new List<GrowthShapeStage> {
            await CaptureGrowthShapeStageAsync(client, database, "Startup", 0, 0, [])
        };
        for (int index = 0; index < batchSizes.Count; index++) {
            IReadOnlyList<SampleCandidate> samples = manifest.Samples.Take(batchSizes[index]).ToArray();
            await ClearIsolatedResourcesAsync(mediaStorage, processRoot);
            await ResetProviderMetadataAsync(database, samples);
            long previousTask = await ScalarLongAsync(database, "SELECT COALESCE(MAX(Id),0) FROM Tasks");
            await PostJsonAsync(client, "/api/videos/batch/sync", samples.Select(value => value.MovieId).ToArray());
            IReadOnlyList<TaskRow> tasks = await WaitForHeapWalkTasksAsync(database, previousTask, samples.Count,
                TimeSpan.FromMinutes(90), bridge);
            result.Add(await CaptureGrowthShapeStageAsync(client, database, $"Batch{index + 1}_{samples.Count}",
                index + 1, samples.Count, tasks));
        }
        return result;
    }
    finally { try { if (!bridge.HasExited) { bridge.CloseMainWindow(); if (!bridge.WaitForExit(5000)) bridge.Kill(true); } } catch { } }
}

async Task<GrowthShapeStage> RunGrowthShapeProcessAsync(string runRoot, string repo, string baseline, SampleManifest manifest,
    int sampleCount, int batchIndex, int port, bool downloadImages, bool startupOnly)
{
    string processRoot = Path.Combine(runRoot, $"fresh-{batchIndex}-{sampleCount}");
    string database = Path.Combine(processRoot, "data", "LocalMediaManager.db");
    string mediaStorage = Path.Combine(processRoot, "MediaStorage");
    string logs = Path.Combine(processRoot, "logs");
    Directory.CreateDirectory(Path.GetDirectoryName(database)!);
    Directory.CreateDirectory(mediaStorage);
    Directory.CreateDirectory(logs);
    await BackupAsync(baseline, database);
    await ConfigureIsolationAsync(database, mediaStorage, downloadImages);
    IReadOnlyList<SampleCandidate> samples = manifest.Samples.Take(sampleCount).ToArray();
    await ResetProviderMetadataAsync(database, samples);
    string bridgeExe = Path.Combine(repo, "backend", "LocalMediaManager.Bridge", "bin", "Release", "net8.0", "LocalMediaManager.Bridge.exe");
    if (!File.Exists(bridgeExe)) throw new FileNotFoundException("Build the Release Bridge before running validation.", bridgeExe);
    string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    string bridgeUrl = $"http://127.0.0.1:{port}";
    using Process bridge = StartBridge(bridgeExe, database, processRoot, mediaStorage, bridgeUrl, token, logs,
        enableValidationDiagnostics: true, validationImageMode: "Full");
    using var client = new HttpClient { BaseAddress = new Uri(bridgeUrl), Timeout = TimeSpan.FromSeconds(90) };
    client.DefaultRequestHeaders.Add("X-LMM-Session", token);
    try {
        await WaitForBridgeAsync(client, bridge, TimeSpan.FromSeconds(30));
        if (startupOnly) return await CaptureGrowthShapeStageAsync(client, database, "Startup", 0, 0, []);
        long previousTask = await ScalarLongAsync(database, "SELECT COALESCE(MAX(Id),0) FROM Tasks");
        await PostJsonAsync(client, "/api/videos/batch/sync", samples.Select(value => value.MovieId).ToArray());
        IReadOnlyList<TaskRow> tasks = await WaitForHeapWalkTasksAsync(database, previousTask, samples.Count,
            TimeSpan.FromMinutes(90), bridge);
        return await CaptureGrowthShapeStageAsync(client, database, $"Fresh{batchIndex}_{sampleCount}", batchIndex, sampleCount, tasks);
    }
    finally { try { if (!bridge.HasExited) { bridge.CloseMainWindow(); if (!bridge.WaitForExit(5000)) bridge.Kill(true); } } catch { } }
}

async Task<GrowthShapeStage> CaptureGrowthShapeStageAsync(HttpClient client, string database, string label, int batchIndex,
    int sampleCount, IReadOnlyList<TaskRow> tasks)
{
    await Task.Delay(TimeSpan.FromSeconds(30));
    var snapshots = new List<MemoryRuntimeSnapshot>();
    await CaptureMemorySnapshotAsync(client, database, snapshots, label + "FullGc", tasks.Count(value => Terminal(value.Status)), true, captureNativeHeap: true);
    await Task.Delay(TimeSpan.FromSeconds(30));
    await CaptureMemorySnapshotAsync(client, database, snapshots, label + "Settled", tasks.Count(value => Terminal(value.Status)), false, captureNativeHeap: true);
    BridgeRuntimeMemorySnapshot bridge = snapshots[^1].Bridge;
    ImagePipelineValidationCounters? counters = bridge.ImagePipeline;
    BridgeNativeHeapSnapshot? native = bridge.NativeHeap;
    long privateCommit = bridge.VirtualMemorySummaries.Where(value => value.Type == "Private").Sum(value => value.CommittedBytes);
    return new(label, batchIndex, sampleCount, tasks.Count(value => value.Status == "Completed"),
        tasks.Count(value => value.Status == "CompletedWithWarnings"), tasks.Count(value => value.Status == "NoResult"),
        tasks.Count(value => value.Status == "Failed"), tasks.Count(value => value.Status == "Cancelled"),
        counters?.DownloadedImages ?? 0, counters?.ReadBytes ?? 0, counters?.WrittenImages ?? 0,
        bridge.PrivateMemoryBytes, privateCommit, bridge.ManagedHeapBytes, bridge.LargeObjectHeapBytes,
        bridge.PinnedObjectHeapBytes, bridge.HandleCount, bridge.ThreadCount, native?.BusyBytes,
        native?.BusyEntries, native?.FreeBytes, native?.Complete == true && native.WalkSucceeded && native.BusyBytes >= 0,
        counters, snapshots);
}

async Task EnsureLocalMetaTubeProviderAsync()
{
    if (await IsTcpPortOpenAsync("127.0.0.1", 8080)) return;
    const string dockerDesktop = @"C:\Program Files\Docker\Docker\Docker Desktop.exe";
    if (!File.Exists(dockerDesktop))
        throw new FileNotFoundException("Docker Desktop is required for the isolated local MetaTube provider.", dockerDesktop);
    Process.Start(new ProcessStartInfo { FileName = dockerDesktop, UseShellExecute = true });

    Stopwatch timer = Stopwatch.StartNew();
    while (timer.Elapsed < TimeSpan.FromMinutes(3)) {
        if (await TryRunDockerAsync("info")) {
            await TryRunDockerAsync("start metatube");
            string mdcContainers = await RunDockerOutputAsync("ps -aq --filter ancestor=mdcng/mdc:latest");
            foreach (string container in mdcContainers.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                await TryRunDockerAsync($"start {container}");
            if (await IsTcpPortOpenAsync("127.0.0.1", 8080)) return;
        }
        await Task.Delay(TimeSpan.FromSeconds(3));
    }
    throw new TimeoutException("Docker Desktop started, but the local MetaTube container did not expose 127.0.0.1:8080 within three minutes.");
}

async Task<bool> IsTcpPortOpenAsync(string host, int port)
{
    using var client = new TcpClient();
    try {
        await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(2));
        return true;
    }
    catch (SocketException) { return false; }
    catch (TimeoutException) { return false; }
}

async Task<bool> TryRunDockerAsync(string arguments)
{
    try {
        using Process process = Process.Start(new ProcessStartInfo {
            FileName = "docker", Arguments = arguments, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start Docker CLI.");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        return process.ExitCode == 0;
    }
    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or TimeoutException) { return false; }
}

async Task<string> RunDockerOutputAsync(string arguments)
{
    try {
        using Process process = Process.Start(new ProcessStartInfo {
            FileName = "docker", Arguments = arguments, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start Docker CLI.");
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        return process.ExitCode == 0 ? output : string.Empty;
    }
    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or TimeoutException) { return string.Empty; }
}

async Task<int> HeapWalkAsync(string[] input)
{
    if (input.Length is < 5 or > 6) return Usage();
    string root = Path.GetFullPath(input[1]);
    string repo = Path.GetFullPath(input[2]);
    string runLabel = input[3].Trim();
    string requestedMode = input[4].Trim();
    bool downloadImages = !requestedMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
    string imageMode = downloadImages && Enum.TryParse<ImagePipelineValidationMode>(requestedMode, true, out ImagePipelineValidationMode parsedMode)
        ? parsedMode.ToString()
        : "Full";
    if (!requestedMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) && !Enum.TryParse<ImagePipelineValidationMode>(requestedMode, true, out _)) return Usage();
    int port = input.Length == 6 && int.TryParse(input[5], out int parsed) ? parsed : 48120;
    if (string.IsNullOrWhiteSpace(runLabel) || runLabel.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        throw new ArgumentException("Run label is not a valid directory name.");

    string baseline = Path.Combine(root, "baseline", "LocalMediaManager.db");
    string manifestPath = Path.Combine(root, "manifests", "sample-20.json");
    if (!File.Exists(baseline) || !File.Exists(manifestPath)) throw new InvalidOperationException("Run prepare first.");
    SampleManifest manifest = JsonSerializer.Deserialize<SampleManifest>(await File.ReadAllTextAsync(manifestPath), jsonOptions)
        ?? throw new InvalidDataException("Sample manifest could not be read.");
    string runRoot = Path.Combine(root, runLabel);
    if (Directory.Exists(runRoot) && Directory.EnumerateFileSystemEntries(runRoot).Any())
        throw new InvalidOperationException($"HeapWalk validation root already contains evidence: {runRoot}");
    string database = Path.Combine(runRoot, "data", "LocalMediaManager.db");
    string evidence = Path.Combine(runRoot, "evidence");
    string logs = Path.Combine(runRoot, "logs");
    string mediaStorage = Path.Combine(runRoot, "MediaStorage");
    Directory.CreateDirectory(Path.GetDirectoryName(database)!);
    Directory.CreateDirectory(evidence);
    Directory.CreateDirectory(logs);
    Directory.CreateDirectory(mediaStorage);
    await BackupAsync(baseline, database);
    await ConfigureIsolationAsync(database, mediaStorage, downloadImages);
    await ManifestAsync(["manifest", manifestPath, Path.Combine(evidence, "stage3-manifest.json")]);
    IReadOnlyList<MovieSnapshot> source = await SnapshotAsync(database, manifest.Samples);
    await WriteJsonAsync(Path.Combine(evidence, "source-before-reset.json"), source);
    await ResetProviderMetadataAsync(database, manifest.Samples);
    long previousMaxTask = await ScalarLongAsync(database, "SELECT COALESCE(MAX(Id),0) FROM Tasks");

    string bridgeExe = Path.Combine(repo, "backend", "LocalMediaManager.Bridge", "bin", "Release", "net8.0", "LocalMediaManager.Bridge.exe");
    if (!File.Exists(bridgeExe)) throw new FileNotFoundException("Build the Release Bridge before running validation.", bridgeExe);
    string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    string bridgeUrl = $"http://127.0.0.1:{port}";
    using Process bridge = StartBridge(bridgeExe, database, runRoot, mediaStorage, bridgeUrl, token, logs,
        enableValidationDiagnostics: true, validationImageMode: imageMode);
    using var client = new HttpClient { BaseAddress = new Uri(bridgeUrl), Timeout = TimeSpan.FromSeconds(90) };
    client.DefaultRequestHeaders.Add("X-LMM-Session", token);
    try
    {
        await WaitForBridgeAsync(client, bridge, TimeSpan.FromSeconds(30));
        await Task.Delay(TimeSpan.FromSeconds(30));
        var snapshots = new List<MemoryRuntimeSnapshot>();
        await CaptureMemorySnapshotAsync(client, database, snapshots, "Startup", 0, false, captureNativeHeap: true);
        await PostJsonAsync(client, "/api/videos/batch/sync", manifest.Samples.Select(value => value.MovieId).ToArray());
        IReadOnlyList<TaskRow> tasks = await WaitForHeapWalkTasksAsync(database, previousMaxTask, manifest.Samples.Count,
            TimeSpan.FromMinutes(90), bridge);
        await Task.Delay(TimeSpan.FromSeconds(30));
        await CaptureMemorySnapshotAsync(client, database, snapshots, "FullGc", tasks.Count(value => Terminal(value.Status)), true, captureNativeHeap: true);
        await Task.Delay(TimeSpan.FromSeconds(30));
        await CaptureMemorySnapshotAsync(client, database, snapshots, "AfterGc", tasks.Count(value => Terminal(value.Status)), false, captureNativeHeap: true);
        await CaptureMemorySnapshotAsync(client, database, snapshots, "AfterGcHeapWalkRepeat", tasks.Count(value => Terminal(value.Status)), false, captureNativeHeap: true);
        string integrity = await ScalarTextAsync(database, "PRAGMA integrity_check") ?? "unknown";
        long foreignKeys = await ScalarLongAsync(database, "SELECT COUNT(*) FROM pragma_foreign_key_check");
        ImagePipelineValidationCounters? counters = snapshots.LastOrDefault()?.Bridge.ImagePipeline;
        var result = new HeapWalkRunResult(runLabel, requestedMode, manifest.Samples.Count,
            tasks.Count(value => value.Status == "Completed"), tasks.Count(value => value.Status == "CompletedWithWarnings"),
            tasks.Count(value => value.Status == "NoResult"), tasks.Count(value => value.Status == "Failed"),
            integrity, foreignKeys, counters, snapshots);
        await WriteJsonAsync(Path.Combine(evidence, "heapwalk-snapshots.json"), snapshots);
        await WriteJsonAsync(Path.Combine(evidence, "heapwalk-result.json"), result);
        await WriteJsonAsync(Path.Combine(evidence, "tasks.json"), tasks);
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
        return integrity == "ok" && foreignKeys == 0 && tasks.All(value => value.Status is not "Failed" and not "Cancelled") ? 0 : 4;
    }
    finally
    {
        try { if (!bridge.HasExited) { bridge.CloseMainWindow(); if (!bridge.WaitForExit(5000)) bridge.Kill(true); } }
        catch { }
    }
}

async Task<IReadOnlyList<TaskRow>> WaitForHeapWalkTasksAsync(string database, long previousMaxTask, int expected,
    TimeSpan timeout, Process bridge)
{
    Stopwatch timer = Stopwatch.StartNew();
    while (timer.Elapsed < timeout)
    {
        IReadOnlyList<TaskRow> tasks = await ReadTasksAsync(database, previousMaxTask);
        if (tasks.Count == expected && tasks.All(value => Terminal(value.Status))) return tasks;
        if (bridge.HasExited) throw new InvalidOperationException($"Bridge exited during HeapWalk validation: {bridge.ExitCode}");
        await Task.Delay(1000);
    }
    throw new TimeoutException($"HeapWalk validation did not reach {expected} terminal tasks within {timeout}.");
}

async Task<MemoryTaskWaitResult> WaitForMemoryTasksAsync(string database, long previousMaxTask, int expected,
    TimeSpan timeout, Process bridge, HttpClient client, List<MemoryRuntimeSnapshot> snapshots, string batch)
{
    Stopwatch timer = Stopwatch.StartNew();
    var captured = new HashSet<int>();
    int[] milestones = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100];
    while (timer.Elapsed < timeout) {
        IReadOnlyList<TaskRow> tasks = await ReadTasksAsync(database, previousMaxTask);
        int complete = tasks.Count(value => Terminal(value.Status));
        foreach (int milestone in milestones.Where(value => complete >= value && captured.Add(value)))
            await CaptureMemorySnapshotAsync(client, database, snapshots, $"{batch}After{milestone}", complete, false);
        if (tasks.Count == expected && complete == expected) {
            await CaptureMemorySnapshotAsync(client, database, snapshots, $"{batch}Completed", complete, false);
            await Task.Delay(TimeSpan.FromSeconds(30));
            await CaptureMemorySnapshotAsync(client, database, snapshots, $"{batch}Idle30s", complete, false);
            await Task.Delay(TimeSpan.FromSeconds(30));
            await CaptureMemorySnapshotAsync(client, database, snapshots, $"{batch}Idle60s", complete, false);
            await CaptureMemorySnapshotAsync(client, database, snapshots, $"{batch}AfterFullGc", complete, true);
            await Task.Delay(TimeSpan.FromSeconds(30));
            await CaptureMemorySnapshotAsync(client, database, snapshots, $"{batch}AfterFullGcIdle30s", complete, false);
            await CaptureMemorySnapshotAsync(client, database, snapshots, $"{batch}AfterSqlitePoolClear", complete, false, clearSqlitePools: true);
            await Task.Delay(TimeSpan.FromSeconds(30));
            await CaptureMemorySnapshotAsync(client, database, snapshots, $"{batch}AfterSqlitePoolClearIdle30s", complete, false);
            return new(tasks, timer.Elapsed);
        }
        if (bridge.HasExited) throw new InvalidOperationException($"Bridge exited during {batch}: {bridge.ExitCode}");
        await Task.Delay(1000);
    }
    throw new TimeoutException($"{batch} did not reach {expected} terminal tasks within {timeout}.");
}

async Task CaptureMemorySnapshotAsync(HttpClient client, string database, List<MemoryRuntimeSnapshot> snapshots,
    string label, int completedTasks, bool fullGc, bool clearSqlitePools = false, bool captureNativeHeap = false)
{
    bool includeNativeHeap = captureNativeHeap || label.Contains("AfterFullGc", StringComparison.Ordinal);
    JsonElement json = await PostJsonAsync(client, $"/api/validation/runtime-snapshot?fullGc={fullGc.ToString().ToLowerInvariant()}&clearSqlitePools={clearSqlitePools.ToString().ToLowerInvariant()}&includeNativeHeap={includeNativeHeap.ToString().ToLowerInvariant()}", new { });
    BridgeRuntimeMemorySnapshot bridge = JsonSerializer.Deserialize<BridgeRuntimeMemorySnapshot>(json.GetRawText(), jsonOptions)
        ?? throw new InvalidDataException("Bridge runtime diagnostics returned an invalid payload.");
    long taskResults = await ScalarLongAsync(database, "SELECT COUNT(*) FROM Tasks WHERE TaskType='Sync'");
    long logs = await ScalarLongAsync(database, "SELECT COUNT(*) FROM TaskLogs");
    long resources = await ScalarLongAsync(database, "SELECT (SELECT COUNT(*) FROM Images) + (SELECT COUNT(*) FROM NfoDocuments)");
    snapshots.Add(new(label, completedTasks, bridge, taskResults, logs, resources,
        null, null, null, "Active HTTP requests, SQLite connections, and open streams are not exposed by the current runtime APIs; they are recorded as unavailable rather than inferred."));
}

Task ClearIsolatedResourcesAsync(string mediaStorage, string runRoot)
{
    string root = Path.GetFullPath(runRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string target = Path.GetFullPath(mediaStorage).TrimEnd(Path.DirectorySeparatorChar);
    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Refusing to clear a resource path outside the isolated validation root.");
    if (Directory.Exists(target)) Directory.Delete(target, true);
    Directory.CreateDirectory(target);
    return Task.CompletedTask;
}

async Task<List<SampleCandidate>> ReadCandidatesAsync(string database, IMovieNumberExtractor extractor)
{
    var result = new List<SampleCandidate>();
    await using SqliteConnection connection = await OpenAsync(database, true);
    bool hasDirectors = await TableExistsAsync(connection, "MovieDirectors");
    await using SqliteCommand command = connection.CreateCommand();
    command.CommandText = $"""
        SELECT m.Id,trim(m.Code),f.FilePath,
               CASE WHEN trim(m.Code)<>upper(trim(m.Code)) THEN 1 ELSE 0 END,
               (SELECT COUNT(*) FROM MovieSeries x WHERE x.MovieId=m.Id),
               {(hasDirectors ? "(SELECT COUNT(*) FROM MovieDirectors x WHERE x.MovieId=m.Id)" : "0")},
               (SELECT COUNT(*) FROM MovieActors x JOIN Actors a ON a.Id=x.ActorId WHERE x.MovieId=m.Id),
               (SELECT COUNT(*) FROM MovieGenres x JOIN Genres g ON g.Id=x.GenreId WHERE x.MovieId=m.Id),
               (SELECT COUNT(*) FROM Images x WHERE x.MovieId=m.Id AND x.ImageType='Preview'),
               (SELECT COUNT(*) FROM Images x WHERE x.MovieId=m.Id AND x.ImageType='Poster'),
               (CASE WHEN trim(COALESCE(m.Title,''))<>'' AND m.Title<>m.Code THEN 1 ELSE 0 END
                +CASE WHEN trim(COALESCE(m.Description,''))<>'' THEN 1 ELSE 0 END
                +CASE WHEN trim(COALESCE(m.ReleaseDate,''))<>'' THEN 1 ELSE 0 END
                +CASE WHEN m.DurationSeconds>0 THEN 1 ELSE 0 END
                +CASE WHEN EXISTS(SELECT 1 FROM MovieActors x WHERE x.MovieId=m.Id) THEN 1 ELSE 0 END
                +CASE WHEN EXISTS(SELECT 1 FROM MovieGenres x WHERE x.MovieId=m.Id) THEN 1 ELSE 0 END
                +CASE WHEN EXISTS(SELECT 1 FROM MovieStudios x WHERE x.MovieId=m.Id) THEN 1 ELSE 0 END
                +CASE WHEN EXISTS(SELECT 1 FROM MovieSeries x WHERE x.MovieId=m.Id) THEN 1 ELSE 0 END),
               COALESCE((SELECT IsFavorite FROM UserMovieState x WHERE x.MovieId=m.Id),0),
               COALESCE((SELECT HasUserRating FROM UserMovieState x WHERE x.MovieId=m.Id),0),
               (SELECT COUNT(*) FROM MovieTags x WHERE x.MovieId=m.Id),
               (SELECT COUNT(*) FROM PlayHistory x WHERE x.MovieId=m.Id),
               (SELECT COUNT(*) FROM Images x WHERE x.MovieId=m.Id AND (x.IsLocked=1 OR x.Ownership='User')),
               (SELECT COUNT(*) FROM NfoDocuments x WHERE x.MovieId=m.Id AND (x.IsLocked=1 OR x.Ownership='User'))
          FROM Movies m
          JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
          JOIN Libraries l ON l.Id=f.LibraryId AND l.LibraryType='Standard' AND l.IsEnabled=1
         WHERE COALESCE(f.ExistsState,'')<>'Missing' AND trim(COALESCE(m.Code,''))<>''
         ORDER BY m.Id
        """;
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) {
        long id = reader.GetInt64(0);
        string code = reader.GetString(1);
        string path = reader.GetString(2);
        MovieNumberExtractionResult extraction = extractor.Extract(Path.GetFileName(path));
        if (string.IsNullOrWhiteSpace(extraction.NormalizedNumber)
            || extraction.Confidence < extractor.MinimumAutoSyncConfidence
            || !extraction.NormalizedNumber.Equals(code, StringComparison.OrdinalIgnoreCase)) continue;
        result.Add(new(id, code, path, File.Exists(path), reader.GetInt64(3) == 1, code.Count(char.IsDigit),
            reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9),
            reader.GetInt32(10), reader.GetInt64(11) == 1, reader.GetInt64(12) == 1, reader.GetInt32(13), reader.GetInt32(14),
            reader.GetInt32(15), reader.GetInt32(16), StableOrder(id, code)));
    }
    return result;
}

List<SampleCandidate> SelectFixedSamples(List<SampleCandidate> candidates, int count)
{
    var selected = new List<SampleCandidate>();
    var used = new HashSet<long>();
    Func<SampleCandidate, bool>[] coverage = [
        value => value.LowercaseCode,
        value => value.DigitCount >= 4,
        value => value.SeriesCount > 0,
        value => value.SeriesCount == 0,
        value => value.DirectorCount > 0,
        value => value.DirectorCount == 0,
        value => value.ActorCount == 1,
        value => value.ActorCount > 1,
        value => value.GenreCount is > 0 and <= 3,
        value => value.GenreCount >= 8,
        value => value.PreviewCount is > 0 and <= 3,
        value => value.PreviewCount >= 8,
        value => value.PosterCount > 0,
        value => value.PosterCount == 0,
        value => value.ProviderFieldCount is > 0 and < 8,
        value => value.ProviderFieldCount == 0,
        value => value.Favorite,
        value => value.HasUserRating,
        value => value.UserTagCount > 0,
        value => value.PlayHistoryCount > 0,
        value => value.LockedImageCount > 0,
        value => value.LockedNfoCount > 0,
        value => value.MediaPathAccessible,
        value => !value.MediaPathAccessible,
    ];
    foreach (Func<SampleCandidate, bool> criterion in coverage) {
        SampleCandidate? match = candidates.Where(value => !used.Contains(value.MovieId) && criterion(value))
            .OrderBy(value => value.StableOrder).FirstOrDefault();
        if (match is not null) { selected.Add(match); used.Add(match.MovieId); }
    }
    selected.AddRange(candidates.Where(value => !used.Contains(value.MovieId)).OrderBy(value => value.StableOrder).Take(count - selected.Count));
    return selected.Take(count).ToList();
}

async Task ConfigureIsolationAsync(string database, string mediaStorage, bool downloadImages = true)
{
    await using SqliteConnection connection = await OpenAsync(database, false);
    string at = DateTimeOffset.UtcNow.ToString("O");
    int validationImageTimeout = int.TryParse(Environment.GetEnvironmentVariable("LMM_VALIDATION_IMAGE_TIMEOUT_SECONDS"), out int parsedTimeout)
        ? Math.Clamp(parsedTimeout, 1, 60)
        : 60;
    var values = new Dictionary<string, (object Value, string Type)> {
        ["metadata.metatube.enabled"] = (true, "boolean"),
        ["metadata.metatube.baseUrl"] = ("http://127.0.0.1:8080/", "string"),
        ["metadata.metatube.timeoutSeconds"] = (validationImageTimeout, "integer"),
        ["metadata.metatube.downloadImages"] = (downloadImages, "boolean"),
        ["metadata.metatube.writeNfo"] = (true, "boolean"),
        ["metadata.metatube.autoExecute"] = (true, "boolean"),
        ["metadata.metatube.nonDestructive"] = (true, "boolean"),
        ["metadata.mdcNg.enabled"] = (false, "boolean"),
        ["metadata.javbus.enabled"] = (true, "boolean"),
        ["metadata.javbus.downloadImages"] = (downloadImages, "boolean"),
        ["metadata.javbus.fillMissingOnly"] = (true, "boolean"),
        ["metadata.dmm.enabled"] = (false, "boolean"),
        ["metadata.javdb.enabled"] = (false, "boolean"),
        ["mediaStorage.rootPath"] = (mediaStorage, "string"),
        ["mediaStorage.directory.posters"] = ("Posters", "string"),
        ["mediaStorage.directory.thumbnails"] = ("Thumbnails", "string"),
        ["mediaStorage.directory.fanart"] = ("Fanart", "string"),
        ["mediaStorage.directory.wallCrops"] = ("WallCrops", "string"),
        ["mediaStorage.directory.gif"] = ("Gif", "string"),
        ["mediaStorage.directory.nfo"] = ("Nfo", "string"),
        ["nfo.export.outputDirectory"] = (Path.Combine(mediaStorage, "Nfo"), "string"),
        ["nfo.export.policy"] = ("SkipExisting", "string"),
    };
    foreach ((string key, (object value, string type)) in values) {
        await ExecuteAsync(connection, """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES($key,$value,$type,$at)
            ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType=excluded.ValueType,UpdatedAt=excluded.UpdatedAt
            """, ("$key", key), ("$value", JsonSerializer.Serialize(value)), ("$type", type), ("$at", at));
    }
}

async Task ResetProviderMetadataAsync(string database, IReadOnlyList<SampleCandidate> samples)
{
    await using SqliteConnection connection = await OpenAsync(database, false);
    await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    foreach (SampleCandidate sample in samples) {
        foreach (string relation in new[] { "MovieActors", "MovieGenres", "MovieSeries", "MovieStudios", "MovieDirectors" }) {
            if (await TableExistsAsync(connection, relation, transaction))
                await ExecuteTransactionAsync(connection, transaction, $"DELETE FROM {relation} WHERE MovieId=$movie", ("$movie", sample.MovieId));
        }
        await ExecuteTransactionAsync(connection, transaction, "DELETE FROM ExternalIds WHERE EntityType='Movie' AND EntityId=$movie", ("$movie", sample.MovieId));
        await ExecuteTransactionAsync(connection, transaction, "DELETE FROM Images WHERE MovieId=$movie AND COALESCE(IsLocked,0)=0 AND COALESCE(Ownership,'Provider')<>'User'", ("$movie", sample.MovieId));
        await ExecuteTransactionAsync(connection, transaction, "DELETE FROM NfoDocuments WHERE MovieId=$movie AND COALESCE(IsLocked,0)=0 AND COALESCE(Ownership,'Provider')<>'User'", ("$movie", sample.MovieId));
        await ExecuteTransactionAsync(connection, transaction, """
            UPDATE Movies SET Title=Code,OriginalTitle=NULL,SortTitle=Code,Description=NULL,ReleaseDate=NULL,
                   DurationSeconds=0,ProviderRating=NULL,NfoPath=NULL,IsScraped=0,ScrapeStatus='pending',UpdatedAt=$at
             WHERE Id=$movie
            """, ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$movie", sample.MovieId));
    }
    await transaction.CommitAsync();
}

async Task<IReadOnlyList<MovieSnapshot>> SnapshotAsync(string database, IReadOnlyList<SampleCandidate> samples)
{
    var result = new List<MovieSnapshot>();
    await using SqliteConnection connection = await OpenAsync(database, true);
    foreach (SampleCandidate sample in samples) {
        string userHash = await QueryHashAsync(connection, "SELECT * FROM UserMovieState WHERE MovieId=$movie ORDER BY MovieId", sample.MovieId);
        string favoriteHash = await QueryHashAsync(connection, "SELECT IsFavorite FROM UserMovieState WHERE MovieId=$movie", sample.MovieId);
        string ratingHash = await QueryHashAsync(connection, "SELECT UserRating,HasUserRating FROM UserMovieState WHERE MovieId=$movie", sample.MovieId);
        string watchHash = await QueryHashAsync(connection, "SELECT PlayCount,LastPlayedAt,LastPositionSeconds FROM UserMovieState WHERE MovieId=$movie", sample.MovieId);
        string notesHash = await QueryHashAsync(connection, "SELECT Notes FROM UserMovieState WHERE MovieId=$movie", sample.MovieId);
        string tagsHash = await QueryHashAsync(connection, "SELECT * FROM MovieTags WHERE MovieId=$movie ORDER BY TagId", sample.MovieId);
        string playHash = await QueryHashAsync(connection, "SELECT * FROM PlayHistory WHERE MovieId=$movie ORDER BY Id", sample.MovieId);
        string mediaHash = await QueryHashAsync(connection, "SELECT * FROM MediaFiles WHERE MovieId=$movie ORDER BY Id", sample.MovieId);
        string lockedImagesHash = await QueryHashAsync(connection, "SELECT * FROM Images WHERE MovieId=$movie AND (IsLocked=1 OR Ownership='User') ORDER BY Id", sample.MovieId);
        string lockedNfoHash = await QueryHashAsync(connection, "SELECT * FROM NfoDocuments WHERE MovieId=$movie AND (IsLocked=1 OR Ownership='User') ORDER BY Id", sample.MovieId);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT trim(COALESCE(m.Code,'')),trim(COALESCE(m.Title,'')),trim(COALESCE(m.Description,'')),
                   trim(COALESCE(m.ReleaseDate,'')),m.DurationSeconds,trim(COALESCE(m.NfoPath,'')),
                   (SELECT COUNT(*) FROM MovieActors x JOIN Actors a ON a.Id=x.ActorId WHERE x.MovieId=m.Id),
                   (SELECT COUNT(*) FROM MovieGenres x JOIN Genres g ON g.Id=x.GenreId WHERE x.MovieId=m.Id),
                   (SELECT COUNT(*) FROM MovieDirectors x JOIN Directors d ON d.Id=x.DirectorId WHERE x.MovieId=m.Id),
                   (SELECT COUNT(*) FROM MovieStudios x JOIN Studios s ON s.Id=x.StudioId WHERE x.MovieId=m.Id),
                   (SELECT COUNT(*) FROM MovieSeries x JOIN Series s ON s.Id=x.SeriesId WHERE x.MovieId=m.Id),
                   (SELECT COUNT(*) FROM Images x WHERE x.MovieId=m.Id AND x.ImageType='Poster'),
                   (SELECT COUNT(*) FROM Images x WHERE x.MovieId=m.Id AND x.ImageType IN ('Fanart','BigPic')),
                   (SELECT COUNT(*) FROM Images x WHERE x.MovieId=m.Id AND x.ImageType='Preview'),
                   (SELECT COUNT(*) FROM NfoDocuments x WHERE x.MovieId=m.Id)
              FROM Movies m WHERE m.Id=$movie
            """;
        command.Parameters.AddWithValue("$movie", sample.MovieId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException($"Movie vanished from isolated database: {sample.MovieId}");
        result.Add(new(sample.MovieId, reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetString(5),
            reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14),
            userHash, favoriteHash, ratingHash, watchHash, notesHash, tagsHash, playHash, mediaHash, lockedImagesHash, lockedNfoHash));
    }
    return result;
}

async Task<BatchVerification> VerifyBatchAsync(string baseline, string database, string mediaStorage,
    IReadOnlyList<MovieSnapshot> source, IReadOnlyList<MovieSnapshot> before, IReadOnlyList<MovieSnapshot> after,
    IReadOnlyList<TaskRow> tasks, IReadOnlyList<TaskLogRow> logs, TimeSpan elapsed, string integrity, long foreignKeys)
{
    int completed = tasks.Count(value => value.Status == "Completed");
    int warnings = tasks.Count(value => value.Status == "CompletedWithWarnings");
    int failed = tasks.Count(value => value.Status == "Failed");
    int noMatch = tasks.Count(value => value.Status == "NoResult");
    int blocked = tasks.Count(value => value.Status == "Blocked");
    int cancelled = tasks.Count(value => value.Status == "Cancelled");
    int protectedChanges = source.Zip(after).Count(pair =>
        pair.First.UserHash != pair.Second.UserHash || pair.First.UserTagsHash != pair.Second.UserTagsHash
        || pair.First.PlayHistoryHash != pair.Second.PlayHistoryHash || pair.First.MediaFilesHash != pair.Second.MediaFilesHash
        || pair.First.LockedImagesHash != pair.Second.LockedImagesHash || pair.First.LockedNfoHash != pair.Second.LockedNfoHash);
    int metadataSuccess = after.Count(value => !string.IsNullOrWhiteSpace(value.Title) && value.Title != value.Code && value.ActorCount > 0 && value.GenreCount > 0);
    int poster = after.Count(value => value.PosterCount > 0);
    int fanart = after.Count(value => value.FanartCount > 0);
    int preview = after.Count(value => value.PreviewCount > 0);
    int nfo = after.Count(value => value.NfoCount > 0 && !string.IsNullOrWhiteSpace(value.NfoPath) && File.Exists(value.NfoPath));
    OptionalFieldStatistics optionalFields = await BuildOptionalFieldStatisticsAsync(database, source, after, tasks);
    string ids = string.Join(',', after.Select(value => value.MovieId));
    long baselineDuplicateActors = await ScalarLongAsync(baseline, "SELECT COUNT(*) FROM (SELECT NormalizedName,COUNT(*) c FROM Actors WHERE trim(COALESCE(NormalizedName,''))<>'' GROUP BY NormalizedName HAVING c>1)");
    long baselineDuplicateGenres = await ScalarLongAsync(baseline, "SELECT COUNT(*) FROM (SELECT NormalizedName,COUNT(*) c FROM Genres WHERE trim(COALESCE(NormalizedName,''))<>'' GROUP BY NormalizedName HAVING c>1)");
    long duplicateActors = Math.Max(0, await ScalarLongAsync(database, "SELECT COUNT(*) FROM (SELECT NormalizedName,COUNT(*) c FROM Actors WHERE trim(COALESCE(NormalizedName,''))<>'' GROUP BY NormalizedName HAVING c>1)") - baselineDuplicateActors);
    long duplicateGenres = Math.Max(0, await ScalarLongAsync(database, "SELECT COUNT(*) FROM (SELECT NormalizedName,COUNT(*) c FROM Genres WHERE trim(COALESCE(NormalizedName,''))<>'' GROUP BY NormalizedName HAVING c>1)") - baselineDuplicateGenres);
    long duplicateMovieActors = await ScalarLongAsync(database, $"SELECT COUNT(*) FROM (SELECT MovieId,ActorId,RoleName,COUNT(*) c FROM MovieActors WHERE MovieId IN ({ids}) GROUP BY MovieId,ActorId,RoleName HAVING c>1)");
    long duplicateMovieGenres = await ScalarLongAsync(database, $"SELECT COUNT(*) FROM (SELECT MovieId,GenreId,COUNT(*) c FROM MovieGenres WHERE MovieId IN ({ids}) GROUP BY MovieId,GenreId HAVING c>1)");
    long duplicateResources = await ScalarLongAsync(database, $"SELECT COUNT(*) FROM (SELECT MovieId,ImageType,lower(FilePath),COUNT(*) c FROM Images WHERE MovieId IN ({ids}) GROUP BY MovieId,ImageType,lower(FilePath) HAVING c>1)");
    long missingResources = await MissingRegisteredResourcesAsync(database, after.Select(value => value.MovieId).ToArray());
    long unregisteredFiles = await CountUnregisteredFilesAsync(database, mediaStorage);
    double[] latencies = tasks.Select(value => value.ElapsedMilliseconds).Where(value => value > 0).Order().ToArray();
    double p95 = Percentile(latencies, 0.95);
    int metaTubeCalls = logs.Count(value => value.Message.Equals("[MetaTube] 开始", StringComparison.OrdinalIgnoreCase));
    int javBusCalls = logs.Count(value => value.Message.Equals("[JavBus] 开始", StringComparison.OrdinalIgnoreCase));
    int challenges = logs.Count(value => value.Message.Contains("ChallengePage", StringComparison.OrdinalIgnoreCase) || value.Message.Contains("driver-verify", StringComparison.OrdinalIgnoreCase));
    int rateLimits = logs.Count(value => value.Message.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase)
        || value.Message.Contains("RateLimited", StringComparison.OrdinalIgnoreCase)
        || value.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
    bool passed = completed + warnings >= Math.Ceiling(tasks.Count * 0.95)
        && failed <= 1 && protectedChanges == 0 && duplicateActors == 0 && duplicateGenres == 0
        && duplicateMovieActors == 0 && duplicateMovieGenres == 0
        && duplicateResources == 0 && unregisteredFiles == 0 && integrity == "ok" && foreignKeys == 0
        && optionalFields.Director.DataRegression == 0 && optionalFields.Series.DataRegression == 0
        && optionalFields.Director.WriteFailure == 0 && optionalFields.Series.WriteFailure == 0;
    return new(passed, tasks.Count, completed, warnings, noMatch, blocked, failed, cancelled,
        metadataSuccess, poster, fanart, preview, nfo, protectedChanges,
        duplicateActors, duplicateGenres, duplicateMovieActors, duplicateMovieGenres, duplicateResources,
        missingResources, unregisteredFiles, integrity, foreignKeys, elapsed.TotalMilliseconds,
        tasks.Count == 0 ? 0 : elapsed.TotalMilliseconds / tasks.Count, p95, metaTubeCalls, javBusCalls, challenges, rateLimits,
        optionalFields);
}

async Task<OptionalFieldStatistics> BuildOptionalFieldStatisticsAsync(string database,
    IReadOnlyList<MovieSnapshot> source, IReadOnlyList<MovieSnapshot> after, IReadOnlyList<TaskRow> tasks)
{
    var sourceProvided = new Dictionary<long, HashSet<string>>();
    await using (SqliteConnection connection = await OpenAsync(database, true)) {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT TaskId,AppliedJson FROM MetadataSyncSnapshots
             WHERE AppliedJson IS NOT NULL AND RolledBackAt IS NULL
             ORDER BY Id
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            long taskId = reader.GetInt64(0);
            string json = reader.GetString(1);
            HashSet<string> fields = ReadProvidedOptionalFields(json);
            if (fields.Count > 0) sourceProvided[taskId] = fields;
        }
    }

    var byMovie = tasks.GroupBy(value => value.MovieId).ToDictionary(group => group.Key,
        group => group.OrderByDescending(value => value.TaskId).First());
    OptionalFieldCounts Director() => ClassifyOptionalField("Director", source, after, byMovie, sourceProvided,
        value => value.DirectorCount);
    OptionalFieldCounts Series() => ClassifyOptionalField("Series", source, after, byMovie, sourceProvided,
        value => value.SeriesCount);
    return new(Director(), Series());
}

static HashSet<string> ReadProvidedOptionalFields(string json)
{
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    try {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!TryGetProperty(document.RootElement, "FieldSources", out JsonElement sources)
            || sources.ValueKind != JsonValueKind.Object) return result;
        foreach (JsonProperty field in sources.EnumerateObject())
            if (field.Name.Equals("Director", StringComparison.OrdinalIgnoreCase)
                || field.Name.Equals("Series", StringComparison.OrdinalIgnoreCase)) result.Add(field.Name);
    }
    catch (JsonException) { }
    return result;
}

static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
{
    foreach (JsonProperty property in element.EnumerateObject()) {
        if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
            value = property.Value;
            return true;
        }
    }
    value = default;
    return false;
}

static OptionalFieldCounts ClassifyOptionalField(string field, IReadOnlyList<MovieSnapshot> source,
    IReadOnlyList<MovieSnapshot> after, IReadOnlyDictionary<long, TaskRow> taskByMovie,
    IReadOnlyDictionary<long, HashSet<string>> sourceProvided, Func<MovieSnapshot, int> relationCount)
{
    int provided = 0;
    int written = 0;
    int notApplicable = 0;
    int writeFailure = 0;
    int dataRegression = 0;
    foreach (MovieSnapshot current in after) {
        bool fieldProvided = taskByMovie.TryGetValue(current.MovieId, out TaskRow? task)
            && task is not null
            && sourceProvided.TryGetValue(task.TaskId, out HashSet<string>? fields)
            && fields.Contains(field);
        if (fieldProvided) {
            provided++;
            if (relationCount(current) > 0) written++;
            else writeFailure++;
        }
        else {
            notApplicable++;
        }

        MovieSnapshot before = source.First(value => value.MovieId == current.MovieId);
        if (relationCount(before) > 0 && relationCount(current) == 0) dataRegression++;
    }
    return new(provided, written, notApplicable, writeFailure, dataRegression);
}

Process StartBridge(string executable, string database, string dataRoot, string mediaStorage, string url, string token,
    string logRoot, bool enableValidationDiagnostics = false, string? validationImageMode = null)
{
    var start = new ProcessStartInfo(executable) {
        WorkingDirectory = Path.GetDirectoryName(executable)!,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    start.Environment["LMM_DATABASE_PATH"] = database;
    start.Environment["LMM_CONFIG_DATABASE_PATH"] = Path.Combine(dataRoot, "config", "app_configs.sqlite");
    start.Environment["LMM_DATA_ROOT"] = dataRoot;
    start.Environment["LMM_LEGACY_ROOT"] = dataRoot;
    start.Environment["LMM_IMAGE_ROOT"] = mediaStorage;
    start.Environment["LMM_BRIDGE_URL"] = url;
    start.Environment["LMM_BRIDGE_TOKEN"] = token;
    if (enableValidationDiagnostics) start.Environment["LMM_VALIDATION_DIAGNOSTICS"] = "1";
    if (!string.IsNullOrWhiteSpace(validationImageMode)) start.Environment["LMM_VALIDATION_IMAGE_MODE"] = validationImageMode;
    Process process = Process.Start(start) ?? throw new InvalidOperationException("Bridge process did not start.");
    _ = PumpAsync(process.StandardOutput, Path.Combine(logRoot, "bridge-stdout.log"));
    _ = PumpAsync(process.StandardError, Path.Combine(logRoot, "bridge-stderr.log"));
    return process;
}

async Task WaitForBridgeAsync(HttpClient client, Process process, TimeSpan timeout)
{
    Stopwatch timer = Stopwatch.StartNew();
    while (timer.Elapsed < timeout) {
        if (process.HasExited) throw new InvalidOperationException($"Bridge exited before readiness: {process.ExitCode}");
        try { using HttpResponseMessage response = await client.GetAsync("/health"); if (response.IsSuccessStatusCode) return; }
        catch (HttpRequestException) { }
        await Task.Delay(250);
    }
    throw new TimeoutException("Bridge readiness timed out.");
}

async Task<TaskWaitResult> WaitForTasksAsync(string database, long previousMaxTask, int expected, TimeSpan timeout, Process bridge)
{
    Stopwatch timer = Stopwatch.StartNew();
    var samples = new List<RuntimeSample>();
    while (timer.Elapsed < timeout) {
        IReadOnlyList<TaskRow> tasks = await ReadTasksAsync(database, previousMaxTask);
        try {
            bridge.Refresh();
            samples.Add(new(DateTimeOffset.UtcNow.ToString("O"), timer.Elapsed.TotalMilliseconds,
                bridge.WorkingSet64, bridge.PrivateMemorySize64, bridge.HandleCount,
                tasks.Count(value => value.Status == "Pending"), tasks.Count(value => value.Status == "Running")));
        }
        catch (InvalidOperationException) { }
        if (tasks.Count == expected && tasks.All(value => Terminal(value.Status))) return new(tasks, samples);
        await Task.Delay(1000);
    }
    throw new TimeoutException($"Batch did not reach {expected} terminal tasks within {timeout}.");
}

async Task<IReadOnlyList<TaskRow>> ReadTasksAsync(string database, long previousMaxTask)
{
    var result = new List<TaskRow>();
    await using SqliteConnection connection = await OpenAsync(database, true);
    await using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        SELECT t.Id,COALESCE(t.CurrentMovieId,0),COALESCE(t.Provider,''),t.Status,COALESCE(t.Stage,''),
               COALESCE(t.RetryCount,0),COALESCE(t.ErrorMessage,''),COALESCE(t.ResultSummary,''),
               COALESCE(t.StartedAt,''),COALESCE(t.CompletedAt,''),
               COALESCE((julianday(t.CompletedAt)-julianday(t.StartedAt))*86400000,0)
          FROM Tasks t WHERE t.Id>$id AND t.TaskType='Sync' ORDER BY t.Id
        """;
    command.Parameters.AddWithValue("$id", previousMaxTask);
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) result.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.GetInt32(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetDouble(10)));
    return result;
}

async Task<IReadOnlyList<TaskLogRow>> ReadTaskLogsAsync(string database, long previousMaxTask)
{
    var result = new List<TaskLogRow>();
    await using SqliteConnection connection = await OpenAsync(database, true);
    await using SqliteCommand command = connection.CreateCommand();
    command.CommandText = "SELECT l.TaskId,l.Level,l.Message,l.CreatedAt FROM TaskLogs l JOIN Tasks t ON t.Id=l.TaskId WHERE t.Id>$id AND t.TaskType='Sync' ORDER BY l.Id";
    command.Parameters.AddWithValue("$id", previousMaxTask);
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
    return result;
}

IReadOnlyList<TaskStageMetric> BuildTaskStageMetrics(IReadOnlyList<TaskRow> tasks, IReadOnlyList<TaskLogRow> logs)
{
    var result = new List<TaskStageMetric>();
    foreach ((TaskRow task, int index) in tasks.Select((value, index) => (value, index))) {
        TaskLogRow[] rows = logs.Where(value => value.TaskId == task.TaskId)
            .OrderBy(value => value.CreatedAt, StringComparer.Ordinal).ToArray();
        DateTimeOffset? At(Func<string, bool> predicate) => rows.FirstOrDefault(value => predicate(value.Message)) is { } row
            && DateTimeOffset.TryParse(row.CreatedAt, out DateTimeOffset at) ? at : null;
        double Span(DateTimeOffset? start, DateTimeOffset? end) => start is not null && end is not null
            ? Math.Max(0, (end.Value - start.Value).TotalMilliseconds) : 0;
        DateTimeOffset? started = DateTimeOffset.TryParse(task.StartedAt, out DateTimeOffset startedAt) ? startedAt : null;
        DateTimeOffset? completed = DateTimeOffset.TryParse(task.CompletedAt, out DateTimeOffset completedAt) ? completedAt : null;
        DateTimeOffset? queued = At(message => message.Contains("同步任务已创建", StringComparison.Ordinal));
        DateTimeOffset? metaStart = At(message => message.Equals("[MetaTube] 开始", StringComparison.Ordinal));
        DateTimeOffset? metaEnd = At(message => message.StartsWith("[MetaTube] 结束", StringComparison.Ordinal));
        DateTimeOffset? javStart = At(message => message.Equals("[JavBus] 开始", StringComparison.Ordinal));
        DateTimeOffset? javEnd = At(message => message.StartsWith("[JavBus] 结束", StringComparison.Ordinal));
        DateTimeOffset? mergeStart = At(message => message.StartsWith("[Metadata Merge]", StringComparison.Ordinal));
        DateTimeOffset? parse = At(message => message.StartsWith("Parse Success", StringComparison.Ordinal));
        DateTimeOffset? download = At(message => message.StartsWith("下载图片", StringComparison.Ordinal));
        DateTimeOffset? nfoStart = At(message => message.StartsWith("生成 NFO", StringComparison.Ordinal));
        DateTimeOffset? nfoEnd = At(message => message.StartsWith("[NFO Write]", StringComparison.Ordinal));
        DateTimeOffset? writerStart = At(message => message is "非破坏合并元数据" or "覆盖同步刮削元数据");
        DateTimeOffset? writerEnd = At(message => message.Equals("[Database Merge] Success", StringComparison.Ordinal));
        int resourceCount = rows.Select(value => Regex.Match(value.Message, @"下载(?:图片|演员头像)（(?<count>\d+) 项）"))
            .Where(value => value.Success).Sum(value => int.Parse(value.Groups["count"].Value, CultureInfo.InvariantCulture));
        result.Add(new(index + 1, task.TaskId, task.MovieId, task.Status, task.RetryCount, task.ElapsedMilliseconds,
            Span(queued, started), Span(started, metaStart), Span(metaStart, metaEnd), Span(javStart, javEnd),
            Span(mergeStart, parse), Span(download, nfoStart ?? writerStart), Span(writerStart, writerEnd),
            Span(writerStart, writerEnd), Span(nfoStart, nfoEnd), Span(writerEnd, completed), resourceCount));
    }
    return result;
}

ProtectedDataAudit BuildProtectedDataAudit(IReadOnlyList<MovieSnapshot> before, IReadOnlyList<MovieSnapshot> after)
{
    int Changed(Func<MovieSnapshot, string> selector) => before.Zip(after).Count(pair => selector(pair.First) != selector(pair.Second));
    return new(
        Changed(value => value.FavoriteHash),
        Changed(value => value.RatingHash),
        Changed(value => value.UserTagsHash),
        Changed(value => value.WatchHash),
        Changed(value => value.PlayHistoryHash),
        Changed(value => value.WatchHash),
        Changed(value => value.NotesHash),
        0,
        Changed(value => value.MediaFilesHash),
        Changed(value => value.LockedImagesHash),
        Changed(value => value.LockedNfoHash),
        "Movies has no separate user-title lock flag; protected title count is reported as not independently measurable.");
}

async Task<DataIntegrityAudit> BuildDataIntegrityAuditAsync(string database, string mediaStorage,
    IReadOnlyList<SampleCandidate> samples, IReadOnlyList<TaskRow> tasks, long previousMaxTask,
    string integrity, long foreignKeys)
{
    string ids = string.Join(',', samples.Select(value => value.MovieId));
    long duplicateMain = await ScalarLongAsync(database, $"SELECT COUNT(*) FROM (SELECT upper(trim(Code)) c,COUNT(*) n FROM Movies WHERE Id IN ({ids}) GROUP BY upper(trim(Code)) HAVING n>1)");
    long duplicateRelations = await ScalarLongAsync(database, $"SELECT (SELECT COUNT(*) FROM (SELECT MovieId,ActorId,RoleName,COUNT(*) n FROM MovieActors WHERE MovieId IN ({ids}) GROUP BY MovieId,ActorId,RoleName HAVING n>1)) + (SELECT COUNT(*) FROM (SELECT MovieId,GenreId,COUNT(*) n FROM MovieGenres WHERE MovieId IN ({ids}) GROUP BY MovieId,GenreId HAVING n>1))");
    long actorZero = await ScalarLongAsync(database, $"SELECT COUNT(*) FROM MovieActors WHERE MovieId IN ({ids}) AND ActorId=0");
    long orphanRelations = await ScalarLongAsync(database, $"SELECT (SELECT COUNT(*) FROM MovieActors x LEFT JOIN Movies m ON m.Id=x.MovieId LEFT JOIN Actors a ON a.Id=x.ActorId WHERE x.MovieId IN ({ids}) AND (m.Id IS NULL OR a.Id IS NULL)) + (SELECT COUNT(*) FROM MovieGenres x LEFT JOIN Movies m ON m.Id=x.MovieId LEFT JOIN Genres g ON g.Id=x.GenreId WHERE x.MovieId IN ({ids}) AND (m.Id IS NULL OR g.Id IS NULL))");
    long duplicateResources = await ScalarLongAsync(database, $"SELECT COUNT(*) FROM (SELECT MovieId,ImageType,lower(FilePath),COUNT(*) n FROM Images WHERE MovieId IN ({ids}) GROUP BY MovieId,ImageType,lower(FilePath) HAVING n>1)");
    long orphanResources = await ScalarLongAsync(database, $"SELECT COUNT(*) FROM Images i LEFT JOIN Movies m ON m.Id=i.MovieId LEFT JOIN Actors a ON a.Id=i.ActorId WHERE (i.MovieId IN ({ids}) OR i.ActorId IS NOT NULL) AND ((i.MovieId IS NOT NULL AND m.Id IS NULL) OR (i.ActorId IS NOT NULL AND a.Id IS NULL))");
    long pending = await ScalarLongAsync(database, $"SELECT COUNT(*) FROM Tasks WHERE Id>{previousMaxTask} AND TaskType='Sync' AND Status='Pending'");
    long running = await ScalarLongAsync(database, $"SELECT COUNT(*) FROM Tasks WHERE Id>{previousMaxTask} AND TaskType='Sync' AND Status='Running'");
    long wrongCode = 0;
    await using (SqliteConnection connection = await OpenAsync(database, true)) {
        foreach (SampleCandidate sample in samples) {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT trim(COALESCE(Code,'')) FROM Movies WHERE Id=$movie";
            command.Parameters.AddWithValue("$movie", sample.MovieId);
            string current = Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? "";
            if (!current.Equals(sample.Code, StringComparison.OrdinalIgnoreCase)) wrongCode++;
        }
    }
    long invalidNfo = 0;
    await using (SqliteConnection connection = await OpenAsync(database, true)) {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT n.FilePath,m.Code FROM NfoDocuments n JOIN Movies m ON m.Id=n.MovieId WHERE n.MovieId IN ({ids}) AND n.Ownership='Provider' AND n.IsLocked=0";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            string path = reader.GetString(0);
            string code = reader.GetString(1);
            try {
                XDocument document = XDocument.Load(path, LoadOptions.None);
                string xml = document.ToString(SaveOptions.DisableFormatting);
                if (!xml.Contains(code, StringComparison.OrdinalIgnoreCase)) invalidNfo++;
            }
            catch { invalidNfo++; }
        }
    }
    long registeredMissing = await MissingRegisteredResourcesAsync(database, samples.Select(value => value.MovieId).ToArray());
    long unregistered = await CountUnregisteredFilesAsync(database, mediaStorage);
    string root = Path.GetFullPath(mediaStorage).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    long unexpectedPaths = 0;
    await using (SqliteConnection connection = await OpenAsync(database, true)) {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT FilePath FROM Images WHERE MovieId IN ({ids}) AND Ownership='Provider' UNION ALL SELECT FilePath FROM NfoDocuments WHERE MovieId IN ({ids}) AND Ownership='Provider'";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            if (reader.IsDBNull(0)) { unexpectedPaths++; continue; }
            string path = Path.GetFullPath(reader.GetString(0));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) unexpectedPaths++;
        }
    }
    string[] files = Directory.Exists(mediaStorage) ? Directory.EnumerateFiles(mediaStorage, "*", SearchOption.AllDirectories).ToArray() : [];
    long emptyFiles = files.LongCount(path => new FileInfo(path).Length == 0);
    long partialFiles = files.LongCount(path => path.EndsWith(".part", StringComparison.OrdinalIgnoreCase));
    long totalBytes = files.Sum(path => new FileInfo(path).Length);
    return new(integrity, foreignKeys, 0, 0, duplicateMain, duplicateRelations, actorZero, orphanRelations,
        duplicateResources, orphanResources, unregistered, registeredMissing, invalidNfo, wrongCode,
        pending, running, unexpectedPaths, emptyFiles, partialFiles, files.LongLength, totalBytes,
        tasks.Count(value => !Terminal(value.Status)));
}

HttpTrafficAudit AnalyzeHttpTraffic(string path, IReadOnlyList<TaskLogRow> taskLogs)
{
    if (!File.Exists(path)) return new(0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<string, int>(), [], "Bridge HTTP log was not found.");
    string handler = "Unknown";
    var requests = new List<(string Handler, string Url)>();
    var statuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (string line in ReadSharedLines(path)) {
        Match handlerMatch = Regex.Match(line, @"HttpClient\.(?<handler>[^.]+)\.(?:LogicalHandler|ClientHandler)");
        if (handlerMatch.Success) handler = handlerMatch.Groups["handler"].Value;
        Match request = Regex.Match(line, @"Start processing HTTP request \w+ (?<url>\S+)");
        if (request.Success) requests.Add((handler, request.Groups["url"].Value));
        Match response = Regex.Match(line, @"Received HTTP response headers after [^ ]+ - (?<status>\d{3})");
        if (response.Success) {
            string key = $"{handler}:{response.Groups["status"].Value}";
            statuses[key] = statuses.GetValueOrDefault(key) + 1;
        }
    }
    var failures = new List<ImageFailureEvidence>();
    foreach (TaskLogRow row in taskLogs.Where(value => value.Message.StartsWith("[Image Resource]", StringComparison.Ordinal))) {
        string Value(string name) => Regex.Match(row.Message, $@"(?:^|; |\] ){Regex.Escape(name)}=(?<value>[^;]+)").Groups["value"].Value;
        failures.Add(new(row.TaskId, Value("Type"), Value("Provider"), Value("Host"), Value("HTTP"),
            Value("ContentType"), Value("ContentLength"), Value("Referer"), Value("UserAgent"), Value("Retry"), Value("Result")));
    }
    var javBus = requests.Where(value => value.Handler.Equals("JavBus", StringComparison.OrdinalIgnoreCase)).ToArray();
    int duplicateJavBus = javBus.GroupBy(value => value.Url, StringComparer.OrdinalIgnoreCase).Sum(group => Math.Max(0, group.Count() - 1));
    int logicalJavBus = taskLogs.Count(value => value.Message.Equals("[JavBus] 开始", StringComparison.Ordinal));
    int javBusPageCacheReuse = statuses.GetValueOrDefault("JavBus:200");
    int metadataImages = requests.Count(value => value.Handler.Equals("MetadataImages", StringComparison.OrdinalIgnoreCase));
    return new(requests.Count,
        requests.Count(value => value.Handler.Equals("MetaTube", StringComparison.OrdinalIgnoreCase) && value.Url.Contains("/search", StringComparison.OrdinalIgnoreCase)),
        requests.Count(value => value.Handler.Equals("MetaTube", StringComparison.OrdinalIgnoreCase) && value.Url.Contains("/movies/", StringComparison.OrdinalIgnoreCase) && !value.Url.Contains("/search", StringComparison.OrdinalIgnoreCase)),
        logicalJavBus, javBus.Length, javBusPageCacheReuse, duplicateJavBus, metadataImages,
        statuses, failures, "HTTP status counts come from named HttpClient handlers; structured image failures contain exact resource type, provider, host and status without sensitive URL data.");
}

IEnumerable<string> ReadSharedLines(string path)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    while (reader.ReadLine() is { } line) yield return line;
}

async Task<long> MissingRegisteredResourcesAsync(string database, long[] ids)
{
    long count = 0;
    await using SqliteConnection connection = await OpenAsync(database, true);
    await using SqliteCommand command = connection.CreateCommand();
    command.CommandText = $"SELECT FilePath FROM Images WHERE MovieId IN ({string.Join(',', ids)}) UNION ALL SELECT FilePath FROM NfoDocuments WHERE MovieId IN ({string.Join(',', ids)})";
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) if (reader.IsDBNull(0) || !File.Exists(reader.GetString(0))) count++;
    return count;
}

async Task<long> CountUnregisteredFilesAsync(string database, string mediaStorage)
{
    if (!Directory.Exists(mediaStorage)) return 0;
    HashSet<string> registered = new(StringComparer.OrdinalIgnoreCase);
    await using SqliteConnection connection = await OpenAsync(database, true);
    await using SqliteCommand command = connection.CreateCommand();
    command.CommandText = "SELECT FilePath FROM Images WHERE FilePath IS NOT NULL UNION SELECT FilePath FROM NfoDocuments WHERE FilePath IS NOT NULL";
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) registered.Add(Path.GetFullPath(reader.GetString(0)));
    return Directory.EnumerateFiles(mediaStorage, "*", SearchOption.AllDirectories)
        .LongCount(path => !registered.Contains(Path.GetFullPath(path)) && !path.Contains(".lmm-temp", StringComparison.OrdinalIgnoreCase));
}

async Task<string> QueryHashAsync(SqliteConnection connection, string sql, long movieId)
{
    await using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$movie", movieId);
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    while (await reader.ReadAsync()) {
        for (int index = 0; index < reader.FieldCount; index++) {
            byte[] bytes = Encoding.UTF8.GetBytes(reader.IsDBNull(index) ? "<NULL>" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? "");
            hash.AppendData(bytes);
            hash.AppendData([0]);
        }
        hash.AppendData([10]);
    }
    return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
}

async Task BackupAsync(string source, string destination)
{
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    await using var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private }.ToString());
    await using var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Private }.ToString());
    await input.OpenAsync();
    await output.OpenAsync();
    input.BackupDatabase(output);
}

async Task<SqliteConnection> OpenAsync(string database, bool readOnly)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
        DataSource = database,
        Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
        Cache = SqliteCacheMode.Private,
    }.ToString());
    await connection.OpenAsync();
    if (!readOnly) await ExecuteAsync(connection, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;");
    return connection;
}

async Task<bool> TableExistsAsync(SqliteConnection connection, string table, SqliteTransaction? transaction = null)
{
    await using SqliteCommand command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
    command.Parameters.AddWithValue("$name", table);
    return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0) > 0;
}

async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] values) =>
    await ExecuteTransactionAsync(connection, null, sql, values);

async Task ExecuteTransactionAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] values)
{
    await using SqliteCommand command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    foreach ((string name, object? value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    await command.ExecuteNonQueryAsync();
}

async Task<string?> ScalarTextAsync(string database, string sql)
{
    await using SqliteConnection connection = await OpenAsync(database, true);
    await using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    return (await command.ExecuteScalarAsync())?.ToString();
}

async Task<long> ScalarLongAsync(string database, string sql)
{
    await using SqliteConnection connection = await OpenAsync(database, true);
    await using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
}

async Task<long> CountStandardCandidatesAsync(string database) => await ScalarLongAsync(database, """
    SELECT COUNT(DISTINCT m.Id) FROM Movies m
    JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
    JOIN Libraries l ON l.Id=f.LibraryId AND l.LibraryType='Standard' AND l.IsEnabled=1
    WHERE COALESCE(f.ExistsState,'')<>'Missing' AND trim(COALESCE(m.Code,''))<>''
    """);

async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
{
    using HttpResponseMessage response = await client.GetAsync(path);
    string text = await response.Content.ReadAsStringAsync();
    response.EnsureSuccessStatusCode();
    using JsonDocument document = JsonDocument.Parse(text);
    return document.RootElement.Clone();
}

async Task<JsonElement> PostJsonAsync<T>(HttpClient client, string path, T payload)
{
    using HttpResponseMessage response = await client.PostAsJsonAsync(path, payload);
    string text = await response.Content.ReadAsStringAsync();
    response.EnsureSuccessStatusCode();
    using JsonDocument document = JsonDocument.Parse(text);
    return document.RootElement.Clone();
}

async Task PumpAsync(StreamReader reader, string path)
{
    await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
    while (await reader.ReadLineAsync() is { } line) { await writer.WriteLineAsync(line); await writer.FlushAsync(); }
}

async Task WriteManifestAsync(string root, int count, IReadOnlyList<SampleCandidate> samples)
{
    Directory.CreateDirectory(Path.Combine(root, "manifests"));
    await WriteJsonAsync(Path.Combine(root, "manifests", $"sample-{count}.json"), new SampleManifest(seed, count, samples));
}

Task WriteJsonAsync(string path, object value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, jsonOptions), new UTF8Encoding(false));
string HashFile(string path)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}
string StableOrder(long id, string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{id}:{code}"))).ToLowerInvariant();
string Csv(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
bool Terminal(string status) => status is "Completed" or "CompletedWithWarnings" or "NoResult" or "Blocked" or "Failed" or "Cancelled";
double Percentile(double[] sorted, double percentile) => sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling(percentile * sorted.Length) - 1];
string RenderBatchSummary(int size, string database, BatchVerification result) => $"""
    # v0.7.8 Production Validation - isolated batch {size}

    - Database: `{database}`
    - Result: {(result.Passed ? "PASS" : "FAIL")}
    - Completed: {result.Completed}
    - CompletedWithWarnings: {result.CompletedWithWarnings}
    - Failed: {result.Failed}
    - NoResult: {result.NoResult}
    - Blocked: {result.Blocked}
    - Protected-data changes: {result.ProtectedDataChanges}
    - Metadata populated: {result.MetadataSuccess}/{result.Total}
    - Poster: {result.Poster}/{result.Total}
    - Fanart: {result.Fanart}/{result.Total}
    - Preview: {result.Preview}/{result.Total}
    - NFO: {result.Nfo}/{result.Total}
    - Director source provided/written/not applicable/write failure/data regression: {result.OptionalFields.Director.SourceProvided}/{result.OptionalFields.Director.Written}/{result.OptionalFields.Director.NotApplicable}/{result.OptionalFields.Director.WriteFailure}/{result.OptionalFields.Director.DataRegression}
    - Series source provided/written/not applicable/write failure/data regression: {result.OptionalFields.Series.SourceProvided}/{result.OptionalFields.Series.Written}/{result.OptionalFields.Series.NotApplicable}/{result.OptionalFields.Series.WriteFailure}/{result.OptionalFields.Series.DataRegression}
    - SQLite integrity_check: `{result.Integrity}`
    - Foreign-key errors: {result.ForeignKeys}
    - Unregistered isolated files: {result.UnregisteredFiles}
    - P95: {result.P95Milliseconds:0} ms
    """;

sealed record SourceEvidence(string ProductionDatabase, long ProductionSize, string ProductionSha256,
    string ProductionLastWriteTimeUtc, string IsolatedDatabase, long IsolatedSize, string IsolatedSha256,
    string CopiedAt, string IntegrityCheck, long ForeignKeyErrors, string ProductionSha256After,
    string ProductionLastWriteTimeUtcAfter);
sealed record SampleManifest(string Seed, int Count, IReadOnlyList<SampleCandidate> Samples);
sealed record Stage3SampleManifest(string Seed, int Count, int Stage2Samples, int NewStage3Samples,
    IReadOnlyList<Stage3ManifestRow> Samples);
sealed record Stage3ManifestRow(int SampleIndex, long MovieId, string VideoCode, string NormalizedCode,
    bool IsStage2Sample, bool IsNewStage3Sample, string SampleCategory, string SelectionReason,
    string ExpectedStandardStatus, bool ExpectedEligibility, string ExpectedProviderPath,
    bool ExpectedJavBus, bool ContainsProtectedUserData, bool ImageRiskSample, string FinalStatus,
    string RootCauseCategory);
sealed record SampleCandidate(long MovieId, string Code, string MediaPath, bool MediaPathAccessible,
    bool LowercaseCode, int DigitCount, int SeriesCount, int DirectorCount, int ActorCount, int GenreCount,
    int PreviewCount, int PosterCount, int ProviderFieldCount, bool Favorite, bool HasUserRating,
    int UserTagCount, int PlayHistoryCount, int LockedImageCount, int LockedNfoCount, string StableOrder);
sealed record MovieSnapshot(long MovieId, string Code, string Title, string Description, string ReleaseDate,
    long DurationSeconds, string NfoPath, int ActorCount, int GenreCount, int DirectorCount, int StudioCount,
    int SeriesCount, int PosterCount, int FanartCount, int PreviewCount, int NfoCount,
    string UserHash, string FavoriteHash, string RatingHash, string WatchHash, string NotesHash,
    string UserTagsHash, string PlayHistoryHash, string MediaFilesHash,
    string LockedImagesHash, string LockedNfoHash);
sealed record TaskRow(long TaskId, long MovieId, string Provider, string Status, string Stage, int RetryCount,
    string Error, string Summary, string StartedAt, string CompletedAt, double ElapsedMilliseconds);
sealed record TaskLogRow(long TaskId, string Level, string Message, string CreatedAt);
sealed record CoverageRow(long MovieId, string Code, string FileName, string FilePath, string Classification,
    bool MetaTubeEvidence, bool JavBusSupplementEvidence, string LatestSyncStatus, double Confidence,
    string NormalizedNumber, string Reason);
sealed record CoverageSummary(int TotalStandard, int MetaTubeCovered, int JavBusSupplemented,
    int ProviderCoverageGap, int NonStandard, int Unknown, string GeneratedAt, string Mode);
sealed record BatchVerification(bool Passed, int Total, int Completed, int CompletedWithWarnings, int NoResult,
    int Blocked, int Failed, int Cancelled, int MetadataSuccess, int Poster, int Fanart, int Preview, int Nfo,
    int ProtectedDataChanges, long DuplicateActors, long DuplicateGenres, long DuplicateMovieActors,
    long DuplicateMovieGenres, long DuplicateResources, long MissingRegisteredResources, long UnregisteredFiles,
    string Integrity, long ForeignKeys, double TotalElapsedMilliseconds, double AverageMilliseconds,
    double P95Milliseconds, int MetaTubeLogEvents, int JavBusLogEvents, int ChallengePages, int RateLimits,
    OptionalFieldStatistics OptionalFields);
sealed record OptionalFieldStatistics(OptionalFieldCounts Director, OptionalFieldCounts Series);
sealed record OptionalFieldCounts(int SourceProvided, int Written, int NotApplicable, int WriteFailure,
    int DataRegression);
sealed record TaskWaitResult(IReadOnlyList<TaskRow> Tasks, IReadOnlyList<RuntimeSample> RuntimeSamples);
sealed record RuntimeSample(string CapturedAt, double ElapsedMilliseconds, long WorkingSetBytes,
    long PrivateMemoryBytes, int HandleCount, int PendingTasks, int RunningTasks);
sealed record MemoryTaskWaitResult(IReadOnlyList<TaskRow> Tasks, TimeSpan Elapsed);
sealed record MemoryRuntimeSnapshot(string Label, int CompletedTasks, BridgeRuntimeMemorySnapshot Bridge,
    long TaskResultCount, long LogEntryCount, long ResourceMetadataCount,
    long? ActiveHttpRequests, long? ActiveSqliteConnections, long? OpenStreams, string AvailabilityNote);
sealed record HeapWalkRunResult(string RunLabel, string Mode, int SampleCount, int Completed,
    int CompletedWithWarnings, int NoResult, int Failed, string Integrity, long ForeignKeys,
    ImagePipelineValidationCounters? ImagePipeline, IReadOnlyList<MemoryRuntimeSnapshot> Snapshots);
sealed record GrowthShapeRunResult(string RunLabel, string Experiment, string SampleSeed, int FixedSampleCount,
    bool DownloadImages, IReadOnlyList<GrowthShapeStage> Stages, string CompletedAt);
sealed record GrowthShapeStage(string Label, int BatchIndex, int SampleCount, int Completed,
    int CompletedWithWarnings, int NoResult, int Failed, int Cancelled, long DownloadedImages,
    long ReadBytes, long WrittenImages, long PrivateMemoryBytes, long ProcessPrivateCommitBytes,
    long ManagedHeapBytes, long LargeObjectHeapBytes, long PinnedObjectHeapBytes, int HandleCount,
    int ThreadCount, long? NativeBusyBytes, long? NativeBusyEntries, long? NativeFreeBytes,
    bool HeapWalkValid, ImagePipelineValidationCounters? ImagePipeline,
    IReadOnlyList<MemoryRuntimeSnapshot> Snapshots);
sealed record TaskStageMetric(int BatchIndex, long TaskId, long MovieId, string FinalStatus, int RetryCount,
    double TotalDuration, double QueueDuration, double NormalizeDuration, double MetaTubeDuration,
    double JavBusDuration, double MergeDuration, double ResourceDownloadDuration, double WriterDuration,
    double DatabaseDuration, double NfoDuration, double ValidationDuration, int ResourceCount);
sealed record ProtectedDataAudit(int FavoritesChanged, int RatingChanged, int CustomTagsChanged,
    int WatchStatusChanged, int PlayCountOrHistoryChanged, int LastPositionChanged, int UserNotesChanged,
    int ProtectedTitleChanged, int MediaFileChanged, int LockedImagesChanged, int LockedNfoChanged, string Note);
sealed record DataIntegrityAudit(string Integrity, long ForeignKeys, long SqliteLocks, long TransactionErrors,
    long DuplicateMainRecords, long DuplicateRelations, long ActorIdZero, long OrphanRelations,
    long DuplicateResources, long OrphanResources, long UnregisteredResources, long RegisteredMissingResources,
    long InvalidNfo, long WrongCodeRecords, long PendingTasks, long RunningTasks, long UnexpectedResourcePaths,
    long EmptyFiles, long PartialFiles, long TotalFiles, long TotalBytes, int NonTerminalTasks);
sealed record ImageFailureEvidence(long TaskId, string ResourceType, string Provider, string Host,
    string HttpStatus, string ContentType, string ContentLength, string Referer, string UserAgent,
    string RetryCount, string Result);
sealed record HttpTrafficAudit(int TotalRequests, int MetaTubeSearchRequests, int MetaTubeDetailRequests,
    int JavBusLogicalCalls, int JavBusHttpRequests, int JavBusAvoidedDetailRequests,
    int SuspectedDuplicateJavBusRequests, int MetadataImageRequests, IReadOnlyDictionary<string, int> StatusByHandler,
    IReadOnlyList<ImageFailureEvidence> ImageFailures, string Note);
