using System.Net;
using System.Text;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class WebMetadataProviderTests
{
    [Fact]
    public async Task DmmSearchChecksAllResultsAndSelectsExactCode()
    {
        string html = """
            <a href="/mono/dvd/-/detail/=/cid=wrong/"><span>ABC-999 Wrong</span></a>
            <a href="/mono/dvd/-/detail/=/cid=right/" title="Right title"><span>ABP-001 Right</span></a>
            """;
        var provider = new DmmProvider(Factory(_ => Ok(html)));
        IReadOnlyList<MetadataSearchResult> results = await provider.SearchAsync("abp001", Context(dmm: Dmm()), CancellationToken.None);
        Assert.Single(results);
        Assert.Equal("ABP-001", results[0].Code);
        Assert.Contains("cid=right", results[0].ExternalId);
    }

    [Fact]
    public async Task DmmDetailParsesFixtureAndRejectsMismatchedCode()
    {
        string html = """
            <meta property="og:title" content="日本語タイトル"><meta property="og:image" content="https://pics.test/original.jpg">
            <div>品番： ABP-001</div><div>発売日： 2026-01-02</div><div>収録時間： 120分</div>
            <a href="/actress/1">Actor A</a><a href="/genre/1">Genre A</a>
            """;
        var provider = new DmmProvider(Factory(_ => Ok(html)));
        ProviderMetadata? value = await provider.GetMetadataAsync(new("DMM", "https://www.dmm.co.jp/detail", "ABP-001", null), Context(dmm: Dmm()), CancellationToken.None);
        Assert.NotNull(value);
        Assert.Equal("日本語タイトル", value!.Title);
        Assert.Equal(7200, value.DurationSeconds);
        Assert.Single(value.Actors);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.GetMetadataAsync(
            new("DMM", "https://www.dmm.co.jp/detail", "XYZ-999", null), Context(dmm: Dmm()), CancellationToken.None));
    }

    [Fact]
    public async Task JavDbSupportsExactCodeAndKeywordResultFixtures()
    {
        string html = """
            <a class="box" href="/v/abc"><div class="uid">ABP-001</div><div class="video-title">Title A</div><img data-src="/covers/a.jpg"></a>
            <a class="box" href="/v/other"><div class="uid">ABC-999</div><div class="video-title">Other</div></a>
            """;
        var provider = new JavDbProvider(Factory(_ => Ok(html)));
        IReadOnlyList<MetadataSearchResult> exact = await provider.SearchAsync("ABP-001", Context(javDb: JavDb()), CancellationToken.None);
        Assert.Single(exact);
        IReadOnlyList<RemoteMetadataResult> actor = await provider.SearchKeywordAsync("Actor A", "actor", Context(javDb: JavDb()), CancellationToken.None);
        Assert.Equal(2, actor.Count);
        Assert.Equal("https://javdb.com/covers/a.jpg", actor[0].PosterUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "拒绝")]
    [InlineData((HttpStatusCode)429, "频繁")]
    public async Task ProviderConnectionClassifiesHttpFailures(HttpStatusCode status, string expected)
    {
        var provider = new JavDbProvider(Factory(_ => new(status)));
        ProviderConnectionResult result = await provider.TestConnectionAsync(Context(javDb: JavDb()), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains(expected, result.Message);
    }

    [Fact]
    public async Task VerificationPageIsNotParsedAsMetadata()
    {
        var provider = new DmmProvider(Factory(_ => Ok("<html>captcha age-check</html>")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SearchAsync("ABP-001", Context(dmm: Dmm()), CancellationToken.None));
    }

    private static WebMetadataSettingsDto Dmm() => new(true, 3, DmmProvider.DefaultBaseUrl, 30, 0, "", true, true);
    private static WebMetadataSettingsDto JavDb() => new(true, 4, JavDbProvider.DefaultBaseUrl, 30, 0, "", true, true);
    private static MetadataProviderContext Context(WebMetadataSettingsDto? dmm = null, WebMetadataSettingsDto? javDb = null) =>
        new(SettingsDefaults.Unified.MetaTube, SettingsDefaults.JavBus, null, dmm ?? SettingsDefaults.Dmm, javDb ?? SettingsDefaults.JavDb);
    private static FakeFactory Factory(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(handler);
    private static HttpResponseMessage Ok(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private sealed class FakeFactory(Func<HttpRequestMessage, HttpResponseMessage> handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new DelegateHandler(handler)) { BaseAddress = new Uri("https://example.test") };
    }
    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = handler(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
