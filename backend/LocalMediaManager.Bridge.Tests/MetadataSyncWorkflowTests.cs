using System.Net;
using System.Text;
using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class MetadataSyncWorkflowTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-sync-tests", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "test.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0004_LibraryScanWorkflow.sql", "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0007_NfoWorkflow.sql", "0008_FileOrganizerWorkflow.sql", "0009_PlaybackSettings.sql", "0012_MediaStorageSettings.sql", "0013_DirectorMetadata.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task MetaTubeUsesExactCodeAndLegacyProviderPreference()
    {
        var factory = new FakeHttpClientFactory(request => {
            string body = request.RequestUri!.AbsolutePath.Contains("search")
                ? """{"data":[{"provider":"AVBASE","id":"a","number":"ABP-001","title":"A"},{"provider":"FANZA","id":"f","number":"ABP-001","title":"F"}]}"""
                : """{"data":{"provider":"FANZA","id":"f","number":"ABP-001","title":"Remote title","summary":"Plot","runtime":120,"release_date":"2026-01-02","maker":"Maker","backdrop_url":"https://img.example/backdrop.jpg","actors":["Actor A"],"genres":["Genre A"],"preview_images":["https://img.example/1.jpg"]}}""";
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        });
        var provider = new MetaTubeProvider(factory);
        var settings = new MetaTubeSettingsDto(true, "http://127.0.0.1:8080/", 30, true, false, true, true);
        MetadataProviderContext context = Context(settings);
        IReadOnlyList<MetadataSearchResult> results = await provider.SearchAsync("abp_001", context, CancellationToken.None);
        Assert.Equal("FANZA", results[0].Provider);
        ProviderMetadata? metadata = await provider.GetMetadataAsync(results[0], context, CancellationToken.None);
        Assert.NotNull(metadata);
        Assert.Equal("ABP-001", metadata.Code);
        Assert.Equal(7200, metadata.DurationSeconds);
        Assert.Contains(metadata.Images, image => image.Type == "Poster" && image.Url.Contains("/v1/images/primary/FANZA/f"));
        Assert.Contains(metadata.Images, image => image.Type == "Fanart");
    }

    [Fact]
    public async Task MetaTubeSearchNotFoundIsReportedAsEmptyResultWithoutRetries()
    {
        int requests = 0;
        var provider = new MetaTubeProvider(new FakeHttpClientFactory(_ => {
            requests++;
            return new(HttpStatusCode.NotFound) { Content = new StringContent("not found") };
        }));
        var settings = new MetaTubeSettingsDto(true, "http://127.0.0.1:8080/", 30, true, false, true, true);

        IReadOnlyList<MetadataSearchResult> results = await provider.SearchAsync("NO-RESULT", Context(settings), CancellationToken.None);

        Assert.Empty(results);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task MetadataWriteFillsEmptyFieldsAndPreservesManualData()
    {
        await using var connection = await Open();
        string at = DateTimeOffset.UtcNow.ToString("O");
        await Execute(connection, "INSERT INTO Movies(Id,Code,Title,Description,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'ABP-001','Manual title',NULL,0,0,'pending','Test',$at,$at)", ("$at", at));
        await Execute(connection, "INSERT INTO Tasks(Id,TaskType,Status,Progress,TotalItems,CompletedItems,CreatedAt,CurrentMovieId) VALUES(1,'Sync','WritingMetadata',80,1,0,$at,1)", ("$at", at));
        await Execute(connection, "INSERT INTO Tags(Id,Name,NormalizedName,Source,CreatedAt,UpdatedAt) VALUES(1,'My tag','MY TAG','User',$at,$at)", ("$at", at));
        await Execute(connection, "INSERT INTO MovieTags(MovieId,TagId,CreatedAt) VALUES(1,1,$at)", ("$at", at));
        var movie = new SyncMovie(1, "ABP-001", "Manual title", null, null, 0, null, null);
        var metadata = new ProviderMetadata("FANZA", "remote-1", "ABP-001", "Remote title", "Remote plot", null,
            "Remote maker", null, "Remote series", 7200, "2026-01-02", null, ["Remote genre"], ["Remote actor"], []);
        await new MetadataWriteService(Database).ApplyAsync(1, movie, metadata, new([], null, []), false, CancellationToken.None);
        Assert.Equal("Manual title", await Text(connection, "SELECT Title FROM Movies WHERE Id=1"));
        Assert.Equal("Remote plot", await Text(connection, "SELECT Description FROM Movies WHERE Id=1"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM MovieTags WHERE MovieId=1"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM MovieActors WHERE MovieId=1"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM MetadataSyncSnapshots WHERE TaskId=1 AND AppliedAt IS NOT NULL"));
    }

    [Fact]
    public async Task MetadataOverwriteRefreshesScrapedFieldsAndDirectorRelations()
    {
        await using var connection = await Open();
        string at = DateTimeOffset.UtcNow.ToString("O");
        await Execute(connection, "INSERT INTO Movies(Id,Code,Title,Description,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'OLD-001','Old title','Old plot',60,1,'complete','Test',$at,$at)", ("$at", at));
        await Execute(connection, "INSERT INTO Tasks(Id,TaskType,Status,Progress,TotalItems,CompletedItems,CreatedAt,CurrentMovieId) VALUES(1,'Sync','WritingMetadata',80,1,0,$at,1)", ("$at", at));
        await Execute(connection, "INSERT INTO Genres(Id,Name,NormalizedName) VALUES(1,'Old genre','OLD GENRE'); INSERT INTO MovieGenres(MovieId,GenreId) VALUES(1,1)");
        await Execute(connection, "INSERT INTO Series(Id,Name,NormalizedName) VALUES(1,'Old series','OLD SERIES'); INSERT INTO MovieSeries(MovieId,SeriesId,SortOrder) VALUES(1,1,0)");
        await Execute(connection, "INSERT INTO Actors(Id,Name,NormalizedName,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'Old actor','OLD ACTOR','Test',$at,$at); INSERT INTO MovieActors(MovieId,ActorId,RoleName,SortOrder) VALUES(1,1,'',0)", ("$at", at));
        await Execute(connection, "INSERT INTO Tags(Id,Name,NormalizedName,Source,CreatedAt,UpdatedAt) VALUES(1,'User tag','USER TAG','User',$at,$at); INSERT INTO MovieTags(MovieId,TagId,CreatedAt) VALUES(1,1,$at)", ("$at", at));
        var movie = new SyncMovie(1, "OLD-001", "Old title", "Old plot", null, 60, null, null);
        var metadata = new ProviderMetadata("FANZA", "remote-2", "NEW-001", "New title", "New plot", "Director A",
            "Studio A", null, "Series A", 7200, "2026-01-02", null, ["New genre"], ["Actor A"], []);

        await new MetadataWriteService(Database).ApplyAsync(1, movie, metadata, new([], "Z:\\Media\\NFO\\NEW-001\\NEW-001.nfo", []), true, CancellationToken.None);

        Assert.Equal("NEW-001", await Text(connection, "SELECT Code FROM Movies WHERE Id=1"));
        Assert.Equal("New title", await Text(connection, "SELECT Title FROM Movies WHERE Id=1"));
        Assert.Equal("New plot", await Text(connection, "SELECT Description FROM Movies WHERE Id=1"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM MovieGenres mg JOIN Genres g ON g.Id=mg.GenreId WHERE mg.MovieId=1 AND g.Name='New genre'"));
        Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM MovieGenres mg JOIN Genres g ON g.Id=mg.GenreId WHERE mg.MovieId=1 AND g.Name='Old genre'"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM MovieDirectors md JOIN Directors d ON d.Id=md.DirectorId WHERE md.MovieId=1 AND d.Name='Director A'"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM MovieActors ma JOIN Actors a ON a.Id=ma.ActorId WHERE ma.MovieId=1 AND a.Name='Actor A'"));
        Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM MovieActors ma JOIN Actors a ON a.Id=ma.ActorId WHERE ma.MovieId=1 AND a.Name='Old actor'"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM MovieTags WHERE MovieId=1"));
    }

    [Fact]
    public async Task ProviderSettingsPersistThroughUnifiedSettingsService()
    {
        var service = new MetadataProviderSettingsService(Database);
        MetaTubeSettingsDto saved = await service.SaveMetaTubeAsync(new(false, "http://localhost:8080", 500, false, true, false, false));
        MetaTubeSettingsDto read = await service.ReadMetaTubeAsync();
        Assert.False(read.Enabled);
        Assert.Equal("http://localhost:8080/", read.BaseUrl);
        Assert.Equal(180, read.TimeoutSeconds);
        Assert.True(read.NonDestructive);
        Assert.Equal(saved, read);
    }

    [Fact]
    public async Task JavBusNormalizesCodeAndParsesMovieHtml()
    {
        string html = JavBusHtml();
        var provider = new JavBusProvider(new FakeHttpClientFactory(_ => new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") }));
        MetadataProviderContext context = Context(javBus: new(true, 2, "https://www.javbus.com/", 30, 1, "secret-cookie", true, true));

        IReadOnlyList<MetadataSearchResult> results = await provider.SearchAsync("abp001", context, CancellationToken.None);
        ProviderMetadata? metadata = await provider.GetMetadataAsync(results[0], context, CancellationToken.None);

        Assert.Equal("ABP-001", results[0].Code);
        Assert.NotNull(metadata);
        Assert.Equal("JavBus", metadata.Provider);
        Assert.Equal("ABP-001", metadata.Code);
        Assert.Equal("Sample Title", metadata.Title);
        Assert.Equal("Director A", metadata.Director);
        Assert.Equal("Studio A", metadata.Studio);
        Assert.Equal("Series A", metadata.Series);
        Assert.Equal(7200, metadata.DurationSeconds);
        Assert.Contains("Actor A", metadata.Actors);
        Assert.Contains("Drama", metadata.Genres);
        Assert.Contains(metadata.Images, image => image.Type == "Poster" && image.Url == "https://www.javbus.com/cover.jpg");
    }

    [Fact]
    public async Task JavBusMissingFieldsAndMissingImagesDoNotCrash()
    {
        string html = """<html><body><h3>ABP-002 Sparse</h3><p><span class="header">識別碼:</span> ABP-002</p></body></html>""";
        var provider = new JavBusProvider(new FakeHttpClientFactory(_ => new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") }));

        ProviderMetadata? metadata = await provider.GetMetadataAsync(new("JavBus", "ABP-002", "ABP-002", null), Context(), CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Empty(metadata.Images);
        Assert.Empty(metadata.Actors);
        Assert.Empty(metadata.Genres);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "未找到")]
    [InlineData(HttpStatusCode.Forbidden, "请求被拒绝")]
    [InlineData((HttpStatusCode)429, "请求过于频繁")]
    public async Task JavBusClassifiesHttpFailures(HttpStatusCode status, string expected)
    {
        var provider = new JavBusProvider(new FakeHttpClientFactory(_ => new(status) { Content = new StringContent("") }));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetMetadataAsync(new("JavBus", "ABP-404", "ABP-404", null), Context(), CancellationToken.None));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task JavBusSettingsPersistAndCookieIsNotLogged()
    {
        var service = new MetadataProviderSettingsService(Database);
        JavBusSettingsDto saved = await service.SaveJavBusAsync(new(true, 3, "https://www.javbus.com", 500, 5, "adult=secret", true, false));
        JavBusSettingsDto read = await service.ReadJavBusAsync();

        Assert.True(read.Enabled);
        Assert.Equal(3, read.Priority);
        Assert.Equal("https://www.javbus.com/", read.BaseUrl);
        Assert.Equal(180, read.TimeoutSeconds);
        Assert.Equal(3, read.RetryCount);
        Assert.Equal("adult=secret", read.Cookie);
        Assert.True(saved.FillMissingOnly);
    }

    [Fact]
    public async Task InterruptedSyncIsRecoveredBeforeWorkerContinues()
    {
        await using (var connection = await Open()) {
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'ABP-001','ABP-001',0,0,'pending','Test',$at,$at)", ("$at", at));
            await Execute(connection, "INSERT INTO Tasks(Id,TaskType,Status,Stage,Provider,Progress,TotalItems,CompletedItems,CreatedAt,UpdatedAt,CurrentMovieId) VALUES(1,'Sync','FetchingMetadata','FetchingMetadata','MetaTube',22,1,0,$at,$at,1)", ("$at", at));
        }
        var settings = new MetadataProviderSettingsService(Database);
        await settings.SaveMetaTubeAsync(new(false, "http://127.0.0.1:8080/", 30, false, false, false, true));
        var factory = new FakeHttpClientFactory(_ => new(HttpStatusCode.ServiceUnavailable));
        var resolver = new MediaStoragePathResolver(Database, root);
        var executor = new MetadataSyncExecutor(Database, resolver, settings, CreateDiagnostics(settings), new MetaTubeProvider(factory),
            new MetadataWriteService(Database), new ImageDownloadService(factory), new NfoService(Database, resolver), new TaskLogService(Database));

        await executor.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await executor.StopAsync(CancellationToken.None);

        await using SqliteConnection verify = await Open();
        string? status = await Text(verify, "SELECT Status FROM Tasks WHERE Id=1");
        Assert.NotEqual("FetchingMetadata", status);
        Assert.Equal(1, await Scalar(verify, "SELECT RetryCount FROM Tasks WHERE Id=1"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM TaskLogs WHERE TaskId=1 AND Message LIKE '%异常中断%'"));
    }

    [Fact]
    public async Task BatchSyncCreatesDistinctTasksAndReusesExistingActiveTask()
    {
        await using (var connection = await Open()) {
            await InsertMovie(connection, 1, "BATCH-001");
            await InsertMovie(connection, 2, "BATCH-002");
        }
        MetadataSyncExecutor executor = CreateExecutor();

        BatchTaskMutationResult result = await executor.EnqueueBatchAsync([1, 2, 2]);
        BatchTaskMutationResult second = await executor.EnqueueBatchAsync([1]);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, second.Count);
        await using SqliteConnection verify = await Open();
        Assert.Equal(2, await Scalar(verify, "SELECT COUNT(*) FROM Tasks WHERE TaskType='Sync'"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM Tasks WHERE TaskType='Sync' AND CurrentMovieId=1"));
    }

    [Fact]
    public async Task LibrarySyncEnqueuesAllActiveMoviesInLibrary()
    {
        await using (var connection = await Open()) {
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, "INSERT INTO Libraries(Id,Name,IsEnabled,SortOrder,CreatedAt,UpdatedAt) VALUES(1,'A',1,0,$at,$at),(2,'B',1,1,$at,$at)", ("$at", at));
            for (int i = 1; i <= 120; i++) {
                await InsertMovie(connection, i, $"LIB-{i:000}");
                long libraryId = i <= 100 ? 1 : 2;
                await Execute(connection, "INSERT INTO MediaFiles(MovieId,LibraryId,FilePath,NormalizedPath,FileName,MediaType,SourceType,IsPrimary,ExistsState,CreatedAt,UpdatedAt) VALUES($movie,$library,$path,$path,$name,'Video','Test',1,'Present',$at,$at)",
                    ("$movie", i), ("$library", libraryId), ("$path", $"Z:\\Videos\\LIB-{i:000}.mp4"), ("$name", $"LIB-{i:000}.mp4"), ("$at", at));
            }
        }
        MetadataSyncExecutor executor = CreateExecutor();

        BatchTaskMutationResult scoped = await executor.EnqueueLibraryAsync(1);
        BatchTaskMutationResult all = await executor.EnqueueLibraryAsync();

        await using SqliteConnection verify = await Open();
        Assert.Equal(100, scoped.Count);
        Assert.Equal(120, all.Count);
        Assert.Equal(120, await Scalar(verify, "SELECT COUNT(*) FROM Tasks WHERE TaskType='Sync'"));
        Assert.Equal(100, await Scalar(verify, "SELECT COUNT(DISTINCT t.CurrentMovieId) FROM Tasks t JOIN MediaFiles f ON f.MovieId=t.CurrentMovieId WHERE f.LibraryId=1"));
    }

    [Fact]
    public async Task BatchCancelOnlyCancelsSyncTasks()
    {
        await using (var connection = await Open()) {
            await InsertMovie(connection, 1, "CANCEL-001");
            await InsertMovie(connection, 2, "CANCEL-002");
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, "INSERT INTO Tasks(Id,TaskType,Status,Stage,Provider,Progress,TotalItems,CompletedItems,CreatedAt,UpdatedAt,CurrentMovieId) VALUES(10,'Sync','Pending','Pending','MetaTube',0,1,0,$at,$at,1),(11,'Sync','Running','FetchingMetadata','MetaTube',20,1,0,$at,$at,2),(12,'Scan','Running','Running',NULL,0,1,0,$at,$at,NULL)", ("$at", at));
        }
        var service = new TaskCommandService(Database, null!, CreateExecutor(), null!, null!, null!, null!);

        BatchTaskMutationResult result = await service.CancelSyncBatchAsync([10, 11, 11]);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CancelSyncBatchAsync([12]));

        Assert.Equal(2, result.Count);
        await using SqliteConnection verify = await Open();
        Assert.Equal(2, await Scalar(verify, "SELECT COUNT(*) FROM Tasks WHERE TaskType='Sync' AND Status='Cancelled'"));
        Assert.Equal(2, await Scalar(verify, "SELECT COUNT(*) FROM TaskLogs WHERE Message LIKE '%用户取消%'"));
        Assert.Equal("Running", await Text(verify, "SELECT Status FROM Tasks WHERE Id=12"));
    }

    [Fact]
    public async Task BatchCancelCanCancelVisibleActiveTaskTypes()
    {
        await using (var connection = await Open()) {
            await InsertMovie(connection, 1, "CANCEL-001");
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, "INSERT INTO Tasks(Id,TaskType,Status,Stage,Provider,Progress,TotalItems,CompletedItems,CreatedAt,UpdatedAt,CurrentMovieId) VALUES(10,'Sync','Pending','Pending','MetaTube',0,1,0,$at,$at,1),(11,'Scan','Running','Running',NULL,20,1,0,$at,$at,NULL),(12,'Screenshot','Completed','Completed','FFmpeg',100,1,1,$at,$at,1)", ("$at", at));
        }
        var service = new TaskCommandService(Database, CreateLibraryService(), CreateExecutor(), null!, null!, CreateImageGenerationService(), null!);

        BatchTaskMutationResult result = await service.CancelBatchAsync([10, 11, 12]);

        Assert.Equal(2, result.Count);
        await using SqliteConnection verify = await Open();
        Assert.Equal(2, await Scalar(verify, "SELECT COUNT(*) FROM Tasks WHERE Status='Cancelled'"));
        Assert.Equal("Completed", await Text(verify, "SELECT Status FROM Tasks WHERE Id=12"));
    }

    [Fact]
    public async Task SyncWorkerClaimsManualTasksEvenWhenProviderToggleIsDisabled()
    {
        await using (var connection = await Open()) {
            await InsertMovie(connection, 1, "AUTO-001");
        }
        MetadataProviderSettingsService settings = new(Database);
        await settings.SaveMetaTubeAsync(new(false, "http://127.0.0.1:8080/", 30, false, false, false, true));
        var factory = new FakeHttpClientFactory(request => {
            string body = request.RequestUri!.AbsolutePath.Contains("search")
                ? """{"data":[{"provider":"FANZA","id":"auto","number":"AUTO-001","title":"Auto"}]}"""
                : """{"data":{"provider":"FANZA","id":"auto","number":"AUTO-001","title":"Auto title","summary":"Plot","runtime":60,"release_date":"2026-01-02","maker":"Maker","actors":[],"genres":[],"preview_images":[]}}""";
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        });
        var resolver = new MediaStoragePathResolver(Database, root);
        var executor = new MetadataSyncExecutor(Database, resolver, settings, CreateDiagnostics(settings, factory), new MetaTubeProvider(factory),
            new MetadataWriteService(Database), new ImageDownloadService(factory), new NfoService(Database, resolver), new TaskLogService(Database));
        MetadataSyncLaunchResult launch = await executor.EnqueueAsync(1, "Manual");

        await executor.StartAsync(CancellationToken.None);
        string? status = null;
        for (int attempt = 0; attempt < 50 && status != "Completed"; attempt++) {
            await Task.Delay(50);
            await using SqliteConnection check = await Open();
            status = await Text(check, $"SELECT Status FROM Tasks WHERE Id={launch.TaskId}");
        }
        await executor.StopAsync(CancellationToken.None);

        Assert.Equal("Completed", status);
    }

    public Task DisposeAsync() { try { Directory.Delete(root, true); } catch { } return Task.CompletedTask; }
    private MetadataSyncExecutor CreateExecutor()
    {
        var settings = new MetadataProviderSettingsService(Database);
        var factory = new FakeHttpClientFactory(_ => new(HttpStatusCode.ServiceUnavailable));
        var resolver = new MediaStoragePathResolver(Database, root);
        return new(Database, resolver, settings, CreateDiagnostics(settings), new MetaTubeProvider(factory),
            new MetadataWriteService(Database), new ImageDownloadService(factory), new NfoService(Database, resolver), new TaskLogService(Database));
    }
    private static ProviderDiagnosticsService CreateDiagnostics(MetadataProviderSettingsService settings, IHttpClientFactory? factory = null)
    {
        IHttpClientFactory diagnosticsFactory = factory ?? new FakeHttpClientFactory(_ => new(HttpStatusCode.OK) { Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json") });
        return new(settings, new MdcNgProvider(), new MetaTubeProvider(diagnosticsFactory), new JavBusProvider(diagnosticsFactory),
            enableNetworkFiltering: false);
    }
    private LibraryWorkflowService CreateLibraryService() => new(Database);
    private ImageGenerationTaskService CreateImageGenerationService()
    {
        var resolver = new MediaStoragePathResolver(Database, root);
        return new(Database, resolver, new ImageWorkflowService(Database, root, resolver), new TaskLogService(Database), new FfmpegLocator(Database, root));
    }
    private async Task<SqliteConnection> Open() { var c = new SqliteConnection($"Data Source={Database}"); await c.OpenAsync(); return c; }
    private static MetadataProviderContext Context(MetaTubeSettingsDto? metaTube = null, JavBusSettingsDto? javBus = null, string? preferredSource = null) =>
        new(metaTube ?? new(true, "http://127.0.0.1:8080/", 30, true, false, true, true),
            javBus ?? SettingsDefaults.JavBus,
            preferredSource);
    private static string JavBusHtml() => """
        <html><body>
        <h3>ABP-001 Sample Title</h3>
        <a class="bigImage" href="/cover.jpg"><img src="/thumb.jpg"></a>
        <p><span class="header">識別碼:</span> ABP-001</p>
        <p><span class="header">發行日期:</span> 2024-01-02</p>
        <p><span class="header">長度:</span> 120分鐘</p>
        <p><span class="header">導演:</span> <a>Director A</a></p>
        <p><span class="header">製作商:</span> <a>Studio A</a></p>
        <p><span class="header">發行商:</span> <a>Publisher A</a></p>
        <p><span class="header">系列:</span> <a>Series A</a></p>
        <a href="/star/abc">Actor A</a><a href="/star/def">Actor B</a>
        <a href="/genre/drama">Drama</a><a href="/genre/hd">HD</a>
        <a class="sample-box" href="/sample1.jpg">sample</a>
        </body></html>
        """;
    private static Task InsertMovie(SqliteConnection c, long id, string code)
    {
        string at = DateTimeOffset.UtcNow.ToString("O");
        return Execute(c, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt) VALUES($id,$code,$code,0,0,'pending','Test',$at,$at)", ("$id", id), ("$code", code), ("$at", at));
    }
    private static async Task Execute(SqliteConnection c, string sql, params (string,object?)[] values) { await using var x=c.CreateCommand();x.CommandText=sql;foreach(var(n,v)in values)x.Parameters.AddWithValue(n,v??DBNull.Value);await x.ExecuteNonQueryAsync(); }
    private static async Task<long> Scalar(SqliteConnection c,string sql){await using var x=c.CreateCommand();x.CommandText=sql;return Convert.ToInt64(await x.ExecuteScalarAsync()??0L);}
    private static async Task<string?> Text(SqliteConnection c,string sql){await using var x=c.CreateCommand();x.CommandText=sql;return(await x.ExecuteScalarAsync())?.ToString();}

    private sealed class FakeHttpClientFactory(Func<HttpRequestMessage,HttpResponseMessage> response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(response), disposeHandler: true);
        private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
        }
    }
}
