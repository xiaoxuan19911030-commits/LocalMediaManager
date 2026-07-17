using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class SafeDeleteWorkflowTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-safe-delete-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");
    private string media = "";
    private string nfo = "";
    private ProductWriter writer = null!;
    private RatingHistoryService ratings = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        media = Path.Combine(root, "SONE-104-C.mp4");
        nfo = Path.Combine(root, "SONE-104.nfo");
        await File.WriteAllBytesAsync(media, new byte[] { 1, 2, 3, 4 });
        await File.WriteAllTextAsync(nfo, "<movie />");
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0010_DeletedMovieRatings.sql", "0011_RemoveRatingRetentionClearSetting.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
        string at = DateTimeOffset.UtcNow.ToString("O");
        await Execute(connection, "INSERT INTO Movies(Id,Code,Title,NfoPath,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'SONE-104-C','Movie',$nfo,0,0,'pending','Test',$at,$at)", ("$nfo", nfo), ("$at", at));
        await Execute(connection, "INSERT INTO MediaFiles(Id,MovieId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,CreatedAt,UpdatedAt) VALUES(1,1,$path,$path,'SONE-104-C.mp4','.mp4',4,'Video','Test',1,'Present',$at,$at)", ("$path", media), ("$at", at));
        await Execute(connection, "INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,PlayCount,LastPositionSeconds,UpdatedAt,HasUserRating) VALUES(1,1,4,2,0,$at,1)", ("$at", at));
        ratings = new RatingHistoryService(Database);
        writer = new ProductWriter(Database, ratings);
    }

    [Theory]
    [InlineData("SONE-104", "SONE-104")]
    [InlineData("sone104", "SONE-104")]
    [InlineData("SONE_104", "SONE-104")]
    [InlineData("[SONE-104] title", "SONE-104")]
    [InlineData("SONE-104-C", "SONE-104")]
    [InlineData("SONE-104-4K", "SONE-104")]
    public void MovieCodeNormalizerHandlesCommonForms(string input, string expected) =>
        Assert.Equal(expected, MovieCodeNormalizer.Normalize(input));

    [Fact]
    public async Task RatingHistorySavesBeforeMetadataDeleteAndMediaFileRemains()
    {
        MovieDeletePreview preview = await writer.PreviewDeleteMovieAsync(1);
        await writer.DeleteMovieAsync(1, new(preview.ConfirmationToken));

        await using SqliteConnection verify = await Open();
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM DeletedMovieRatings WHERE NormalizedMovieCode='SONE-104' AND Rating=4"));
        Assert.True(File.Exists(media));
    }

    [Fact]
    public async Task SafeDeletePlanIncludesNfoButExcludesOtherMovieResources()
    {
        string other = Path.Combine(root, "OTHER-001.jpg");
        await File.WriteAllBytesAsync(other, new byte[] { 9 });
        await using (SqliteConnection connection = await Open()) {
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(2,'OTHER-001','Other',0,0,'pending','Test',$at,$at)", ("$at", at));
            await Execute(connection, "INSERT INTO Images(MovieId,ImageType,FilePath,CreatedAt,UpdatedAt) VALUES(2,'Poster',$path,$at,$at)", ("$path", other), ("$at", at));
        }
        var service = new SafeDeleteWorkflowService(Database, writer, new TaskLogService(Database), ratings);

        SafeDeletePreview preview = await service.PreviewAsync(new([1], "media"));

        Assert.Contains(preview.Items[0].Files, file => file.Kind == "NFO" && file.Path == nfo && file.WillDelete);
        Assert.DoesNotContain(preview.Items[0].Files, file => file.Path == other);
    }

    [Fact]
    public async Task ScanImportRestoresByNormalizedCodeAndDoesNotOverwriteExistingRating()
    {
        await ratings.RememberExplicitRatingAsync(1, 4);
        string at = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = await Open();
        await using SqliteTransaction tx = (SqliteTransaction)await connection.BeginTransactionAsync();
        await Execute(connection, tx, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(3,'sone104','New',0,0,'pending','Test',$at,$at)", ("$at", at));

        bool restored = await RatingHistoryService.RestoreForImportedMovieAsync(connection, tx, 3, "sone104", at);
        await tx.CommitAsync();

        Assert.True(restored);
        Assert.Equal(4, await Scalar(connection, "SELECT UserRating FROM UserMovieState WHERE MovieId=3"));
    }

    [Fact]
    public async Task ScanImportDoesNotRestoreWhenCurrentMovieAlreadyHasRating()
    {
        await ratings.RememberExplicitRatingAsync(1, 4);
        string at = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = await Open();
        await using SqliteTransaction tx = (SqliteTransaction)await connection.BeginTransactionAsync();
        await Execute(connection, tx, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(3,'SONE-104','New',0,0,'pending','Test',$at,$at)", ("$at", at));
        await Execute(connection, tx, "INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,PlayCount,LastPositionSeconds,UpdatedAt,HasUserRating) VALUES(3,0,2,0,0,$at,1)", ("$at", at));

        bool restored = await RatingHistoryService.RestoreForImportedMovieAsync(connection, tx, 3, "SONE-104", at);
        await tx.CommitAsync();

        Assert.False(restored);
        Assert.Equal(2, await Scalar(connection, "SELECT UserRating FROM UserMovieState WHERE MovieId=3"));
    }

    [Fact]
    public async Task RatingHistorySettingCanDisableSaveAndRestore()
    {
        await ratings.SaveSettingsAsync(new(false));
        await ratings.RememberExplicitRatingAsync(1, 5);
        string at = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection verify = await Open();
        Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM DeletedMovieRatings WHERE Rating=5"));
        await Execute(verify, "INSERT INTO DeletedMovieRatings(NormalizedMovieCode,Rating,UpdatedAt) VALUES('SONE-104',5,$at)", ("$at", at));
        await using SqliteTransaction tx = (SqliteTransaction)await verify.BeginTransactionAsync();
        await Execute(verify, tx, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(3,'SONE-104','New',0,0,'pending','Test',$at,$at)", ("$at", at));

        bool restored = await RatingHistoryService.RestoreForImportedMovieAsync(verify, tx, 3, "SONE-104", at);
        await tx.CommitAsync();

        Assert.False(restored);
        Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM UserMovieState WHERE MovieId=3"));
    }

    [Fact]
    public async Task ClearingCurrentRatingDoesNotDeleteRetainedRating()
    {
        await ratings.RememberExplicitRatingAsync(1, 4);

        await writer.SetUserStateAsync(1, new(null, null, true));

        await using SqliteConnection verify = await Open();
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM DeletedMovieRatings WHERE NormalizedMovieCode='SONE-104' AND Rating=4"));
        Assert.Equal(0, await Scalar(verify, "SELECT COALESCE(HasUserRating,0) FROM UserMovieState WHERE MovieId=1"));
    }

    [Fact]
    public async Task RemovedClearSettingKeyIsNotCreatedByMigration()
    {
        await using SqliteConnection verify = await Open();
        Assert.Equal(0, await Scalar(verify, "SELECT COUNT(*) FROM AppSettings WHERE Key='ratingHistory.deleteOnClear'"));
    }

    [Fact]
    public void FfmpegLocatorUsesBundledBeforeConfiguredPath()
    {
        string bundled = Path.Combine(root, "tools", "ffmpeg", "ffmpeg.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
        File.WriteAllText(bundled, "");
        var locator = new FfmpegLocator(Database, root);

        FfmpegLookupResult result = locator.Locate();

        Assert.True(result.Found);
        Assert.Equal("Bundled", result.Source);
        Assert.Equal(bundled, result.Path);
    }

    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); try { Directory.Delete(root, true); } catch { } return Task.CompletedTask; }
    private async Task<SqliteConnection> Open() { var connection = new SqliteConnection($"Data Source={Database}"); await connection.OpenAsync(); return connection; }
    private static async Task<long> Scalar(SqliteConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L); }
    private static async Task Execute(SqliteConnection connection, string sql, params (string, object?)[] values) { await using var command = connection.CreateCommand(); command.CommandText = sql; foreach ((string name, object? value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value); await command.ExecuteNonQueryAsync(); }
    private static async Task Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string, object?)[] values) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach ((string name, object? value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value); await command.ExecuteNonQueryAsync(); }
}
