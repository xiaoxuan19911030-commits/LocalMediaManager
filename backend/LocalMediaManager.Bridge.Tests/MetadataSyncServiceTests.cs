using System.Net;
using System.Text;
using LocalMediaManager.Bridge;
using Microsoft.Data.Sqlite;
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
        foreach (string file in new[] { "0001_InitialSchema.sql", "0005_MetadataSyncWorkflow.sql" }) {
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "migrations", file));
            await command.ExecuteNonQueryAsync();
        }
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

    private MetadataSyncService CreateService(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var settings = new MetadataProviderSettingsService(Database);
        return new(settings, new MdcNgProvider(new FakeFactory(handler)));
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
