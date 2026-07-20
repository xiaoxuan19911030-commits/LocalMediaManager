using System.Net;
using System.Text;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class ActorProfileProviderTests
{
    [Fact]
    public async Task MinnanoExactNameParsesBirthdayHeightCupAndAliases()
    {
        string search = """<a href="/actress/profile/123">架空 花子</a><a href="/actress/profile/456">同名 別人</a>""";
        string detail = """
            <meta property="og:image" content="https://img.test/a.jpg">
            <div>生年月日：1992-03-04</div><div>身長：165cm</div><div>カップ：C</div>
            <a href="/actress/alias">花子</a>
            """;
        var provider = new MinnanoActorProfileProvider(Factory(request => request.RequestUri!.AbsolutePath.Contains("search_result") ? Ok(search) : Ok(detail)));
        IReadOnlyList<ActorProfileCandidate> result = await provider.SearchAsync("架空 花子", [], SettingsDefaults.Minnano with { Enabled = true }, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("1992-03-04", result[0].Profile.BirthDate);
        Assert.Equal(165, result[0].Profile.HeightCm);
        Assert.Equal("C", result[0].Profile.Cup);
    }

    [Fact]
    public async Task MinnanoDoesNotMergeSameLookingDifferentName()
    {
        var provider = new MinnanoActorProfileProvider(Factory(_ => Ok("""<a href="/actress/profile/456">同名 別人</a>""")));
        Assert.Empty(await provider.SearchAsync("架空 花子", [], SettingsDefaults.Minnano with { Enabled = true }, CancellationToken.None));
    }

    [Fact]
    public async Task WikipediaExactPageExtractsStructuredFieldsAndShortDescription()
    {
        string json = """
            {"query":{"pages":[
              {"title":"架空花子","fullurl":"https://ja.wikipedia.org/wiki/test","extract":"架空花子（1992年3月4日 - ）は、日本の俳優。出身地は東京都。身長 165 cm。"},
              {"title":"架空花子 (曖昧さ回避)","fullurl":"https://ja.wikipedia.org/wiki/dis","extract":"同名の人物の曖昧さ回避ページ。"}
            ]}}
            """;
        var provider = new WikipediaJpActorProfileProvider(Factory(_ => Ok(json, "application/json")));
        IReadOnlyList<ActorProfileCandidate> result = await provider.SearchAsync("架空花子", [], SettingsDefaults.WikipediaJp with { Enabled = true }, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("1992-03-04", result[0].Profile.BirthDate);
        Assert.Equal(165, result[0].Profile.HeightCm);
        Assert.Contains("東京都", result[0].Profile.BirthPlace);
        Assert.True(result[0].Profile.Description!.Length < 500);
    }

    [Fact]
    public async Task WikipediaDisambiguationAndNonTargetPagesAreRejected()
    {
        string json = """{"query":{"pages":[{"title":"別人","fullurl":"https://ja.wikipedia.org/wiki/x","extract":"架空の人物。"},{"title":"架空花子","fullurl":"https://ja.wikipedia.org/wiki/y","extract":"同名の人物の曖昧さ回避ページ。"}]}}""";
        var provider = new WikipediaJpActorProfileProvider(Factory(_ => Ok(json, "application/json")));
        Assert.Empty(await provider.SearchAsync("架空花子", [], SettingsDefaults.WikipediaJp with { Enabled = true }, CancellationToken.None));
    }

    private static FakeFactory Factory(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(handler);
    private static HttpResponseMessage Ok(string value, string mediaType = "text/html") => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, mediaType) };
    private sealed class FakeFactory(Func<HttpRequestMessage, HttpResponseMessage> handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(handler));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = handler(request); response.RequestMessage = request; return Task.FromResult(response);
        }
    }
}
