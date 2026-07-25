using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SkiaSharp;

string repo = Path.GetFullPath(args.ElementAtOrDefault(0) ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
string sourceDatabase = Path.GetFullPath(args.ElementAtOrDefault(1) ?? @"D:\Local Media Manager Next Data\data\LocalMediaManager.db");
string runRoot = Path.GetFullPath(args.ElementAtOrDefault(2) ?? Path.Combine(@"D:\Local Media Manager Next Smoke", "0.7.0-sync-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
int sampleCount = Math.Clamp(int.TryParse(args.ElementAtOrDefault(3), out int requested) ? requested : 10, 3, 100);
bool offlineMedia = string.Equals(args.ElementAtOrDefault(4), "offline", StringComparison.OrdinalIgnoreCase);
string[] requestedCodes = (args.ElementAtOrDefault(5) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
string database = Path.Combine(runRoot, "data", "LocalMediaManager.db");
string mediaRoot = Path.Combine(runRoot, "media");
string imageRoot = Path.Combine(runRoot, "images");
string nfoRoot = Path.Combine(runRoot, "nfo");
string logRoot = Path.Combine(runRoot, "logs");
string evidenceRoot = Path.Combine(runRoot, "evidence");
string bridgeUrl = "http://127.0.0.1:47841";
string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
Directory.CreateDirectory(Path.GetDirectoryName(database)!);
Directory.CreateDirectory(mediaRoot); Directory.CreateDirectory(imageRoot); Directory.CreateDirectory(nfoRoot);
Directory.CreateDirectory(logRoot); Directory.CreateDirectory(evidenceRoot);

await BackupDatabaseAsync(sourceDatabase, database);
await UpgradeAsync(database, Path.Combine(repo, "backend", "LocalMediaManager.Migration", "migrations"));
List<Sample> samples = await PrepareSamplesAsync(database, mediaRoot, imageRoot, nfoRoot, sampleCount, offlineMedia, requestedCodes);
List<Snapshot> before = await SnapshotAsync(database, samples);
await WriteJsonAsync(Path.Combine(evidenceRoot, "before.json"), before);

string bridgeDll = Path.Combine(repo, "backend", "LocalMediaManager.Bridge", "bin", "Release", "net8.0", "LocalMediaManager.Bridge.dll");
if (!File.Exists(bridgeDll)) throw new FileNotFoundException("Release Bridge 尚未构建。", bridgeDll);
using Process bridge = StartBridge(bridgeDll, database, imageRoot, bridgeUrl, token, logRoot);
try {
    using var client = new HttpClient { BaseAddress = new Uri(bridgeUrl), Timeout = TimeSpan.FromMinutes(2) };
    client.DefaultRequestHeaders.Add("X-LMM-Session", token);
    await WaitForBridgeAsync(client, bridge);
    ProviderProbe[] probes = await ProbeProvidersAsync(client);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "provider-network-probe.json"), probes);

    var taskIds = new List<long>();
    foreach (Sample sample in samples) {
        using HttpResponseMessage response = await client.PostAsync($"/api/videos/{sample.Id}/sync", null);
        response.EnsureSuccessStatusCode();
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        taskIds.Add(payload.RootElement.GetProperty("taskId").GetInt64());
    }

    // Exercise task controls on isolated data before allowing the batch to settle.
    if (taskIds.Count >= 2) {
        await PostAsync(client, $"/api/tasks/{taskIds[0]}/pause");
        await PostAsync(client, $"/api/tasks/{taskIds[0]}/resume");
        await PostAsync(client, $"/api/tasks/{taskIds[1]}/cancel");
        await PostAsync(client, $"/api/tasks/{taskIds[1]}/retry");
    }

    await WaitForTasksAsync(database, taskIds, TimeSpan.FromMinutes(45));
    List<Snapshot> after = await SnapshotAsync(database, samples);
    List<TaskEvidence> tasks = await ReadTasksAsync(database, taskIds);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "after.json"), after);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "tasks.json"), tasks);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "task-logs.json"), await ReadTaskLogsAsync(database, taskIds));
    ProviderStats[] providerStats = await ReadProviderStatsAsync(database, taskIds);
    await WriteJsonAsync(Path.Combine(evidenceRoot, "provider-stats.json"), providerStats);

    string integrity = await ScalarTextAsync(database, "PRAGMA integrity_check") ?? "unknown";
    long foreignKeys = await ScalarLongFromPathAsync(database, "SELECT COUNT(*) FROM pragma_foreign_key_check");
    long temporaryFiles = Directory.EnumerateFiles(imageRoot, "*", SearchOption.AllDirectories).Count(path => path.Contains(".lmm-temp", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".part", StringComparison.OrdinalIgnoreCase));
    int protectedUsers = before.Zip(after).Count(pair => pair.First.Favorite == pair.Second.Favorite && pair.First.UserRating == pair.Second.UserRating && pair.First.Notes == pair.Second.Notes && pair.First.UserTags.SequenceEqual(pair.Second.UserTags));
    int protectedImages = samples.Count(sample => sample.ProtectedImageHash is not null && File.Exists(sample.ProtectedImagePath) && Hash(sample.ProtectedImagePath!) == sample.ProtectedImageHash);
    int protectedNfos = samples.Count(sample => sample.ProtectedNfoText is not null && File.Exists(sample.ProtectedNfoPath) && File.ReadAllText(sample.ProtectedNfoPath!) == sample.ProtectedNfoText);
    int completed = tasks.Count(task => task.Status == "Completed");
    int completedWithWarnings = tasks.Count(task => task.Status == "CompletedWithWarnings");
    int failed = tasks.Count(task => task.Status == "Failed");
    int noResult = tasks.Count(task => task.Status == "NoResult");
    int blocked = tasks.Count(task => task.Status == "Blocked");
    int providerMismatch = tasks.Count(task => task.Error.Contains("番号不匹配", StringComparison.OrdinalIgnoreCase));
    int networkFailures = tasks.Count(task => task.Error.Contains("连续请求", StringComparison.OrdinalIgnoreCase));
    int cancelled = tasks.Count(task => task.Status == "Cancelled");
    int imagesAdded = after.Sum(value => value.ImageCount) - before.Sum(value => value.ImageCount);
    int nfosAdded = after.Sum(value => value.NfoCount) - before.Sum(value => value.NfoCount);
    int nfoFilesCreated = Directory.Exists(Path.Combine(imageRoot, "NFO"))
        ? Directory.EnumerateFiles(Path.Combine(imageRoot, "NFO"), "*.nfo", SearchOption.AllDirectories).Count()
        : 0;
    string report = RenderReport(runRoot, sourceDatabase, samples, tasks, providerStats, completed, completedWithWarnings, failed, cancelled, noResult, blocked, providerMismatch, networkFailures, imagesAdded, nfosAdded, nfoFilesCreated,
        protectedUsers, protectedImages, protectedNfos, temporaryFiles, integrity, foreignKeys);
    await File.WriteAllTextAsync(Path.Combine(runRoot, "0.7.5-SYNC-PROVIDER-SMOKE.md"), report, new UTF8Encoding(false));
    Console.WriteLine(report);
    return integrity == "ok" && foreignKeys == 0 && temporaryFiles == 0 && protectedUsers == samples.Count ? 0 : 3;
}
finally {
    if (!bridge.HasExited) { bridge.Kill(true); await bridge.WaitForExitAsync(); }
}

static async Task BackupDatabaseAsync(string source, string destination) {
    if (!File.Exists(source)) throw new FileNotFoundException("找不到源数据库。", source);
    await using var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=source,Mode=SqliteOpenMode.ReadOnly,Cache=SqliteCacheMode.Private }.ToString());
    await using var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=destination,Mode=SqliteOpenMode.ReadWriteCreate,Cache=SqliteCacheMode.Private }.ToString());
    await input.OpenAsync(); await output.OpenAsync(); input.BackupDatabase(output);
}

static async Task UpgradeAsync(string database, string migrationRoot) {
    await using var connection = await OpenAsync(database, false);
    long version = await ScalarLongAsync(connection, "SELECT CAST(Value AS INTEGER) FROM DatabaseMetadata WHERE Key='SchemaVersion'");
    foreach (string file in Directory.EnumerateFiles(migrationRoot, "*.sql").Order()) {
        int number = int.Parse(Path.GetFileName(file)[..4]); if (number <= version) continue;
        if (await MigrationRecordedAsync(connection, number)) continue;
        if (number == 14 && await ColumnExistsAsync(connection, "Actors", "HeightCm")) continue;
        await using var command = connection.CreateCommand(); command.CommandText = await File.ReadAllTextAsync(file); await command.ExecuteNonQueryAsync();
    }
}
static async Task<bool> MigrationRecordedAsync(SqliteConnection connection,int version){if(!await TableExistsAsync(connection,"SchemaMigrations"))return false;await using var x=connection.CreateCommand();x.CommandText="SELECT COUNT(*) FROM SchemaMigrations WHERE Version=$version";x.Parameters.AddWithValue("$version",version);return Convert.ToInt64(await x.ExecuteScalarAsync()??0L)>0;}
static async Task<bool> ColumnExistsAsync(SqliteConnection connection,string table,string column){await using var x=connection.CreateCommand();x.CommandText=$"PRAGMA table_info({table})";await using var r=await x.ExecuteReaderAsync();while(await r.ReadAsync())if(string.Equals(r.GetString(1),column,StringComparison.OrdinalIgnoreCase))return true;return false;}
static async Task<bool> TableExistsAsync(SqliteConnection connection,string table){await using var x=connection.CreateCommand();x.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";x.Parameters.AddWithValue("$table",table);return Convert.ToInt64(await x.ExecuteScalarAsync()??0L)>0;}

static async Task<List<Sample>> PrepareSamplesAsync(string database, string mediaRoot, string imageRoot, string nfoRoot, int count,
    bool offlineMedia, IReadOnlyList<string> requestedCodes) {
    await using var connection = await OpenAsync(database, false);
    await ExecuteAsync(connection, """
        INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES
        ('metadata.metatube.enabled','true','boolean',$at),
        ('metadata.metatube.baseUrl','\"http://127.0.0.1:8080/\"','string',$at),
        ('metadata.metatube.timeoutSeconds','60','integer',$at),
        ('metadata.metatube.downloadImages','true','boolean',$at),
        ('metadata.metatube.writeNfo','true','boolean',$at),
        ('metadata.metatube.autoExecute','true','boolean',$at),
        ('metadata.mdcNg.enabled','false','boolean',$at),
        ('metadata.javbus.enabled','true','boolean',$at),
        ('metadata.javbus.priority','2','integer',$at),
        ('metadata.javbus.baseUrl','\"https://www.javbus.com/\"','string',$at),
        ('metadata.javbus.timeoutSeconds','45','integer',$at),
        ('metadata.javbus.retryCount','1','integer',$at),
        ('metadata.javbus.cookie','\"\"','secret',$at),
        ('metadata.javbus.downloadImages','true','boolean',$at),
        ('metadata.dmm.enabled','false','boolean',$at),
        ('metadata.javdb.enabled','false','boolean',$at),
        ('mediaStorage.rootPath',$imageRoot,'string',$at),
        ('mediaStorage.directory.posters','\"Posters\"','string',$at),
        ('mediaStorage.directory.thumbnails','\"Thumbnails\"','string',$at),
        ('mediaStorage.directory.fanart','\"Fanart\"','string',$at),
        ('mediaStorage.directory.previews','\"Previews\"','string',$at),
        ('mediaStorage.directory.screenshots','\"Screenshots\"','string',$at),
        ('mediaStorage.directory.wallCrops','\"WallCrops\"','string',$at),
        ('mediaStorage.directory.gif','\"Gif\"','string',$at),
        ('mediaStorage.directory.nfo','\"Nfo\"','string',$at),
        ('nfo.export.outputDirectory',$nfo,'string',$at)
        ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType=excluded.ValueType,UpdatedAt=excluded.UpdatedAt
        """, ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$nfo", JsonSerializer.Serialize(nfoRoot)), ("$imageRoot", JsonSerializer.Serialize(imageRoot)));
    var samples = new List<Sample>();
    if (requestedCodes.Count > 0) {
        foreach (string requestedCode in requestedCodes.Take(count)) {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT m.Id,trim(m.Code),
                  COALESCE((SELECT IsFavorite FROM UserMovieState WHERE MovieId=m.Id),0),
                  COALESCE((SELECT HasUserRating FROM UserMovieState WHERE MovieId=m.Id),0),
                  (SELECT COUNT(*) FROM MovieTags WHERE MovieId=m.Id),
                  (SELECT COUNT(*) FROM MovieActors WHERE MovieId=m.Id)
                FROM Movies m
                WHERE upper(trim(m.Code))=upper($code)
                  AND EXISTS(SELECT 1 FROM MediaFiles f JOIN Libraries l ON l.Id=f.LibraryId
                              WHERE f.MovieId=m.Id AND l.LibraryType='Standard')
                ORDER BY m.Id DESC LIMIT 1
                """;
            command.Parameters.AddWithValue("$code", requestedCode);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                samples.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)==1, reader.GetInt64(3)==1, reader.GetInt32(4), reader.GetInt32(5)));
        }
    }
    else await using (var command = connection.CreateCommand()) {
        command.CommandText = """
            SELECT m.Id,trim(m.Code),
              COALESCE((SELECT IsFavorite FROM UserMovieState WHERE MovieId=m.Id),0),
              COALESCE((SELECT HasUserRating FROM UserMovieState WHERE MovieId=m.Id),0),
              (SELECT COUNT(*) FROM MovieTags WHERE MovieId=m.Id),
              (SELECT COUNT(*) FROM MovieActors WHERE MovieId=m.Id)
            FROM Movies m WHERE trim(COALESCE(m.Code,''))<>''
              AND EXISTS(SELECT 1 FROM MediaFiles f JOIN Libraries l ON l.Id=f.LibraryId
                          WHERE f.MovieId=m.Id AND l.LibraryType='Standard')
            ORDER BY 3 DESC,4 DESC,5 DESC,6 DESC,m.Id LIMIT $count
            """;
        command.Parameters.AddWithValue("$count", count);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) samples.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)==1, reader.GetInt64(3)==1, reader.GetInt32(4), reader.GetInt32(5)));
    }
    if (samples.Count < count) throw new InvalidOperationException($"数据库中只有 {samples.Count} 部符合条件的 Standard 影片可用于烟测。");
    for (int index=0; index<samples.Count; index++) {
        Sample sample=samples[index]; string safe=Safe(sample.Code); string media=Path.Combine(mediaRoot,safe+".mp4");
        await ResetProviderMetadataAsync(connection, sample.Id);
        if (!offlineMedia) await File.WriteAllBytesAsync(media,[0,0,0,24,102,116,121,112,105,115,111,109]);
        await ExecuteAsync(connection,"UPDATE MediaFiles SET FilePath=$path,NormalizedPath=$normalized,FileName=$name,ExistsState=$state WHERE Id=(SELECT Id FROM MediaFiles WHERE MovieId=$id AND IsPrimary=1 ORDER BY Id LIMIT 1)",( "$path",media),("$normalized",media.ToUpperInvariant()),("$name",Path.GetFileName(media)),("$state",offlineMedia?"Missing":"Present"),("$id",sample.Id));
        if (index < 5) {
            string image=Path.Combine(imageRoot,"BigPic",safe+".jpg"); Directory.CreateDirectory(Path.GetDirectoryName(image)!); WriteJpeg(image);
            samples[index]=sample with { ProtectedImagePath=image,ProtectedImageHash=Hash(image) };
        }
        if (index is >=5 and <10) {
            string nfo=Path.Combine(nfoRoot,safe+".nfo"); string text=$"<?xml version=\"1.0\" encoding=\"utf-8\"?><movie><title>User protected {safe}</title><id>{safe}</id></movie>"; await File.WriteAllTextAsync(nfo,text,new UTF8Encoding(false));
            samples[index]=sample with { ProtectedNfoPath=nfo,ProtectedNfoText=text };
        }
    }
    return samples;
}

static async Task ResetProviderMetadataAsync(SqliteConnection connection, long movieId)
{
    await using var transaction = await connection.BeginTransactionAsync();
    foreach (string relation in new[] { "MovieActors", "MovieGenres", "MovieSeries", "MovieStudios", "MovieDirectors" }) {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = $"DELETE FROM {relation} WHERE MovieId=$id";
        command.Parameters.AddWithValue("$id", movieId);
        await command.ExecuteNonQueryAsync();
    }
    await ExecuteTransactionAsync(connection, transaction, "DELETE FROM ExternalIds WHERE EntityType='Movie' AND EntityId=$id", ("$id", movieId));
    await ExecuteTransactionAsync(connection, transaction, "DELETE FROM Images WHERE MovieId=$id AND COALESCE(IsLocked,0)=0 AND COALESCE(Ownership,'Provider')<>'User'", ("$id", movieId));
    await ExecuteTransactionAsync(connection, transaction, "DELETE FROM NfoDocuments WHERE MovieId=$id AND COALESCE(IsLocked,0)=0 AND COALESCE(Ownership,'Provider')<>'User'", ("$id", movieId));
    await ExecuteTransactionAsync(connection, transaction, """
        UPDATE Movies SET Title=Code,OriginalTitle=NULL,SortTitle=Code,Description=NULL,ReleaseDate=NULL,
          DurationSeconds=0,ProviderRating=NULL,NfoPath=NULL,IsScraped=0,ScrapeStatus='pending',UpdatedAt=$at
        WHERE Id=$id
        """, ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$id", movieId));
    await transaction.CommitAsync();
}

static Process StartBridge(string dll,string database,string imageRoot,string url,string token,string logRoot) {
    var info=new ProcessStartInfo("dotnet",$"\"{dll}\"") { UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
    info.Environment["LMM_DATABASE_PATH"]=database;info.Environment["LMM_IMAGE_ROOT"]=imageRoot;info.Environment["LMM_BRIDGE_URL"]=url;info.Environment["LMM_BRIDGE_TOKEN"]=token;info.Environment["LMM_CONFIG_DATABASE_PATH"]=Path.Combine(logRoot,"no-legacy-config.db");
    var process=Process.Start(info)??throw new InvalidOperationException("无法启动隔离 Bridge。");
    _=PumpAsync(process.StandardOutput,Path.Combine(logRoot,"bridge-stdout.log"));_=PumpAsync(process.StandardError,Path.Combine(logRoot,"bridge-stderr.log"));return process;
}
static async Task PumpAsync(StreamReader reader,string path){await using var writer=new StreamWriter(path,false,new UTF8Encoding(false)){AutoFlush=true};while(await reader.ReadLineAsync() is string line)await writer.WriteLineAsync(line);}
static async Task WaitForBridgeAsync(HttpClient client,Process process){for(int i=0;i<60;i++){if(process.HasExited)throw new InvalidOperationException($"Bridge 提前退出：{process.ExitCode}");try{using var response=await client.GetAsync("/health");if(response.IsSuccessStatusCode)return;}catch(HttpRequestException){}await Task.Delay(500);}throw new TimeoutException("Bridge 未在 30 秒内就绪。");}
static async Task PostAsync(HttpClient client,string path){using var response=await client.PostAsync(path,null);response.EnsureSuccessStatusCode();}
static async Task<ProviderProbe[]> ProbeProvidersAsync(HttpClient client)
{
    var values = new List<ProviderProbe>();
    var tests = new (string Name, string Path, object Body)[] {
        ("MetaTube", "/api/settings/providers/metatube/test", new { enabled=true, baseUrl="http://127.0.0.1:8080/", timeoutSeconds=15, downloadImages=true, writeNfo=true, autoExecute=true, nonDestructive=true }),
        ("JavBus", "/api/settings/providers/javbus/test", new { enabled=true, priority=2, baseUrl="https://www.javbus.com/", timeoutSeconds=20, retryCount=0, cookie="", downloadImages=true, fillMissingOnly=true }),
        ("DMM", "/api/settings/providers/dmm/test", new { enabled=false, priority=3, baseUrl="https://www.dmm.co.jp/", timeoutSeconds=20, retryCount=0, cookie="", downloadImages=true, fillMissingOnly=true }),
        ("JavDB", "/api/settings/providers/javdb/test", new { enabled=false, priority=4, baseUrl="https://javdb.com/", timeoutSeconds=20, retryCount=0, cookie="", downloadImages=true, fillMissingOnly=true }),
        ("Minnano", "/api/settings/providers/minnano/test", new { enabled=false, priority=1, baseUrl="https://www.minnano-av.com/", timeoutSeconds=20, retryCount=0, cookie="", downloadImages=true, fillMissingOnly=true }),
        ("Wikipedia JP", "/api/settings/providers/wikipedia-jp/test", new { enabled=false, priority=2, baseUrl="https://ja.wikipedia.org/", timeoutSeconds=20, retryCount=0, cookie="", downloadImages=false, fillMissingOnly=true }),
    };
    foreach ((string name, string path, object body) in tests) {
        try {
            using HttpResponseMessage response = await client.PostAsJsonAsync(path, body);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) values.Add(new(name, false, 0, $"HTTP {(int)response.StatusCode}: {json}"));
            else {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                values.Add(new(name, root.GetProperty("success").GetBoolean(), root.GetProperty("elapsedMilliseconds").GetInt64(), root.GetProperty("message").GetString() ?? ""));
            }
        } catch (Exception error) {
            values.Add(new(name, false, 0, error.Message));
        }
    }
    return values.ToArray();
}
static async Task WaitForTasksAsync(string database,IReadOnlyList<long> ids,TimeSpan timeout){var watch=Stopwatch.StartNew();while(watch.Elapsed<timeout){await using var c=await OpenAsync(database,true);await using var x=c.CreateCommand();x.CommandText=$"SELECT COUNT(*) FROM Tasks WHERE Id IN ({string.Join(',',ids)}) AND Status NOT IN ('Completed','CompletedWithWarnings','NoResult','Blocked','Failed','Cancelled')";if(Convert.ToInt64(await x.ExecuteScalarAsync()??0L)==0)return;await Task.Delay(1000);}throw new TimeoutException("MetaTube 批量任务未在时限内结束。");}

static async Task<List<Snapshot>> SnapshotAsync(string database,IReadOnlyList<Sample> samples){var result=new List<Snapshot>();await using var c=await OpenAsync(database,true);foreach(Sample sample in samples){await using var x=c.CreateCommand();x.CommandText="""SELECT m.Id,m.Code,COALESCE(m.Title,''),COALESCE(m.Description,''),COALESCE(m.ReleaseDate,''),m.DurationSeconds,COALESCE(s.IsFavorite,0),CASE WHEN COALESCE(s.HasUserRating,0)=1 THEN s.UserRating END,COALESCE(s.Notes,''),(SELECT COUNT(*) FROM Images WHERE MovieId=m.Id),(SELECT COUNT(*) FROM NfoDocuments WHERE MovieId=m.Id),EXISTS(SELECT 1 FROM NfoDocuments WHERE MovieId=m.Id AND FilePath<>''),(SELECT COALESCE(json_group_array(t.Name),'[]') FROM Tags t JOIN MovieTags mt ON mt.TagId=t.Id WHERE mt.MovieId=m.Id AND t.Source='User'),(SELECT COUNT(*) FROM MovieActors WHERE MovieId=m.Id) FROM Movies m LEFT JOIN UserMovieState s ON s.MovieId=m.Id WHERE m.Id=$id""";x.Parameters.AddWithValue("$id",sample.Id);await using var r=await x.ExecuteReaderAsync();await r.ReadAsync();result.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt32(5),r.GetInt64(6)==1,r.IsDBNull(7)?null:r.GetDouble(7),r.GetString(8),r.GetInt32(9),r.GetInt32(10),r.GetInt64(11)==1,JsonSerializer.Deserialize<string[]>(r.GetString(12))??[],r.GetInt32(13)));}return result;}
static async Task<List<TaskEvidence>> ReadTasksAsync(string database,IReadOnlyList<long> ids){var list=new List<TaskEvidence>();await using var c=await OpenAsync(database,true);await using var x=c.CreateCommand();x.CommandText=$"SELECT Id,COALESCE(CurrentMovieId,0),Status,COALESCE(Stage,''),COALESCE(ErrorMessage,''),RetryCount,COALESCE(ResultJson,'') FROM Tasks WHERE Id IN ({string.Join(',',ids)}) ORDER BY Id";await using var r=await x.ExecuteReaderAsync();while(await r.ReadAsync())list.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt32(5),r.GetString(6)));return list;}
static async Task<List<object>> ReadTaskLogsAsync(string database,IReadOnlyList<long> ids){var list=new List<object>();await using var c=await OpenAsync(database,true);await using var x=c.CreateCommand();x.CommandText=$"SELECT TaskId,Level,Message,CreatedAt FROM TaskLogs WHERE TaskId IN ({string.Join(',',ids)}) ORDER BY Id";await using var r=await x.ExecuteReaderAsync();while(await r.ReadAsync())list.Add(new{taskId=r.GetInt64(0),level=r.GetString(1),message=r.GetString(2),createdAt=r.GetString(3)});return list;}
static async Task<ProviderStats[]> ReadProviderStatsAsync(string database,IReadOnlyList<long> ids){var list=new List<ProviderStats>();await using var c=await OpenAsync(database,true);await using var x=c.CreateCommand();x.CommandText=$"SELECT COALESCE(Provider,'Unknown'),Status,COALESCE(ResultJson,'') FROM Tasks WHERE Id IN ({string.Join(',',ids)}) ORDER BY Id";await using var r=await x.ExecuteReaderAsync();while(await r.ReadAsync()){string json=r.GetString(2);list.Add(new(r.GetString(0),r.GetString(1),json.Contains("\"Title\":",StringComparison.OrdinalIgnoreCase),json.Contains("\"Actors\":",StringComparison.OrdinalIgnoreCase),json.Contains("\"Genres\":",StringComparison.OrdinalIgnoreCase),json.Contains("\"ImagesDownloaded\":",StringComparison.OrdinalIgnoreCase)));}return list.ToArray();}
static string RenderReport(string root,string source,IReadOnlyList<Sample> samples,IReadOnlyList<TaskEvidence> tasks,IReadOnlyList<ProviderStats> providerStats,int completed,int completedWithWarnings,int failed,int cancelled,int noResult,int blocked,int providerMismatch,int networkFailures,int images,int nfos,int nfoFiles,int users,int protectedImages,int protectedNfos,long temporary,string integrity,long foreignKeys)=>$"""
    # Local Media Manager 0.7.5 real sync provider smoke

    - Executed: {DateTimeOffset.Now:O}
    - Source database: `{Path.GetFileName(source)}` (opened read-only and copied with SQLite backup API)
    - Isolated run: `{Path.GetFileName(root)}`
    - Samples: {samples.Count}
    - Completed: {completed}
    - Completed with warnings: {completedWithWarnings}
    - Failed: {failed}
    - Cancelled: {cancelled}
    - No result: {noResult}
    - Blocked: {blocked}
    - Provider code mismatch: {providerMismatch}
    - Network/provider request failures: {networkFailures}
    - Images registered: {images}
    - NFO database records added: {nfos}
    - NFO physical files created: {nfoFiles}
    - User state/tag sets preserved: {users}/{samples.Count}
    - Existing image files preserved: {protectedImages}/{samples.Count(s=>s.ProtectedImageHash is not null)}
    - Existing user NFO preserved: {protectedNfos}/{samples.Count(s=>s.ProtectedNfoText is not null)}
    - Temporary files remaining: {temporary}
    - `integrity_check`: `{integrity}`
    - `foreign_key_check` rows: {foreignKeys}
    - Retried tasks: {tasks.Count(t=>t.RetryCount>0)}
    - Provider success: {string.Join("; ", providerStats.GroupBy(s=>s.Provider).Select(g=>$"{g.Key}: {g.Count(x=>x.Status is "Completed" or "CompletedWithWarnings")}/{g.Count()}"))}

    Evidence: `evidence/before.json`, `evidence/after.json`, `evidence/tasks.json`, `evidence/task-logs.json`, `evidence/provider-stats.json`, and Bridge logs under `logs/`.
    """;
static async Task<SqliteConnection> OpenAsync(string path,bool readOnly){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWrite,Cache=SqliteCacheMode.Private}.ToString());await c.OpenAsync();if(!readOnly)await ExecuteAsync(c,"PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");return c;}
static async Task ExecuteAsync(SqliteConnection c,string sql,params(string,object?)[] p){await using var x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);await x.ExecuteNonQueryAsync();}
static async Task ExecuteTransactionAsync(SqliteConnection c,System.Data.Common.DbTransaction tx,string sql,params(string,object?)[] p){await using var x=c.CreateCommand();x.Transaction=(SqliteTransaction)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);await x.ExecuteNonQueryAsync();}
static async Task<long> ScalarLongAsync(SqliteConnection c,string sql){await using var x=c.CreateCommand();x.CommandText=sql;return Convert.ToInt64(await x.ExecuteScalarAsync()??0L);}
static async Task<long> ScalarLongFromPathAsync(string path,string sql){await using var c=await OpenAsync(path,true);return await ScalarLongAsync(c,sql);}
static async Task<string?> ScalarTextAsync(string path,string sql){await using var c=await OpenAsync(path,true);await using var x=c.CreateCommand();x.CommandText=sql;return(await x.ExecuteScalarAsync())?.ToString();}
static string Safe(string value)=>string.Concat(value.Select(ch=>Path.GetInvalidFileNameChars().Contains(ch)?'_':ch));
static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
static void WriteJpeg(string path){using var bitmap=new SKBitmap(320,480);using var canvas=new SKCanvas(bitmap);canvas.Clear(new SKColor(36,44,58));using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Jpeg,88);using var stream=File.Create(path);data.SaveTo(stream);}
static Task WriteJsonAsync(string path,object value)=>File.WriteAllTextAsync(path,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
sealed record Sample(long Id,string Code,bool Favorite,bool HasRating,int TagCount,int ActorCount,string? ProtectedImagePath=null,string? ProtectedImageHash=null,string? ProtectedNfoPath=null,string? ProtectedNfoText=null);
sealed record Snapshot(long Id,string Code,string Title,string Description,string ReleaseDate,int DurationSeconds,bool Favorite,double? UserRating,string Notes,int ImageCount,int NfoCount,bool NfoExists,string[] UserTags,int ActorCount);
sealed record TaskEvidence(long Id,long MovieId,string Status,string Stage,string Error,int RetryCount,string ResultJson);
sealed record ProviderStats(string Provider,string Status,bool TitleSignal,bool ActorSignal,bool GenreSignal,bool ImageSignal);
sealed record ProviderProbe(string Provider,bool Success,long ElapsedMilliseconds,string Message);
