using System.Text.Json;
using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class MetadataRepairWorkflowTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-metadata-repair-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "data", "test.db");
    private string Storage => Path.Combine(root, "MediaStorage");
    private string StandardVideo => Path.Combine(root, "media", "SONE-001.mp4");
    private string LocalVideo => Path.Combine(root, "local", "local-title.mp4");
    private string MismatchVideo => Path.Combine(root, "media-2", "SONE-002.mp4");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Database)!);
        Directory.CreateDirectory(Storage);
        Write(StandardVideo);
        Write(LocalVideo);
        Write(MismatchVideo);
        await using SqliteConnection connection = new($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] {
            "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql",
            "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql",
            "0012_MediaStorageSettings.sql", "0015_LibraryTypesAndLocalMedia.sql",
        }) {
            await ExecuteAsync(connection, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file)));
        }
        string at = Now();
        await ExecuteAsync(connection, """
            INSERT INTO Libraries(Id,Name,IsEnabled,SortOrder,CreatedAt,UpdatedAt,LibraryType)
            VALUES(1,'Standard',1,0,$at,$at,'Standard'),(2,'Local',1,1,$at,$at,'Local');
            """, ("$at", at));
        await ExecuteAsync(connection, """
            INSERT INTO Movies(Id,Code,Title,ReleaseDate,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt)
            VALUES(1,'SONE-001','Standard','2026-01-01',3600,1,'complete','Test',$at,$at),
                  (2,'','Local',NULL,120,0,'local','Test',$at,$at),
                  (3,'WRONG-002','Mismatch','2026-01-02',3600,0,'pending','Test',$at,$at);
            """, ("$at", at));
        await InsertMediaAsync(connection, 1, 1, StandardVideo, "SONE-001.mp4", at);
        await InsertMediaAsync(connection, 2, 2, LocalVideo, "local-title.mp4", at);
        await InsertMediaAsync(connection, 3, 1, MismatchVideo, "SONE-002.mp4", at);
        await ExecuteAsync(connection, "UPDATE AppSettings SET ValueJson=$value,UpdatedAt=$at WHERE Key='mediaStorage.rootPath'",
            ("$value", JsonSerializer.Serialize(Storage)), ("$at", at));
    }

    [Fact]
    public async Task ValidResourceIsNeverOverwritten()
    {
        string valid = Path.Combine(Storage, "Posters", "valid.jpg");
        string alternate = Path.Combine(Storage, "Posters", "SONE-001.jpg");
        Write(valid); Write(alternate);
        await InsertImageAsync("Poster", valid, "Valid");

        MetadataRepairPreview preview = await ScanAsync();

        Assert.DoesNotContain(preview.Items, item => item.MovieId == 1 && item.ResourceType == "Poster" && item.SafeToApply);
        Assert.True(preview.Counts.ExistingValidSkipped > 0);
    }

    [Fact]
    public async Task MissingDatabasePosterPathWithUniqueCandidateCanBeRepaired()
    {
        string missing = Path.Combine(Storage, "Posters", "missing.jpg");
        string candidate = Path.Combine(Storage, "Posters", "SONE-001.jpg");
        Write(candidate);
        await InsertImageAsync("Poster", missing, "Unknown");

        MetadataRepairPreview preview = await ScanAsync();

        MetadataRepairCandidate item = Assert.Single(preview.Items, value => value.ResourceType == "Poster" && value.SafeToApply);
        Assert.Equal("RepairImagePath", item.Action);
        Assert.Equal(candidate, item.CandidatePath);
    }

    [Fact]
    public async Task MultiplePosterCandidatesRequireManualReview()
    {
        Write(Path.Combine(Storage, "Posters", "SONE-001.jpg"));
        Write(Path.Combine(Storage, "Posters", "SONE-001.png"));

        MetadataRepairPreview preview = await ScanAsync();

        Assert.Equal(2, preview.Items.Count(item => item.ResourceType == "Poster" && item.Status == "Conflict"));
        Assert.DoesNotContain(preview.Items, item => item.ResourceType == "Poster" && item.SafeToApply);
    }

    [Fact]
    public async Task LowConfidenceCandidateIsNeverAutomatic()
    {
        Write(Path.Combine(Storage, "Posters", "archive-SONE-001-extra.jpg"));

        MetadataRepairPreview preview = await ScanAsync();

        MetadataRepairCandidate item = Assert.Single(preview.Items, value => value.ResourceType == "Poster");
        Assert.Equal("LowConfidence", item.Status);
        Assert.False(item.SafeToApply);
        Assert.True(item.Confidence < 80);
    }

    [Fact]
    public async Task UnregisteredFanartCanBeAssociated()
    {
        string candidate = Path.Combine(Storage, "Fanart", "SONE-001.jpg");
        Write(candidate);

        MetadataRepairPreview completed = await ApplyAsync();

        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM Images WHERE MovieId=1 AND ImageType='Fanart' AND FilePath=$path", ("$path", candidate)));
        Assert.Equal(1, completed.Counts.Applied);
        Assert.Equal("Applied", Assert.Single(completed.Items, item => item.ResourceType == "Fanart").Status);
    }

    [Fact]
    public async Task ExistingNfoWithoutStatusCanBeLinked()
    {
        string nfo = Path.Combine(Storage, "NFO", "SONE-001", "SONE-001.nfo");
        Write(nfo);

        MetadataRepairPreview completed = await ApplyAsync();

        Assert.Equal(nfo, await TextAsync("SELECT NfoPath FROM Movies WHERE Id=1"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM NfoDocuments WHERE MovieId=1 AND FilePath=$path", ("$path", nfo)));
        Assert.Equal(1, completed.After?.NfoMovies);
    }

    [Fact]
    public async Task MissingNfoEntityRemainsAnAnomaly()
    {
        string missing = Path.Combine(Storage, "NFO", "SONE-001", "missing.nfo");
        await ExecuteAsync("UPDATE Movies SET NfoPath=$path WHERE Id=1", ("$path", missing));

        MetadataRepairPreview preview = await ScanAsync();

        Assert.True(preview.Counts.InvalidDatabaseRecords >= 1);
        Assert.DoesNotContain(preview.Items, item => item.ResourceType == "NFO" && item.SafeToApply);
        Assert.Equal(missing, await TextAsync("SELECT NfoPath FROM Movies WHERE Id=1"));
    }

    [Fact]
    public async Task LocalMoviesAreExcluded()
    {
        Write(Path.Combine(Path.GetDirectoryName(LocalVideo)!, "local-title.jpg"));

        MetadataRepairPreview preview = await ScanAsync();

        Assert.DoesNotContain(preview.Items, item => item.MovieId == 2);
        Assert.Equal(2, preview.Counts.ScannedStandardMovies);
    }

    [Fact]
    public async Task CodeReidentificationMismatchIsExcluded()
    {
        Write(Path.Combine(Storage, "Posters", "WRONG-002.jpg"));

        MetadataRepairPreview preview = await ScanAsync();

        Assert.Equal(1, preview.Counts.ExcludedCodeMismatch);
        Assert.DoesNotContain(preview.Items, item => item.MovieId == 3);
    }

    [Fact]
    public async Task DryRunDoesNotWriteMetadataTables()
    {
        Write(Path.Combine(Storage, "Fanart", "SONE-001.jpg"));

        MetadataRepairPreview preview = await ScanAsync();

        Assert.Equal("PreviewReady", preview.Status);
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM Images"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM NfoDocuments"));
        Assert.True(string.IsNullOrEmpty(await TextAsync("SELECT NfoPath FROM Movies WHERE Id=1")));
    }

    [Fact]
    public async Task ExecuteWritesAllItemsInOneTransaction()
    {
        Write(Path.Combine(Storage, "Fanart", "SONE-001.jpg"));
        Write(Path.Combine(Storage, "Previews", "SONE-001", "SONE-001-01.jpg"));

        MetadataRepairPreview completed = await ApplyAsync();

        Assert.Equal("Completed", completed.Status);
        Assert.Equal(2, await ScalarAsync("SELECT COUNT(*) FROM Images WHERE MovieId=1"));
        Assert.Equal(2, await ScalarAsync("SELECT COUNT(*) FROM OperationAudit WHERE OperationType='MetadataRepair'"));
    }

    [Fact]
    public async Task ExecuteFailureRollsBackWholeTransaction()
    {
        string fanart = Path.Combine(Storage, "Fanart", "SONE-001.jpg");
        string poster = Path.Combine(Storage, "Posters", "SONE-001.jpg");
        Write(fanart); Write(poster);
        MetadataRepairWorkflow service = CreateService();
        MetadataRepairPreview preview = await ScanAsync(service);
        File.Delete(poster);
        await service.ExecuteConfirmedAsync(preview.TaskId, preview.ConfirmationToken);

        await service.ProcessPendingOnceAsync();
        MetadataRepairPreview failed = await service.GetAsync(preview.TaskId);

        Assert.Equal("Failed", failed.Status);
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM Images"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM OperationAudit WHERE OperationType='MetadataRepair'"));
    }

    [Fact]
    public async Task RepairSessionCanRollback()
    {
        string fanart = Path.Combine(Storage, "Fanart", "SONE-001.jpg");
        Write(fanart);
        MetadataRepairWorkflow service = CreateService();
        MetadataRepairPreview completed = await ApplyAsync(service);

        await service.RollbackAsync(completed.TaskId);

        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM Images WHERE MovieId=1"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM OperationAudit WHERE OperationType='MetadataRepair' AND RevertedAt IS NOT NULL"));
        Assert.False((await service.GetAsync(completed.TaskId)).CanRollback);
    }

    [Fact]
    public async Task MetadataHealthRefreshesAfterRepair()
    {
        Write(Path.Combine(Storage, "Fanart", "SONE-001.jpg"));

        MetadataRepairPreview completed = await ApplyAsync();

        Assert.NotNull(completed.After);
        Assert.Equal(completed.Before.FanartMovies + 1, completed.After!.FanartMovies);
    }

    [Fact]
    public async Task CancelledScanWritesNoMetadata()
    {
        Write(Path.Combine(Storage, "Fanart", "SONE-001.jpg"));
        MetadataRepairWorkflow service = CreateService();
        MetadataRepairLaunchResult launch = await service.StartDryRunAsync(defaultOptions);

        await service.CancelAsync(launch.TaskId);
        await service.ProcessPendingOnceAsync();

        Assert.Equal("Cancelled", (await service.GetAsync(launch.TaskId)).Status);
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM Images"));
    }

    [Fact]
    public async Task RepeatedExecutionIsIdempotent()
    {
        string fanart = Path.Combine(Storage, "Fanart", "SONE-001.jpg");
        Write(fanart);
        await ApplyAsync();

        MetadataRepairPreview second = await ScanAsync();

        Assert.DoesNotContain(second.Items, item => item.ResourceType == "Fanart" && item.SafeToApply);
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM Images WHERE MovieId=1 AND ImageType='Fanart'"));
    }

    [Fact]
    public async Task UnavailableNasDoesNotClearHistoricalState()
    {
        string unavailable = @"Q:\offline-nas\SONE-001.jpg";
        await InsertImageAsync("Poster", unavailable, "Unknown");

        MetadataRepairPreview preview = await ScanAsync();

        MetadataRepairCandidate item = Assert.Single(preview.Items, value => value.Action == "MarkMissing");
        Assert.Equal("Skipped", item.Status);
        Assert.False(item.SafeToApply);
        Assert.Equal("Unknown", await TextAsync("SELECT ValidationStatus FROM Images WHERE MovieId=1 AND ImageType='Poster'"));
    }

    [Fact]
    public async Task MissingContainingDirectoryDoesNotMarkHistoricalState()
    {
        string unavailable = Path.Combine(root, "temporarily-offline", "SONE-001.jpg");
        await InsertImageAsync("Poster", unavailable, "Unknown");

        MetadataRepairPreview preview = await ScanAsync();

        MetadataRepairCandidate item = Assert.Single(preview.Items, value => value.Action == "MarkMissing");
        Assert.Equal("Skipped", item.Status);
        Assert.False(item.SafeToApply);
        Assert.Equal("Unknown", await TextAsync("SELECT ValidationStatus FROM Images WHERE MovieId=1 AND ImageType='Poster'"));
    }

    [Fact]
    public async Task RenamedVideoCanUseNumberNamedLocalPoster()
    {
        string renamed = Path.Combine(Path.GetDirectoryName(StandardVideo)!, "SONE-001+favorite.mp4");
        File.Move(StandardVideo, renamed);
        await ExecuteAsync("UPDATE MediaFiles SET FilePath=$path,NormalizedPath=$normalized,FileName=$name WHERE MovieId=1",
            ("$path", renamed), ("$normalized", renamed.ToUpperInvariant()), ("$name", Path.GetFileName(renamed)));
        string poster = Path.Combine(Path.GetDirectoryName(renamed)!, "SONE-001.jpg");
        Write(poster);

        MetadataRepairPreview preview = await ScanAsync();

        MetadataRepairCandidate item = Assert.Single(preview.Items, value => value.ResourceType == "Poster" && value.SafeToApply);
        Assert.Equal(poster, item.CandidatePath);
        Assert.Equal(90, item.Confidence);
    }

    [Fact]
    public async Task RepairBaselineMatchesFullMetadataHealthInventory()
    {
        string missing = Path.Combine(Storage, "Covers", "missing.jpg");
        string unregistered = Path.Combine(Storage, "Fanart", "SONE-001.jpg");
        Write(unregistered);
        await InsertImageAsync("Poster", missing, "Unknown");
        var resolver = new MediaStoragePathResolver(Database, root);
        MediaStorageSettingsDto settings = await resolver.GetSettingsAsync();
        MetadataHealthSummary expected = await MetadataHealthReader.ReadAsync(Database, settings);

        MetadataRepairPreview preview = await ScanAsync();

        Assert.Equal(expected.Coverage.InvalidResourceRecords, preview.Before.InvalidResourceRecords);
        Assert.Equal(expected.Coverage.UnregisteredResources, preview.Before.UnregisteredResources);
        Assert.Equal(expected.Coverage.Nfo.PhysicalMovies, preview.Before.NfoMovies);
    }

    private static readonly MetadataRepairScanCommand defaultOptions = new();

    private MetadataRepairWorkflow CreateService()
    {
        var resolver = new MediaStoragePathResolver(Database, root);
        var health = new MetadataHealthAnalysisService(Database, resolver);
        return new(Database, resolver, health, new MovieNumberExtractor(FindMovieNumberRules()), new TaskLogService(Database));
    }

    private async Task<MetadataRepairPreview> ScanAsync(MetadataRepairWorkflow? service = null)
    {
        service ??= CreateService();
        MetadataRepairLaunchResult launch = await service.StartDryRunAsync(defaultOptions);
        await service.ProcessPendingOnceAsync();
        return await service.GetAsync(launch.TaskId);
    }

    private async Task<MetadataRepairPreview> ApplyAsync(MetadataRepairWorkflow? service = null)
    {
        service ??= CreateService();
        MetadataRepairPreview preview = await ScanAsync(service);
        await service.ExecuteConfirmedAsync(preview.TaskId, preview.ConfirmationToken);
        await service.ProcessPendingOnceAsync();
        return await service.GetAsync(preview.TaskId);
    }

    private async Task InsertImageAsync(string type, string path, string status)
    {
        await ExecuteAsync("""
            INSERT INTO Images(MovieId,ImageType,FilePath,IsPrimary,ValidationStatus,CreatedAt,UpdatedAt)
            VALUES(1,$type,$path,1,$status,$at,$at)
            """, ("$type", type), ("$path", path), ("$status", status), ("$at", Now()));
    }

    private static async Task InsertMediaAsync(SqliteConnection connection, long movieId, long libraryId, string path, string name, string at)
    {
        await ExecuteAsync(connection, """
            INSERT INTO MediaFiles(MovieId,LibraryId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,DurationSeconds,CreatedAt,UpdatedAt)
            VALUES($movie,$library,$path,$normalized,$name,'.mp4',3,'Video','Test',1,'Present',3600,$at,$at)
            """, ("$movie", movieId), ("$library", libraryId), ("$path", path), ("$normalized", path.ToUpperInvariant()), ("$name", name), ("$at", at));
    }

    private async Task ExecuteAsync(string sql, params (string, object?)[] parameters)
    {
        await using SqliteConnection connection = new($"Data Source={Database}");
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql, parameters);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql, params (string, object?)[] parameters)
    {
        await using SqliteConnection connection = new($"Data Source={Database}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<string?> TextAsync(string sql)
    {
        await using SqliteConnection connection = new($"Data Source={Database}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
    }

    private static string FindMovieNumberRules()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent) {
            string file = Path.Combine(current.FullName, "backend", "LocalMediaManager.Bridge", "movie-number-rules.json");
            if (File.Exists(file)) return file;
        }
        throw new FileNotFoundException("movie-number-rules.json");
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch { }
        return Task.CompletedTask;
    }
}
