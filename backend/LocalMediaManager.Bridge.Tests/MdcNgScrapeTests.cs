using System.Net;
using System.Text;
using System.Text.Json;
using LocalMediaManager.Bridge;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class MdcNgScrapeTests
{
    [Fact]
    public void AdapterMapsMdcJsonToUnifiedMovieMetadata()
    {
        using JsonDocument document = JsonDocument.Parse("""
        {
          "data": {
            "number": "abc_001",
            "title": "Localized Title",
            "original_title": "Original Title",
            "plot": "Plot text",
            "actors": [{"name":"Actor A"}, "Actor B"],
            "director": "Director A",
            "maker": "Studio A",
            "series": "Series A",
            "genres": ["Drama", "Feature"],
            "country": "JP",
            "release_date": "2026/01/02",
            "runtime": 120,
            "rating": 4.8,
            "poster": "https://img.example/poster.jpg?x=1",
            "thumb": "https://img.example/thumb.jpg",
            "cover": "https://img.example/fanart.jpg",
            "ActorPhotos": "https://img.example/actor-a.jpg,https://img.example/actor-b.jpg",
            "extra_fanart": ["https://img.example/1.jpg", "https://img.example/2.jpg"],
            "trailer": "https://video.example/trailer.mp4"
          }
        }
        """);

        MovieMetadata metadata = MdcNgAdapter.ToMovieMetadata(document.RootElement);

        Assert.Equal("MDC-NG", metadata.Provider);
        Assert.Equal("ABC-001", metadata.Code);
        Assert.Equal("Localized Title", metadata.Title);
        Assert.Equal("Original Title", metadata.OriginalTitle);
        Assert.Equal("Plot text", metadata.Description);
        Assert.Contains("Actor A", metadata.Actors);
        Assert.Contains("Actor B", metadata.Actors);
        Assert.Equal("Director A", metadata.Director);
        Assert.Equal("Studio A", metadata.Studio);
        Assert.Equal("Series A", metadata.Series);
        Assert.Contains("Drama", metadata.Tags);
        Assert.Equal("JP", metadata.Country);
        Assert.Equal("2026-01-02", metadata.ReleaseDate);
        Assert.Equal(7200, metadata.DurationSeconds);
        Assert.Equal(4.8m, metadata.Rating);
        Assert.Equal("https://img.example/poster.jpg", metadata.Poster);
        Assert.Equal("https://img.example/thumb.jpg", metadata.Thumb);
        Assert.Equal("https://img.example/fanart.jpg", metadata.Fanart);
        Assert.Equal(2, metadata.ExtraFanart.Count);
        Assert.Equal("https://img.example/actor-a.jpg", metadata.ActorImages[0].ImageUrl);
        Assert.Equal("https://video.example/trailer.mp4", metadata.Trailer);
    }

    [Fact]
    public async Task ProviderCreatesMdcJobPollsTaskAndReturnsUnifiedMetadataWithoutWriting()
    {
        var requests = new List<HttpRequestMessage>();
        int manualJobPolls = 0;
        int taskPolls = 0;
        var provider = new MdcNgProvider(new FakeFactory(request => {
            requests.Add(CloneRequest(request));
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/manual-jobs") {
                manualJobPolls++;
                return Json(manualJobPolls == 1
                    ? """{"data":[],"num_pages":0,"total_count":0}"""
                    : """{"data":[{"id":7,"source_pathes":"[\"Z:\\Movies\\ABP-001.mp4\"]","status":1}],"num_pages":1,"total_count":1}""");
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/manual-jobs")
                return new(HttpStatusCode.OK) { Content = new StringContent("") };
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/tasks_full") {
                taskPolls++;
                return taskPolls == 1
                    ? Json("""{"data":[],"num_pages":0,"total_count":0}""")
                    : Json("""
                      {
                        "data":[{
                          "id":9,
                          "manual_job_id":7,
                          "status":-1,
                          "stage":400,
                          "metadata":{"Number":"abp_001","Title":"Remote title","Actors":"Actor A","Runtime":"90","Poster":"https://img.example/p.jpg","UserRating":"4.7"}
                        }],
                        "num_pages":1,
                        "total_count":1
                      }
                      """);
            }
            return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }));

        MdcNgScrapeResult result = await provider.ScrapeAsync(
            new("Z:\\Movies\\ABP-001.mp4", "ABP-001", TimeoutSeconds: 10),
            new(true, "http://127.0.0.1:5800/", "http://127.0.0.1:9207/", 10, "", true),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("7", result.JobId);
        Assert.Equal("9", result.TaskId);
        Assert.NotNull(result.Metadata);
        Assert.Equal("ABP-001", result.Metadata.Code);
        Assert.Equal("Remote title", result.Metadata.Title);
        Assert.Equal(5400, result.Metadata.DurationSeconds);
        Assert.Equal(4.7m, result.Metadata.Rating);
        HttpRequestMessage post = Assert.Single(requests, request => request.Method == HttpMethod.Post);
        string body = await post.Content!.ReadAsStringAsync();
        Assert.Contains("\"pathes\"", body);
        Assert.Contains("ABP-001.mp4", body);
        Assert.Contains("\"link_mode\":3", body);
        Assert.True(manualJobPolls >= 2);
        Assert.True(taskPolls >= 2);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        if (request.Content is not null) {
            string body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            clone.Content = new StringContent(body, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType ?? "application/json");
        }
        return clone;
    }

    private sealed class FakeFactory(Func<HttpRequestMessage, HttpResponseMessage> handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(handler));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
