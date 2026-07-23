using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class FileOrganizerWorkflowTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-organizer-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");
    private string Source => Path.Combine(root, "source", "old-name.mp4");
    private string Destination => Path.Combine(root, "organized");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        Directory.CreateDirectory(Destination);
        await File.WriteAllBytesAsync(Source, [1, 2, 3, 4, 5]);
        await using var connection = new SqliteConnection($"Data Source={Database}"); await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql", "0008_FileOrganizerWorkflow.sql", "0009_PlaybackSettings.sql", "0013_DirectorMetadata.sql" }) {
            await using var command=connection.CreateCommand();command.CommandText=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"migrations",file));await command.ExecuteNonQueryAsync();
        }
        string at=DateTimeOffset.UtcNow.ToString("O");
        await Execute(connection,"INSERT INTO Movies(Id,Code,Title,ReleaseDate,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'ORG-001','Organizer title','2026-07-16',0,0,'pending','Test',$at,$at)",( "$at",at));
        await Execute(connection,"INSERT INTO MediaFiles(Id,MovieId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,DurationSeconds,CreatedAt,UpdatedAt) VALUES(1,1,$path,$normalized,'old-name.mp4','.mp4',5,'Video','Local',1,'Present',0,$at,$at)",( "$path",Source),("$normalized",Normalize(Source)),("$at",at));
    }

    [Fact]
    public async Task DryRunDoesNotMoveAndConfirmedTaskUpdatesFileAndDatabase()
    {
        var service = new FileOrganizerService(Database, new TaskLogService(Database));
        OrganizerPreview preview = await service.DryRunAsync(new([1], "{Code} - {Title}", Destination));

        Assert.True(File.Exists(Source)); Assert.False(File.Exists(preview.Items[0].DestinationPath));
        Assert.Equal("PreviewReady", preview.Status); Assert.Equal(1, preview.ValidItems);

        OrganizerLaunchResult launch = await service.ExecuteConfirmedAsync(preview.TaskId, preview.ConfirmationToken);
        await service.StartAsync(CancellationToken.None);
        string? status=null;
        for(int attempt=0;attempt<50&&status!="Completed";attempt++){await Task.Delay(50);await using SqliteConnection check=await Open();status=await Text(check,$"SELECT Status FROM Tasks WHERE Id={launch.TaskId}");}
        await service.StopAsync(CancellationToken.None);

        string target=preview.Items[0].DestinationPath;
        Assert.Equal("Completed",status);Assert.False(File.Exists(Source));Assert.True(File.Exists(target));
        await using SqliteConnection verify=await Open();
        Assert.Equal(target,await Text(verify,"SELECT FilePath FROM MediaFiles WHERE Id=1"));
        Assert.Equal("DbUpdated",await Text(verify,"SELECT Status FROM FileOperationJournal WHERE TaskId="+preview.TaskId));
        Assert.Equal(1,await Scalar(verify,"SELECT COUNT(*) FROM OperationAudit WHERE OperationType='FileOrganize'"));
    }

    [Fact]
    public async Task ExistingDestinationIsReportedAndNeverOverwritten()
    {
        string target=Path.Combine(Destination,"ORG-001.mp4");await File.WriteAllBytesAsync(target,[9,9,9]);
        var service=new FileOrganizerService(Database,new TaskLogService(Database));

        OrganizerPreview preview=await service.DryRunAsync(new([1],"{Code}",Destination));

        Assert.Equal(1,preview.ConflictItems);Assert.Contains("禁止覆盖",preview.Items[0].Conflict);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ExecuteConfirmedAsync(preview.TaskId,preview.ConfirmationToken));
        Assert.True(File.Exists(Source));Assert.Equal(new byte[]{9,9,9},await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task SourceChangeAfterPreviewInvalidatesConfirmation()
    {
        var service=new FileOrganizerService(Database,new TaskLogService(Database));
        OrganizerPreview preview=await service.DryRunAsync(new([1],"{Code}",Destination));
        await File.AppendAllTextAsync(Source,"changed");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ExecuteConfirmedAsync(preview.TaskId,preview.ConfirmationToken));

        Assert.True(File.Exists(Source));Assert.False(File.Exists(preview.Items[0].DestinationPath));
    }

    [Fact]
    public async Task StartupRecoveryRestoresFileMovedBeforeDatabaseCommit()
    {
        string target=Path.Combine(Destination,"ORG-001.mp4");
        File.Move(Source,target,false);
        await using(SqliteConnection connection=await Open()){
            string at=DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection,"INSERT INTO Tasks(Id,TaskType,Status,Stage,Progress,TotalItems,CompletedItems,CreatedAt,UpdatedAt) VALUES(20,'Organizer','Running','MovingFiles',0,1,0,$at,$at)",( "$at",at));
            await Execute(connection,"INSERT INTO FileOperationJournal(TaskId,MovieId,MediaFileId,SourcePath,DestinationPath,OperationType,Status,SourceFingerprint,CreatedAt,UpdatedAt) VALUES(20,1,1,$source,$target,'MoveAndRename','Planned','5:0',$at,$at)",( "$source",Source),("$target",target),("$at",at));
        }
        var service=new FileOrganizerService(Database,new TaskLogService(Database));

        await service.StartAsync(CancellationToken.None);await Task.Delay(150);await service.StopAsync(CancellationToken.None);

        Assert.True(File.Exists(Source));Assert.False(File.Exists(target));
        await using SqliteConnection verify=await Open();
        Assert.Equal("RolledBack",await Text(verify,"SELECT Status FROM FileOperationJournal WHERE TaskId=20"));
        Assert.Equal("Failed",await Text(verify,"SELECT Status FROM Tasks WHERE Id=20"));
        Assert.Equal(Source,await Text(verify,"SELECT FilePath FROM MediaFiles WHERE Id=1"));
    }

    [Fact]
    public async Task LocalMovieWithEmptyVidRemovesExtraSeparators()
    {
        await using (SqliteConnection connection = await Open())
            await Execute(connection, "UPDATE Movies SET Code='',Title='  Local title  ' WHERE Id=1");
        var service = new FileOrganizerService(Database, new TaskLogService(Database));

        OrganizerPreview preview = await service.DryRunAsync(new([1], "{VID}+{Title}", Destination));

        Assert.EndsWith(Path.Combine("organized", "Local title.mp4"), preview.Items.Single().DestinationPath);
    }

    [Fact]
    public async Task CustomInformationSeparatorIsAppliedWithoutChangingExtension()
    {
        var service = new FileOrganizerService(Database, new TaskLogService(Database));

        OrganizerPreview preview = await service.DryRunAsync(new([1], "{VID}+{Title}", Destination, "_", "·", true));

        Assert.EndsWith(Path.Combine("organized", "ORG-001_Organizer title.mp4"), preview.Items.Single().DestinationPath);
    }

    public Task DisposeAsync(){try{Directory.Delete(root,true);}catch{}return Task.CompletedTask;}
    private async Task<SqliteConnection> Open(){var c=new SqliteConnection($"Data Source={Database}");await c.OpenAsync();return c;}
    private static async Task Execute(SqliteConnection c,string sql,params(string,object?)[] p){await using var x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in p)x.Parameters.AddWithValue(n,v??DBNull.Value);await x.ExecuteNonQueryAsync();}
    private static async Task<long> Scalar(SqliteConnection c,string sql){await using var x=c.CreateCommand();x.CommandText=sql;return Convert.ToInt64(await x.ExecuteScalarAsync()??0L);}
    private static async Task<string?> Text(SqliteConnection c,string sql){await using var x=c.CreateCommand();x.CommandText=sql;return(await x.ExecuteScalarAsync())?.ToString();}
    private static string Normalize(string path)=>Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar,Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
}
