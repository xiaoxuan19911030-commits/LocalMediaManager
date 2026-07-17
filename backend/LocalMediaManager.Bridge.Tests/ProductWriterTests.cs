using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class ProductWriterTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-writer-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");
    private ProductWriter writer = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        writer = new ProductWriter(Database);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
        await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'TEST-001','Test',0,0,'unknown','Test','2026-01-01','2026-01-01'),(2,'TEST-002','Test 2',0,0,'unknown','Test','2026-01-01','2026-01-01')");
        await Execute(connection, "INSERT INTO MediaFiles(Id,MovieId,FilePath,NormalizedPath,FileName,FileSize,MediaType,SourceType,IsPrimary,ExistsState,DurationSeconds,CreatedAt,UpdatedAt) VALUES(1,1,'C:\\media\\TEST-001.mp4','c:\\media\\test-001.mp4','TEST-001.mp4',1,'Video','Local',1,'Present',0,'2026-01-01','2026-01-01'),(2,2,'D:\\copy\\TEST-001.mp4','d:\\copy\\test-001.mp4','TEST-001.mp4',1,'Video','Local',1,'Present',0,'2026-01-01','2026-01-01')");
        await Execute(connection, "INSERT INTO Actors(Id,Name,NormalizedName,LegacySource,LegacyId,CreatedAt,UpdatedAt) VALUES(1,'Actor A','actor a','Test',1,'2026-01-01','2026-01-01'),(2,'Actor B','actor b','Test',2,'2026-01-01','2026-01-01')");
    }

    [Fact]
    public async Task FavoriteAndRatingPersistAndClearWithoutConflatingZero()
    {
        await writer.SetUserStateAsync(1, new(true, 0));
        Assert.Equal((1L, 0d, 1L), await State(1));
        await writer.SetUserStateAsync(1, new(null, null, true));
        Assert.Equal((1L, 0d, 0L), await State(1));
        await using var connection = await Open();
        Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM OperationAudit WHERE OperationType='UserState'"));
    }

    [Fact]
    public async Task BatchRatingPersistsAndCanClearSelectedMovies()
    {
        await writer.SetRatingsAsync(new([1, 2, 2], 4.5));
        Assert.Equal((0L, 4.5d, 1L), await State(1));
        Assert.Equal((0L, 4.5d, 1L), await State(2));

        await writer.SetRatingsAsync(new([1, 2], null, true));

        Assert.Equal((0L, 0d, 0L), await State(1));
        Assert.Equal((0L, 0d, 0L), await State(2));
        await using var connection = await Open();
        Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM OperationAudit WHERE OperationType='BatchRating'"));
    }

    [Fact]
    public async Task TagsSupportCreateBindBatchAndConfirmedDelete()
    {
        var created = await writer.CreateTagAsync(new("My Tag", null, null));
        await writer.UpdateMovieTagsAsync(1, new([created.Id], []));
        await writer.UpdateBatchTagsAsync(new([1, 2], [created.Id], []));
        await using (var connection = await Open()) Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM MovieTags WHERE TagId=" + created.Id));
        var preview = await writer.PreviewDeleteTagAsync(created.Id);
        Assert.Equal(2, preview.AffectedMovies);
        var deleted = await writer.DeleteTagAsync(created.Id, new(preview.ConfirmationToken));
        await using (var verify = await Open()) {
            Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM Tags WHERE Id=" + created.Id));
            Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM OperationAudit WHERE OperationType='TagDelete'"));
        }
        await writer.RollbackAsync(deleted.AuditId);
        await using var restored = await Open();
        Assert.Equal(1, await Scalar(restored, "SELECT COUNT(*) FROM Tags WHERE Id=" + created.Id));
        Assert.Equal(2, await Scalar(restored, "SELECT COUNT(*) FROM MovieTags WHERE TagId=" + created.Id));
        Assert.Equal(1, await Scalar(restored, "SELECT COUNT(*) FROM OperationAudit WHERE OperationType='TagDelete' AND RevertedAt IS NOT NULL"));
    }

    [Fact]
    public async Task DeletedRatingRestoresOnlyWhenTargetHasNoNewRating()
    {
        await writer.SetUserStateAsync(1, new(null, 4.5));
        Assert.True(await writer.RememberDeletedRatingAsync(1));
        Assert.True(await writer.RestoreDeletedRatingAsync(2));
        Assert.Equal((0L, 4.5d, 1L), await State(2));
        await writer.SetUserStateAsync(2, new(null, 3));
        Assert.False(await writer.RestoreDeletedRatingAsync(2));
        Assert.Equal((0L, 3d, 1L), await State(2));
    }

    [Fact]
    public async Task ActorRelationsAndPlaybackHistoryAreTransactional()
    {
        await writer.SetMovieActorsAsync(1, new([1, 2, 2]));
        await writer.RecordPlaybackAsync(1, 1, "test-player", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        await using var connection = await Open();
        Assert.Equal(2, await Scalar(connection, "SELECT COUNT(*) FROM MovieActors WHERE MovieId=1"));
        Assert.Equal(1, await Scalar(connection, "SELECT PlayCount FROM UserMovieState WHERE MovieId=1"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM PlayHistory WHERE MovieId=1 AND Completed=1"));
    }

    [Fact]
    public async Task MovieDeleteRequiresPreviewBacksUpAndRemembersOnlyFilenameAndRating()
    {
        await writer.SetUserStateAsync(1, new(null, 4));
        var preview = await writer.PreviewDeleteMovieAsync(1);
        Assert.True(preview.RatingWillBeRemembered);
        await writer.DeleteMovieAsync(1, new(preview.ConfirmationToken));
        await using var connection = await Open();
        Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM Movies WHERE Id=1"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM DeletedRatingMemory WHERE NormalizedFileName='test-001.mp4' AND Rating=4"));
        Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM DeletedRatingMemory WHERE FileName LIKE '%media%'"));
        Assert.True(Directory.GetFiles(Path.Combine(root, "backups", "operations"), "movie-delete-*.db", SearchOption.AllDirectories).Length == 1);
    }

    [Fact]
    public async Task ActorIdZeroRepairUsesPreviewTaskAndDatabaseBackup()
    {
        await using (var connection = await Open()) {
            await Execute(connection, "INSERT INTO Actors(Id,Name,NormalizedName,LegacySource,LegacyId,CreatedAt,UpdatedAt) VALUES(0,'Actor A','actor a','Test',99,'2026-01-01','2026-01-01')");
            await Execute(connection, "INSERT INTO MovieActors(MovieId,ActorId,RoleName,SortOrder) VALUES(1,0,'',0)");
        }
        var preview = await writer.PreviewActorRepairAsync();
        Assert.Equal(1, preview.CandidateActors);
        Assert.Equal(1, preview.AffectedRelations);
        await writer.ApplyActorRepairAsync(new(preview.ConfirmationToken));
        await using var verify = await Open();
        Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM Actors WHERE Id=0"));
        Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM MovieActors WHERE ActorId=0"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM MovieActors WHERE MovieId=1 AND ActorId=1"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM Tasks WHERE TaskType='ActorRepair' AND Status='Completed'"));
        Assert.True(Directory.GetFiles(Path.Combine(root, "backups", "operations"), "actor-repair-*.db", SearchOption.AllDirectories).Length == 1);
    }

    private async Task<(long Favorite, double Rating, long RatingSet)> State(long movieId)
    {
        await using var connection = await Open(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IsFavorite,UserRating,HasUserRating FROM UserMovieState WHERE MovieId=$id"; command.Parameters.AddWithValue("$id", movieId);
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); return (reader.GetInt64(0), reader.GetDouble(1), reader.GetInt64(2));
    }
    private async Task<SqliteConnection> Open() { var connection = new SqliteConnection($"Data Source={Database}"); await connection.OpenAsync(); return connection; }
    private static async Task<long> Scalar(SqliteConnection connection, string sql) { await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync()); }
    private static async Task Execute(SqliteConnection connection, string sql) { await using var command=connection.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync(); }
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); try { Directory.Delete(root, true); } catch { } return Task.CompletedTask; }
}
