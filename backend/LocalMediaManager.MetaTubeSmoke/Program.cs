using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SkiaSharp;

string repo = Path.GetFullPath(args.ElementAtOrDefault(0) ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
string sourceDatabase = Path.GetFullPath(args.ElementAtOrDefault(1) ?? @"D:\Local Media Manager Next Data\data\LocalMediaManager.db");
string runRoot = Path.GetFullPath(args.ElementAtOrDefault(2) ?? Path.Combine(@"D:\Local Media Manager Next Smoke", "0.4.3-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
int sampleCount = Math.Clamp(int.TryParse(args.ElementAtOrDefault(3), out int requested) ? requested : 30, 30, 50);
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
List<Sample> samples = await PrepareSamplesAsync(database, mediaRoot, imageRoot, nfoRoot, sampleCount);
List<Snapshot> before = await SnapshotAsync(database, samples);
await WriteJsonAsync(Path.Combine(evidenceRoot, "before.json"), before);

string bridgeDll = Path.Combine(repo, "backend", "LocalMediaManager.Bridge", "bin", "Release", "net8.0", "LocalMediaManager.Bridge.dll");
if (!File.Exists(bridgeDll)) throw new FileNotFoundException("Release Bridge 尚未构建。", bridgeDll);
using Process bridge = StartBridge(bridgeDll, database, imageRoot, bridgeUrl, token, logRoot);
try {
    using var client = new HttpClient { BaseAddress = new Uri(bridgeUrl), Timeout = TimeSpan.FromMinutes(2) };
    client.DefaultRequestHeaders.Add("X-LMM-Session", token);
    await WaitForBridgeAsync(client, bridge);

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

    string integrity = await ScalarTextAsync(database, "PRAGMA integrity_check") ?? "unknown";
    long foreignKeys = await ScalarLongFromPathAsync(database, "SELECT COUNT(*) FROM pragma_foreign_key_check");
    long temporaryFiles = Directory.EnumerateFiles(imageRoot, "*", SearchOption.AllDirectories).Count(path => path.Contains(".lmm-temp", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".part", StringComparison.OrdinalIgnoreCase));
    int protectedUsers = before.Zip(after).Count(pair => pair.First.Favorite == pair.Second.Favorite && pair.First.UserRating == pair.Second.UserRating && pair.First.Notes == pair.Second.Notes && pair.First.UserTags.SequenceEqual(pair.Second.UserTags));
    int protectedImages = samples.Count(sample => sample.ProtectedImageHash is not null && File.Exists(sample.ProtectedImagePath) && Hash(sample.ProtectedImagePath!) == sample.ProtectedImageHash);
    int protectedNfos = samples.Count(sample => sample.ProtectedNfoText is not null && File.Exists(sample.ProtectedNfoPath) && File.ReadAllText(sample.ProtectedNfoPath!) == sample.ProtectedNfoText);
    int completed = tasks.Count(task => task.Status == "Completed");
    int failed = tasks.Count(task => task.Status == "Failed");
    int noResult = tasks.Count(task => task.Error.Contains("未找到", StringComparison.OrdinalIgnoreCase));
    int providerMismatch = tasks.Count(task => task.Error.Contains("番号不匹配", StringComparison.OrdinalIgnoreCase));
    int networkFailures = tasks.Count(task => task.Error.Contains("连续请求", StringComparison.OrdinalIgnoreCase));
    int cancelled = tasks.Count(task => task.Status == "Cancelled");
    int imagesAdded = after.Sum(value => value.ImageCount) - before.Sum(value => value.ImageCount);
    int nfosAdded = after.Count(value => value.NfoExists) - before.Count(value => value.NfoExists);
    string report = RenderReport(runRoot, sourceDatabase, samples, tasks, completed, failed, cancelled, noResult, providerMismatch, networkFailures, imagesAdded, nfosAdded,
        protectedUsers, protectedImages, protectedNfos, temporaryFiles, integrity, foreignKeys);
    await File.WriteAllTextAsync(Path.Combine(runRoot, "0.4.3-METATUBE-SMOKE.md"), report, new UTF8Encoding(false));
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
        await using var command = connection.CreateCommand(); command.CommandText = await File.ReadAllTextAsync(file); await command.ExecuteNonQueryAsync();
    }
}

static async Task<List<Sample>> PrepareSamplesAsync(string database, string mediaRoot, string imageRoot, string nfoRoot, int count) {
    await using var connection = await OpenAsync(database, false);
    await ExecuteAsync(connection, """
        INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES
        ('metadata.metatube.enabled','true','boolean',$at),
        ('metadata.metatube.baseUrl','\"http://127.0.0.1:8080/\"','string',$at),
        ('metadata.metatube.timeoutSeconds','60','integer',$at),
        ('metadata.metatube.downloadImages','true','boolean',$at),
        ('metadata.metatube.writeNfo','true','boolean',$at),
        ('metadata.metatube.autoExecute','true','boolean',$at),
        ('nfo.export.outputDirectory',$nfo,'string',$at)
        ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType=excluded.ValueType,UpdatedAt=excluded.UpdatedAt
        """, ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$nfo", JsonSerializer.Serialize(nfoRoot)));
    var samples = new List<Sample>();
    await using (var command = connection.CreateCommand()) {
        command.CommandText = """
            SELECT m.Id,trim(m.Code),
              COALESCE((SELECT IsFavorite FROM UserMovieState WHERE MovieId=m.Id),0),
              COALESCE((SELECT HasUserRating FROM UserMovieState WHERE MovieId=m.Id),0),
              (SELECT COUNT(*) FROM MovieTags WHERE MovieId=m.Id),
              (SELECT COUNT(*) FROM MovieActors WHERE MovieId=m.Id)
            FROM Movies m WHERE trim(COALESCE(m.Code,''))<>''
            ORDER BY 3 DESC,4 DESC,5 DESC,6 DESC,m.Id LIMIT $count
            """;
        command.Parameters.AddWithValue("$count", count);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) samples.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)==1, reader.GetInt64(3)==1, reader.GetInt32(4), reader.GetInt32(5)));
    }
    if (samples.Count < count) throw new InvalidOperationException($"数据库中只有 {samples.Count} 部可用于烟测的影片。");
    for (int index=0; index<samples.Count; index++) {
        Sample sample=samples[index]; string safe=Safe(sample.Code); string media=Path.Combine(mediaRoot,safe+".mp4"); await File.WriteAllBytesAsync(media,[0,0,0,24,102,116,121,112,105,115,111,109]);
        await ExecuteAsync(connection,"UPDATE MediaFiles SET FilePath=$path,NormalizedPath=$normalized,FileName=$name,ExistsState='Present' WHERE Id=(SELECT Id FROM MediaFiles WHERE MovieId=$id AND IsPrimary=1 ORDER BY Id LIMIT 1)",( "$path",media),("$normalized",media.ToUpperInvariant()),("$name",Path.GetFileName(media)),("$id",sample.Id));
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

static Process StartBridge(string dll,string database,string imageRoot,string url,string token,string logRoot) {
    var info=new ProcessStartInfo("dotnet",$"\"{dll}\"") { UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
    info.Environment["LMM_DATABASE_PATH"]=database;info.Environment["LMM_IMAGE_ROOT"]=imageRoot;info.Environment["LMM_BRIDGE_URL"]=url;info.Environment["LMM_BRIDGE_TOKEN"]=token;info.Environment["LMM_CONFIG_DATABASE_PATH"]=Path.Combine(logRoot,"no-legacy-config.db");
    var process=Process.Start(info)??throw new InvalidOperationException("无法启动隔离 Bridge。");
    _=PumpAsync(process.StandardOutput,Path.Combine(logRoot,"bridge-stdout.log"));_=PumpAsync(process.StandardError,Path.Combine(logRoot,"bridge-stderr.log"));return process;
}
static async Task PumpAsync(StreamReader reader,string path){await using var writer=new StreamWriter(path,false,new UTF8Encoding(false)){AutoFlush=true};while(await reader.ReadLineAsync() is string line)await writer.WriteLineAsync(line);}
static async Task WaitForBridgeAsync(HttpClient client,Process process){for(int i=0;i<60;i++){if(process.HasExited)throw new InvalidOperationException($"Bridge 提前退出：{process.ExitCode}");try{using var response=await client.GetAsync("/health");if(response.IsSuccessStatusCode)return;}catch(HttpRequestException){}await Task.Delay(500);}throw new TimeoutException("Bridge 未在 30 秒内就绪。");}
static async Task PostAsync(HttpClient client,string path){using var response=await client.PostAsync(path,null);response.EnsureSuccessStatusCode();}
static async Task WaitForTasksAsync(string database,IReadOnlyList<long> ids,TimeSpan timeout){var watch=Stopwatch.StartNew();while(watch.Elapsed<timeout){await using var c=await OpenAsync(database,true);await using var x=c.CreateCommand();x.CommandText=$"SELECT COUNT(*) FROM Tasks WHERE Id IN ({string.Join(',',ids)}) AND Status NOT IN ('Completed','Failed','Cancelled')";if(Convert.ToInt64(await x.ExecuteScalarAsync()??0L)==0)return;await Task.Delay(1000);}throw new TimeoutException("MetaTube 批量任务未在时限内结束。");}

static async Task<List<Snapshot>> SnapshotAsync(string database,IReadOnlyList<Sample> samples){var result=new List<Snapshot>();await using var c=await OpenAsync(database,true);foreach(Sample sample in samples){await using var x=c.CreateCommand();x.CommandText="""SELECT m.Id,m.Code,COALESCE(m.Title,''),COALESCE(m.Description,''),COALESCE(m.ReleaseDate,''),m.DurationSeconds,COALESCE(s.IsFavorite,0),CASE WHEN COALESCE(s.HasUserRating,0)=1 THEN s.UserRating END,COALESCE(s.Notes,''),(SELECT COUNT(*) FROM Images WHERE MovieId=m.Id),(SELECT COUNT(*) FROM NfoDocuments WHERE MovieId=m.Id),EXISTS(SELECT 1 FROM NfoDocuments WHERE MovieId=m.Id AND FilePath<>''),(SELECT COALESCE(json_group_array(t.Name),'[]') FROM Tags t JOIN MovieTags mt ON mt.TagId=t.Id WHERE mt.MovieId=m.Id AND t.Source='User'),(SELECT COUNT(*) FROM MovieActors WHERE MovieId=m.Id) FROM Movies m LEFT JOIN UserMovieState s ON s.MovieId=m.Id WHERE m.Id=$id""";x.Parameters.AddWithValue("$id",sample.Id);await using var r=await x.ExecuteReaderAsync();await r.ReadAsync();result.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt32(5),r.GetInt64(6)==1,r.IsDBNull(7)?null:r.GetDouble(7),r.GetString(8),r.GetInt32(9),r.GetInt32(10),r.GetInt64(11)==1,JsonSerializer.Deserialize<string[]>(r.GetString(12))??[],r.GetInt32(13)));}return result;}
static async Task<List<TaskEvidence>> ReadTasksAsync(string database,IReadOnlyList<long> ids){var list=new List<TaskEvidence>();await using var c=await OpenAsync(database,true);await using var x=c.CreateCommand();x.CommandText=$"SELECT Id,COALESCE(CurrentMovieId,0),Status,COALESCE(Stage,''),COALESCE(ErrorMessage,''),RetryCount,COALESCE(ResultJson,'') FROM Tasks WHERE Id IN ({string.Join(',',ids)}) ORDER BY Id";await using var r=await x.ExecuteReaderAsync();while(await r.ReadAsync())list.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt32(5),r.GetString(6)));return list;}
static async Task<List<object>> ReadTaskLogsAsync(string database,IReadOnlyList<long> ids){var list=new List<object>();await using var c=await OpenAsync(database,true);await using var x=c.CreateCommand();x.CommandText=$"SELECT TaskId,Level,Message,CreatedAt FROM TaskLogs WHERE TaskId IN ({string.Join(',',ids)}) ORDER BY Id";await using var r=await x.ExecuteReaderAsync();while(await r.ReadAsync())list.Add(new{taskId=r.GetInt64(0),level=r.GetString(1),message=r.GetString(2),createdAt=r.GetString(3)});return list;}
static string RenderReport(string root,string source,IReadOnlyList<Sample> samples,IReadOnlyList<TaskEvidence> tasks,int completed,int failed,int cancelled,int noResult,int providerMismatch,int networkFailures,int images,int nfos,int users,int protectedImages,int protectedNfos,long temporary,string integrity,long foreignKeys)=>$"""
    # Local Media Manager 0.4.3 MetaTube real smoke

    - Executed: {DateTimeOffset.Now:O}
    - Source database: `{source}` (opened read-only and copied with SQLite backup API)
    - Isolated root: `{root}`
    - Samples: {samples.Count}
    - Completed: {completed}
    - Failed: {failed}
    - Cancelled: {cancelled}
    - No result: {noResult}
    - Provider code mismatch: {providerMismatch}
    - Network/provider request failures: {networkFailures}
    - Images registered: {images}
    - NFO documents registered: {nfos}
    - User state/tag sets preserved: {users}/{samples.Count}
    - Existing image files preserved: {protectedImages}/{samples.Count(s=>s.ProtectedImageHash is not null)}
    - Existing user NFO preserved: {protectedNfos}/{samples.Count(s=>s.ProtectedNfoText is not null)}
    - Temporary files remaining: {temporary}
    - `integrity_check`: `{integrity}`
    - `foreign_key_check` rows: {foreignKeys}
    - Retried tasks: {tasks.Count(t=>t.RetryCount>0)}

    Evidence: `evidence/before.json`, `evidence/after.json`, `evidence/tasks.json`, `evidence/task-logs.json`, and Bridge logs under `logs/`.
    """;
static async Task<SqliteConnection> OpenAsync(string path,bool readOnly){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWrite,Cache=SqliteCacheMode.Private}.ToString());await c.OpenAsync();if(!readOnly)await ExecuteAsync(c,"PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");return c;}
static async Task ExecuteAsync(SqliteConnection c,string sql,params(string,object?)[] p){await using var x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);await x.ExecuteNonQueryAsync();}
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
