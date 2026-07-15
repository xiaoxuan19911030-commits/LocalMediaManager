using System.Collections.Concurrent;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record MetadataSyncLaunchResult(long TaskId, string Status, string Message);
public sealed record SyncMovie(long Id, string Code, string? Title, string? Description, string? ReleaseDate,
    int DurationSeconds, string? PrimaryFile, string? NfoPath);
public sealed record SavedImage(string Type, string Path, string SourceUrl, long Size, bool Created);
public sealed record PreparedFiles(IReadOnlyList<SavedImage> Images, string? NfoPath, IReadOnlyList<string> CreatedPaths);

public sealed class TaskLogService(string databasePath)
{
    public async Task WriteAsync(long taskId, string level, string message, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO TaskLogs(TaskId,Level,Message,CreatedAt) VALUES($task,$level,$message,$at)";
        command.Parameters.AddWithValue("$task", taskId);
        command.Parameters.AddWithValue("$level", level);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private async Task<SqliteConnection> OpenAsync() {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Cache = SqliteCacheMode.Shared }.ToString());
        await connection.OpenAsync(); return connection;
    }
}

public sealed class ImageDownloadService(IHttpClientFactory clients)
{
    public async Task<IReadOnlyList<SavedImage>> DownloadAsync(string imageRoot, string code,
        IReadOnlyList<MetadataImage> images, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var saved = new List<SavedImage>();
        if (images.Count == 0) return saved;
        string safeCode = SafeFileName(code);
        using HttpClient client = clients.CreateClient("MetadataImages");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        int previewIndex = 0;
        foreach (MetadataImage image in images) {
            cancellationToken.ThrowIfCancellationRequested();
            string folder = image.Type == "Poster" ? "SmallPic" : "ExtraPic";
            string suffix = image.Type == "Poster" ? "" : $"-{++previewIndex:00}";
            string targetDirectory = Path.Combine(imageRoot, folder);
            Directory.CreateDirectory(targetDirectory);
            string target = Path.Combine(targetDirectory, safeCode + suffix + Extension(image.Url));
            if (File.Exists(target)) {
                saved.Add(new(image.Type, target, image.Url, new FileInfo(target).Length, false));
                continue;
            }
            string temporary = target + ".lmm-download";
            try {
                using HttpResponseMessage response = await client.GetAsync(image.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (FileStream destination = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await source.CopyToAsync(destination, cancellationToken);
                if (new FileInfo(temporary).Length == 0) throw new InvalidDataException("下载的图片为空。");
                File.Move(temporary, target, false);
                saved.Add(new(image.Type, target, image.Url, new FileInfo(target).Length, true));
            } catch {
                if (File.Exists(temporary)) File.Delete(temporary);
                throw;
            }
        }
        return saved;
    }

    private static string SafeFileName(string value) {
        string result = string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }
    private static string Extension(string url) {
        string extension = Path.GetExtension(Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.AbsolutePath : url).ToLowerInvariant();
        return extension is ".png" or ".webp" or ".jpeg" ? extension : ".jpg";
    }
}

public sealed class NfoService
{
    public async Task<(string? Path, bool Created)> WriteAsync(SyncMovie movie, ProviderMetadata metadata, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(movie.PrimaryFile)) return (null, false);
        string path = Path.ChangeExtension(movie.PrimaryFile, ".nfo");
        if (File.Exists(path)) return (path, false);
        var root = new XElement("movie",
            new XElement("title", metadata.Title ?? movie.Title ?? metadata.Code),
            new XElement("originaltitle", metadata.Title ?? ""),
            new XElement("id", metadata.Code),
            new XElement("plot", metadata.Description ?? ""),
            new XElement("premiered", metadata.ReleaseDate ?? ""),
            new XElement("runtime", metadata.DurationSeconds is > 0 ? metadata.DurationSeconds / 60 : 0),
            new XElement("director", metadata.Director ?? ""),
            new XElement("studio", metadata.Studio ?? ""),
            metadata.Genres.Select(value => new XElement("genre", value)),
            metadata.Actors.Select(value => new XElement("actor", new XElement("name", value))),
            new XElement("source", metadata.Provider),
            new XElement("sourceid", metadata.ExternalId));
        string temporary = path + ".lmm-write";
        await File.WriteAllTextAsync(temporary, new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).ToString(), cancellationToken);
        File.Move(temporary, path, false);
        return (path, true);
    }
}

public sealed class MetadataWriteService(string databasePath)
{
    public async Task<string> ApplyAsync(long taskId, SyncMovie movie, ProviderMetadata metadata,
        PreparedFiles files, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string before = JsonSerializer.Serialize(new { movie.Title, movie.Description, movie.ReleaseDate, movie.DurationSeconds, movie.NfoPath });
        long snapshotId = await InsertIdAsync(connection, transaction, """
            INSERT INTO MetadataSyncSnapshots(TaskId,MovieId,Provider,BeforeJson,CreatedAt)
            VALUES($task,$movie,$provider,$before,$at); SELECT last_insert_rowid();
            """, ("$task", taskId), ("$movie", movie.Id), ("$provider", metadata.Provider), ("$before", before), ("$at", Now()));

        await ExecuteAsync(connection, transaction, """
            UPDATE Movies SET
              Code=CASE WHEN trim(ifnull(Code,''))='' THEN $code ELSE Code END,
              Title=CASE WHEN trim(ifnull(Title,''))='' OR Title=Code THEN COALESCE($title,Title) ELSE Title END,
              SortTitle=CASE WHEN trim(ifnull(SortTitle,''))='' OR SortTitle=Code THEN COALESCE($title,SortTitle) ELSE SortTitle END,
              Description=CASE WHEN trim(ifnull(Description,''))='' THEN $description ELSE Description END,
              ReleaseDate=CASE WHEN trim(ifnull(ReleaseDate,''))='' THEN $release ELSE ReleaseDate END,
              DurationSeconds=CASE WHEN DurationSeconds=0 THEN COALESCE($duration,0) ELSE DurationSeconds END,
              NfoPath=CASE WHEN trim(ifnull(NfoPath,''))='' THEN $nfo ELSE NfoPath END,
              IsScraped=1,ScrapeStatus='complete',UpdatedAt=$at
            WHERE Id=$movie
            """, ("$code", metadata.Code), ("$title", metadata.Title), ("$description", metadata.Description),
            ("$release", metadata.ReleaseDate), ("$duration", metadata.DurationSeconds), ("$nfo", files.NfoPath),
            ("$at", Now()), ("$movie", movie.Id));
        await ExecuteAsync(connection, transaction,
            "INSERT OR IGNORE INTO ExternalIds(EntityType,EntityId,Provider,ExternalId) VALUES('Movie',$movie,$provider,$external)",
            ("$movie", movie.Id), ("$provider", metadata.Provider), ("$external", metadata.ExternalId));

        foreach (string genre in metadata.Genres) {
            long id = await EnsureNamedAsync(connection, transaction, "Genres", genre);
            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieGenres(MovieId,GenreId) VALUES($movie,$id)", ("$movie", movie.Id), ("$id", id));
        }
        foreach (string actor in metadata.Actors) {
            long id = await EnsureActorAsync(connection, transaction, actor);
            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieActors(MovieId,ActorId,RoleName,SortOrder) VALUES($movie,$id,'',999)", ("$movie", movie.Id), ("$id", id));
        }
        foreach ((string? name, string relation) in new[] { (metadata.Studio, "Studio"), (metadata.Publisher, "Publisher") }) {
            if (string.IsNullOrWhiteSpace(name)) continue;
            long id = await EnsureNamedAsync(connection, transaction, "Studios", name);
            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieStudios(MovieId,StudioId,RelationType) VALUES($movie,$id,$type)", ("$movie", movie.Id), ("$id", id), ("$type", relation));
        }
        if (!string.IsNullOrWhiteSpace(metadata.Series)) {
            long id = await EnsureNamedAsync(connection, transaction, "Series", metadata.Series);
            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO MovieSeries(MovieId,SeriesId,SortOrder) VALUES($movie,$id,0)", ("$movie", movie.Id), ("$id", id));
        }
        foreach (SavedImage image in files.Images.Where(value => value.Created))
            await ExecuteAsync(connection, transaction, """
                INSERT OR IGNORE INTO Images(MovieId,ActorId,ImageType,FilePath,SourceUrl,FileSize,IsPrimary,SourceProvider,DownloadedAt,CreatedAt,UpdatedAt)
                VALUES($movie,NULL,$type,$path,$url,$size,$primary,$provider,$at,$at,$at)
                """, ("$movie", movie.Id), ("$type", image.Type), ("$path", image.Path), ("$url", image.SourceUrl),
                ("$size", image.Size), ("$primary", image.Type == "Poster" ? 1 : 0), ("$provider", metadata.Provider), ("$at", Now()));
        string applied = JsonSerializer.Serialize(new { metadata.Provider, metadata.ExternalId, metadata.Code, metadata.Title,
            ImagesDownloaded = files.Images.Count(value => value.Created), ImagesPreserved = files.Images.Count(value => !value.Created),
            Genres = metadata.Genres.Count, Actors = metadata.Actors.Count, NonDestructive = true });
        await ExecuteAsync(connection, transaction, "UPDATE MetadataSyncSnapshots SET AppliedJson=$applied,AppliedAt=$at WHERE Id=$id",
            ("$applied", applied), ("$at", Now()), ("$id", snapshotId));
        await transaction.CommitAsync(cancellationToken);
        return applied;
    }

    private static async Task<long> EnsureNamedAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string table, string value) {
        string name = value.Trim(); string normalized = Normalize(name);
        string idColumn = table == "Genres" ? "GenreId" : table == "Studios" ? "StudioId" : "SeriesId";
        _ = idColumn;
        long existing = await ScalarLongAsync(c, tx, $"SELECT COALESCE(MAX(Id),0) FROM {table} WHERE NormalizedName=$name", ("$name", normalized));
        if (existing > 0) return existing;
        string columns = table == "Genres" ? "Name,NormalizedName" : table == "Studios" ? "Name,NormalizedName,Description" : "Name,NormalizedName,Description,ExternalId";
        string values = table == "Genres" ? "$name,$normalized" : table == "Studios" ? "$name,$normalized,NULL" : "$name,$normalized,NULL,NULL";
        return await InsertIdAsync(c, tx, $"INSERT INTO {table}({columns}) VALUES({values}); SELECT last_insert_rowid();", ("$name", name), ("$normalized", normalized));
    }
    private static async Task<long> EnsureActorAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string value) {
        string name = value.Trim(); string normalized = Normalize(name);
        long existing = await ScalarLongAsync(c, tx, "SELECT COALESCE(MAX(Id),0) FROM Actors WHERE NormalizedName=$name", ("$name", normalized));
        if (existing > 0) return existing;
        return await InsertIdAsync(c, tx, """
            INSERT INTO Actors(Name,NormalizedName,LegacySource,CreatedAt,UpdatedAt)
            VALUES($name,$normalized,'MetaTube',$at,$at); SELECT last_insert_rowid();
            """, ("$name", name), ("$normalized", normalized), ("$at", Now()));
    }
    private async Task<SqliteConnection> OpenAsync() { var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=databasePath, Mode=SqliteOpenMode.ReadWrite, Cache=SqliteCacheMode.Shared }.ToString()); await c.OpenAsync(); return c; }
    private static string Normalize(string value) => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    private static async Task ExecuteAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string sql, params (string,object?)[] p) { await using var x=c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText=sql; foreach(var (n,v) in p)x.Parameters.AddWithValue(n,v??DBNull.Value); await x.ExecuteNonQueryAsync(); }
    private static async Task<long> InsertIdAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string sql, params (string,object?)[] p) { await using var x=c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText=sql; foreach(var (n,v) in p)x.Parameters.AddWithValue(n,v??DBNull.Value); return Convert.ToInt64(await x.ExecuteScalarAsync()); }
    private static async Task<long> ScalarLongAsync(SqliteConnection c, System.Data.Common.DbTransaction tx, string sql, params (string,object?)[] p) { await using var x=c.CreateCommand(); x.Transaction=(SqliteTransaction)tx; x.CommandText=sql; foreach(var (n,v) in p)x.Parameters.AddWithValue(n,v??DBNull.Value); return Convert.ToInt64(await x.ExecuteScalarAsync()??0L); }
}

public sealed class MetadataSyncExecutor(
    string databasePath,
    string imageRoot,
    MetadataProviderSettingsService settingsService,
    IMetadataProvider provider,
    MetadataWriteService writer,
    ImageDownloadService images,
    NfoService nfo,
    TaskLogService logs) : BackgroundService
{
    private static readonly string[] ActiveStates = ["Preparing", "FetchingMetadata", "DownloadingImages", "WritingMetadata", "WritingNfo", "Running"];
    private readonly ConcurrentDictionary<long, CancellationTokenSource> cancellations = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested) {
            try {
                MetaTubeSettingsDto settings = await settingsService.ReadMetaTubeAsync();
                long? taskId = settings.Enabled && settings.AutoExecute ? await ClaimAsync(stoppingToken) : null;
                if (taskId is null) { await Task.Delay(750, stoppingToken); continue; }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cancellations[taskId.Value] = linked;
                try { await ExecuteOneAsync(taskId.Value, settings, linked.Token); }
                finally { cancellations.TryRemove(taskId.Value, out _); }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { Console.Error.WriteLine($"Metadata sync runner: {error}"); await Task.Delay(1000, stoppingToken); }
        }
    }

    public async Task<TaskMutationResult> PauseAsync(long taskId) {
        await UpdateTaskAsync(taskId, "Paused", "Paused", null, null, false);
        await logs.WriteAsync(taskId, "Info", "同步任务已暂停；当前网络步骤完成后停止推进。");
        return new(taskId, "Paused", "同步任务已暂停。");
    }
    public async Task<TaskMutationResult> ResumeAsync(long taskId) {
        await UpdateTaskAsync(taskId, "Pending", "Pending", null, null, false);
        await logs.WriteAsync(taskId, "Info", "同步任务已继续并重新进入队列。");
        return new(taskId, "Pending", "同步任务已继续。");
    }
    public async Task<TaskMutationResult> CancelAsync(long taskId) {
        if (cancellations.TryGetValue(taskId, out CancellationTokenSource? source)) source.Cancel();
        await UpdateTaskAsync(taskId, "Cancelled", "Cancelled", null, "用户取消", true);
        await logs.WriteAsync(taskId, "Warning", "同步任务已由用户取消。");
        return new(taskId, "Cancelled", "同步任务已取消；已有有效数据未被覆盖。");
    }
    public async Task<MetadataSyncLaunchResult> RetryAsync(long taskId) {
        await using var connection = await OpenAsync();
        string? status = await ScalarTextAsync(connection, "SELECT Status FROM Tasks WHERE Id=$id AND TaskType='Sync'", ("$id", taskId));
        if (status is null) throw new KeyNotFoundException("同步任务不存在。");
        if (status is not ("Failed" or "Cancelled")) throw new InvalidOperationException("只有失败或已取消的同步任务可以重试。");
        await ExecuteAsync(connection, "UPDATE Tasks SET Status='Retrying',Stage='Retrying',RetryCount=RetryCount+1,ErrorMessage=NULL,CompletedAt=NULL,CancellationRequested=0,UpdatedAt=$at WHERE Id=$id", ("$at", Now()), ("$id", taskId));
        await logs.WriteAsync(taskId, "Info", "同步任务已进入重试队列。");
        return new(taskId, "Retrying", "同步任务已进入重试队列。");
    }
    public async Task<MetadataSyncLaunchResult> EnqueueAsync(long movieId, string trigger) {
        await using var connection = await OpenAsync();
        if (await ScalarLongAsync(connection, "SELECT COUNT(*) FROM Movies WHERE Id=$id", ("$id", movieId)) == 0) throw new KeyNotFoundException("影片不存在。");
        long existing = await ScalarLongAsync(connection, "SELECT COALESCE(MAX(Id),0) FROM Tasks WHERE TaskType='Sync' AND CurrentMovieId=$movie AND Status NOT IN ('Completed','Failed','Cancelled')", ("$movie", movieId));
        if (existing > 0) return new(existing, "Pending", "该影片已有同步任务。");
        long id = await InsertIdAsync(connection, """
            INSERT INTO Tasks(TaskType,Status,Stage,Provider,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt,CurrentMovieId)
            VALUES('Sync','Pending','Pending','MetaTube',0,1,0,$payload,$at,$at,$movie); SELECT last_insert_rowid();
            """, ("$payload", JsonSerializer.Serialize(new { MovieId=movieId, Trigger=trigger })), ("$at", Now()), ("$movie", movieId));
        await logs.WriteAsync(id, "Info", $"同步任务已创建（{trigger}）。");
        return new(id, "Pending", "同步任务已创建。");
    }

    private async Task ExecuteOneAsync(long taskId, MetaTubeSettingsDto settings, CancellationToken cancellationToken)
    {
        var createdPaths = new List<string>();
        try {
            SyncMovie movie = await ReadMovieAsync(taskId, cancellationToken);
            await StageAsync(taskId, "Preparing", 8, $"准备影片 {movie.Code}", cancellationToken);
            await EnsureRunnableAsync(taskId, cancellationToken);
            if (string.IsNullOrWhiteSpace(movie.Code)) throw new InvalidOperationException("影片没有可用于同步的番号。");

            await StageAsync(taskId, "FetchingMetadata", 22, $"MetaTube 搜索：{movie.Code}", cancellationToken);
            IReadOnlyList<MetadataSearchResult> results = await provider.SearchAsync(movie.Code, settings, cancellationToken);
            MetadataSearchResult? selected = results.FirstOrDefault();
            if (selected is null) throw new InvalidOperationException($"MetaTube 未找到 {movie.Code} 的结果。");
            ProviderMetadata? metadata = await provider.GetMetadataAsync(selected, settings, cancellationToken);
            if (metadata is null) throw new InvalidOperationException("MetaTube 返回结果缺少可用元数据。");
            if (!Comparable(metadata.Code).Equals(Comparable(movie.Code), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"MetaTube 结果番号不匹配：期望 {movie.Code}，实际 {metadata.Code}。");

            IReadOnlyList<SavedImage> savedImages = [];
            if (settings.DownloadImages) {
                await StageAsync(taskId, "DownloadingImages", 48, $"下载图片（{metadata.Images.Count} 项）", cancellationToken);
                savedImages = await images.DownloadAsync(imageRoot, movie.Code, await provider.GetImagesAsync(metadata, cancellationToken), settings.TimeoutSeconds, cancellationToken);
                createdPaths.AddRange(savedImages.Where(value => value.Created).Select(value => value.Path));
            }
            string? nfoPath = null;
            if (settings.WriteNfo) {
                await StageAsync(taskId, "WritingNfo", 68, "生成 NFO（已有文件不会覆盖）", cancellationToken);
                (nfoPath, bool created) = await nfo.WriteAsync(movie, metadata, cancellationToken);
                if (created && nfoPath is not null) createdPaths.Add(nfoPath);
            }
            await StageAsync(taskId, "WritingMetadata", 82, "非破坏合并元数据", cancellationToken);
            string summary = await writer.ApplyAsync(taskId, movie, metadata, new(savedImages, nfoPath, createdPaths), cancellationToken);
            await CompleteAsync(taskId, summary, cancellationToken);
        } catch (OperationCanceledException) {
            await SafeDeleteAsync(createdPaths);
            await MarkCancelledAsync(taskId);
        } catch (Exception error) {
            await SafeDeleteAsync(createdPaths);
            await FailAsync(taskId, error);
        }
    }

    private async Task RecoverInterruptedAsync(CancellationToken token) {
        await using var connection = await OpenAsync();
        var interrupted = new List<long>();
        await using (var command = connection.CreateCommand()) {
            command.CommandText = $"SELECT Id FROM Tasks WHERE TaskType='Sync' AND Status IN ({string.Join(',', ActiveStates.Select((_,i)=>"$s"+i))})";
            for(int i=0;i<ActiveStates.Length;i++) command.Parameters.AddWithValue("$s"+i,ActiveStates[i]);
            await using var reader = await command.ExecuteReaderAsync(token); while(await reader.ReadAsync(token)) interrupted.Add(reader.GetInt64(0));
        }
        foreach(long id in interrupted) {
            await ExecuteAsync(connection, "UPDATE Tasks SET Status='Retrying',Stage='Retrying',RetryCount=RetryCount+1,ErrorMessage='上次异常中断',UpdatedAt=$at WHERE Id=$id", ("$at",Now()),("$id",id));
            await logs.WriteAsync(id,"Warning","上次异常中断，任务已恢复到可重试队列。",token);
        }
    }
    private async Task<long?> ClaimAsync(CancellationToken token) {
        await using var connection = await OpenAsync(); await using var tx=await connection.BeginTransactionAsync(token);
        long id=await ScalarLongAsync(connection,"SELECT COALESCE(MIN(Id),0) FROM Tasks WHERE TaskType='Sync' AND Status IN ('Pending','Retrying')",tx);
        if(id==0){await tx.RollbackAsync(token);return null;}
        await ExecuteAsync(connection,"UPDATE Tasks SET Status='Preparing',Stage='Preparing',Provider='MetaTube',Progress=3,StartedAt=COALESCE(StartedAt,$at),UpdatedAt=$at,CancellationRequested=0 WHERE Id=$id",tx,("$at",Now()),("$id",id));
        await tx.CommitAsync(token); return id;
    }
    private async Task<SyncMovie> ReadMovieAsync(long taskId,CancellationToken token) {
        await using var c=await OpenAsync(); await using var x=c.CreateCommand(); x.CommandText="""
            SELECT m.Id,COALESCE(m.Code,''),m.Title,m.Description,m.ReleaseDate,m.DurationSeconds,m.NfoPath,
                   (SELECT FilePath FROM MediaFiles WHERE MovieId=m.Id AND IsPrimary=1 AND MediaType='Video' ORDER BY Id LIMIT 1)
            FROM Tasks t JOIN Movies m ON m.Id=COALESCE(t.CurrentMovieId,json_extract(t.PayloadJson,'$.MovieId')) WHERE t.Id=$task
            """; x.Parameters.AddWithValue("$task",taskId); await using var r=await x.ExecuteReaderAsync(token);
        if(!await r.ReadAsync(token))throw new KeyNotFoundException("同步任务关联的影片不存在。");
        return new(r.GetInt64(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),r.GetInt32(5),r.IsDBNull(7)?null:r.GetString(7),r.IsDBNull(6)?null:r.GetString(6));
    }
    private async Task StageAsync(long id,string stage,double progress,string message,CancellationToken token){await EnsureRunnableAsync(id,token);await using var c=await OpenAsync();await ExecuteAsync(c,"UPDATE Tasks SET Status=$stage,Stage=$stage,Progress=$progress,UpdatedAt=$at WHERE Id=$id",("$stage",stage),("$progress",progress),("$at",Now()),("$id",id));await logs.WriteAsync(id,"Info",message,token);}
    private async Task EnsureRunnableAsync(long id,CancellationToken token){while(true){token.ThrowIfCancellationRequested();await using var c=await OpenAsync();string? s=await ScalarTextAsync(c,"SELECT Status FROM Tasks WHERE Id=$id",("$id",id));if(s=="Cancelled")throw new OperationCanceledException(token);if(s!="Paused")return;await Task.Delay(250,token);}}
    private async Task CompleteAsync(long id,string summary,CancellationToken token){await using var c=await OpenAsync();await ExecuteAsync(c,"UPDATE Tasks SET Status='Completed',Stage='Completed',Progress=100,CompletedItems=1,ResultJson=$result,ResultSummary='元数据同步完成',ErrorMessage=NULL,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",("$result",summary),("$at",Now()),("$id",id));await logs.WriteAsync(id,"Info","元数据、图片与 NFO 工作流已完成。",token);}
    private async Task FailAsync(long id,Exception error){try{await using var c=await OpenAsync();await ExecuteAsync(c,"UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,ResultSummary='同步失败，已有有效数据未被覆盖',CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",("$error",error.Message),("$at",Now()),("$id",id));await logs.WriteAsync(id,"Error",error.Message);}catch(Exception e){Console.Error.WriteLine($"Could not persist sync failure {id}: {e}");}}
    private async Task MarkCancelledAsync(long id){try{await using var c=await OpenAsync();await ExecuteAsync(c,"UPDATE Tasks SET Status='Cancelled',Stage='Cancelled',ResultSummary='用户取消',CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",("$at",Now()),("$id",id));}catch(Exception e){Console.Error.WriteLine(e);}}
    private async Task UpdateTaskAsync(long id,string status,string stage,double? progress,string? error,bool cancel){await using var c=await OpenAsync();if(await ScalarLongAsync(c,"SELECT COUNT(*) FROM Tasks WHERE Id=$id AND TaskType='Sync'",("$id",id))==0)throw new KeyNotFoundException("同步任务不存在。");await ExecuteAsync(c,"UPDATE Tasks SET Status=$status,Stage=$stage,Progress=COALESCE($progress,Progress),ErrorMessage=$error,CancellationRequested=$cancel,UpdatedAt=$at WHERE Id=$id",("$status",status),("$stage",stage),("$progress",progress),("$error",error),("$cancel",cancel?1:0),("$at",Now()),("$id",id));}
    private async Task<SqliteConnection> OpenAsync(){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=databasePath,Mode=SqliteOpenMode.ReadWrite,Cache=SqliteCacheMode.Shared}.ToString());await c.OpenAsync();await ExecuteAsync(c,"PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");return c;}
    private static async Task SafeDeleteAsync(IEnumerable<string> paths){foreach(string p in paths.Reverse())try{if(File.Exists(p))File.Delete(p);}catch{await Task.Yield();}}
    private static string Comparable(string value)=>value.Replace("-","").Replace("_","").Replace(" ","").Trim();
    private static string Now()=>DateTimeOffset.UtcNow.ToString("O");
    private static Task ExecuteAsync(SqliteConnection c,string sql,params (string,object?)[] p)=>ExecuteAsync(c,sql,null,p);
    private static async Task ExecuteAsync(SqliteConnection c,string sql,System.Data.Common.DbTransaction? tx,params (string,object?)[] p){await using var x=c.CreateCommand();x.Transaction=(SqliteTransaction?)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);await x.ExecuteNonQueryAsync();}
    private static async Task<long> InsertIdAsync(SqliteConnection c,string sql,params (string,object?)[] p){await using var x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return Convert.ToInt64(await x.ExecuteScalarAsync());}
    private static Task<long> ScalarLongAsync(SqliteConnection c,string sql,params (string,object?)[] p)=>ScalarLongAsync(c,sql,null,p);
    private static async Task<long> ScalarLongAsync(SqliteConnection c,string sql,System.Data.Common.DbTransaction? tx,params (string,object?)[] p){await using var x=c.CreateCommand();x.Transaction=(SqliteTransaction?)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return Convert.ToInt64(await x.ExecuteScalarAsync()??0L);}
    private static async Task<string?> ScalarTextAsync(SqliteConnection c,string sql,params (string,object?)[] p){await using var x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return(await x.ExecuteScalarAsync())?.ToString();}
}

public sealed class TaskCommandService(string databasePath, LibraryWorkflowService libraries, MetadataSyncExecutor sync)
{
    public async Task<TaskMutationResult> PauseAsync(long id)=>await TypeAsync(id)=="Sync"?await sync.PauseAsync(id):await libraries.PauseTaskAsync(id);
    public async Task<TaskMutationResult> ResumeAsync(long id)=>await TypeAsync(id)=="Sync"?await sync.ResumeAsync(id):await libraries.ResumeTaskAsync(id);
    public async Task<TaskMutationResult> CancelAsync(long id)=>await TypeAsync(id)=="Sync"?await sync.CancelAsync(id):await libraries.CancelTaskAsync(id);
    public async Task<object> RetryAsync(long id)=>await TypeAsync(id)=="Sync"?await sync.RetryAsync(id):await libraries.RetryTaskAsync(id);
    private async Task<string> TypeAsync(long id){await using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=databasePath,Mode=SqliteOpenMode.ReadOnly}.ToString());await c.OpenAsync();await using var x=c.CreateCommand();x.CommandText="SELECT TaskType FROM Tasks WHERE Id=$id";x.Parameters.AddWithValue("$id",id);return(await x.ExecuteScalarAsync())?.ToString()??throw new KeyNotFoundException("任务不存在。");}
}
