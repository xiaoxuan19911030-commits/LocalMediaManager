using System.Diagnostics;
using System.Text.Json;
using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class MetadataHealthStatisticsTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-metadata-health-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LocalMoviesDoNotEnterStandardDenominator()
    {
        Scenario scenario = await CreateScenarioAsync();

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(3, result.Scope.AllMovies);
        Assert.Equal(1, result.Scope.StandardMovies);
        Assert.Equal(1, result.Scope.LocalMovies);
        Assert.Equal(1, result.TotalMovies);
    }

    [Fact]
    public async Task UnassignedMoviesDoNotEnterStandardDenominator()
    {
        Scenario scenario = await CreateScenarioAsync();

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(1, result.Scope.UnassignedMovies);
        Assert.Equal(1, result.TotalMovies);
    }

    [Fact]
    public async Task MovieGenresProvideProviderTagCoverage()
    {
        Scenario scenario = await CreateScenarioAsync();
        await AddGenreAsync(scenario.Database);

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(1, result.Coverage.ProviderTagMovies);
        Assert.Equal(0, Field(result, "officialTags").Missing);
    }

    [Fact]
    public async Task MovieTagsDoNotProvideProviderTagCoverage()
    {
        Scenario scenario = await CreateScenarioAsync();
        await AddUserTagAsync(scenario.Database);

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(0, result.Coverage.ProviderTagMovies);
        Assert.Equal(1, result.Coverage.UserTagMovies);
        Assert.Equal(1, Field(result, "officialTags").Missing);
    }

    [Fact]
    public async Task GenreCoverageRemainsValidWithoutMovieTags()
    {
        Scenario scenario = await CreateScenarioAsync();
        await AddGenreAsync(scenario.Database);

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(1, result.Coverage.ProviderTagMovies);
        Assert.Equal(0, result.Coverage.UserTagMovies);
    }

    [Fact]
    public async Task ImageDatabaseRecordWithoutPhysicalFileIsNotCovered()
    {
        Scenario scenario = await CreateScenarioAsync();
        string missing = Path.Combine(scenario.StorageRoot, "Posters", "MISSING.jpg");
        await InsertImageAsync(scenario.Database, "Poster", missing);

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(1, result.Coverage.Poster.DatabaseMovies);
        Assert.Equal(0, result.Coverage.Poster.PhysicalMovies);
        Assert.Equal(1, result.Coverage.Poster.MissingFileRecords);
    }

    [Fact]
    public async Task PhysicalImageWithoutDatabaseRecordIsReportedAsUnregistered()
    {
        Scenario scenario = await CreateScenarioAsync();
        string path = Path.Combine(scenario.StorageRoot, "Posters", "ORPHAN.jpg");
        WriteFile(path);

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(0, result.Coverage.Poster.DatabaseMovies);
        Assert.Equal(0, result.Coverage.Poster.PhysicalMovies);
        Assert.Equal(1, result.Coverage.Poster.UnregisteredFiles);
        Assert.Equal(1, result.Coverage.UnregisteredResources);
    }

    [Fact]
    public async Task NfoDatabaseStateWithoutPhysicalFileIsNotCovered()
    {
        Scenario scenario = await CreateScenarioAsync();
        string missing = Path.Combine(scenario.StorageRoot, "NFO", "TEST-001", "TEST-001.nfo");
        await ExecuteAsync(scenario.Database, "UPDATE Movies SET NfoPath=$path WHERE Id=1", ("$path", missing));

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(1, result.Coverage.Nfo.DatabaseMovies);
        Assert.Equal(1, result.Coverage.Nfo.ValidPathMovies);
        Assert.Equal(0, result.Coverage.Nfo.PhysicalMovies);
        Assert.Equal(1, result.Coverage.Nfo.MissingFileRecords);
    }

    [Fact]
    public async Task NfoDocumentPhysicalFileCountsWhenMoviePathIsEmpty()
    {
        Scenario scenario = await CreateScenarioAsync();
        string nfo = Path.Combine(scenario.StorageRoot, "NFO", "TEST-001", "TEST-001.nfo");
        WriteFile(nfo);
        await ExecuteAsync(scenario.Database, """
            INSERT INTO NfoDocuments(MovieId,FilePath,Ownership,IsLocked,EncodingName,CreatedAt,UpdatedAt)
            VALUES(1,$path,'LMM',0,'utf-8',$at,$at)
            """, ("$path", nfo), ("$at", Now));

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(1, result.Coverage.Nfo.DatabaseMovies);
        Assert.Equal(1, result.Coverage.Nfo.ValidPathMovies);
        Assert.Equal(1, result.Coverage.Nfo.PhysicalMovies);
        Assert.Equal(0, result.Coverage.Nfo.MissingFileRecords);
    }

    [Fact]
    public async Task DeferredInventoryReturnsCurrentCoverageThenPublishesFullInventory()
    {
        Scenario scenario = await CreateScenarioAsync();
        await AddCompleteMetadataAsync(scenario);
        string networkRoot = $@"\\localhost\lmm-health-{Guid.NewGuid():N}";
        await ExecuteAsync(scenario.Database, "UPDATE AppSettings SET ValueJson=$value,UpdatedAt=$at WHERE Key='mediaStorage.rootPath'",
            ("$value", JsonSerializer.Serialize(networkRoot)), ("$at", Now));
        var service = CreateService(scenario);

        MetadataHealthSummary quick = await service.GetAsync();

        Assert.Equal(1, quick.CompleteMovies);
        Assert.False(quick.Coverage.ResourceInventoryComplete);
        MetadataHealthAnalysisState completed = await WaitForAnalysisAsync(service);
        Assert.NotNull(completed.Result);
        Assert.True(completed.Result.Coverage.ResourceInventoryComplete);
        Assert.Equal(quick.CompleteMovies, completed.Result.CompleteMovies);
    }

    [Fact]
    public async Task RemoteRegisteredResourceDefersInventoryWithoutBlockingInitialSnapshot()
    {
        Scenario scenario = await CreateScenarioAsync();
        await AddCompleteMetadataAsync(scenario);
        await InsertImageAsync(scenario.Database, "Preview", $@"\\localhost\lmm-health-{Guid.NewGuid():N}\preview.jpg");
        var service = CreateService(scenario);
        Stopwatch timer = Stopwatch.StartNew();

        MetadataHealthSummary quick = await service.GetAsync().WaitAsync(TimeSpan.FromSeconds(2));
        timer.Stop();

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
        Assert.False(quick.Coverage.ResourceInventoryComplete);
        Assert.Equal(1, quick.CompleteMovies);
        service.Cancel();
        Assert.False((await WaitForAnalysisAsync(service)).Running);
    }

    [Fact]
    public async Task DashboardSkipsRemoteCacheAndRecentCardFileProbes()
    {
        Scenario scenario = await CreateScenarioAsync();
        await AddCompleteMetadataAsync(scenario);
        string remote = $@"\\localhost\lmm-health-{Guid.NewGuid():N}\missing.jpg";
        await InsertImageAsync(scenario.Database, "Preview", remote);
        await ExecuteAsync(scenario.Database, """
            INSERT INTO ImageCacheEntries(MovieId,SourceImagePath,CachePath,CacheKind,CreatedAt)
            VALUES(1,$path,$path,'CardThumbnail',$at)
            """, ("$path", remote), ("$at", Now));
        var healthService = CreateService(scenario);
        MetadataHealthSummary health = await healthService.GetAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Stopwatch timer = Stopwatch.StartNew();

        DashboardDto dashboard = await ProductReader.ReadDashboardAsync(scenario.Database, "http://localhost", health, timeout.Token);
        timer.Stop();

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(0, dashboard.Maintenance.CacheProblems);
        healthService.Cancel();
        Assert.False((await WaitForAnalysisAsync(healthService)).Running);
    }

    [Fact]
    public async Task SettingsLoadRemainsResponsiveAfterDeferredInventoryStarts()
    {
        Scenario scenario = await CreateScenarioAsync();
        await AddCompleteMetadataAsync(scenario);
        await InsertImageAsync(scenario.Database, "Preview", $@"\\localhost\lmm-health-{Guid.NewGuid():N}\preview.jpg");
        var health = CreateService(scenario);
        MetadataHealthSummary quick = await health.GetAsync();
        var coordinator = new SettingsSaveCoordinator(
            scenario.Database,
            scenario.Root,
            Path.Combine(scenario.Root, "missing-legacy.db"),
            new MetadataProviderSettingsService(scenario.Database),
            new NfoService(scenario.Database, new MediaStoragePathResolver(scenario.Database, scenario.Root)),
            new PlaybackSettingsService(scenario.Database, Path.Combine(scenario.Root, "missing-legacy.db")),
            new RatingHistoryService(scenario.Database));

        UnifiedSettingsDto settings = await coordinator.ReadAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(quick.Coverage.ResourceInventoryComplete);
        Assert.NotNull(settings.MediaStorage);
        health.Cancel();
        Assert.False((await WaitForAnalysisAsync(health)).Running);
    }

    [Fact]
    public async Task BrokenActorRelationDoesNotCountAsCoverage()
    {
        Scenario scenario = await CreateScenarioAsync();
        await ExecuteAsync(scenario.Database, "PRAGMA foreign_keys=OFF; INSERT INTO MovieActors(MovieId,ActorId,RoleName,SortOrder) VALUES(1,999,'',0)");

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(0, result.Coverage.ActorMovies);
        Assert.Equal(1, Field(result, "actors").Missing);
    }

    [Fact]
    public async Task DashboardAndAnalyzerUseTheExactSameSnapshot()
    {
        Scenario scenario = await CreateScenarioAsync();
        var service = CreateService(scenario);
        MetadataHealthSummary summary = await service.GetAsync();

        DashboardDto dashboard = await ProductReader.ReadDashboardAsync(scenario.Database, "http://localhost", summary);

        Assert.Same(summary, dashboard.MetadataHealth);
        Assert.Equal(summary.TotalMovies, dashboard.StandardMovieCount);
        Assert.Equal(summary.CompleteMovies, dashboard.CompleteMetadataCount);
    }

    [Fact]
    public async Task SyncCompletionInvalidationRefreshesStatistics()
    {
        Scenario scenario = await CreateScenarioAsync();
        var service = CreateService(scenario);
        MetadataHealthSummary before = await service.GetAsync();
        await AddCompleteMetadataAsync(scenario);
        await ExecuteAsync(scenario.Database, "INSERT INTO Tasks(TaskType,Status,Progress,TotalItems,CompletedItems,CreatedAt,UpdatedAt,CompletedAt) VALUES('Sync','Completed',100,1,1,$at,$at,$at)", ("$at", Now));

        service.Invalidate();
        MetadataHealthSummary after = await service.GetAsync();

        Assert.Equal(0, before.CompleteMovies);
        Assert.Equal(1, after.CompleteMovies);
        Assert.NotSame(before, after);
    }

    [Fact]
    public async Task DatabaseStampInvalidatesCachedStatistics()
    {
        Scenario scenario = await CreateScenarioAsync();
        var service = CreateService(scenario);
        MetadataHealthSummary before = await service.GetAsync();
        await Task.Delay(25);
        await ExecuteAsync(scenario.Database, "UPDATE Movies SET Title='Changed',UpdatedAt=$at WHERE Id=1", ("$at", DateTimeOffset.UtcNow.ToString("O")));

        Assert.True(service.GetState().Invalidated);
        MetadataHealthSummary after = await service.GetAsync();

        Assert.NotSame(before, after);
        Assert.False(service.GetState().Invalidated);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("title")]
    [InlineData("releaseDate")]
    [InlineData("studio")]
    [InlineData("actors")]
    [InlineData("officialTags")]
    [InlineData("poster")]
    [InlineData("fanart")]
    [InlineData("nfo")]
    public async Task EveryRequiredConditionIndependentlyControlsCompleteness(string missingField)
    {
        Scenario scenario = await CreateScenarioAsync();
        CompletePaths paths = await AddCompleteMetadataAsync(scenario);
        await RemoveRequiredFieldAsync(scenario.Database, paths, missingField);

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(0, result.CompleteMovies);
        Assert.Equal(1, result.IncompleteMovies);
        Assert.Equal(1, Field(result, missingField).Missing);
    }

    [Fact]
    public async Task UserTagsDoNotAffectCompleteState()
    {
        Scenario scenario = await CreateScenarioAsync();
        await AddCompleteMetadataAsync(scenario);

        MetadataHealthSummary withoutUserTag = await AnalyzeAsync(scenario);
        await AddUserTagAsync(scenario.Database);
        MetadataHealthSummary withUserTag = await AnalyzeAsync(scenario);

        Assert.Equal(1, withoutUserTag.CompleteMovies);
        Assert.Equal(1, withUserTag.CompleteMovies);
        Assert.Equal(1, Field(withoutUserTag, "userTags").Missing);
        Assert.Equal(0, Field(withUserTag, "userTags").Missing);
        Assert.False(Field(withUserTag, "userTags").Required);
    }

    [Fact]
    public async Task ScreenshotDoesNotSubstituteForPosterOrFanart()
    {
        Scenario scenario = await CreateScenarioAsync();
        string screenshot = Path.Combine(scenario.StorageRoot, "Screenshots", "TEST-001", "01.jpg");
        WriteFile(screenshot);
        await InsertImageAsync(scenario.Database, "Screenshot", screenshot);

        MetadataHealthSummary result = await AnalyzeAsync(scenario);

        Assert.Equal(1, result.Coverage.Screenshot.PhysicalMovies);
        Assert.Equal(0, result.Coverage.Poster.PhysicalMovies);
        Assert.Equal(0, result.Coverage.Fanart.PhysicalMovies);
    }

    private async Task<Scenario> CreateScenarioAsync()
    {
        string scenarioRoot = Path.Combine(root, Guid.NewGuid().ToString("N"));
        string database = Path.Combine(scenarioRoot, "data", "test.db");
        string storageRoot = Path.Combine(scenarioRoot, "MediaStorage");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        Directory.CreateDirectory(storageRoot);
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        foreach (string file in new[] {
            "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0005_MetadataSyncWorkflow.sql",
            "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql", "0009_PlaybackSettings.sql", "0012_MediaStorageSettings.sql", "0015_LibraryTypesAndLocalMedia.sql",
        }) {
            await using SqliteCommand migration = connection.CreateCommand();
            migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await migration.ExecuteNonQueryAsync();
        }
        await ExecuteAsync(connection, "INSERT INTO Libraries(Id,Name,IsEnabled,SortOrder,CreatedAt,UpdatedAt,LibraryType) VALUES(1,'Standard',1,0,$at,$at,'Standard'),(2,'Local',1,1,$at,$at,'Local')", ("$at", Now));
        await ExecuteAsync(connection, """
            INSERT INTO Movies(Id,Code,Title,ReleaseDate,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt,ImportedAt)
            VALUES(1,'TEST-001','Standard title','2026-01-01',3600,1,'complete','Test',$at,$at,$at),
                  (2,'','Local title',NULL,120,0,'local','Test',$at,$at,$at),
                  (3,'','Unassigned title',NULL,120,0,'unknown','Test',$at,$at,$at)
            """, ("$at", Now));
        await ExecuteAsync(connection, """
            INSERT INTO MediaFiles(MovieId,LibraryId,FilePath,NormalizedPath,FileName,FileSize,MediaType,SourceType,IsPrimary,ExistsState,DurationSeconds,CreatedAt,UpdatedAt)
            VALUES(1,1,$standard,$standard,'TEST-001.mp4',1,'Video','Test',1,'Present',3600,$at,$at),
                  (2,2,$local,$local,'local.mp4',1,'Video','Test',1,'Present',120,$at,$at),
                  (3,NULL,$unassigned,$unassigned,'unassigned.mp4',1,'Video','Test',1,'Present',120,$at,$at)
            """, ("$standard", Path.Combine(scenarioRoot, "TEST-001.mp4")), ("$local", Path.Combine(scenarioRoot, "local.mp4")),
            ("$unassigned", Path.Combine(scenarioRoot, "unassigned.mp4")), ("$at", Now));
        await ExecuteAsync(connection, "UPDATE AppSettings SET ValueJson=$value,UpdatedAt=$at WHERE Key='mediaStorage.rootPath'",
            ("$value", JsonSerializer.Serialize(storageRoot)), ("$at", Now));
        return new(database, scenarioRoot, storageRoot);
    }

    private static MetadataHealthAnalysisService CreateService(Scenario scenario) =>
        new(scenario.Database, new MediaStoragePathResolver(scenario.Database, scenario.Root));

    private static Task<MetadataHealthSummary> AnalyzeAsync(Scenario scenario) => CreateService(scenario).GetAsync();

    private static async Task AddGenreAsync(string database)
    {
        await ExecuteAsync(database, "INSERT OR IGNORE INTO Genres(Id,Name,NormalizedName) VALUES(1,'Drama','DRAMA'); INSERT OR IGNORE INTO MovieGenres(MovieId,GenreId) VALUES(1,1)");
    }

    private static async Task AddUserTagAsync(string database)
    {
        await ExecuteAsync(database, "INSERT OR IGNORE INTO Tags(Id,Name,NormalizedName,Source,CreatedAt,UpdatedAt) VALUES(1,'Favorite','FAVORITE','User',$at,$at); INSERT OR IGNORE INTO MovieTags(MovieId,TagId,CreatedAt) VALUES(1,1,$at)", ("$at", Now));
    }

    private static async Task<CompletePaths> AddCompleteMetadataAsync(Scenario scenario)
    {
        string poster = Path.Combine(scenario.StorageRoot, "Posters", "TEST-001.jpg");
        string fanart = Path.Combine(scenario.StorageRoot, "Fanart", "TEST-001.jpg");
        string nfo = Path.Combine(scenario.StorageRoot, "NFO", "TEST-001", "TEST-001.nfo");
        WriteFile(poster);
        WriteFile(fanart);
        WriteFile(nfo);
        await ExecuteAsync(scenario.Database, "INSERT OR IGNORE INTO Actors(Id,Name,NormalizedName,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'Actor','ACTOR','Test',$at,$at); INSERT OR IGNORE INTO MovieActors(MovieId,ActorId,RoleName,SortOrder) VALUES(1,1,'',0)", ("$at", Now));
        await AddGenreAsync(scenario.Database);
        await ExecuteAsync(scenario.Database, "INSERT OR IGNORE INTO Studios(Id,Name,NormalizedName) VALUES(1,'Studio','STUDIO'); INSERT OR IGNORE INTO MovieStudios(MovieId,StudioId,RelationType) VALUES(1,1,'Studio')");
        await InsertImageAsync(scenario.Database, "Poster", poster);
        await InsertImageAsync(scenario.Database, "Fanart", fanart);
        await ExecuteAsync(scenario.Database, "UPDATE Movies SET NfoPath=$nfo,UpdatedAt=$at WHERE Id=1", ("$nfo", nfo), ("$at", Now));
        return new(poster, fanart, nfo);
    }

    private static async Task RemoveRequiredFieldAsync(string database, CompletePaths paths, string field)
    {
        switch (field) {
            case "code": await ExecuteAsync(database, "UPDATE Movies SET Code='test-001' WHERE Id=1"); break;
            case "title": await ExecuteAsync(database, "UPDATE Movies SET Title='' WHERE Id=1"); break;
            case "releaseDate": await ExecuteAsync(database, "UPDATE Movies SET ReleaseDate=NULL WHERE Id=1"); break;
            case "studio": await ExecuteAsync(database, "DELETE FROM MovieStudios WHERE MovieId=1"); break;
            case "actors": await ExecuteAsync(database, "DELETE FROM MovieActors WHERE MovieId=1"); break;
            case "officialTags": await ExecuteAsync(database, "DELETE FROM MovieGenres WHERE MovieId=1"); break;
            case "poster": File.Delete(paths.Poster); break;
            case "fanart": File.Delete(paths.Fanart); break;
            case "nfo": File.Delete(paths.Nfo); break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    private static async Task InsertImageAsync(string database, string type, string path)
    {
        await ExecuteAsync(database, "INSERT INTO Images(MovieId,ImageType,FilePath,IsPrimary,ValidationStatus,CreatedAt,UpdatedAt) VALUES(1,$type,$path,1,'Valid',$at,$at)",
            ("$type", type), ("$path", path), ("$at", Now));
    }

    private static MetadataHealthField Field(MetadataHealthSummary summary, string key) => summary.Fields.Single(field => field.Key == key);

    private static async Task<MetadataHealthAnalysisState> WaitForAnalysisAsync(MetadataHealthAnalysisService service)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        MetadataHealthAnalysisState state;
        do {
            state = service.GetState();
            if (!state.Running) return state;
            await Task.Delay(25);
        } while (DateTimeOffset.UtcNow < deadline);
        throw new TimeoutException("Metadata health background inventory did not finish.");
    }

    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test");
    }

    private static async Task ExecuteAsync(string database, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new SqliteConnection($"Data Source={database}");
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql, parameters);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private const string Now = "2026-07-24T00:00:00Z";
    private sealed record Scenario(string Database, string Root, string StorageRoot);
    private sealed record CompletePaths(string Poster, string Fanart, string Nfo);

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(root, true); } catch { }
        return Task.CompletedTask;
    }
}
