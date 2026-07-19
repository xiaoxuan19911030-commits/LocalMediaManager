using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class DuplicateOrganizerWorkflowTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-duplicate-organizer-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");
    private string media1 = "";
    private string media2 = "";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        media1 = Path.Combine(root, "DUP-001-a.mp4");
        media2 = Path.Combine(root, "DUP-001-b.mp4");
        await File.WriteAllBytesAsync(media1, [1, 2, 3]);
        await File.WriteAllBytesAsync(media2, [4, 5, 6]);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0010_DeletedMovieRatings.sql", "0011_RemoveRatingRetentionClearSetting.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
        string at = DateTimeOffset.UtcNow.ToString("O");
        await Execute(connection, """
            INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt)
            VALUES(1,'DUP-001','Keep',0,0,'pending','Test',$at,$at),(2,'DUP-001','Delete',0,0,'pending','Test',$at,$at)
            """, ("$at", at));
        await Execute(connection, """
            INSERT INTO MediaFiles(Id,MovieId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,CreatedAt,UpdatedAt)
            VALUES(1,1,$path1,$norm1,'DUP-001-a.mp4','.mp4',3,'Video','Test',1,'Present',$at,$at),
                  (2,2,$path2,$norm2,'DUP-001-b.mp4','.mp4',3,'Video','Test',1,'Present',$at,$at)
            """, ("$path1", media1), ("$norm1", Normalize(media1)), ("$path2", media2), ("$norm2", Normalize(media2)), ("$at", at));
        await Execute(connection, "INSERT INTO Tags(Id,Name,NormalizedName,Source,CreatedAt,UpdatedAt) VALUES(1,'keep','keep','User',$at,$at),(2,'delete','delete','User',$at,$at)", ("$at", at));
        await Execute(connection, "INSERT INTO MovieTags(MovieId,TagId,CreatedAt) VALUES(1,1,$at),(2,2,$at)", ("$at", at));
    }

    [Fact]
    public async Task PreviewBlocksUnsafeRatingAndNotesConflict()
    {
        string at = DateTimeOffset.UtcNow.ToString("O");
        await using (SqliteConnection connection = await Open()) {
            await Execute(connection, "INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,HasUserRating,PlayCount,Notes,UpdatedAt) VALUES(1,0,4,1,0,'keep note',$at),(2,0,5,1,0,'delete note',$at)", ("$at", at));
        }
        DuplicateOrganizerWorkflowService service = CreateService();

        DuplicateDeletePreview preview = await service.PreviewAsync(new([new("code:DUP-001", 1, [1, 2])], "metadata", true));

        Assert.False(preview.CanExecute);
        Assert.Contains(preview.Blockers, item => item.Contains("评分冲突"));
        Assert.Contains(preview.Blockers, item => item.Contains("用户备注冲突"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(new([new("code:DUP-001", 1, [1, 2])], "metadata", true, preview.ConfirmationToken)));
    }

    [Fact]
    public async Task ExecuteMergesUserDataAndQueuesSafeDeleteForNonKeepCandidates()
    {
        string at = DateTimeOffset.UtcNow.ToString("O");
        await using (SqliteConnection connection = await Open()) {
            await Execute(connection, "INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,HasUserRating,PlayCount,LastPlayedAt,UpdatedAt) VALUES(1,0,0,0,1,'2026-07-18T00:00:00Z',$at),(2,1,4,1,3,'2026-07-19T00:00:00Z',$at)", ("$at", at));
        }
        DuplicateOrganizerWorkflowService service = CreateService();
        DuplicateDeletePreview preview = await service.PreviewAsync(new([new("code:DUP-001", 1, [1, 2])], "metadata", true));

        SafeDeleteLaunchResult launch = await service.ExecuteAsync(new([new("code:DUP-001", 1, [1, 2])], "metadata", true, preview.ConfirmationToken));
        SafeDeleteWorkflowService safeDelete = CreateSafeDelete();
        await safeDelete.StartAsync(CancellationToken.None);
        string? status = null;
        for (int attempt = 0; attempt < 50 && status != "Completed"; attempt++) {
            await Task.Delay(50);
            await using SqliteConnection check = await Open();
            status = await Text(check, $"SELECT Status FROM Tasks WHERE Id={launch.TaskId}");
        }
        await safeDelete.StopAsync(CancellationToken.None);

        await using SqliteConnection verify = await Open();
        Assert.Equal("Completed", status);
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM Movies WHERE Id=1"));
        Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM Movies WHERE Id=2"));
        Assert.Equal(1, await Scalar(verify, "SELECT IsFavorite FROM UserMovieState WHERE MovieId=1"));
        Assert.Equal(4, await Scalar(verify, "SELECT UserRating FROM UserMovieState WHERE MovieId=1"));
        Assert.Equal(4, await Scalar(verify, "SELECT PlayCount FROM UserMovieState WHERE MovieId=1"));
        Assert.Equal(2, await Scalar(verify, "SELECT COUNT(*) FROM MovieTags WHERE MovieId=1"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM OperationAudit WHERE OperationType='DuplicateMerge'"));
        Assert.True(File.Exists(media1));
        Assert.True(File.Exists(media2));
    }

    private DuplicateOrganizerWorkflowService CreateService() => new(Database, CreateSafeDelete());
    private SafeDeleteWorkflowService CreateSafeDelete()
    {
        var ratings = new RatingHistoryService(Database);
        return new(Database, new ProductWriter(Database, ratings), new TaskLogService(Database), ratings);
    }
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); try { Directory.Delete(root, true); } catch { } return Task.CompletedTask; }
    private async Task<SqliteConnection> Open() { var connection = new SqliteConnection($"Data Source={Database}"); await connection.OpenAsync(); return connection; }
    private static async Task<long> Scalar(SqliteConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L); }
    private static async Task<string?> Text(SqliteConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; return (await command.ExecuteScalarAsync())?.ToString(); }
    private static async Task Execute(SqliteConnection connection, string sql, params (string, object?)[] values) { await using var command = connection.CreateCommand(); command.CommandText = sql; foreach ((string name, object? value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value); await command.ExecuteNonQueryAsync(); }
    private static string Normalize(string path) => Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
}
