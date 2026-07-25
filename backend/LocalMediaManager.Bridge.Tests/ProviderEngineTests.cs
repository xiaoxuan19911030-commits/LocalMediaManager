using System.Net;
using System.Text.Json;
using LocalMediaManager.Bridge;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class ProviderEngineTests
{
    [Fact]
    public void CatalogDeclaresStableOrderCapabilitiesAndRequirements()
    {
        Assert.Equal(["MDC-NG", "MetaTube", "JavBus", "Mock"], ProviderCatalog.All.Select(value => value.Name));
        Assert.True(ProviderCatalog.Get("MDC-NG").RequiresPath);
        Assert.True(ProviderCatalog.Get("MDC-NG").RequiresMedia);
        Assert.False(ProviderCatalog.Get("MetaTube").RequiresPath);
        Assert.True(ProviderCatalog.Supports("JavBus", "Actors"));
        Assert.True(ProviderCatalog.Supports("MetaTube", "Tags"));
        Assert.Contains("SearchByCode", ProviderCatalog.Get("Mock").Capabilities);
    }

    [Fact]
    public void ManagerResolvesProvidersAndHonorsEnabledOrder()
    {
        var mdc = new FakeProvider("MDC-NG");
        var meta = new FakeProvider("MetaTube");
        var bus = new FakeProvider("JavBus");
        var manager = new ProviderManager([bus, meta, mdc]);

        MetadataProviderContext context = Context() with { MdcNg = SettingsDefaults.MdcNg with { Enabled = false } };
        Assert.Same(meta, manager.ResolveMetadataProvider("metatube"));
        Assert.Equal(["MetaTube", "JavBus"], manager.OrderedMetadataProviders(context).Select(value => value.Name));
        Assert.Equal(["JavBus"], manager.OrderedMetadataProviders(context with { PreferredSource = "JavBus" }).Select(value => value.Name));
    }

    [Fact]
    public async Task SdkProviderCanRegisterWithoutChangingSynchronizationDispatch()
    {
        var sdk = new DirectSdkProvider();
        var manager = new ProviderManager(new IProviderSdk[] { sdk });

        Assert.Same(sdk, manager.Resolve("Mock"));
        ProviderPlaygroundResult result = await manager.ExecuteAsync("Mock", "ABW-001", Context(), true, CancellationToken.None);
        Assert.Equal("SDK ABW-001", result.ProviderResult?.Title);
    }

    [Fact]
    public async Task CacheKeyUsesProviderAndNormalizedCode()
    {
        var provider = new FakeProvider("JavBus");
        var manager = new ProviderManager([provider]);

        ProviderPlaygroundResult first = await manager.ExecuteAsync("JavBus", "sone-001", Context(), false, CancellationToken.None);
        ProviderPlaygroundResult second = await manager.ExecuteAsync("JavBus", "SONE001", Context(), false, CancellationToken.None);

        Assert.False(first.Cache.Hit);
        Assert.True(second.Cache.Hit);
        Assert.Equal(1, provider.SearchCalls);
        Assert.Equal("JavBus:SONE-001", second.Cache.Key);
    }

    [Fact]
    public async Task CacheExpiresAndCanBeCleared()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-25T00:00:00Z");
        var provider = new FakeProvider("MetaTube");
        var manager = new ProviderManager([provider], cacheTtl: TimeSpan.FromMinutes(1), clock: () => now);

        await manager.ExecuteAsync("MetaTube", "ABW-001", Context(), false, CancellationToken.None);
        now = now.AddMinutes(2);
        await manager.ExecuteAsync("MetaTube", "ABW-001", Context(), false, CancellationToken.None);
        Assert.Equal(2, provider.SearchCalls);
        Assert.Equal(1, manager.ClearCache());
    }

    [Fact]
    public async Task MockProviderIsOfflineDeterministicAndPlaygroundNeverWrites()
    {
        var manager = new ProviderManager([new MockMetadataProvider()]);
        ProviderPlaygroundResult result = await manager.ExecuteAsync("Mock", "IPX-123", Context(), true, CancellationToken.None);

        Assert.Equal("Mock IPX-123", result.ProviderResult?.Title);
        Assert.Contains(result.Diagnostics, value => value.Stage == "Writer" && value.Status == "Skipped");
        Assert.Contains(result.RawResponses, value => value.Format == "json" && value.Content.Contains("offline"));
        Assert.True(result.ParserSucceeded);
    }

    [Fact]
    public async Task RawCaptureRedactsSecrets()
    {
        var provider = new FakeProvider("JavBus", raw: "Cookie: secret-value Authorization=BearerToken password=hunter2 {\"token\":\"json-secret\"}");
        var manager = new ProviderManager([provider]);
        ProviderPlaygroundResult result = await manager.ExecuteAsync("JavBus", "IPX-123", Context(), true, CancellationToken.None);
        string raw = Assert.Single(result.RawResponses).Content;

        Assert.DoesNotContain("secret-value", raw);
        Assert.DoesNotContain("BearerToken", raw);
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("json-secret", raw);
        Assert.Contains("<redacted>", raw);
    }

    [Theory]
    [InlineData("HTTP 404 Not Found", ProviderFailureCategory.NotFound)]
    [InlineData("HTTP 429 Too Many Requests", ProviderFailureCategory.RateLimited)]
    [InlineData("HTTP 403 Forbidden", ProviderFailureCategory.Authentication)]
    [InlineData("Cloudflare challenge", ProviderFailureCategory.Cloudflare)]
    [InlineData("connection refused", ProviderFailureCategory.Network)]
    [InlineData("path mapping is not configured", ProviderFailureCategory.Configuration)]
    public void FailureClassifierSeparatesOperationalCauses(string message, ProviderFailureCategory expected)
    {
        Assert.Equal(expected, ProviderFailureClassifier.Classify(new HttpRequestException(message)));
    }

    [Fact]
    public void FailureClassifierSeparatesTimeoutParserAndUnsupported()
    {
        Assert.Equal(ProviderFailureCategory.Timeout, ProviderFailureClassifier.Classify(new TimeoutException()));
        Assert.Equal(ProviderFailureCategory.Parser, ProviderFailureClassifier.Classify(new JsonException()));
        Assert.Equal(ProviderFailureCategory.Unsupported, ProviderFailureClassifier.Classify(new NotSupportedException()));
    }

    [Fact]
    public async Task BenchmarkAggregatesSuccessAndFailure()
    {
        var success = new FakeProvider("MetaTube");
        var manager = new ProviderManager([success]);
        await manager.ExecuteAsync("MetaTube", "ABW-001", Context(), true, CancellationToken.None);
        success.Error = new HttpRequestException("HTTP 429 Too Many Requests");
        await manager.ExecuteAsync("MetaTube", "ABW-002", Context(), true, CancellationToken.None);

        ProviderBenchmarkSnapshot benchmark = Assert.Single(manager.Dashboard(Context())).Benchmark;
        Assert.Equal(2, benchmark.RequestCount);
        Assert.Equal(1, benchmark.SuccessCount);
        Assert.Equal(1, benchmark.FailureCount);
        Assert.Equal(50, benchmark.SuccessRate);
        Assert.Equal(ProviderFailureCategory.RateLimited, benchmark.LastFailureCategory);
    }

    [Theory]
    [MemberData(nameof(ParserCases))]
    public void JavBusParserRegressionHasRequiredCoverage(string code)
    {
        string html = $"""
            <html><body>
              <h3>{code} Parser Fixture</h3>
              <a class="bigImage" href="/images/{code}.jpg">cover</a>
              <a href="/genre/fixture">Fixture Genre</a>
              <a href="/star/fixture"><img src="/actors/a.jpg"/>Fixture Actor</a>
              <a class="sample-box" href="/samples/{code}-01.jpg">sample</a>
            </body></html>
            """;
        ProviderMetadata result = JavBusParser.Parse(html, "https://example.test/", code);

        Assert.Equal(code, result.Code);
        Assert.Contains("Parser Fixture", result.Title);
        Assert.Contains("Fixture Actor", result.Actors);
        Assert.Contains("Fixture Genre", result.Genres);
        Assert.Contains(result.Images, value => value.Type == "Poster");
        Assert.Contains(result.Images, value => value.Type == "Preview");
    }

    public static IEnumerable<object[]> ParserCases() => Enumerable.Range(1, 50)
        .Select(index => new object[] { $"TST-{index:000}" });

    private static MetadataProviderContext Context() => new(
        SettingsDefaults.Unified.MetaTube with { Enabled = true },
        SettingsDefaults.Unified.JavBus with { Enabled = true }) {
        MdcNg = SettingsDefaults.MdcNg with { Enabled = true },
    };

    private sealed class FakeProvider(string name, string? raw = null) : IMetadataProvider
    {
        public string Name => name;
        public int SearchCalls { get; private set; }
        public Exception? Error { get; set; }

        public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext context, CancellationToken cancellationToken)
        {
            SearchCalls++;
            if (Error is not null) throw Error;
            if (context.ResponseCapture is not null && raw is not null)
                await context.ResponseCapture(Name, "html", raw, cancellationToken);
            return [new(Name, code, code, code)];
        }

        public Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext context, CancellationToken cancellationToken) =>
            Task.FromResult<ProviderMetadata?>(new(Name, result.ExternalId, result.Code, $"Title {result.Code}", "Plot", "Director",
                "Studio", null, "Series", 6000, "2026-01-01", "https://example.test", ["Genre"], ["Actor"],
                [new("Poster", "https://example.test/poster.jpg", Name)]));

        public Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken) => Task.FromResult(metadata.Images);
        public Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionResult(true, Name, "ok", 1));
    }

    private sealed class DirectSdkProvider : IProviderSdk
    {
        public ProviderDescriptor Descriptor => ProviderCatalog.Get("Mock");
        public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetadataSearchResult>>([new("Mock", code, code, code)]);
        public Task<ProviderMetadata?> GetDetailAsync(MetadataSearchResult result, MetadataProviderContext context, CancellationToken cancellationToken) =>
            Task.FromResult<ProviderMetadata?>(new("Mock", result.ExternalId, result.Code, $"SDK {result.Code}", null, null, null, null, null,
                null, null, null, [], [], []));
        public Task<ProviderConnectionResult> HealthAsync(MetadataProviderContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderConnectionResult(true, "Mock", "ok", 0));
    }
}
