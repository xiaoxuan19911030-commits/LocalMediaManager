using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class LegacyCompletionPart2Tests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-legacy-part2-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql", "0008_FileOrganizerWorkflow.sql", "0009_PlaybackSettings.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task TaskCleanupDeletesOnlyTerminalTasksAndLogs()
    {
        await using var connection = await Open();
        string at = DateTimeOffset.UtcNow.ToString("O");
        await Execute(connection, "INSERT INTO Tasks(Id,TaskType,Status,Progress,TotalItems,CompletedItems,CreatedAt,UpdatedAt) VALUES(1,'Sync','Completed',100,1,1,$at,$at),(2,'Scan','Failed',100,1,0,$at,$at),(3,'Organizer','Cancelled',20,2,1,$at,$at),(4,'Sync','Running',50,2,1,$at,$at)", ("$at", at));
        await Execute(connection, "INSERT INTO TaskLogs(TaskId,Level,Message,CreatedAt) VALUES(1,'Info','done',$at),(2,'Error','failed',$at),(4,'Info','running',$at)", ("$at", at));
        var service = new TaskCommandService(Database, null!, null!, null!, null!);

        TaskCleanupResult completed = await service.CleanupAsync("completed");
        Assert.Equal(1, completed.Count);
        Assert.Equal(3, await Scalar(connection, "SELECT COUNT(*) FROM Tasks"));
        Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM TaskLogs WHERE TaskId=1"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(4));
        TaskCleanupResult terminal = await service.CleanupAsync("terminal");
        Assert.Equal(2, terminal.Count);
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM Tasks WHERE Status='Running'"));
    }

    [Fact]
    public async Task AdvancedSearchSupportsUnratedAndExactStarBuckets()
    {
        await using var connection = await Open();
        string at = DateTimeOffset.UtcNow.ToString("O");
        for (int id = 1; id <= 4; id++) {
            await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt,ImportedAt) VALUES($id,$code,$code,0,0,'pending','Test',$at,$at,$at)", ("$id", id), ("$code", $"RATE-{id:000}"), ("$at", at));
            await Execute(connection, "INSERT INTO MediaFiles(MovieId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,CreatedAt,UpdatedAt) VALUES($id,$path,$path,$file,'.mp4',1,'Video','Test',1,'Available',$at,$at)", ("$id", id), ("$path", Path.Combine(root, $"rate-{id}.mp4")), ("$file", $"rate-{id}.mp4"), ("$at", at));
        }
        await Execute(connection, "INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,HasUserRating,UpdatedAt) VALUES(1,0,5,1,$at),(2,0,4.5,1,$at),(3,0,3,1,$at),(4,0,0,0,$at)", ("$at", at));

        MediaPageDto five = await ProductReader.AdvancedSearchAsync(Database, "http://127.0.0.1:47831", "", null, null, null, null, 0, "5", "all", "all", "all", null, "newest", 24, 0);
        Assert.Single(five.Items);
        Assert.Equal("RATE-001", five.Items[0].Code);

        MediaPageDto four = await ProductReader.AdvancedSearchAsync(Database, "http://127.0.0.1:47831", "", null, null, null, null, 0, "4", "all", "all", "all", null, "newest", 24, 0);
        Assert.Single(four.Items);
        Assert.Equal("RATE-002", four.Items[0].Code);

        MediaPageDto unrated = await ProductReader.AdvancedSearchAsync(Database, "http://127.0.0.1:47831", "", null, null, null, null, 0, "unrated", "all", "all", "all", null, "newest", 24, 0);
        Assert.Single(unrated.Items);
        Assert.Equal("RATE-004", unrated.Items[0].Code);
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(root, true); } catch { }
        return Task.CompletedTask;
    }

    private async Task<SqliteConnection> Open()
    {
        var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task Execute(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> Scalar(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
    }
}
