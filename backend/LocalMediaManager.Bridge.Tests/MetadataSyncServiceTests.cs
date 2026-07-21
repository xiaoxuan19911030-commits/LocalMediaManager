using System.Net;
using System.Text;
using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class MetadataSyncServiceTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lmm-metadata-sync-service", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "settings.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        await using var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        foreach (string file in new[] { "0001_InitialSchema.sql", "0003_UserStateAuditAndRatingMemory.sql", "0005_MetadataSyncWorkflow.sql", "0006_ImageAssetWorkflow.sql", "0012_MediaStorageSettings.sql", "0013_DirectorMetadata.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
        await Execute(connection, "UPDATE AppSettings SET ValueJson=$root WHERE Key='mediaStorage.rootPath'",
            ("$root", System.Text.Json.JsonSerializer.Serialize(Path.Combine(root, "MediaStorage"))));
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(root, true); } catch { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SyncSuccessReturnsUnifiedMetadataResult()
    {
        var service = CreateService(request => {
            if (request.Method == HttpMethod.Post) return new(HttpStatusCode.OK) { Content = new StringContent("") };
            if (request.RequestUri!.AbsolutePath == "/api/manual-jobs")
                return Json("""{"data":[{"id":11,"source_pathes":"[\"/media/SONE-104.mp4\"]","status":1,"total_count":1}]}""");
            if (request.RequestUri!.AbsolutePath == "/api/tasks_full")
                return Json("""
                {"data":[{"id":21,"manual_job_id":11,"status":1,"metadata":{"Number":"SONE-104","Title":"Remote title","Actors":"Actor A","Studio":"Studio A","Runtime":"124","UserRating":"4.17","Poster":"https://img.example/poster.jpg"}}]}
                """);
            return new(HttpStatusCode.NotFound);
        });

        MetadataSyncResult result = await service.SyncAsync(new("SONE-104", "/media/SONE-104.mp4", "mdc-ng"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("mdc-ng", result.ProviderId);
        Assert.True(result.ElapsedMilliseconds >= 0);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Metadata);
        Assert.Equal("SONE-104", result.Metadata.Code);
        Assert.Equal("Remote title", result.Metadata.Title);
        Assert.Contains("Actor A", result.Metadata.Actors);
        Assert.Equal("Studio A", result.Metadata.Studio);
        Assert.Equal(7440, result.Metadata.DurationSeconds);
        Assert.Equal(4.17m, result.Metadata.Rating);
    }

    [Fact]
    public async Task SyncMovieImportsMovieMetadataIntoSqliteAndDetailReadsFromDatabase()
    {
        await using (SqliteConnection connection = await Open()) {
            string at = DateTimeOffset.UtcNow.ToString("O");
            await Execute(connection, """
                INSERT INTO Movies(Id,Code,Title,DurationSeconds,IsScraped,ScrapeStatus,LegacySource,CreatedAt,UpdatedAt)
                VALUES(1,'SONE-104','SONE-104',0,0,'pending','Test',$at,$at)
                """, ("$at", at));
            await Execute(connection, """
                INSERT INTO MediaFiles(MovieId,FilePath,NormalizedPath,FileName,MediaType,SourceType,IsPrimary,ExistsState,CreatedAt,UpdatedAt)
                VALUES(1,'/media/SONE-104.mp4','/media/SONE-104.mp4','SONE-104.mp4','Video','Test',1,'Present',$at,$at)
                """, ("$at", at));
        }
        var service = CreateService(request => {
            if (request.Method == HttpMethod.Post) return new(HttpStatusCode.OK) { Content = new StringContent("") };
            if (request.RequestUri!.AbsolutePath == "/api/manual-jobs")
                return Json("""{"data":[{"id":11,"source_pathes":"[\"/media/SONE-104.mp4\"]","status":1,"total_count":1}]}""");
            if (request.RequestUri!.AbsolutePath == "/api/tasks_full")
                return Json("""
                {"data":[{"id":21,"manual_job_id":11,"status":1,"metadata":{
                  "Number":"SONE-104","Title":"Imported title","OriginalTitle":"Original imported title",
                  "Outline":"Imported plot","Actors":[{"name":"Actor A","image":"https://img.example/actor-a.png"},"Actor B"],"Director":"Director A",
                  "Studio":"Studio A","Series":"Series A","Tags":"Drama,HD","Release":"2024-06-10",
                  "Runtime":"124","UserRating":"4.17","Poster":"https://img.example/poster.png",
                  "Fanart":"https://img.example/fanart.png"
                }}]}
                """);
            if (request.RequestUri!.Host == "img.example") {
                var color = request.RequestUri.AbsolutePath.Contains("fanart") ? SKColors.DarkSlateBlue
                    : request.RequestUri.AbsolutePath.Contains("actor") ? SKColors.HotPink
                    : SKColors.CornflowerBlue;
                return new(HttpStatusCode.OK) {
                    Content = new ByteArrayContent(CreatePng(80, 120, color)) { Headers = { ContentType = new("image/png") } }
                };
            }
            return new(HttpStatusCode.NotFound);
        }, withImporter: true, withImages: true);

        MetadataSyncResult result = await service.SyncMovieAsync(1, "mdc-ng", overwrite: false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.ImportTaskId);
        await using SqliteConnection verify = await Open();
        Assert.Equal("Imported title", await Text(verify, "SELECT Title FROM Movies WHERE Id=1"));
        Assert.Equal("Original imported title", await Text(verify, "SELECT OriginalTitle FROM Movies WHERE Id=1"));
        Assert.Equal("Imported plot", await Text(verify, "SELECT Description FROM Movies WHERE Id=1"));
        Assert.Equal("2024-06-10", await Text(verify, "SELECT ReleaseDate FROM Movies WHERE Id=1"));
        Assert.Equal(7440, await Scalar(verify, "SELECT DurationSeconds FROM Movies WHERE Id=1"));
        Assert.Equal("4.17", await Text(verify, "SELECT ProviderRating FROM Movies WHERE Id=1"));
        Assert.Equal(2, await Scalar(verify, "SELECT COUNT(*) FROM MovieActors WHERE MovieId=1"));
        Assert.Equal(2, await Scalar(verify, "SELECT COUNT(*) FROM MovieGenres WHERE MovieId=1"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM MovieDirectors WHERE MovieId=1"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM MovieStudios WHERE MovieId=1"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM MovieSeries WHERE MovieId=1"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM Images WHERE MovieId=1 AND ImageType='Poster' AND ValidationStatus='Valid'"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM Images WHERE MovieId=1 AND ImageType='Fanart' AND ValidationStatus='Valid'"));
        Assert.Equal(1, await Scalar(verify, "SELECT COUNT(*) FROM Images WHERE ActorId=(SELECT Id FROM Actors WHERE Name='Actor A') AND ImageType='ActorAvatar' AND ValidationStatus='Valid'"));

        MovieDetailDto? detail = await ProductReader.ReadMovieAsync(Database, "http://localhost", 1);
        Assert.NotNull(detail);
        Assert.Equal("Imported title", detail.Title);
        Assert.Equal("Imported plot", detail.Description);
        Assert.Equal(4.17, detail.ProviderRating);
        Assert.Contains(detail.Actors, actor => actor.Name == "Actor A");
        Assert.Contains(detail.Genres, genre => genre.Name == "Drama");
        var assets = new ImageAssetService(Database, Path.Combine(root, "MediaStorage"));
        Assert.NotNull(await assets.ResolveMovieAsync(1, "original", "poster"));
        Assert.NotNull(await assets.ResolveMovieAsync(1, "original", "fanart"));
    }

    [Fact]
    public async Task ProviderFailureReturnsStructuredError()
    {
        var service = CreateService(request => {
            if (request.Method == HttpMethod.Post) return new(HttpStatusCode.OK) { Content = new StringContent("") };
            if (request.RequestUri!.AbsolutePath == "/api/manual-jobs")
                return Json("""{"data":[{"id":12,"source_pathes":"[\"/media/NO-RESULT.mp4\"]","status":2,"total_count":0}]}""");
            if (request.RequestUri!.AbsolutePath == "/api/tasks_full")
                return Json("""{"data":[]}""");
            return new(HttpStatusCode.NotFound);
        });

        MetadataSyncResult result = await service.SyncAsync(new("NO-RESULT", "/media/NO-RESULT.mp4", "mdc-ng"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Metadata);
        Assert.Equal("PROVIDER_FAILED", result.ErrorCode);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task CancellationStopsSync()
    {
        var service = CreateService(_ => new(HttpStatusCode.OK) { Content = new StringContent("""{"data":[]}""") });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SyncAsync(new("SONE-104", "/media/SONE-104.mp4", "mdc-ng"), cancellation.Token));
    }

    [Fact]
    public void ScrapePreviewEndpointUsesMetadataSyncService()
    {
        string program = File.ReadAllText(FindRepoFile("backend/LocalMediaManager.Bridge/Program.cs"));
        int route = program.IndexOf("/api/settings/providers/mdc-ng/scrape-preview", StringComparison.Ordinal);
        Assert.True(route >= 0);
        string endpoint = program[route..Math.Min(program.Length, route + 320)];

        Assert.Contains("MetadataSyncService", endpoint);
        Assert.DoesNotContain("MdcNgProvider provider", endpoint);
        Assert.DoesNotContain("provider.ScrapeAsync", endpoint);
    }

    private MetadataSyncService CreateService(Func<HttpRequestMessage, HttpResponseMessage> handler, bool withImporter = false, bool withImages = false)
    {
        var settings = new MetadataProviderSettingsService(Database);
        MovieMetadataImporter? importer = withImporter
            ? new MovieMetadataImporter(Database, new MetadataWriteService(Database))
            : null;
        var factory = new FakeFactory(handler);
        MovieImageImporter? images = withImages
            ? new MovieImageImporter(Database, Path.Combine(root, "MediaStorage"), new MediaStoragePathResolver(Database, root), new ImageDownloadService(factory), factory)
            : null;
        return new(settings, new MdcNgProvider(factory), importer, images);
    }

    private static byte[] CreatePng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private async Task<SqliteConnection> Open()
    {
        var connection = new SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task Execute(SqliteConnection connection, string sql, params (string, object?)[] values)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> Scalar(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<string?> Text(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string FindRepoFile(string relative)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            string candidate = Path.Combine(current.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException(relative);
    }

    private sealed class FakeFactory(Func<HttpRequestMessage, HttpResponseMessage> handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(handler));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(handler(request));
        }
    }
}
