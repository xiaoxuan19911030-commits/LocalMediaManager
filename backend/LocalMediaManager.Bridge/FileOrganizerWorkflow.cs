using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace LocalMediaManager.Bridge;

public sealed record OrganizerPlanCommand(IReadOnlyList<long> MovieIds, string FileNameTemplate,
    string? DestinationDirectory);
public sealed record OrganizerItem(long MovieId, long MediaFileId, string SourcePath, string DestinationPath,
    string Operation, bool Valid, string? Conflict, long FileSize);
public sealed record OrganizerPreview(long TaskId, string Status, string ConfirmationToken,
    IReadOnlyList<OrganizerItem> Items, IReadOnlyList<string> Warnings, long ValidItems, long ConflictItems);
public sealed record OrganizerExecuteCommand(string ConfirmationToken);
public sealed record OrganizerLaunchResult(long TaskId, string Status, long TotalItems, string Message);

public sealed class FileOrganizerService(
    string databasePath,
    TaskLogService logs) : BackgroundService
{
    private readonly ConcurrentDictionary<long, CancellationTokenSource> cancellations = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await RecoverInterruptedAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception error) { Console.Error.WriteLine($"File organizer recovery skipped: {error}"); }
        while (!stoppingToken.IsCancellationRequested) {
            try {
                long? taskId = await ClaimAsync(stoppingToken);
                if (taskId is null) { await Task.Delay(750, stoppingToken); continue; }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cancellations[taskId.Value] = linked;
                try { await ExecuteTaskAsync(taskId.Value, linked.Token); }
                finally { cancellations.TryRemove(taskId.Value, out _); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { Console.Error.WriteLine($"Organizer runner: {error}"); await Task.Delay(1000, stoppingToken); }
        }
    }

    public async Task<OrganizerPreview> DryRunAsync(OrganizerPlanCommand input,
        CancellationToken cancellationToken = default)
    {
        long[] movieIds = input.MovieIds.Where(id => id > 0).Distinct().Order().ToArray();
        if (movieIds.Length == 0) throw new ArgumentException("至少选择一部影片。");
        if (movieIds.Length > 500) throw new ArgumentException("单次整理最多 500 部影片。");
        string template = string.IsNullOrWhiteSpace(input.FileNameTemplate) ? "{Code}" : input.FileNameTemplate.Trim();
        ValidateTemplate(template);
        string? destination = string.IsNullOrWhiteSpace(input.DestinationDirectory)
            ? null : Path.GetFullPath(input.DestinationDirectory.Trim());
        string now = Now();
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long taskId = await InsertIdAsync(connection, transaction, """
            INSERT INTO Tasks(TaskType,Status,Stage,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt)
            VALUES('Organizer','PreviewReady','DryRun',0,0,0,$payload,$at,$at); SELECT last_insert_rowid();
            """, cancellationToken, ("$payload", JsonSerializer.Serialize(new { MovieIds=movieIds, FileNameTemplate=template, DestinationDirectory=destination })), ("$at", now));
        IReadOnlyList<OrganizerItem> items = await BuildItemsAsync(connection, transaction, movieIds, template, destination, cancellationToken);
        foreach (OrganizerItem item in items) {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO FileOperationJournal(TaskId,MovieId,MediaFileId,SourcePath,DestinationPath,OperationType,Status,SourceFingerprint,ErrorMessage,CreatedAt,UpdatedAt)
                VALUES($task,$movie,$file,$source,$destination,$operation,'Planned',$fingerprint,$error,$at,$at)
                """, ("$task", taskId), ("$movie", item.MovieId), ("$file", item.MediaFileId),
                ("$source", item.SourcePath), ("$destination", item.DestinationPath), ("$operation", item.Operation),
                ("$fingerprint", Fingerprint(item.SourcePath)), ("$error", item.Conflict), ("$at", now));
        }
        await ExecuteAsync(connection, transaction,
            "UPDATE Tasks SET TotalItems=$total,ResultSummary=$summary WHERE Id=$id",
            ("$total", items.Count), ("$summary", $"Dry Run：{items.Count(item => item.Valid)} 可执行，{items.Count(item => !item.Valid)} 冲突。"), ("$id", taskId));
        await transaction.CommitAsync(cancellationToken);
        await logs.WriteAsync(taskId, "Info", "文件整理 Dry Run 已完成；尚未修改任何文件路径。", cancellationToken);
        return await PreviewAsync(taskId, cancellationToken);
    }

    public async Task<OrganizerPreview> PreviewAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadOnly, cancellationToken);
        string? status = await ScalarTextAsync(connection,
            "SELECT Status FROM Tasks WHERE Id=$id AND TaskType='Organizer'", cancellationToken, ("$id", taskId));
        if (status is null) throw new KeyNotFoundException("整理任务不存在。");
        var items = new List<OrganizerItem>();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT MovieId,MediaFileId,SourcePath,DestinationPath,OperationType,ErrorMessage FROM FileOperationJournal WHERE TaskId=$task ORDER BY Id";
        command.Parameters.AddWithValue("$task", taskId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) {
            string source = reader.GetString(2); string destination = reader.GetString(3);
            string? conflict = reader.IsDBNull(5) ? ValidateCurrent(source, destination) : reader.GetString(5);
            items.Add(new(reader.GetInt64(0), reader.GetInt64(1), source, destination, reader.GetString(4),
                conflict is null, conflict, File.Exists(source) ? new FileInfo(source).Length : 0));
        }
        string token = PreviewToken(taskId, items);
        return new(taskId, status, token, items,
            ["执行前会再次校验源文件指纹和目标冲突；目标文件已存在时绝不覆盖。"],
            items.LongCount(item => item.Valid), items.LongCount(item => !item.Valid));
    }

    public async Task<OrganizerLaunchResult> ExecuteConfirmedAsync(long taskId, string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        OrganizerPreview preview = await PreviewAsync(taskId, cancellationToken);
        Verify(preview.ConfirmationToken, confirmationToken);
        if (preview.ConflictItems > 0) throw new InvalidOperationException("预览仍有冲突，必须修正后重新 Dry Run。");
        await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, cancellationToken);
        int changed = await ExecutePlainAsync(connection,
            "UPDATE Tasks SET Status='Pending',Stage='Pending',ErrorMessage=NULL,CompletedAt=NULL,CancellationRequested=0,UpdatedAt=$at WHERE Id=$id AND TaskType='Organizer' AND Status='PreviewReady'",
            cancellationToken, ("$at", Now()), ("$id", taskId));
        if (changed == 0) throw new InvalidOperationException("整理任务不在可执行的预览状态。");
        await logs.WriteAsync(taskId, "Info", "用户已确认整理预览，任务进入执行队列。", cancellationToken);
        return new(taskId, "Pending", preview.ValidItems, "文件整理任务已进入任务中心。");
    }

    public Task<TaskMutationResult> PauseAsync(long id) => UpdateCommandAsync(id, "Paused", "Paused", "文件整理任务已暂停。", false);
    public Task<TaskMutationResult> ResumeAsync(long id) => UpdateCommandAsync(id, "Pending", "Pending", "文件整理任务已继续。", false);
    public async Task<TaskMutationResult> CancelAsync(long id) { if(cancellations.TryGetValue(id,out CancellationTokenSource? source))source.Cancel();return await UpdateCommandAsync(id,"Cancelled","Cancelled","文件整理任务已取消；已完成项目保留在日志中。",true); }
    public async Task<OrganizerLaunchResult> RetryAsync(long id) { await using SqliteConnection c=await OpenAsync(SqliteOpenMode.ReadWrite);string? status=await ScalarTextAsync(c,"SELECT Status FROM Tasks WHERE Id=$id AND TaskType='Organizer'",CancellationToken.None,("$id",id));if(status is null)throw new KeyNotFoundException("整理任务不存在。");if(status is not ("Failed" or "Cancelled"))throw new InvalidOperationException("只有失败或已取消的整理任务可以重试。");await ExecutePlainAsync(c,"UPDATE Tasks SET Status='Pending',Stage='Pending',ErrorMessage=NULL,CompletedAt=NULL,CancellationRequested=0,RetryCount=RetryCount+1,UpdatedAt=$at WHERE Id=$id",CancellationToken.None,("$at",Now()),("$id",id));long total=await ScalarLongAsync(c,"SELECT TotalItems FROM Tasks WHERE Id=$id",CancellationToken.None,("$id",id));return new(id,"Pending",total,"整理任务已重试。"); }

    private async Task ExecuteTaskAsync(long taskId, CancellationToken token)
    {
        int completed = 0, failed = 0;
        try {
            await using SqliteConnection connection = await OpenAsync(SqliteOpenMode.ReadWrite, token);
            await ExecutePlainAsync(connection,"UPDATE Tasks SET Status='Running',Stage='MovingFiles',StartedAt=COALESCE(StartedAt,$at),UpdatedAt=$at WHERE Id=$id",token,("$at",Now()),("$id",taskId));
            var rows = await ReadJournalAsync(connection, taskId, token);
            foreach (JournalRow row in rows) {
                token.ThrowIfCancellationRequested();
                string? status = await ScalarTextAsync(connection,"SELECT Status FROM Tasks WHERE Id=$id",token,("$id",taskId));
                if(status=="Paused")return;if(status=="Cancelled")return;
                try { await ExecuteItemAsync(connection, taskId, row, token); }
                catch(Exception error){failed++;await MarkJournalAsync(connection,row.Id,"Failed",error.Message,token);await logs.WriteAsync(taskId,"Error",$"整理失败：{row.SourcePath} → {row.DestinationPath}；{error.Message}",token);}
                completed++;double progress=rows.Count==0?100:completed*100d/rows.Count;
                await ExecutePlainAsync(connection,"UPDATE Tasks SET Progress=$progress,CompletedItems=$completed,CurrentMovieId=$movie,UpdatedAt=$at WHERE Id=$id",token,("$progress",progress),("$completed",completed),("$movie",row.MovieId),("$at",Now()),("$id",taskId));
            }
            string summary=$"文件整理完成：{completed-failed} 成功，{failed} 失败。";
            await ExecutePlainAsync(connection,"UPDATE Tasks SET Status=$status,Stage=$stage,Progress=100,ResultSummary=$summary,ErrorMessage=$error,CompletedAt=$at,UpdatedAt=$at,CurrentMovieId=NULL WHERE Id=$id",token,("$status",failed==0?"Completed":"Failed"),("$stage",failed==0?"Completed":"Failed"),("$summary",summary),("$error",failed==0?null:summary),("$at",Now()),("$id",taskId));
            await logs.WriteAsync(taskId,failed==0?"Info":"Warning",summary,token);
        } catch(OperationCanceledException){await using SqliteConnection c=await OpenAsync(SqliteOpenMode.ReadWrite);await ExecutePlainAsync(c,"UPDATE Tasks SET Status='Cancelled',Stage='Cancelled',CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",CancellationToken.None,("$at",Now()),("$id",taskId));}
        catch(Exception error){await using SqliteConnection c=await OpenAsync(SqliteOpenMode.ReadWrite);await ExecutePlainAsync(c,"UPDATE Tasks SET Status='Failed',Stage='Failed',ErrorMessage=$error,CompletedAt=$at,UpdatedAt=$at WHERE Id=$id",CancellationToken.None,("$error",error.Message),("$at",Now()),("$id",taskId));await logs.WriteAsync(taskId,"Error",error.Message);}
    }

    private async Task ExecuteItemAsync(SqliteConnection connection,long taskId,JournalRow row,CancellationToken token)
    {
        string? conflict=ValidateCurrent(row.SourcePath,row.DestinationPath);if(conflict is not null)throw new IOException(conflict);
        if(Fingerprint(row.SourcePath)!=row.SourceFingerprint)throw new IOException("源文件在预览后发生变化，请重新 Dry Run。");
        Directory.CreateDirectory(Path.GetDirectoryName(row.DestinationPath)!);
        File.Move(row.SourcePath,row.DestinationPath,false);
        await MarkJournalAsync(connection,row.Id,"Moved",null,token);
        try {
            await using var transaction=await connection.BeginTransactionAsync(token);
            await using(SqliteCommand update=connection.CreateCommand()){update.Transaction=(SqliteTransaction)transaction;update.CommandText="UPDATE MediaFiles SET FilePath=$path,NormalizedPath=$normalized,FileName=$name,UpdatedAt=$at WHERE Id=$id AND FilePath=$source";update.Parameters.AddWithValue("$path",row.DestinationPath);update.Parameters.AddWithValue("$normalized",NormalizePath(row.DestinationPath));update.Parameters.AddWithValue("$name",Path.GetFileName(row.DestinationPath));update.Parameters.AddWithValue("$at",Now());update.Parameters.AddWithValue("$id",row.MediaFileId);update.Parameters.AddWithValue("$source",row.SourcePath);if(await update.ExecuteNonQueryAsync(token)!=1)throw new InvalidOperationException("数据库路径在预览后发生变化，文件已恢复原路径。");}
            await ExecuteAsync(connection,transaction,"UPDATE FileOperationJournal SET Status='DbUpdated',UpdatedAt=$at,CompletedAt=$at WHERE Id=$id",("$at",Now()),("$id",row.Id));
            await ExecuteAsync(connection,transaction,"INSERT INTO OperationAudit(OperationType,EntityType,EntityId,BeforeJson,AfterJson,CreatedAt) VALUES('FileOrganize','MediaFile',$id,$before,$after,$at)",("$id",row.MediaFileId),("$before",JsonSerializer.Serialize(new{Path=row.SourcePath})),("$after",JsonSerializer.Serialize(new{Path=row.DestinationPath,TaskId=taskId})),("$at",Now()));
            await transaction.CommitAsync(token);
        } catch {
            if(File.Exists(row.DestinationPath)&&!File.Exists(row.SourcePath))File.Move(row.DestinationPath,row.SourcePath,false);
            await MarkJournalAsync(connection,row.Id,"RolledBack","数据库更新失败，文件已恢复原路径。",CancellationToken.None);
            throw;
        }
    }

    private async Task RecoverInterruptedAsync(CancellationToken token)
    {
        await using SqliteConnection connection=await OpenAsync(SqliteOpenMode.ReadWrite,token);
        await using SqliteCommand command=connection.CreateCommand();command.CommandText="SELECT Id,TaskId,SourcePath,DestinationPath FROM FileOperationJournal WHERE Status IN ('Planned','Moved') ORDER BY Id";
        var rows=new List<(long Id,long Task,string Source,string Destination)>();await using(SqliteDataReader reader=await command.ExecuteReaderAsync(token)){while(await reader.ReadAsync(token))rows.Add((reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetString(3)));}
        foreach(var row in rows){if(!File.Exists(row.Destination)||File.Exists(row.Source))continue;try{File.Move(row.Destination,row.Source,false);await MarkJournalAsync(connection,row.Id,"RolledBack","应用上次异常退出后已自动恢复原路径。",token);}catch(Exception error){await MarkJournalAsync(connection,row.Id,"RecoveryRequired",error.Message,token);}}
        await ExecutePlainAsync(connection,"UPDATE Tasks SET Status='Failed',Stage='RecoveryReport',ErrorMessage='上次整理异常中断；请查看任务日志和恢复报告。',UpdatedAt=$at WHERE TaskType='Organizer' AND Status IN ('Preparing','Running')",token,("$at",Now()));
    }

    private static async Task<IReadOnlyList<OrganizerItem>> BuildItemsAsync(SqliteConnection c,System.Data.Common.DbTransaction tx,long[] movieIds,string template,string? destination,CancellationToken token)
    {
        var items=new List<OrganizerItem>();
        foreach(long movieId in movieIds){
            string code="",title="",release="",actors="";
            await using(SqliteCommand movie=c.CreateCommand()){movie.Transaction=(SqliteTransaction)tx;movie.CommandText="""SELECT COALESCE(m.Code,''),COALESCE(m.Title,''),COALESCE(m.ReleaseDate,''),(SELECT group_concat(a.Name,' ') FROM MovieActors ma JOIN Actors a ON a.Id=ma.ActorId WHERE ma.MovieId=m.Id) FROM Movies m WHERE m.Id=$id""";movie.Parameters.AddWithValue("$id",movieId);await using SqliteDataReader r=await movie.ExecuteReaderAsync(token);if(!await r.ReadAsync(token))continue;code=r.GetString(0);title=r.GetString(1);release=r.GetString(2);actors=r.IsDBNull(3)?"":r.GetString(3);}
            var files=new List<(long Id,string Path,long Size)>();await using(SqliteCommand query=c.CreateCommand()){query.Transaction=(SqliteTransaction)tx;query.CommandText="SELECT Id,FilePath,COALESCE(FileSize,0) FROM MediaFiles WHERE MovieId=$id AND MediaType='Video' ORDER BY IsPrimary DESC,Id";query.Parameters.AddWithValue("$id",movieId);await using SqliteDataReader r=await query.ExecuteReaderAsync(token);while(await r.ReadAsync(token))files.Add((r.GetInt64(0),r.GetString(1),r.GetInt64(2)));}
            string baseName=ApplyTemplate(template,code,title,release,actors);
            for(int index=0;index<files.Count;index++){var file=files[index];string suffix=files.Count>1?$"-part{index+1}":"";string target=Path.Combine(destination??Path.GetDirectoryName(file.Path)!,baseName+suffix+Path.GetExtension(file.Path));string? conflict=ValidateCurrent(file.Path,target);long duplicate=await ScalarLongAsync(c,"SELECT COUNT(*) FROM MediaFiles WHERE Id<>$id AND NormalizedPath=$path",token,("$id",file.Id),("$path",NormalizePath(target)));if(duplicate>0)conflict="数据库中已有相同目标路径，禁止覆盖。";items.Add(new(movieId,file.Id,file.Path,target,destination is null?"Rename":"MoveAndRename",conflict is null,conflict,file.Size));}
        }
        return items;
    }
    private static string ApplyTemplate(string template,string code,string title,string release,string actors){string year=release.Length>=4?release[..4]:"";string value=template.Replace("{Code}",code,StringComparison.OrdinalIgnoreCase).Replace("{Title}",title,StringComparison.OrdinalIgnoreCase).Replace("{Year}",year,StringComparison.OrdinalIgnoreCase).Replace("{Actors}",actors,StringComparison.OrdinalIgnoreCase).Trim();foreach(char invalid in Path.GetInvalidFileNameChars())value=value.Replace(invalid,'_');while(value.EndsWith('.')||value.EndsWith(' '))value=value[..^1];if(string.IsNullOrWhiteSpace(value))throw new ArgumentException("整理后的文件名为空。");return value;}
    private static void ValidateTemplate(string template){string cleaned=template;foreach(string token in new[]{"{Code}","{Title}","{Year}","{Actors}"})cleaned=cleaned.Replace(token,"",StringComparison.OrdinalIgnoreCase);if(cleaned.Contains('{')||cleaned.Contains('}'))throw new ArgumentException("模板只支持 {Code}、{Title}、{Year}、{Actors}。");}
    private static string? ValidateCurrent(string source,string destination){if(!File.Exists(source))return "源文件不存在或磁盘不可用。";if(string.Equals(Path.GetFullPath(source),Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase))return "源路径与目标路径相同。";if(File.Exists(destination)||Directory.Exists(destination))return "目标已存在，禁止覆盖。";string? root=Path.GetPathRoot(destination);if(string.IsNullOrWhiteSpace(root)||!Directory.Exists(root))return "目标磁盘或共享路径不可用。";return null;}
    private static string Fingerprint(string path){if(!File.Exists(path))return "missing";var file=new FileInfo(path);return $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";}
    private static string PreviewToken(long taskId,IEnumerable<OrganizerItem> items){string state=string.Join('|',items.Select(item=>$"{item.MediaFileId}:{Path.GetFullPath(item.SourcePath)}:{Path.GetFullPath(item.DestinationPath)}:{Fingerprint(item.SourcePath)}:{item.Conflict}"));return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"organizer|{taskId}|{state}"))).ToLowerInvariant();}
    private static void Verify(string expected,string supplied){if(!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected),Encoding.UTF8.GetBytes(supplied??"")))throw new UnauthorizedAccessException("整理预览已变化，请重新 Dry Run 后确认。");}
    private async Task<long?> ClaimAsync(CancellationToken token){await using SqliteConnection c=await OpenAsync(SqliteOpenMode.ReadWrite,token);long id=await ScalarLongAsync(c,"SELECT COALESCE(MIN(Id),0) FROM Tasks WHERE TaskType='Organizer' AND Status='Pending'",token);if(id==0)return null;await ExecutePlainAsync(c,"UPDATE Tasks SET Status='Preparing',Stage='Preparing',UpdatedAt=$at WHERE Id=$id AND Status='Pending'",token,("$at",Now()),("$id",id));return id;}
    private async Task<TaskMutationResult> UpdateCommandAsync(long id,string status,string stage,string message,bool cancel){await using SqliteConnection c=await OpenAsync(SqliteOpenMode.ReadWrite);if(await ScalarLongAsync(c,"SELECT COUNT(*) FROM Tasks WHERE Id=$id AND TaskType='Organizer'",CancellationToken.None,("$id",id))==0)throw new KeyNotFoundException("整理任务不存在。");await ExecutePlainAsync(c,"UPDATE Tasks SET Status=$status,Stage=$stage,CancellationRequested=$cancel,CompletedAt=CASE WHEN $status='Cancelled' THEN $at ELSE CompletedAt END,UpdatedAt=$at WHERE Id=$id",CancellationToken.None,("$status",status),("$stage",stage),("$cancel",cancel?1:0),("$at",Now()),("$id",id));await logs.WriteAsync(id,status=="Cancelled"?"Warning":"Info",message);return new(id,status,message);}
    private static async Task<List<JournalRow>> ReadJournalAsync(SqliteConnection c,long taskId,CancellationToken token){await using SqliteCommand x=c.CreateCommand();x.CommandText="SELECT Id,MovieId,MediaFileId,SourcePath,DestinationPath,SourceFingerprint FROM FileOperationJournal WHERE TaskId=$task AND Status IN ('Planned','Failed','RolledBack') ORDER BY Id";x.Parameters.AddWithValue("$task",taskId);var rows=new List<JournalRow>();await using SqliteDataReader r=await x.ExecuteReaderAsync(token);while(await r.ReadAsync(token))rows.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5)));return rows;}
    private static Task MarkJournalAsync(SqliteConnection c,long id,string status,string? error,CancellationToken token)=>ExecutePlainAsync(c,"UPDATE FileOperationJournal SET Status=$status,ErrorMessage=$error,UpdatedAt=$at,CompletedAt=CASE WHEN $done=1 THEN $at ELSE CompletedAt END WHERE Id=$id",token,("$status",status),("$error",error),("$at",Now()),("$done",status is "DbUpdated" or "RolledBack" or "Failed"?1:0),("$id",id));
    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode,CancellationToken token=default){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=databasePath,Mode=mode,Cache=SqliteCacheMode.Private}.ToString());await c.OpenAsync(token);await using SqliteCommand x=c.CreateCommand();x.CommandText="PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";await x.ExecuteNonQueryAsync(token);return c;}
    private static async Task<int> ExecutePlainAsync(SqliteConnection c,string sql,CancellationToken token,params(string,object?)[] p){await using SqliteCommand x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return await x.ExecuteNonQueryAsync(token);}
    private static async Task ExecuteAsync(SqliteConnection c,System.Data.Common.DbTransaction tx,string sql,params(string,object?)[] p){await using SqliteCommand x=c.CreateCommand();x.Transaction=(SqliteTransaction)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);await x.ExecuteNonQueryAsync();}
    private static async Task<long> InsertIdAsync(SqliteConnection c,System.Data.Common.DbTransaction tx,string sql,CancellationToken token,params(string,object?)[] p){await using SqliteCommand x=c.CreateCommand();x.Transaction=(SqliteTransaction)tx;x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return Convert.ToInt64(await x.ExecuteScalarAsync(token));}
    private static async Task<long> ScalarLongAsync(SqliteConnection c,string sql,CancellationToken token,params(string,object?)[] p){await using SqliteCommand x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return Convert.ToInt64(await x.ExecuteScalarAsync(token)??0L);}
    private static async Task<string?> ScalarTextAsync(SqliteConnection c,string sql,CancellationToken token,params(string,object?)[] p){await using SqliteCommand x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);return(await x.ExecuteScalarAsync(token))?.ToString();}
    private static string NormalizePath(string path)=>Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar,Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
    private static string Now()=>DateTimeOffset.UtcNow.ToString("O");
    private sealed record JournalRow(long Id,long MovieId,long MediaFileId,string SourcePath,string DestinationPath,string SourceFingerprint);
}
