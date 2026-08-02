using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

const string BridgeUrl = "http://127.0.0.1:47841";
string repo = Path.GetFullPath(args.ElementAtOrDefault(0) ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
string sourceDatabase = Path.GetFullPath(args.ElementAtOrDefault(1) ?? @"D:\自用软件\部署安装目录\本地媒体管理器\数据\data\LocalMediaManager.db");
string runRoot = Path.GetFullPath(args.ElementAtOrDefault(2) ?? Path.Combine(@"D:\自用软件\测试目录\本地媒体管理器\smoke", "0.7.7-metatube-stage2-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
string[] codes = (args.ElementAtOrDefault(3) ?? "ABF-087,ABF-120,ABF-131,ABF-219,ABF-327")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (codes.Length != 5) throw new ArgumentException("Stage 2 requires exactly five movie codes.");

string database = Path.Combine(runRoot, "data", "LocalMediaManager.db");
string imageRoot = Path.Combine(runRoot, "MediaStorage");
string evidenceRoot = Path.Combine(runRoot, "evidence");
string logRoot = Path.Combine(runRoot, "logs");
Directory.CreateDirectory(Path.GetDirectoryName(database)!);
Directory.CreateDirectory(imageRoot);
Directory.CreateDirectory(evidenceRoot);
Directory.CreateDirectory(logRoot);

await BackupDatabaseAsync(sourceDatabase, database);
List<Sample> samples = await FindSamplesAsync(database, codes);
List<MovieSnapshot> sourceSnapshots = await SnapshotAsync(database, samples);
await WriteJsonAsync(Path.Combine(evidenceRoot, "source-protected-snapshot.json"), sourceSnapshots);

await ConfigureIsolatedDatabaseAsync(database, imageRoot, samples);
await ClearProviderMetadataAsync(database, samples);
List<MovieSnapshot> before = await SnapshotAsync(database, samples);
await WriteJsonAsync(Path.Combine(evidenceRoot, "before.json"), before);

string bridgeDll = Path.Combine(repo, "backend", "LocalMediaManager.Bridge", "bin", "Release", "net8.0", "LocalMediaManager.Bridge.dll");
if (!File.Exists(bridgeDll)) throw new FileNotFoundException("Release Bridge is not built.", bridgeDll);
string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
using Process bridge = StartBridge(bridgeDll, database, imageRoot, token, logRoot);
try {
    using var client = new HttpClient { BaseAddress = new Uri(BridgeUrl), Timeout = TimeSpan.FromMinutes(5) };
    client.DefaultRequestHeaders.Add("X-LMM-Session", token);
    await WaitForBridgeAsync(client, bridge);

    JsonElement metaTubeProbe = await PostJsonAsync(client, "/api/settings/providers/metatube/test", new {
        enabled = true, baseUrl = "http://127.0.0.1:8080/", timeoutSeconds = 60,
        downloadImages = true, writeNfo = true, autoExecute = true, nonDestructive = true,
    });
    await WriteJsonAsync(Path.Combine(evidenceRoot, "metatube-connection.json"), metaTubeProbe);

    var playground = new List<JsonElement>();
    foreach (Sample sample in samples) {
        playground.Add(await PostJsonAsync(client, "/api/developer/providers/test", new {
            provider = "MetaTube", code = sample.Code, bypassCache = true,
        }));
    }
    await WriteJsonAsync(Path.Combine(evidenceRoot, "provider-results.json"), playground);

    JsonElement healthBefore = await GetJsonAsync(client, "/api/metadata/health");
    var detailBefore = new List<JsonElement>();
    foreach (Sample sample in samples) detailBefore.Add(await GetJsonAsync(client, $"/api/videos/{sample.Id}"));
    await WriteJsonAsync(Path.Combine(evidenceRoot, "metadata-health-before.json"), healthBefore);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "detail-api-before.json"), detailBefore);

    var taskIds = new List<long>();
    foreach (Sample sample in samples) {
        using HttpResponseMessage response = await client.PostAsync($"/api/videos/{sample.Id}/sync?source=metatube", null);
        response.EnsureSuccessStatusCode();
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        taskIds.Add(payload.RootElement.GetProperty("taskId").GetInt64());
    }
    await WaitForTasksAsync(database, taskIds, TimeSpan.FromMinutes(45));

    JsonElement healthAfter = await GetJsonAsync(client, "/api/metadata/health");
    var detailAfter = new List<JsonElement>();
    var imageApiAfter = new List<JsonElement>();
    foreach (Sample sample in samples) {
        detailAfter.Add(await GetJsonAsync(client, $"/api/videos/{sample.Id}"));
        imageApiAfter.Add(await GetJsonAsync(client, $"/api/videos/{sample.Id}/images"));
    }
    List<MovieSnapshot> after = await SnapshotAsync(database, samples);
    List<TaskEvidence> tasks = await ReadTasksAsync(database, taskIds);
    List<TaskLog> taskLogs = await ReadTaskLogsAsync(database, taskIds);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "after.json"), after);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "metadata-health-after.json"), healthAfter);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "detail-api-after.json"), detailAfter);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "image-api-after.json"), imageApiAfter);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "tasks-and-sync-history.json"), tasks);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "task-logs.json"), taskLogs);

    string integrity = await ScalarTextFromPathAsync(database, "PRAGMA integrity_check") ?? "unknown";
    long foreignKeys = await ScalarLongFromPathAsync(database, "SELECT COUNT(*) FROM pragma_foreign_key_check");
    Verification verification = Verify(sourceSnapshots, before, after, tasks, taskLogs, playground, integrity, foreignKeys);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "verification.json"), verification);
    string report = RenderReport(runRoot, sourceDatabase, samples, before, after, tasks, healthBefore, healthAfter, verification);
    string reportPath = Path.Combine(runRoot, "0.7.7-METATUBE-STAGE2.md");
    await File.WriteAllTextAsync(reportPath, report, new UTF8Encoding(false));
    Console.WriteLine(report);
    Console.WriteLine($"REPORT_PATH={reportPath}");
    Console.WriteLine($"ISOLATED_DATABASE={database}");
    return verification.Passed ? 0 : 4;
}
finally {
    if (!bridge.HasExited) {
        bridge.Kill(true);
        await bridge.WaitForExitAsync();
    }
}

static async Task BackupDatabaseAsync(string source, string destination)
{
    if (!File.Exists(source)) throw new FileNotFoundException("Source database was not found.", source);
    await using var input = new SqliteConnection(new SqliteConnectionStringBuilder {
        DataSource = source, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private,
    }.ToString());
    await using var output = new SqliteConnection(new SqliteConnectionStringBuilder {
        DataSource = destination, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Private,
    }.ToString());
    await input.OpenAsync();
    await output.OpenAsync();
    input.BackupDatabase(output);
}

static async Task<List<Sample>> FindSamplesAsync(string database, IReadOnlyList<string> codes)
{
    var samples = new List<Sample>();
    await using var connection = await OpenAsync(database, true);
    foreach (string code in codes) {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.Id,trim(m.Code)
              FROM Movies m
             WHERE upper(trim(m.Code))=upper($code)
               AND EXISTS(
                   SELECT 1 FROM MediaFiles f
                   JOIN Libraries l ON l.Id=f.LibraryId
                  WHERE f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
                    AND l.LibraryType='Standard')
             ORDER BY m.Id DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$code", code);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException($"Standard movie not found: {code}");
        samples.Add(new(reader.GetInt64(0), reader.GetString(1)));
    }
    if (samples.Select(value => value.Id).Distinct().Count() != 5)
        throw new InvalidOperationException("The five codes do not resolve to five distinct movies.");
    return samples;
}

static async Task ConfigureIsolatedDatabaseAsync(string database, string imageRoot, IReadOnlyList<Sample> samples)
{
    await using var connection = await OpenAsync(database, false);
    string now = DateTimeOffset.UtcNow.ToString("O");
    var values = new Dictionary<string, (string Value, string Type)> {
        ["metadata.metatube.enabled"] = ("true", "boolean"),
        ["metadata.metatube.baseUrl"] = (JsonSerializer.Serialize("http://127.0.0.1:8080/"), "string"),
        ["metadata.metatube.timeoutSeconds"] = ("60", "integer"),
        ["metadata.metatube.downloadImages"] = ("true", "boolean"),
        ["metadata.metatube.writeNfo"] = ("true", "boolean"),
        ["metadata.metatube.autoExecute"] = ("true", "boolean"),
        ["metadata.mdcNg.enabled"] = ("false", "boolean"),
        ["metadata.javbus.enabled"] = ("false", "boolean"),
        ["metadata.dmm.enabled"] = ("false", "boolean"),
        ["metadata.javdb.enabled"] = ("false", "boolean"),
        ["mediaStorage.rootPath"] = (JsonSerializer.Serialize(imageRoot), "string"),
        ["mediaStorage.directory.posters"] = (JsonSerializer.Serialize("Posters"), "string"),
        ["mediaStorage.directory.thumbnails"] = (JsonSerializer.Serialize("Thumbnails"), "string"),
        ["mediaStorage.directory.fanart"] = (JsonSerializer.Serialize("Fanart"), "string"),
        ["mediaStorage.directory.previews"] = (JsonSerializer.Serialize("Previews"), "string"),
        ["mediaStorage.directory.screenshots"] = (JsonSerializer.Serialize("Screenshots"), "string"),
        ["mediaStorage.directory.wallCrops"] = (JsonSerializer.Serialize("WallCrops"), "string"),
        ["mediaStorage.directory.gif"] = (JsonSerializer.Serialize("Gif"), "string"),
        ["mediaStorage.directory.nfo"] = (JsonSerializer.Serialize("Nfo"), "string"),
        ["nfo.export.outputDirectory"] = (JsonSerializer.Serialize(Path.Combine(imageRoot, "Nfo")), "string"),
        ["nfo.export.policy"] = (JsonSerializer.Serialize("SkipExisting"), "string"),
    };
    foreach ((string key, (string value, string type)) in values) {
        await ExecuteAsync(connection, """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES($key,$value,$type,$at)
            ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType=excluded.ValueType,UpdatedAt=excluded.UpdatedAt
            """, ("$key", key), ("$value", value), ("$type", type), ("$at", now));
    }
    string sampleIds = string.Join(',', samples.Select(value => value.Id));
    await ExecuteAsync(connection, $"""
        UPDATE MediaFiles SET ExistsState='Missing'
         WHERE IsPrimary=1 AND MediaType='Video' AND MovieId NOT IN ({sampleIds})
        """);
}

static async Task ClearProviderMetadataAsync(string database, IReadOnlyList<Sample> samples)
{
    await using var connection = await OpenAsync(database, false);
    foreach (Sample sample in samples) {
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (string relation in new[] { "MovieActors", "MovieGenres", "MovieSeries", "MovieStudios", "MovieDirectors" }) {
            if (!await TableExistsAsync(connection, relation, transaction)) continue;
            await ExecuteTransactionAsync(connection, transaction, $"DELETE FROM {relation} WHERE MovieId=$id", ("$id", sample.Id));
        }
        await ExecuteTransactionAsync(connection, transaction, "DELETE FROM ExternalIds WHERE EntityType='Movie' AND EntityId=$id", ("$id", sample.Id));
        await ExecuteTransactionAsync(connection, transaction, """
            DELETE FROM Images WHERE MovieId=$id
             AND COALESCE(IsLocked,0)=0 AND COALESCE(Ownership,'Provider')<>'User'
            """, ("$id", sample.Id));
        if (await TableExistsAsync(connection, "NfoDocuments", transaction)) {
            await ExecuteTransactionAsync(connection, transaction, """
                DELETE FROM NfoDocuments WHERE MovieId=$id
                 AND COALESCE(IsLocked,0)=0 AND COALESCE(Ownership,'Provider')<>'User'
                """, ("$id", sample.Id));
        }
        await ExecuteTransactionAsync(connection, transaction, """
            UPDATE Movies SET Title=Code,OriginalTitle=NULL,SortTitle=Code,Description=NULL,ReleaseDate=NULL,
                   DurationSeconds=0,ProviderRating=NULL,NfoPath=NULL,IsScraped=0,ScrapeStatus='pending',UpdatedAt=$at
             WHERE Id=$id
            """, ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$id", sample.Id));
        await transaction.CommitAsync();
    }
}

static Process StartBridge(string dll, string database, string imageRoot, string token, string logRoot)
{
    var info = new ProcessStartInfo("dotnet", $"\"{dll}\"") {
        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
    };
    info.Environment["LMM_DATABASE_PATH"] = database;
    info.Environment["LMM_IMAGE_ROOT"] = imageRoot;
    info.Environment["LMM_DATA_ROOT"] = Path.GetDirectoryName(Path.GetDirectoryName(database)!)!;
    info.Environment["LMM_CONFIG_DATABASE_PATH"] = Path.Combine(logRoot, "isolated-config.sqlite");
    info.Environment["LMM_BRIDGE_URL"] = BridgeUrl;
    info.Environment["LMM_BRIDGE_TOKEN"] = token;
    Process process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the isolated Bridge.");
    _ = PumpAsync(process.StandardOutput, Path.Combine(logRoot, "bridge-stdout.log"));
    _ = PumpAsync(process.StandardError, Path.Combine(logRoot, "bridge-stderr.log"));
    return process;
}

static async Task PumpAsync(StreamReader reader, string path)
{
    await using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
    while (await reader.ReadLineAsync() is string line) await writer.WriteLineAsync(line);
}

static async Task WaitForBridgeAsync(HttpClient client, Process process)
{
    for (int attempt = 0; attempt < 120; attempt++) {
        if (process.HasExited) throw new InvalidOperationException($"Bridge exited early with code {process.ExitCode}.");
        try {
            using HttpResponseMessage response = await client.GetAsync("/health");
            if (response.IsSuccessStatusCode) return;
        } catch (HttpRequestException) { }
        await Task.Delay(500);
    }
    throw new TimeoutException("The isolated Bridge did not become healthy in 60 seconds.");
}

static async Task<JsonElement> PostJsonAsync(HttpClient client, string path, object body)
{
    using HttpResponseMessage response = await client.PostAsJsonAsync(path, body);
    string content = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new HttpRequestException($"POST {path}: HTTP {(int)response.StatusCode}: {content}");
    using JsonDocument document = JsonDocument.Parse(content);
    return document.RootElement.Clone();
}

static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
{
    using HttpResponseMessage response = await client.GetAsync(path);
    string content = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new HttpRequestException($"GET {path}: HTTP {(int)response.StatusCode}: {content}");
    using JsonDocument document = JsonDocument.Parse(content);
    return document.RootElement.Clone();
}

static async Task WaitForTasksAsync(string database, IReadOnlyList<long> taskIds, TimeSpan timeout)
{
    Stopwatch watch = Stopwatch.StartNew();
    string ids = string.Join(',', taskIds);
    while (watch.Elapsed < timeout) {
        await using var connection = await OpenAsync(database, true);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM Tasks WHERE Id IN ({ids}) AND Status NOT IN ('Completed','CompletedWithWarnings','NoResult','Blocked','Failed','Cancelled')";
        if (Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L) == 0) return;
        await Task.Delay(1000);
    }
    throw new TimeoutException("The five MetaTube tasks did not finish within 45 minutes.");
}

static async Task<List<MovieSnapshot>> SnapshotAsync(string database, IReadOnlyList<Sample> samples)
{
    var result = new List<MovieSnapshot>();
    await using var connection = await OpenAsync(database, true);
    foreach (Sample sample in samples) {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.Id,COALESCE(m.Code,''),COALESCE(m.Title,''),COALESCE(m.OriginalTitle,''),
                   COALESCE(m.Description,''),COALESCE(m.ReleaseDate,''),COALESCE(m.DurationSeconds,0),
                   m.ProviderRating,COALESCE(m.IsScraped,0),COALESCE(m.ScrapeStatus,''),COALESCE(m.NfoPath,''),
                   COALESCE(s.IsFavorite,0),COALESCE(s.UserRating,0),COALESCE(s.HasUserRating,0),
                   COALESCE(s.PlayCount,0),COALESCE(s.LastPlayedAt,''),COALESCE(s.LastPositionSeconds,0),COALESCE(s.Notes,'')
              FROM Movies m LEFT JOIN UserMovieState s ON s.MovieId=m.Id WHERE m.Id=$id
            """;
        command.Parameters.AddWithValue("$id", sample.Id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException($"Movie disappeared: {sample.Id}");
        var core = new MovieCore(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetInt64(6), reader.IsDBNull(7) ? null : reader.GetDouble(7),
            reader.GetInt64(8) == 1, reader.GetString(9), reader.GetString(10));
        var user = new UserProtection(reader.GetInt64(11) == 1, reader.GetDouble(12), reader.GetInt64(13) == 1,
            reader.GetInt64(14), reader.GetString(15), reader.GetInt64(16), reader.GetString(17),
            await ReadNamesAsync(connection, "Tags", "MovieTags", "TagId", sample.Id),
            await ReadPlayHistoryAsync(connection, sample.Id));
        RelationSnapshot relations = new(
            await ReadNamesAsync(connection, "Actors", "MovieActors", "ActorId", sample.Id),
            await ReadNamesAsync(connection, "Genres", "MovieGenres", "GenreId", sample.Id),
            await ReadNamesAsync(connection, "Directors", "MovieDirectors", "DirectorId", sample.Id),
            await ReadNamesAsync(connection, "Studios", "MovieStudios", "StudioId", sample.Id),
            await ReadNamesAsync(connection, "Series", "MovieSeries", "SeriesId", sample.Id));
        List<MediaFileSnapshot> mediaFiles = await ReadMediaFilesAsync(connection, sample.Id);
        List<ImageSnapshot> images = await ReadImagesAsync(connection, sample.Id);
        List<NfoSnapshot> nfos = await ReadNfosAsync(connection, sample.Id);
        string[] externalIds = await ReadExternalIdsAsync(connection, sample.Id);
        MovieHealth health = CalculateHealth(core, relations, images, nfos);
        result.Add(new(sample.Id, sample.Code, core, user, relations, mediaFiles, images, nfos, externalIds, health));
    }
    return result;
}

static async Task<string[]> ReadNamesAsync(SqliteConnection connection, string table, string relation, string key, long movieId)
{
    if (!await TableExistsAsync(connection, table) || !await TableExistsAsync(connection, relation)) return [];
    await using var command = connection.CreateCommand();
    command.CommandText = $"SELECT e.Name FROM {table} e JOIN {relation} r ON r.{key}=e.Id WHERE r.MovieId=$id ORDER BY e.Name COLLATE NOCASE";
    command.Parameters.AddWithValue("$id", movieId);
    var values = new List<string>();
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) values.Add(reader.GetString(0));
    return values.ToArray();
}

static async Task<List<PlayHistorySnapshot>> ReadPlayHistoryAsync(SqliteConnection connection, long movieId)
{
    var values = new List<PlayHistorySnapshot>();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id,MovieId,MediaFileId,StartedAt,EndedAt,PositionSeconds,DurationSeconds,Completed,PlayerName,LegacyId FROM PlayHistory WHERE MovieId=$id ORDER BY Id";
    command.Parameters.AddWithValue("$id", movieId);
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) values.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2),
        reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5), reader.GetInt64(6),
        reader.GetInt64(7) == 1, reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetInt64(9)));
    return values;
}

static async Task<List<MediaFileSnapshot>> ReadMediaFilesAsync(SqliteConnection connection, long movieId)
{
    var values = new List<MediaFileSnapshot>();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id,MovieId,LibraryId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,FileHash FROM MediaFiles WHERE MovieId=$id ORDER BY Id";
    command.Parameters.AddWithValue("$id", movieId);
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) values.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2),
        reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.GetInt64(7), reader.GetString(8), reader.GetString(9), reader.GetInt64(10) == 1, reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetString(12)));
    return values;
}

static async Task<List<ImageSnapshot>> ReadImagesAsync(SqliteConnection connection, long movieId)
{
    var values = new List<ImageSnapshot>();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id,ImageType,COALESCE(FilePath,''),COALESCE(SourceUrl,''),COALESCE(SourceProvider,''),COALESCE(Ownership,''),COALESCE(IsLocked,0),COALESCE(IsPrimary,0),COALESCE(FileSize,0),COALESCE(FileHash,'') FROM Images WHERE MovieId=$id ORDER BY Id";
    command.Parameters.AddWithValue("$id", movieId);
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) {
        string path = reader.GetString(2);
        values.Add(new(reader.GetInt64(0), reader.GetString(1), path, reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetInt64(6) == 1, reader.GetInt64(7) == 1, reader.GetInt64(8), reader.GetString(9),
            File.Exists(path), File.Exists(path) ? Hash(path) : null));
    }
    return values;
}

static async Task<List<NfoSnapshot>> ReadNfosAsync(SqliteConnection connection, long movieId)
{
    if (!await TableExistsAsync(connection, "NfoDocuments")) return [];
    var values = new List<NfoSnapshot>();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id,FilePath,Ownership,IsLocked,COALESCE(SourceProvider,''),COALESCE(FileHash,'') FROM NfoDocuments WHERE MovieId=$id ORDER BY Id";
    command.Parameters.AddWithValue("$id", movieId);
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) {
        string path = reader.GetString(1);
        values.Add(new(reader.GetInt64(0), path, reader.GetString(2), reader.GetInt64(3) == 1, reader.GetString(4),
            reader.GetString(5), File.Exists(path), File.Exists(path) ? Hash(path) : null));
    }
    return values;
}

static async Task<string[]> ReadExternalIdsAsync(SqliteConnection connection, long movieId)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Provider || ':' || ExternalId FROM ExternalIds WHERE EntityType='Movie' AND EntityId=$id ORDER BY Provider,ExternalId";
    command.Parameters.AddWithValue("$id", movieId);
    var values = new List<string>();
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) values.Add(reader.GetString(0));
    return values.ToArray();
}

static MovieHealth CalculateHealth(MovieCore core, RelationSnapshot relations, IReadOnlyList<ImageSnapshot> images, IReadOnlyList<NfoSnapshot> nfos)
{
    bool poster = images.Any(value => value.Type.Equals("Poster", StringComparison.OrdinalIgnoreCase) && value.FileExists);
    bool fanart = images.Any(value => (value.Type.Equals("Fanart", StringComparison.OrdinalIgnoreCase) || value.Type.Equals("BigPic", StringComparison.OrdinalIgnoreCase)) && value.FileExists);
    bool preview = images.Any(value => (value.Type.Equals("Preview", StringComparison.OrdinalIgnoreCase) || value.Type.Equals("ExtraPic", StringComparison.OrdinalIgnoreCase)) && value.FileExists);
    bool nfo = (!string.IsNullOrWhiteSpace(core.NfoPath) && File.Exists(core.NfoPath)) || nfos.Any(value => value.FileExists);
    bool complete = !string.IsNullOrWhiteSpace(core.Code) && core.Code == core.Code.ToUpperInvariant()
        && !string.IsNullOrWhiteSpace(core.Title) && !string.IsNullOrWhiteSpace(core.ReleaseDate)
        && relations.Studios.Length > 0 && relations.Actors.Length > 0 && relations.Genres.Length > 0
        && poster && fanart && nfo;
    return new(!string.IsNullOrWhiteSpace(core.Code), !string.IsNullOrWhiteSpace(core.Title), !string.IsNullOrWhiteSpace(core.ReleaseDate),
        relations.Studios.Length > 0, relations.Actors.Length > 0, relations.Genres.Length > 0, poster, fanart, preview, nfo, complete);
}

static async Task<List<TaskEvidence>> ReadTasksAsync(string database, IReadOnlyList<long> taskIds)
{
    string ids = string.Join(',', taskIds);
    var values = new List<TaskEvidence>();
    await using var connection = await OpenAsync(database, true);
    await using var command = connection.CreateCommand();
    command.CommandText = $"""
        SELECT t.Id,COALESCE(t.CurrentMovieId,0),COALESCE(t.Provider,''),t.Status,COALESCE(t.Stage,''),
               COALESCE(t.ErrorMessage,''),t.RetryCount,COALESCE(t.ResultJson,''),COALESCE(t.ResultSummary,''),
               COALESCE(t.StartedAt,''),COALESCE(t.CompletedAt,''),
               COALESCE(s.BeforeJson,''),COALESCE(s.AppliedJson,''),COALESCE(s.Provider,'')
          FROM Tasks t LEFT JOIN MetadataSyncSnapshots s ON s.TaskId=t.Id
         WHERE t.Id IN ({ids}) ORDER BY t.Id
        """;
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) values.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetString(7), reader.GetString(8),
        reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13)));
    return values;
}

static async Task<List<TaskLog>> ReadTaskLogsAsync(string database, IReadOnlyList<long> taskIds)
{
    string ids = string.Join(',', taskIds);
    var values = new List<TaskLog>();
    await using var connection = await OpenAsync(database, true);
    await using var command = connection.CreateCommand();
    command.CommandText = $"SELECT TaskId,Level,Message,CreatedAt FROM TaskLogs WHERE TaskId IN ({ids}) ORDER BY Id";
    await using SqliteDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) values.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
    return values;
}

static Verification Verify(IReadOnlyList<MovieSnapshot> source, IReadOnlyList<MovieSnapshot> before,
    IReadOnlyList<MovieSnapshot> after, IReadOnlyList<TaskEvidence> tasks, IReadOnlyList<TaskLog> logs,
    IReadOnlyList<JsonElement> playground, string integrity, long foreignKeys)
{
    bool Protected(MovieSnapshot left, MovieSnapshot right) =>
        JsonSerializer.Serialize(left.User) == JsonSerializer.Serialize(right.User)
        && JsonSerializer.Serialize(left.MediaFiles) == JsonSerializer.Serialize(right.MediaFiles);
    int sourceProtection = source.Zip(before).Count(pair => Protected(pair.First, pair.Second));
    int afterProtection = before.Zip(after).Count(pair => Protected(pair.First, pair.Second));
    int completed = tasks.Count(value => value.Status == "Completed");
    int warnings = tasks.Count(value => value.Status == "CompletedWithWarnings");
    int fullyPopulated = after.Count(value => value.Health.Complete && value.Health.Preview);
    int registeredTypes = after.Count(value => new[] { "Poster", "Fanart", "Preview" }.All(type =>
        value.Images.Any(image => image.Type.Equals(type, StringComparison.OrdinalIgnoreCase) && image.FileExists && image.FileSize > 0)));
    int nfoFiles = after.Count(value => value.Health.Nfo);
    int fieldSources = tasks.Count(value => value.AppliedJson.Contains("FieldSources", StringComparison.OrdinalIgnoreCase)
        && value.AppliedJson.Contains("MetaTube/", StringComparison.OrdinalIgnoreCase));
    bool onlyMetaTube = tasks.Count == 5 && tasks.All(value => value.Provider.Equals("FANZA", StringComparison.OrdinalIgnoreCase)
        || value.SnapshotProvider.Equals("FANZA", StringComparison.OrdinalIgnoreCase))
        && logs.All(value => !value.Message.Contains("[JavBus]", StringComparison.OrdinalIgnoreCase)
            && !value.Message.Contains("[MDC-NG]", StringComparison.OrdinalIgnoreCase));
    bool playgroundOk = playground.Count == 5 && playground.All(value =>
        value.TryGetProperty("parserSucceeded", out JsonElement parsed) && parsed.GetBoolean()
        && value.TryGetProperty("providerResult", out JsonElement providerResult) && providerResult.ValueKind == JsonValueKind.Object);
    bool passed = sourceProtection == 5 && afterProtection == 5 && completed == 5 && warnings == 0
        && fullyPopulated == 5 && registeredTypes == 5 && nfoFiles == 5 && fieldSources == 5
        && onlyMetaTube && playgroundOk && integrity == "ok" && foreignKeys == 0;
    return new(passed, sourceProtection, afterProtection, completed, warnings, fullyPopulated, registeredTypes,
        nfoFiles, fieldSources, onlyMetaTube, playgroundOk, integrity, foreignKeys);
}

static string RenderReport(string root, string sourceDatabase, IReadOnlyList<Sample> samples,
    IReadOnlyList<MovieSnapshot> before, IReadOnlyList<MovieSnapshot> after, IReadOnlyList<TaskEvidence> tasks,
    JsonElement healthBefore, JsonElement healthAfter, Verification verification)
{
    string rows = string.Join(Environment.NewLine, samples.Select((sample, index) => {
        MovieSnapshot left = before[index]; MovieSnapshot right = after[index]; TaskEvidence task = tasks.Single(value => value.MovieId == sample.Id);
        return $"| {sample.Id} | {sample.Code} | {task.Status} | {left.Relations.Actors.Length}->{right.Relations.Actors.Length} | {left.Relations.Genres.Length}->{right.Relations.Genres.Length} | {left.Health.Poster}->{right.Health.Poster} | {left.Health.Fanart}->{right.Health.Fanart} | {left.Health.Preview}->{right.Health.Preview} | {left.Health.Nfo}->{right.Health.Nfo} | {left.Health.Complete}->{right.Health.Complete} |";
    }));
    long beforeComplete = healthBefore.TryGetProperty("completeMovies", out JsonElement beforeValue) ? beforeValue.GetInt64() : -1;
    long afterComplete = healthAfter.TryGetProperty("completeMovies", out JsonElement afterValue) ? afterValue.GetInt64() : -1;
    return $"""
        # v0.7.7 MetaTube Stage 2 isolated write smoke

        - Executed: {DateTimeOffset.Now:O}
        - Source database: `{Path.GetFileName(sourceDatabase)}` (SQLite read-only backup; source was never written)
        - Isolated root: `{root}`
        - Scope: exactly five Standard movies; MetaTube only
        - Result: {(verification.Passed ? "PASS" : "FAIL")}
        - Tasks: Completed={verification.Completed}, CompletedWithWarnings={verification.CompletedWithWarnings}
        - Protected user state/tags/playback: {verification.ProtectedAfter}/5
        - MediaFile identity/path/association preserved: {verification.ProtectedAfter}/5
        - Poster + Fanart + Preview downloaded and registered: {verification.RegisteredImageTypes}/5
        - NFO physical file and record: {verification.NfoFiles}/5
        - FieldSources recorded as MetaTube source: {verification.FieldSources}/5
        - ProviderResult parser success: {(verification.PlaygroundOk ? "5/5" : "FAIL")}
        - Metadata Health complete count: {beforeComplete} -> {afterComplete}
        - SQLite integrity_check: `{verification.Integrity}`
        - SQLite foreign_key_check rows: {verification.ForeignKeys}
        - Other Provider activity: {(verification.OnlyMetaTube ? "none" : "detected")}

        | MovieId | Code | Task | Actors | Genres | Poster | Fanart | Preview | NFO | Complete |
        |---:|---|---|---:|---:|---|---|---|---|---|
        {rows}

        Evidence files are under `evidence/`: ProviderResult, Merge/Writer snapshots, SQLite Before/After,
        task logs and history, Metadata Health Before/After, detail API Before/After, and image API results.
        """;
}

static async Task<SqliteConnection> OpenAsync(string path, bool readOnly)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
        DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
        Cache = SqliteCacheMode.Private,
    }.ToString());
    await connection.OpenAsync();
    if (!readOnly) await ExecuteAsync(connection, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
    return connection;
}

static async Task<bool> TableExistsAsync(SqliteConnection connection, string table, System.Data.Common.DbTransaction? transaction = null)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction as SqliteTransaction;
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
    command.Parameters.AddWithValue("$table", table);
    return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L) > 0;
}

static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    await command.ExecuteNonQueryAsync();
}

static async Task ExecuteTransactionAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction,
    string sql, params (string Name, object? Value)[] parameters)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction as SqliteTransaction;
    command.CommandText = sql;
    foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    await command.ExecuteNonQueryAsync();
}

static async Task<string?> ScalarTextFromPathAsync(string database, string sql)
{
    await using var connection = await OpenAsync(database, true);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return (await command.ExecuteScalarAsync())?.ToString();
}

static async Task<long> ScalarLongFromPathAsync(string database, string sql)
{
    await using var connection = await OpenAsync(database, true);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
}

static string Hash(string path)
{
    using FileStream stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static Task WriteJsonAsync(string path, object value) => File.WriteAllTextAsync(path,
    JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

sealed record Sample(long Id, string Code);
sealed record MovieCore(long Id, string Code, string Title, string OriginalTitle, string Description, string ReleaseDate,
    long DurationSeconds, double? ProviderRating, bool IsScraped, string ScrapeStatus, string NfoPath);
sealed record UserProtection(bool Favorite, double UserRating, bool HasUserRating, long PlayCount, string LastPlayedAt,
    long LastPositionSeconds, string Notes, string[] UserTags, IReadOnlyList<PlayHistorySnapshot> PlayHistory);
sealed record PlayHistorySnapshot(long Id, long MovieId, long? MediaFileId, string StartedAt, string? EndedAt,
    long PositionSeconds, long DurationSeconds, bool Completed, string? PlayerName, long? LegacyId);
sealed record RelationSnapshot(string[] Actors, string[] Genres, string[] Directors, string[] Studios, string[] Series);
sealed record MediaFileSnapshot(long Id, long MovieId, long? LibraryId, string FilePath, string NormalizedPath,
    string FileName, string? Extension, long FileSize, string MediaType, string SourceType, bool IsPrimary,
    string ExistsState, string? FileHash);
sealed record ImageSnapshot(long Id, string Type, string Path, string SourceUrl, string SourceProvider, string Ownership,
    bool IsLocked, bool IsPrimary, long FileSize, string FileHash, bool FileExists, string? ActualHash);
sealed record NfoSnapshot(long Id, string Path, string Ownership, bool IsLocked, string SourceProvider,
    string FileHash, bool FileExists, string? ActualHash);
sealed record MovieHealth(bool Number, bool Title, bool ReleaseDate, bool Studio, bool Actors, bool Genres,
    bool Poster, bool Fanart, bool Preview, bool Nfo, bool Complete);
sealed record MovieSnapshot(long Id, string Code, MovieCore Core, UserProtection User, RelationSnapshot Relations,
    IReadOnlyList<MediaFileSnapshot> MediaFiles, IReadOnlyList<ImageSnapshot> Images, IReadOnlyList<NfoSnapshot> Nfos,
    string[] ExternalIds, MovieHealth Health);
sealed record TaskEvidence(long Id, long MovieId, string Provider, string Status, string Stage, string Error,
    int RetryCount, string ResultJson, string ResultSummary, string StartedAt, string CompletedAt,
    string BeforeJson, string AppliedJson, string SnapshotProvider);
sealed record TaskLog(long TaskId, string Level, string Message, string CreatedAt);
sealed record Verification(bool Passed, int ProtectedSourceToBaseline, int ProtectedAfter, int Completed,
    int CompletedWithWarnings, int FullyPopulated, int RegisteredImageTypes, int NfoFiles, int FieldSources,
    bool OnlyMetaTube, bool PlaygroundOk, string Integrity, long ForeignKeys);
