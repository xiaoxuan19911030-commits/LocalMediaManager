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
        IReadOnlyList<MetadataSearchResult> results = await provider.SearchAsync("abp_001", settings, CancellationToken.None);
        Assert.Equal("FANZA", results[0].Provider);
        ProviderMetadata? metadata = await provider.GetMetadataAsync(results[0], settings, CancellationToken.None);
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

        IReadOnlyList<MetadataSearchResult> results = await provider.SearchAsync("NO-RESULT", settings, CancellationToken.None);

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
    public async Task InterruptedSyncIsRecoveredToRetryingOnExecutorStart()
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
        var executor = new MetadataSyncExecutor(Database, resolver, settings, new MetaTubeProvider(factory),
            new MetadataWriteService(Database), new ImageDownloadService(factory), new NfoService(Database, resolver), new TaskLogService(Database));

        await executor.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await executor.StopAsync(CancellationToken.None);

        await using SqliteConnection verify = await Open();
        Assert.Equal("Retrying", await Text(verify, "SELECT Status FROM Tasks WHERE Id=1"));
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

    public Task DisposeAsync() { try { Directory.Delete(root, true); } catch { } return Task.CompletedTask; }
    private MetadataSyncExecutor CreateExecutor()
    {
        var settings = new MetadataProviderSettingsService(Database);
        var factory = new FakeHttpClientFactory(_ => new(HttpStatusCode.ServiceUnavailable));
        var resolver = new MediaStoragePathResolver(Database, root);
        return new(Database, resolver, settings, new MetaTubeProvider(factory),
            new MetadataWriteService(Database), new ImageDownloadService(factory), new NfoService(Database, resolver), new TaskLogService(Database));
    }
    private async Task<SqliteConnection> Open() { var c = new SqliteConnection($"Data Source={Database}"); await c.OpenAsync(); return c; }
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
