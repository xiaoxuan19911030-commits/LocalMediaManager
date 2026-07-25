using System.Collections.Concurrent;
using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class MetadataCompletionWorkflowTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-metadata-completion-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "data", "test.db");
    private string Storage => Path.Combine(root, "MediaStorage");
    private string Video => Path.Combine(root, "media", "SONE-001.mp4");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Database)!);
        Directory.CreateDirectory(Storage);
        Write(Video);
        await using SqliteConnection connection = new($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] {
            "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql",
            "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql",
            "0012_MediaStorageSettings.sql", "0013_DirectorMetadata.sql", "0015_LibraryTypesAndLocalMedia.sql",
        }) await ExecuteAsync(connection, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file)));
        string at = Now();
        await ExecuteAsync(connection, """
            INSERT INTO Libraries(Id,Name,IsEnabled,SortOrder,CreatedAt,UpdatedAt,LibraryType)
            VALUES(1,'Standard',1,0,$at,$at,'Standard'),(2,'Local',1,1,$at,$at,'Local');
            INSERT INTO Movies(Id,Code,Title,Description,ReleaseDate,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt)
            VALUES(1,'SONE-001','Existing title','Existing description','2026-01-01',3600,0,'pending','Test',$at,$at);
            """, ("$at", at));
        await InsertMediaAsync(connection, 1, 1, Video, at);
        await ExecuteAsync(connection, """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES
            ('metadata.javbus.enabled','true','boolean',$at),
            ('metadata.metatube.enabled','true','boolean',$at),
            ('metadata.mdcNg.enabled','false','boolean',$at)
            ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,UpdatedAt=excluded.UpdatedAt;
            """, ("$at", at));
    }

    [Fact]
    public async Task DryRunPerformsNoProviderCallsAndWritesNoMetadata()
    {
        var provider = new FakeProviderClient(DefaultMetadata("JavBus", "SONE-001", actors: ["Actor A"]));
        MetadataCompletionPreview preview = await ScanAsync(CreateService(provider), Only("Actors"));

        Assert.Equal("PreviewReady", preview.Status);
        Assert.Empty(provider.Calls);
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM MovieActors"));
        Assert.Equal("Existing description", await TextAsync("SELECT Description FROM Movies WHERE Id=1"));
    }

    [Fact]
    public async Task CapabilityRoutesActorsToJavBusFirst()
    {
        MetadataCompletionPreview preview = await ScanAsync(CreateService(new FakeProviderClient(null)), Only("Actors"));

        MetadataCompletionItem item = Assert.Single(preview.Items, value => value.MovieId == 1);
        Assert.Equal("JavBus", item.ProviderPlan[0]);
        Assert.Contains("Actors", preview.Capabilities.Single(value => value.Provider == "JavBus").Fields);
    }

    [Fact]
    public async Task FieldLevelCompletionDoesNotOverwriteExistingDescription()
    {
        ProviderMetadata metadata = DefaultMetadata("JavBus", "SONE-001", description: "Replacement", actors: ["Actor A"]);
        MetadataCompletionPreview completed = await ExecuteAsync(CreateService(new FakeProviderClient(metadata)), Only("Actors"));

        Assert.Equal("Completed", Assert.Single(completed.Items, value => value.MovieId == 1).Status);
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM MovieActors WHERE MovieId=1"));
        Assert.Equal("Existing description", await TextAsync("SELECT Description FROM Movies WHERE Id=1"));
    }

    [Fact]
    public async Task ExistingTargetFieldIsNotRequestedOrOverwritten()
    {
        ProviderMetadata metadata = DefaultMetadata("JavBus", "SONE-001", description: "Replacement");
        var provider = new FakeProviderClient(metadata);
        MetadataCompletionPreview preview = await ScanAsync(CreateService(provider), Only("Description"));

        Assert.DoesNotContain(preview.Items, item => item.MovieId == 1);
        Assert.Equal(0, preview.Counts.PlannedNetworkMovies);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task TransientProviderFailuresUseExponentialRetry()
    {
        var delays = new DelayRecorder();
        var provider = new FakeProviderClient(DefaultMetadata("JavBus", "SONE-001", actors: ["Actor A"])) { FailuresRemaining = 2 };
        MetadataCompletionPreview completed = await ExecuteAsync(CreateService(provider, delays.DelayAsync), Only("Actors"));

        MetadataCompletionItem item = Assert.Single(completed.Items, value => value.MovieId == 1);
        Assert.Equal("Completed", item.Status);
        Assert.Equal(3, item.Attempts);
        Assert.Contains(delays.Values, value => value >= TimeSpan.FromMilliseconds(250));
        Assert.Contains(delays.Values, value => value >= TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task ProviderRateLimitSerializesSameProviderRequests()
    {
        await AddSecondStandardMovieAsync();
        var delays = new DelayRecorder();
        var provider = new FakeProviderClient(DefaultMetadata("JavBus", "SONE-001", actors: ["Actor A"]));
        MetadataCompletionPreview completed = await ExecuteAsync(CreateService(provider, delays.DelayAsync), Only("Actors") with { Concurrency = 2 });

        Assert.Equal(2, completed.Counts.Completed);
        Assert.Contains(delays.Values, value => value > TimeSpan.Zero);
    }

    [Fact]
    public async Task PausedSessionCanResumeFromPersistedPlan()
    {
        MetadataCompletionWorkflow service = CreateService(new FakeProviderClient(DefaultMetadata("JavBus", "SONE-001", actors: ["Actor A"])));
        MetadataCompletionPreview preview = await ScanAsync(service, Only("Actors"));
        await service.ExecuteConfirmedAsync(preview.TaskId, preview.ConfirmationToken);

        await service.PauseAsync(preview.TaskId);
        Assert.True((await service.GetAsync(preview.TaskId)).CanResume);
        await service.ResumeAsync(preview.TaskId);
        await service.ProcessPendingOnceAsync();

        Assert.Equal("Completed", (await service.GetAsync(preview.TaskId)).Status);
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM MovieActors WHERE MovieId=1"));
    }

    [Fact]
    public async Task CompletionSessionRollbackRestoresDatabaseState()
    {
        MetadataCompletionWorkflow service = CreateService(new FakeProviderClient(DefaultMetadata("JavBus", "SONE-001", actors: ["Actor A"])));
        MetadataCompletionPreview completed = await ExecuteAsync(service, Only("Actors"));
        Assert.True(completed.CanRollback);

        await service.RollbackAsync(completed.TaskId);

        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM MovieActors WHERE MovieId=1"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM OperationAudit WHERE OperationType='MetadataCompletion' AND RevertedAt IS NOT NULL"));
        Assert.False((await service.GetAsync(completed.TaskId)).CanRollback);
    }

    [Fact]
    public async Task CompletionRefreshesMetadataHealthProjection()
    {
        await MakeMovieCompleteExceptActorsAsync();
        MetadataCompletionPreview completed = await ExecuteAsync(
            CreateService(new FakeProviderClient(DefaultMetadata("JavBus", "SONE-001", actors: ["Actor A"]))), Only("Actors"));

        Assert.NotNull(completed.Projection.CompleteAfter);
        Assert.Equal(completed.Projection.CompleteBefore + 1, completed.Projection.CompleteAfter);
    }

    [Fact]
    public async Task RepeatedDryRunIsIdempotent()
    {
        var provider = new FakeProviderClient(DefaultMetadata("JavBus", "SONE-001", actors: ["Actor A"]));
        MetadataCompletionWorkflow service = CreateService(provider);
        await ExecuteAsync(service, Only("Actors"));
        int calls = provider.Calls.Count;

        MetadataCompletionPreview second = await ScanAsync(service, Only("Actors"));

        Assert.Equal(0, second.Counts.PlannedNetworkMovies);
        Assert.Equal(calls, provider.Calls.Count);
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM MovieActors WHERE MovieId=1"));
    }

    [Fact]
    public async Task ProviderNoDataIsClassifiedWithoutWriting()
    {
        MetadataCompletionPreview completed = await ExecuteAsync(CreateService(new FakeProviderClient(null)), Only("Actors"));

        MetadataCompletionItem item = Assert.Single(completed.Items, value => value.MovieId == 1);
        Assert.Equal("CompletedWithErrors", completed.Status);
        Assert.Equal("NoResult", item.Status);
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM MovieActors"));
    }

    [Fact]
    public async Task ProviderDocumentWithoutRequestedFieldsDoesNotMutateMovieStatus()
    {
        string? beforeStatus = await TextAsync("SELECT ScrapeStatus FROM Movies WHERE Id=1");
        string? beforeUpdatedAt = await TextAsync("SELECT UpdatedAt FROM Movies WHERE Id=1");
        ProviderMetadata metadata = DefaultMetadata("JavBus", "SONE-001", description: "Unrequested description");

        MetadataCompletionPreview completed = await ExecuteAsync(
            CreateService(new FakeProviderClient(metadata)), Only("Actors"));

        MetadataCompletionItem item = Assert.Single(completed.Items);
        Assert.Equal("CompletedWithErrors", completed.Status);
        Assert.Equal("NoResult", item.Status);
        Assert.Empty(item.AddedFields);
        Assert.Equal(0, await ScalarAsync("SELECT IsScraped FROM Movies WHERE Id=1"));
        Assert.Equal(beforeStatus, await TextAsync("SELECT ScrapeStatus FROM Movies WHERE Id=1"));
        Assert.Equal(beforeUpdatedAt, await TextAsync("SELECT UpdatedAt FROM Movies WHERE Id=1"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM OperationAudit WHERE OperationType='MetadataCompletion'"));
    }

    [Fact]
    public async Task HistoricalCompletedSessionWithUnsuccessfulItemsIsDisplayedWithErrors()
    {
        MetadataCompletionWorkflow service = CreateService(new FakeProviderClient(null));
        MetadataCompletionPreview completed = await ExecuteAsync(service, Only("Actors"));
        await ExecuteAsync("UPDATE Tasks SET Status='Completed',Stage='Completed' WHERE Id=$id", ("$id", completed.TaskId));

        MetadataCompletionPreview detail = await service.GetAsync(completed.TaskId);
        TaskDto task = Assert.Single(await ProductReader.ReadTasksAsync(Database), value => value.Id == completed.TaskId);

        Assert.Equal("CompletedWithErrors", detail.Status);
        Assert.Equal("CompletedWithErrors", detail.Stage);
        Assert.Equal("CompletedWithErrors", task.Status);
        Assert.Equal("CompletedWithErrors", task.Stage);
    }

    [Fact]
    public async Task CreatedNfoIsReportedAsPhysicalContribution()
    {
        ProviderMetadata metadata = DefaultMetadata("JavBus", "SONE-001", description: "Provider description");

        MetadataCompletionPreview completed = await ExecuteAsync(
            CreateService(new FakeProviderClient(metadata)), Only("NFO"));

        MetadataCompletionItem item = Assert.Single(completed.Items);
        Assert.Equal("Completed", item.Status);
        Assert.Contains("NFO", item.AddedFields);
        string? nfoPath = await TextAsync("SELECT NfoPath FROM Movies WHERE Id=1");
        Assert.NotNull(nfoPath);
        Assert.True(File.Exists(nfoPath), nfoPath);
    }

    [Fact]
    public async Task LocalMoviesNeverEnterCompletionPlan()
    {
        string local = Path.Combine(root, "local", "local-video.mp4");
        Write(local);
        string at = Now();
        await ExecuteAsync("""
            INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt)
            VALUES(2,'','Local title',120,0,'local','Test',$at,$at)
            """, ("$at", at));
        await using (SqliteConnection connection = new($"Data Source={Database}")) {
            await connection.OpenAsync(); await InsertMediaAsync(connection, 2, 2, local, at);
        }

        MetadataCompletionPreview preview = await ScanAsync(CreateService(new FakeProviderClient(null)), Only("Actors"));

        Assert.DoesNotContain(preview.Items, item => item.MovieId == 2);
        Assert.Equal(1, preview.Counts.ScannedStandardMovies);
    }

    [Fact]
    public async Task P1SelectsExactlyTwentyBalancedMoviesFromEligiblePool()
    {
        await AddStandardMoviesAsync(2, 25);
        MetadataCompletionPreview preview = await ScanAsync(CreateService(new FakeProviderClient(null)),
            new MetadataCompletionScanCommand(MaxMovies: 20, SelectionSeed: 74201));

        Assert.Equal(25, preview.Counts.EligibleMovies);
        Assert.Equal(20, preview.Counts.PlannedNetworkMovies);
        Assert.Equal(20, preview.Items.Count);
        Assert.Equal(20, preview.Selection.SelectedMovieIds.Distinct().Count());
        Assert.Equal(74201, preview.Selection.Seed);
        Assert.Equal(["Actors", "Fanart", "Genres", "NFO", "Poster"],
            preview.Selection.BalancedCoverage.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(5, preview.Selection.BalancedCoverage.Values.Distinct().Count());
    }

    [Fact]
    public async Task P1SelectionIsReproducibleAndClampsUnsafeMaximum()
    {
        await AddStandardMoviesAsync(2, 25);
        MetadataCompletionScanCommand options = new(MaxMovies: 200, SelectionSeed: 99173);
        MetadataCompletionPreview first = await ScanAsync(CreateService(new FakeProviderClient(null)), options);
        MetadataCompletionPreview second = await ScanAsync(CreateService(new FakeProviderClient(null)), options);

        Assert.Equal(20, first.Options.MaxMovies);
        Assert.Equal(first.Selection.SelectedMovieIds, second.Selection.SelectedMovieIds);
        Assert.Equal(first.Selection.BalancedCoverage, second.Selection.BalancedCoverage);
    }

    [Fact]
    public async Task P1ExecutionNeverRequestsMoreThanSelectedTwentyMovies()
    {
        await AddStandardMoviesAsync(2, 25);
        var delays = new DelayRecorder();
        var provider = new FakeProviderClient(DefaultMetadata("JavBus", "SONE-001", actors: ["Actor A"]));
        MetadataCompletionPreview completed = await ExecuteAsync(CreateService(provider, delays.DelayAsync),
            Only("Actors") with { MaxMovies = 20, SelectionSeed = 4567 });

        Assert.Equal(20, completed.Items.Count);
        Assert.Equal(20, completed.Counts.Completed);
        Assert.Equal(20, provider.Calls.Count);
        Assert.Equal(20, await ScalarAsync("SELECT COUNT(DISTINCT MovieId) FROM MovieActors"));
        Assert.Equal(20, completed.Counts.ActualProviderRequests["JavBus"]);
    }

    [Fact]
    public async Task ProductionAuditCapturesProviderLogsAndBeforeAfterValues()
    {
        MetadataCompletionPreview completed = await ExecuteAsync(
            CreateService(new FakeProviderClient(DefaultMetadata("JavBus", "SONE-001", actors: ["Actor A"]))),
            Only("Actors") with { SelectionSeed = 1234 });

        MetadataCompletionItem item = Assert.Single(completed.Items);
        Assert.Equal(string.Empty, item.BeforeValues["Actors"]);
        Assert.NotEqual(string.Empty, item.AfterValues["Actors"]);
        Assert.Contains(item.Logs, log => log.Provider == "JavBus" && log.Stage == "RequestStart");
        Assert.Contains(item.Logs, log => log.Provider == "JavBus" && log.Stage == "ProviderLog" && log.HttpStatusCode == 200);
        Assert.Contains(item.Logs, log => log.Provider == "JavBus" && log.Stage == "RequestSuccess");
        Assert.Contains(item.Logs, log => log.Provider == "Database" && log.Stage == "Merge");
        Assert.Equal(1, completed.Counts.ActualProviderRequests["JavBus"]);
    }

    [Fact]
    public async Task DashboardUsesLatestPersistedCompletionEvidence()
    {
        MetadataCompletionWorkflow service = CreateService(new FakeProviderClient(null));
        MetadataCompletionPreview completed = await ExecuteAsync(service,
            Only("Actors") with { SelectionSeed = 81 });
        var resolver = new MediaStoragePathResolver(Database, root);
        MetadataHealthSummary health = await new MetadataHealthAnalysisService(Database, resolver).GetAsync();

        DashboardDto dashboard = await ProductReader.ReadDashboardAsync(Database, "http://127.0.0.1", health);

        Assert.Equal(1, dashboard.MetadataCompletion.EligiblePool);
        Assert.Equal(1, dashboard.MetadataCompletion.SelectedBatch);
        Assert.Equal(1, dashboard.MetadataCompletion.PendingCompletion);
        Assert.Equal(1, dashboard.MetadataCompletion.ProviderAllFailed);
        Assert.Equal(0, dashboard.MetadataCompletion.NumberAnomalies);
        Assert.Equal("NoResult", Assert.Single(completed.Items).Status);
    }

    [Fact]
    public async Task P1PreservesNonTargetMetadataAndUserState()
    {
        string at = Now();
        await ExecuteAsync("""
            INSERT INTO UserMovieState(MovieId,IsFavorite,UserRating,PlayCount,UpdatedAt)
            VALUES(1,1,4.5,7,$at);
            INSERT INTO Tags(Id,Name,NormalizedName,CreatedAt,UpdatedAt) VALUES(1,'User tag','USER TAG',$at,$at);
            INSERT INTO MovieTags(MovieId,TagId,CreatedAt) VALUES(1,1,$at);
            """, ("$at", at));
        ProviderMetadata metadata = DefaultMetadata("JavBus", "SONE-001", "Replacement description",
            ["Unexpected actor"], ["Unexpected genre"]) with {
            Title = "Replacement title", ReleaseDate = "2030-01-01", Director = "Unexpected director",
            Studio = "Unexpected studio", Series = "Filled series", Rating = 1.0m,
        };

        MetadataCompletionPreview completed = await ExecuteAsync(CreateService(new FakeProviderClient(metadata)),
            Only("Series") with { SelectionSeed = 777 });

        Assert.Equal("Completed", Assert.Single(completed.Items).Status);
        Assert.Equal("Existing title", await TextAsync("SELECT Title FROM Movies WHERE Id=1"));
        Assert.Equal("Existing description", await TextAsync("SELECT Description FROM Movies WHERE Id=1"));
        Assert.Equal("2026-01-01", await TextAsync("SELECT ReleaseDate FROM Movies WHERE Id=1"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM MovieActors WHERE MovieId=1"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM MovieGenres WHERE MovieId=1"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM MovieTags WHERE MovieId=1"));
        Assert.Equal(1, await ScalarAsync("SELECT IsFavorite FROM UserMovieState WHERE MovieId=1"));
        Assert.Equal(7, await ScalarAsync("SELECT PlayCount FROM UserMovieState WHERE MovieId=1"));
    }

    private MetadataCompletionWorkflow CreateService(FakeProviderClient provider,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var resolver = new MediaStoragePathResolver(Database, root);
        var health = new MetadataHealthAnalysisService(Database, resolver);
        var factory = new DummyHttpClientFactory();
        return new(Database, resolver, new MetadataProviderSettingsService(Database), provider,
            new MetadataWriteService(Database), new ImageDownloadService(factory), new NfoService(Database, resolver),
            health, new MovieNumberExtractor(FindMovieNumberRules()), new TaskLogService(Database), delay);
    }

    private static MetadataCompletionScanCommand Only(string field) => new(
        Actors: field == "Actors", Genres: field == "Genres", Poster: field == "Poster", Fanart: field == "Fanart",
        Nfo: field == "NFO", Description: field == "Description", Series: field == "Series",
        Director: field == "Director", Studio: field == "Studio", ReleaseDate: field == "ReleaseDate", Concurrency: 4);

    private async Task<MetadataCompletionPreview> ScanAsync(MetadataCompletionWorkflow service, MetadataCompletionScanCommand options)
    {
        MetadataCompletionLaunchResult launch = await service.StartDryRunAsync(options);
        await service.ProcessPendingOnceAsync();
        return await service.GetAsync(launch.TaskId);
    }

    private async Task<MetadataCompletionPreview> ExecuteAsync(MetadataCompletionWorkflow service, MetadataCompletionScanCommand options)
    {
        MetadataCompletionPreview preview = await ScanAsync(service, options);
        await service.ExecuteConfirmedAsync(preview.TaskId, preview.ConfirmationToken);
        await service.ProcessPendingOnceAsync();
        return await service.GetAsync(preview.TaskId);
    }

    private async Task AddSecondStandardMovieAsync()
    {
        string second = Path.Combine(root, "media", "SONE-002.mp4"); Write(second); string at = Now();
        await ExecuteAsync("""
            INSERT INTO Movies(Id,Code,Title,Description,ReleaseDate,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt)
            VALUES(2,'SONE-002','Second','Description','2026-01-02',3600,0,'pending','Test',$at,$at)
            """, ("$at", at));
        await using SqliteConnection connection = new($"Data Source={Database}"); await connection.OpenAsync();
        await InsertMediaAsync(connection, 2, 1, second, at);
    }

    private async Task AddStandardMoviesAsync(int firstId, int lastId)
    {
        string at = Now();
        await using SqliteConnection connection = new($"Data Source={Database}");
        await connection.OpenAsync();
        for (int id = firstId; id <= lastId; id++) {
            string code = $"SONE-{id:000}";
            string path = Path.Combine(root, "media", code + ".mp4");
            Write(path);
            await ExecuteAsync(connection, """
                INSERT INTO Movies(Id,Code,Title,Description,ReleaseDate,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt)
                VALUES($id,$code,$title,'Description','2026-01-02',3600,0,'pending','Test',$at,$at)
                """, ("$id", id), ("$code", code), ("$title", $"Movie {id}"), ("$at", at));
            await InsertMediaAsync(connection, id, 1, path, at);
        }
    }

    private async Task MakeMovieCompleteExceptActorsAsync()
    {
        string poster = Path.Combine(Storage, "Covers", "SONE-001.jpg");
        string fanart = Path.Combine(Storage, "Fanart", "SONE-001.jpg");
        string nfo = Path.Combine(Storage, "NFO", "SONE-001", "SONE-001.nfo");
        Write(poster); Write(fanart); Write(nfo); string at = Now();
        await ExecuteAsync("""
            INSERT INTO Genres(Id,Name,NormalizedName) VALUES(1,'Genre','GENRE');
            INSERT INTO MovieGenres(MovieId,GenreId) VALUES(1,1);
            INSERT INTO Studios(Id,Name,NormalizedName) VALUES(1,'Studio','STUDIO');
            INSERT INTO MovieStudios(MovieId,StudioId,RelationType) VALUES(1,1,'Studio');
            INSERT INTO Images(MovieId,ImageType,FilePath,IsPrimary,CreatedAt,UpdatedAt,Ownership,ValidationStatus)
            VALUES(1,'Poster',$poster,1,$at,$at,'Provider','Valid'),(1,'Fanart',$fanart,1,$at,$at,'Provider','Valid');
            UPDATE Movies SET NfoPath=$nfo WHERE Id=1;
            INSERT INTO NfoDocuments(MovieId,FilePath,Ownership,IsLocked,EncodingName,CreatedAt,UpdatedAt)
            VALUES(1,$nfo,'LMM',0,'utf-8',$at,$at);
            """, ("$poster", poster), ("$fanart", fanart), ("$nfo", nfo), ("$at", at));
    }

    private static ProviderMetadata DefaultMetadata(string provider, string code, string? description = null,
        IReadOnlyList<string>? actors = null, IReadOnlyList<string>? genres = null) =>
        new(provider, code, code, null, description, null, null, null, null, null, null, null,
            genres ?? [], actors ?? [], []);

    private static async Task InsertMediaAsync(SqliteConnection connection, long movieId, long libraryId, string path, string at)
    {
        await ExecuteAsync(connection, """
            INSERT INTO MediaFiles(MovieId,LibraryId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,DurationSeconds,CreatedAt,UpdatedAt)
            VALUES($movie,$library,$path,$normalized,$name,'.mp4',3,'Video','Test',1,'Present',3600,$at,$at)
            """, ("$movie", movieId), ("$library", libraryId), ("$path", path),
            ("$normalized", path.ToUpperInvariant()), ("$name", Path.GetFileName(path)), ("$at", at));
    }

    private async Task ExecuteAsync(string sql, params (string, object?)[] parameters)
    {
        await using SqliteConnection connection = new($"Data Source={Database}"); await connection.OpenAsync();
        await ExecuteAsync(connection, sql, parameters);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using SqliteConnection connection = new($"Data Source={Database}"); await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<string?> TextAsync(string sql)
    {
        await using SqliteConnection connection = new($"Data Source={Database}"); await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private static void Write(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, [1, 2, 3]); }
    private static string FindMovieNumberRules()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent) {
            string path = Path.Combine(current.FullName, "backend", "LocalMediaManager.Bridge", "movie-number-rules.json");
            if (File.Exists(path)) return path;
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

    private sealed class FakeProviderClient(ProviderMetadata? metadata) : IMetadataCompletionProviderClient
    {
        private int failuresRemaining;
        public ConcurrentQueue<string> Calls { get; } = new();
        public int FailuresRemaining { get => failuresRemaining; set => failuresRemaining = value; }

        public async Task<ProviderMetadata?> GetMetadataAsync(string provider, string code, string moviePath,
            MetadataProviderContext context, CancellationToken cancellationToken)
        {
            Calls.Enqueue(provider);
            if (Interlocked.Decrement(ref failuresRemaining) >= 0) throw new HttpRequestException("transient network failure");
            await (context.ProviderLog?.Invoke(provider, "Detail HTTP 200", cancellationToken) ?? Task.CompletedTask);
            ProviderMetadata? result = metadata is null ? null : metadata with { Provider = provider, ExternalId = code, Code = code };
            return result;
        }
    }

    private sealed class DelayRecorder
    {
        public ConcurrentBag<TimeSpan> Values { get; } = [];
        public Task DelayAsync(TimeSpan value, CancellationToken cancellationToken) { Values.Add(value); return Task.CompletedTask; }
    }

    private sealed class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
