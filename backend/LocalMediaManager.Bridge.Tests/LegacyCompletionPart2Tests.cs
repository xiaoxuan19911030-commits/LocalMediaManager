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
        var service = new TaskCommandService(Database, null!, null!, null!, null!, null!, null!, CreateActorProfileCompleteTaskService());

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
    public async Task CleanupAllTasksDeletesOnlyTerminalTasks()
    {
        await using var connection = await Open();
        string at = DateTimeOffset.UtcNow.ToString("O");
        await Execute(connection, "INSERT INTO Tasks(Id,TaskType,Status,Progress,TotalItems,CompletedItems,CreatedAt,UpdatedAt) VALUES(1,'Sync','Completed',100,1,1,$at,$at),(2,'Scan','Running',50,1,0,$at,$at)", ("$at", at));
        await Execute(connection, "INSERT INTO TaskLogs(TaskId,Level,Message,CreatedAt) VALUES(1,'Info','done',$at),(2,'Info','running',$at)", ("$at", at));
        var service = new TaskCommandService(Database, null!, null!, null!, null!, null!, null!, CreateActorProfileCompleteTaskService());

        TaskCleanupResult result = await service.CleanupAsync("all-tasks");

        Assert.Equal(1, result.Count);
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM Tasks WHERE Status='Running'"));
        Assert.Equal(0, await Scalar(connection, "SELECT COUNT(*) FROM TaskLogs WHERE TaskId=1"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM TaskLogs WHERE TaskId=2"));
    }

    [Fact]
    public async Task TaskListReturnsAllTasksAndPrioritizesActiveWork()
    {
        await using var connection = await Open();
        string at = DateTimeOffset.UtcNow.ToString("O");
        for (int id = 1; id <= 220; id++)
            await Execute(connection, "INSERT INTO Tasks(Id,TaskType,Status,Stage,Progress,TotalItems,CompletedItems,CreatedAt,UpdatedAt) VALUES($id,'Sync','Pending','Pending',0,1,0,$at,$at)", ("$id", id), ("$at", at));
        await Execute(connection, "INSERT INTO Tasks(Id,TaskType,Status,Stage,Progress,TotalItems,CompletedItems,CreatedAt,UpdatedAt) VALUES(221,'Sync','FetchingMetadata','FetchingMetadata',22,1,0,$at,$at)", ("$at", at));

        IReadOnlyList<TaskDto> tasks = await ProductReader.ReadTasksAsync(Database);

        Assert.Equal(221, tasks.Count);
        Assert.Equal(221, tasks[0].Id);
        Assert.Equal("FetchingMetadata", tasks[0].Status);
    }

    [Fact]
    public async Task TaskListNormalizesCompletedProgressToOneHundred()
    {
        await using var connection = await Open();
        string at = DateTimeOffset.UtcNow.ToString("O");
        await Execute(connection, "INSERT INTO Tasks(Id,TaskType,Status,Stage,Progress,TotalItems,CompletedItems,CreatedAt,UpdatedAt,CompletedAt) VALUES(1,'Sync','Completed','Completed',22,1,0,$at,$at,$at)", ("$at", at));

        IReadOnlyList<TaskDto> tasks = await ProductReader.ReadTasksAsync(Database);

        TaskDto task = Assert.Single(tasks);
        Assert.Equal(100, task.Progress);
        Assert.Equal(1, task.CompletedItems);
    }

    [Fact]
    public async Task TaskListUsesMovieCodeOrFileNameInsteadOfInternalIds()
    {
        await using var connection = await Open();
        string at = DateTimeOffset.UtcNow.ToString("O");
        await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt,ImportedAt) VALUES(1,'SONE-822','Some title',0,0,'pending','Test',$at,$at,$at),(2,'','',0,0,'pending','Test',$at,$at,$at)", ("$at", at));
        await Execute(connection, "INSERT INTO MediaFiles(MovieId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,CreatedAt,UpdatedAt) VALUES(1,$path1,$path1,'SONE-822.mp4','.mp4',1,'Video','Test',1,'Present',$at,$at),(2,$path2,$path2,'START-497.mp4','.mp4',1,'Video','Test',1,'Present',$at,$at)", ("$path1", Path.Combine(root, "SONE-822.mp4")), ("$path2", Path.Combine(root, "START-497.mp4")), ("$at", at));
        await Execute(connection, "INSERT INTO Tasks(Id,TaskType,Status,Stage,Provider,Progress,TotalItems,CompletedItems,PayloadJson,CreatedAt,UpdatedAt,CurrentMovieId) VALUES(1,'Sync','Pending','Pending','MetaTube',0,1,0,'{}',$at,$at,1),(2,'Sync','Pending','Pending','MetaTube',0,1,0,'{}',$at,$at,2),(3,'Sync','Pending','Pending','MetaTube',0,1,0,'{\"MovieId\":1}',$at,$at,NULL)", ("$at", at));

        IReadOnlyList<TaskDto> tasks = await ProductReader.ReadTasksAsync(Database);

        Assert.Contains(tasks, task => task.Id == 1 && task.Name == "SONE-822");
        Assert.Contains(tasks, task => task.Id == 2 && task.Name == "START-497.mp4");
        Assert.Contains(tasks, task => task.Id == 3 && task.Name == "SONE-822");
        Assert.DoesNotContain(tasks, task => task.Name.Contains('#'));
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

        MediaPageDto five = await ProductReader.AdvancedSearchAsync(Database, "http://127.0.0.1:47831", "", null, null, null, null, null, null, null, null, 0, "5", "all", "all", "all", null, "newest", 24, 0);
        Assert.Single(five.Items);
        Assert.Equal("RATE-001", five.Items[0].Code);

        MediaPageDto four = await ProductReader.AdvancedSearchAsync(Database, "http://127.0.0.1:47831", "", null, null, null, null, null, null, null, null, 0, "4", "all", "all", "all", null, "newest", 24, 0);
        Assert.Single(four.Items);
        Assert.Equal("RATE-002", four.Items[0].Code);

        MediaPageDto unrated = await ProductReader.AdvancedSearchAsync(Database, "http://127.0.0.1:47831", "", null, null, null, null, null, null, null, null, 0, "unrated", "all", "all", "all", null, "newest", 24, 0);
        Assert.Single(unrated.Items);
        Assert.Equal("RATE-004", unrated.Items[0].Code);
    }

    [Fact]
    public async Task DashboardAndMovieWallDefaultCountsOnlyAvailablePrimaryVideos()
    {
        await using var connection = await Open();
        string at = DateTimeOffset.UtcNow.ToString("O");
        for (int id = 1; id <= 2; id++) {
            await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt,ImportedAt) VALUES($id,$code,$code,0,0,'pending','Test',$at,$at,$at)", ("$id", id), ("$code", $"FILE-{id:000}"), ("$at", at));
            await Execute(connection, "INSERT INTO MediaFiles(MovieId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,CreatedAt,UpdatedAt) VALUES($id,$path,$path,$file,'.mp4',1,'Video','Test',1,$state,$at,$at)", ("$id", id), ("$path", Path.Combine(root, $"file-{id}.mp4")), ("$file", $"file-{id}.mp4"), ("$state", id == 1 ? "Present" : "Missing"), ("$at", at));
        }

        DashboardDto dashboard = await ProductReader.ReadDashboardAsync(Database, "http://127.0.0.1:47831");
        MediaPageDto defaultPage = await ProductReader.AdvancedSearchAsync(Database, "http://127.0.0.1:47831", "", null, null, null, null, null, null, null, null, 0, "all", "all", "all", "all", null, "newest", 24, 0);
        MediaPageDto missingPage = await ProductReader.AdvancedSearchAsync(Database, "http://127.0.0.1:47831", "", null, null, null, null, null, null, null, null, 0, "all", "all", "missing", "all", null, "newest", 24, 0);

        Assert.Equal(1, dashboard.MovieCount);
        Assert.Equal(1, defaultPage.Total);
        Assert.Equal("FILE-001", defaultPage.Items[0].Code);
        Assert.Equal(1, missingPage.Total);
        Assert.Equal("FILE-002", missingPage.Items[0].Code);
    }

    [Fact]
    public async Task DataCenterCountsOnlyAvailablePrimaryVideos()
    {
        await using var connection = await Open();
        string at = DateTimeOffset.UtcNow.ToString("O");
        for (int id = 1; id <= 4; id++)
            await Execute(connection, "INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt,ImportedAt) VALUES($id,$code,$code,0,0,'pending','Test',$at,$at,$at)", ("$id", id), ("$code", $"COUNT-{id:000}"), ("$at", at));
        await Execute(connection, """
            INSERT INTO MediaFiles(MovieId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,CreatedAt,UpdatedAt)
            VALUES(1,$path1,$path1,'COUNT-001.mp4','.mp4',1,'Video','Test',1,'Present',$at,$at),
                  (2,$path2,$path2,'COUNT-002.mp4','.mp4',1,'Video','Test',1,'Present',$at,$at),
                  (3,$path3,$path3,'COUNT-003.mp4','.mp4',1,'Video','Test',1,'Missing',$at,$at)
            """, ("$path1", Path.Combine(root, "COUNT-001.mp4")), ("$path2", Path.Combine(root, "COUNT-002.mp4")),
            ("$path3", Path.Combine(root, "COUNT-003.mp4")), ("$at", at));

        MetadataOverviewDto overview = await ProductReader.ReadMetadataOverviewAsync(Database);
        MaintenanceReportDto maintenance = await MaintenanceReader.ReadAsync(Database, root, "http://127.0.0.1:47831", 50, 0);

        Assert.Equal(2, overview.TotalMovies);
        Assert.Equal(1, overview.MissingFiles);
        Assert.Equal(2, maintenance.Stats.TotalMovies);
    }

    [Fact]
    public async Task MovieMetadataStatusUsesGenresForScrapedTags()
    {
        await using var connection = await Open();
        string at = DateTimeOffset.UtcNow.ToString("O");
        await Execute(connection, "INSERT INTO Movies(Id,Code,Title,Description,DurationSeconds,IsScraped,ScrapeStatus,NfoPath,LegacySource,CreatedAt,UpdatedAt,ImportedAt) VALUES(1,'META-001','Meta title','Plot',3600,1,'complete',$nfo,'Test',$at,$at,$at)", ("$nfo", Path.Combine(root, "META-001.nfo")), ("$at", at));
        await Execute(connection, "INSERT INTO MediaFiles(MovieId,FilePath,NormalizedPath,FileName,Extension,FileSize,MediaType,SourceType,IsPrimary,ExistsState,CreatedAt,UpdatedAt) VALUES(1,$path,$path,'META-001.mp4','.mp4',1,'Video','Test',1,'Present',$at,$at)", ("$path", Path.Combine(root, "META-001.mp4")), ("$at", at));
        await Execute(connection, "INSERT INTO Images(MovieId,ImageType,FilePath,IsPrimary,ValidationStatus,CreatedAt,UpdatedAt) VALUES(1,'Poster',$poster,1,'Valid',$at,$at),(1,'Fanart',$fanart,0,'Valid',$at,$at),(1,'Preview',$preview,0,'Valid',$at,$at)", ("$poster", Path.Combine(root, "poster.jpg")), ("$fanart", Path.Combine(root, "fanart.jpg")), ("$preview", Path.Combine(root, "preview.jpg")), ("$at", at));
        await Execute(connection, "INSERT INTO Actors(Id,Name,NormalizedName,LegacySource,CreatedAt,UpdatedAt) VALUES(1,'Actor A','ACTOR A','Test',$at,$at); INSERT INTO MovieActors(MovieId,ActorId,SortOrder) VALUES(1,1,0)", ("$at", at));
        await Execute(connection, "INSERT INTO Genres(Id,Name,NormalizedName) VALUES(1,'Genre A','GENRE A'); INSERT INTO MovieGenres(MovieId,GenreId) VALUES(1,1)");

        MovieDetailDto? movie = await ProductReader.ReadMovieAsync(Database, "http://127.0.0.1:47831", 1);

        Assert.NotNull(movie);
        Assert.True(movie.MetadataStatus.Checks.Single(item => item.Key == "tags").Complete);
        Assert.DoesNotContain("标签", movie.MetadataStatus.MissingItems);
    }

    [Fact]
    public async Task DirectorMigrationIsIdempotentAndReaderCompatible()
    {
        await using var connection = await Open();
        string migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", "0013_DirectorMetadata.sql"));

        await Execute(connection, migration);
        await Execute(connection, migration);
        EntityPageDto directors = await ProductReader.ReadEntitiesPageAsync(Database, "http://127.0.0.1:47831", "directors", "", "count", 24, 0);

        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Directors'"));
        Assert.Equal(1, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MovieDirectors'"));
        Assert.Empty(directors.Items);
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

    private ActorProfileCompleteTaskService CreateActorProfileCompleteTaskService()
    {
        var factory = new DummyHttpClientFactory();
        var providerService = new ActorProfileProviderService(Database, new MetadataProviderSettingsService(Database),
            new MinnanoActorProfileProvider(factory), new WikipediaJpActorProfileProvider(factory), new ActorProfileService(Database));
        return new(Database, providerService, new TaskLogService(Database));
    }

    private sealed class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
