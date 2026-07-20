using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace LocalMediaManager.Bridge;

public sealed record RemoteMetadataResult(string Provider, string ExternalId, string Code, string? Title, string? PosterUrl,
    IReadOnlyList<string> Actors, IReadOnlyList<string> Tags, string SourceUrl, bool ExistsLocally, bool Favorite);

public interface IRemoteMetadataSearchProvider
{
    Task<IReadOnlyList<RemoteMetadataResult>> SearchKeywordAsync(string keyword, string kind, MetadataProviderContext context, CancellationToken cancellationToken);
}

public abstract class HtmlMetadataProvider(IHttpClientFactory clients) : IMetadataProvider
{
    public abstract string Name { get; }
    protected abstract WebMetadataSettingsDto Settings(MetadataProviderContext context);
    protected abstract Uri SearchUri(WebMetadataSettingsDto settings, string query);
    protected abstract IReadOnlyList<MetadataSearchResult> ParseSearch(string html, Uri responseUri, string query);
    protected abstract ProviderMetadata ParseDetail(string html, Uri responseUri, MetadataSearchResult result);

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        WebMetadataSettingsDto settings = Settings(context);
        string normalized = ProviderParsing.NormalizeCode(code);
        Exception? last = null;
        foreach (WebMetadataSettingsDto candidate in Candidates(settings)) {
            try {
                using HttpClient client = CreateClient(candidate, context.NetworkSettings);
                (string html, Uri uri) = await GetAsync(client, SearchUri(candidate, normalized), candidate, cancellationToken, true);
                MetadataSearchResult[] results = ParseSearch(html, uri, normalized)
                    .Where(item => ProviderParsing.Comparable(item.Code) == ProviderParsing.Comparable(normalized)).ToArray();
                if (results.Length > 0 || candidate == Candidates(settings).Last()) return results;
            } catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
                last = error;
            }
        }
        if (last is not null) throw last;
        return [];
    }

    public async Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        WebMetadataSettingsDto settings = Settings(context);
        using HttpClient client = CreateClient(settings, context.NetworkSettings);
        (string html, Uri uri) = await GetAsync(client, new Uri(result.ExternalId), settings, cancellationToken, false);
        ProviderMetadata metadata = ParseDetail(html, uri, result);
        if (ProviderParsing.Comparable(metadata.Code) != ProviderParsing.Comparable(result.Code))
            throw new InvalidDataException($"{Name} returned a different movie code.");
        return ProviderParsing.Useful(metadata) ? metadata : null;
    }

    public Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken) =>
        Task.FromResult(metadata.Images);

    public async Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext context, CancellationToken cancellationToken)
    {
        WebMetadataSettingsDto settings = Settings(context);
        var watch = Stopwatch.StartNew();
        try {
            Uri uri = new(settings.BaseUrl);
            using HttpClient client = CreateClient(settings, context.NetworkSettings);
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, Name, ProviderParsing.StatusMessage(Name, response.StatusCode), watch.ElapsedMilliseconds);
            string html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (ProviderParsing.LooksBlocked(html)) {
                string trace = await ProviderNetworkDiagnostics.ProbeAsync(Name, uri, settings.TimeoutSeconds,
                    request => ProviderNetworkDiagnostics.BrowserHeaders(request, settings.Cookie, settings.BaseUrl), CancellationToken.None, context.NetworkSettings);
                return new(false, Name, trace, watch.ElapsedMilliseconds);
            }
            string okTrace = await ProviderNetworkDiagnostics.ProbeAsync(Name, uri, settings.TimeoutSeconds,
                request => ProviderNetworkDiagnostics.BrowserHeaders(request, settings.Cookie, settings.BaseUrl), CancellationToken.None, context.NetworkSettings);
            return new(true, Name, okTrace, watch.ElapsedMilliseconds);
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException) {
            Uri uri = new(settings.BaseUrl);
            string trace = await ProviderNetworkDiagnostics.ProbeAsync(Name, uri, settings.TimeoutSeconds,
                request => ProviderNetworkDiagnostics.BrowserHeaders(request, settings.Cookie, settings.BaseUrl), CancellationToken.None, context.NetworkSettings);
            return new(false, Name, error is TaskCanceledException ? $"{Name} HTTP Timeout. {trace}" : $"{Name} 网络错误：{error.Message}. {trace}", watch.ElapsedMilliseconds);
        }
    }

    protected HttpClient CreateClient(WebMetadataSettingsDto settings, ProviderNetworkSettingsDto network)
    {
        HttpClient client = network.ProxyMode.Equals("System", StringComparison.OrdinalIgnoreCase)
            ? clients.CreateClient(Name)
            : ProviderHttpClients.Create(settings.TimeoutSeconds, network);
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9,en-US;q=0.7,en;q=0.5");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        if (!string.IsNullOrWhiteSpace(settings.Cookie)) client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", settings.Cookie);
        return client;
    }

    private static IReadOnlyList<WebMetadataSettingsDto> Candidates(WebMetadataSettingsDto settings) =>
        new[] { settings.BaseUrl }.Concat(settings.MirrorUrls ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(url => settings with { BaseUrl = url })
            .ToArray();

    protected static async Task<(string Html, Uri Uri)> GetAsync(HttpClient client, Uri uri, WebMetadataSettingsDto settings,
        CancellationToken cancellationToken, bool notFoundAsEmpty)
    {
        Exception? last = null;
        for (int attempt = 0; attempt <= settings.RetryCount; attempt++) {
            try {
                using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound && notFoundAsEmpty) return ("", response.RequestMessage?.RequestUri ?? uri);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ProviderParsing.StatusMessage(uri.Host, response.StatusCode));
                string html = await response.Content.ReadAsStringAsync(cancellationToken);
                if (ProviderParsing.LooksBlocked(html)) throw new InvalidOperationException($"{uri.Host} returned a login, age-check, or verification page.");
                return (html, response.RequestMessage?.RequestUri ?? uri);
            } catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested) {
                last = error;
                if (attempt < settings.RetryCount) await Task.Delay(500 * (attempt + 1), cancellationToken);
            }
        }
        string trace = await ProviderNetworkDiagnostics.ProbeAsync(uri.Host, uri, settings.TimeoutSeconds,
            request => ProviderNetworkDiagnostics.BrowserHeaders(request, settings.Cookie, uri.GetLeftPart(UriPartial.Authority) + "/"), CancellationToken.None);
        throw new ProviderNetworkException(uri.Host, uri, last is TaskCanceledException ? $"{uri.Host} request timed out. {trace}" : $"{last?.Message}. {trace}", last);
    }
}

public sealed class DmmProvider(IHttpClientFactory clients) : HtmlMetadataProvider(clients)
{
    public const string DefaultBaseUrl = "https://www.dmm.co.jp/";
    public override string Name => "DMM";
    protected override WebMetadataSettingsDto Settings(MetadataProviderContext context) => context.Dmm ?? SettingsDefaults.Dmm;
    protected override Uri SearchUri(WebMetadataSettingsDto settings, string query) =>
        new(new Uri(settings.BaseUrl), $"search/=/searchstr={Uri.EscapeDataString(query)}/");
    protected override IReadOnlyList<MetadataSearchResult> ParseSearch(string html, Uri responseUri, string query) => DmmParser.Search(html, responseUri, query);
    protected override ProviderMetadata ParseDetail(string html, Uri responseUri, MetadataSearchResult result) => DmmParser.Detail(html, responseUri, result);
}

public sealed class JavDbProvider(IHttpClientFactory clients) : HtmlMetadataProvider(clients), IRemoteMetadataSearchProvider
{
    public const string DefaultBaseUrl = "https://javdb.com/";
    public override string Name => "JavDB";
    protected override WebMetadataSettingsDto Settings(MetadataProviderContext context) => context.JavDb ?? SettingsDefaults.JavDb;
    protected override Uri SearchUri(WebMetadataSettingsDto settings, string query) =>
        new(new Uri(settings.BaseUrl), $"search?q={Uri.EscapeDataString(query)}&f=all");
    protected override IReadOnlyList<MetadataSearchResult> ParseSearch(string html, Uri responseUri, string query) => JavDbParser.Search(html, responseUri, query);
    protected override ProviderMetadata ParseDetail(string html, Uri responseUri, MetadataSearchResult result) => JavDbParser.Detail(html, responseUri, result);

    public async Task<IReadOnlyList<RemoteMetadataResult>> SearchKeywordAsync(string keyword, string kind, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        WebMetadataSettingsDto settings = Settings(context);
        using HttpClient client = CreateClient(settings, context.NetworkSettings);
        string prefix = kind.ToLowerInvariant() switch { "actor" => "actors", "tag" => "tags", _ => "search" };
        Uri uri = prefix == "search" ? SearchUri(settings, keyword) : new(new Uri(settings.BaseUrl), $"{prefix}?q={Uri.EscapeDataString(keyword)}");
        (string html, Uri response) = await GetAsync(client, uri, settings, cancellationToken, true);
        return JavDbParser.Remote(html, response);
    }
}

internal static class DmmParser
{
    private static readonly Regex Product = new(@"href=[""'](?<url>[^""']*(?:detail|mono/dvd)[^""']*)[""'][^>]*>(?<text>[\s\S]*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static IReadOnlyList<MetadataSearchResult> Search(string html, Uri baseUri, string query) =>
        Product.Matches(html).Select(match => {
            string text = ProviderParsing.Text(match.Groups["text"].Value);
            string code = ProviderParsing.FindCode(text) ?? query;
            return new MetadataSearchResult("DMM", ProviderParsing.Absolute(match.Groups["url"].Value, baseUri)!, code, ProviderParsing.AttributeOrText(match.Value, "title"));
        }).Where(item => Uri.IsWellFormedUriString(item.ExternalId, UriKind.Absolute)).DistinctBy(item => item.ExternalId).ToArray();

    public static ProviderMetadata Detail(string html, Uri uri, MetadataSearchResult result)
    {
        string code = ProviderParsing.Labeled(html, "品番", "商品番号") ?? ProviderParsing.FindCode(html) ?? result.Code;
        string? title = ProviderParsing.Meta(html, "og:title") ?? ProviderParsing.Heading(html);
        string? image = ProviderParsing.Meta(html, "og:image");
        return new("DMM", uri.ToString(), ProviderParsing.NormalizeCode(code), title, ProviderParsing.Meta(html, "description"),
            ProviderParsing.Labeled(html, "監督"), ProviderParsing.Labeled(html, "メーカー"), ProviderParsing.Labeled(html, "レーベル"),
            ProviderParsing.Labeled(html, "シリーズ"), ProviderParsing.Minutes(ProviderParsing.Labeled(html, "収録時間", "再生時間")),
            ProviderParsing.Date(ProviderParsing.Labeled(html, "発売日", "配信開始日")), uri.ToString(),
            ProviderParsing.Links(html, "genre", "ジャンル"), ProviderParsing.Links(html, "actress", "出演者", "actor"),
            string.IsNullOrWhiteSpace(image) ? [] : [new("Poster", image)]);
    }
}

internal static class JavDbParser
{
    private static readonly Regex Item = new(@"<a[^>]*class=[""'][^""']*box[^""']*[""'][^>]*href=[""'](?<url>[^""']+)[""'][^>]*>(?<body>[\s\S]*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static IReadOnlyList<MetadataSearchResult> Search(string html, Uri baseUri, string query) =>
        Item.Matches(html).Select(match => {
            string body = match.Groups["body"].Value;
            string code = ProviderParsing.ClassText(body, "uid") ?? ProviderParsing.FindCode(body) ?? query;
            return new MetadataSearchResult("JavDB", ProviderParsing.Absolute(match.Groups["url"].Value, baseUri)!, ProviderParsing.NormalizeCode(code), ProviderParsing.ClassText(body, "video-title"));
        }).Where(item => Uri.IsWellFormedUriString(item.ExternalId, UriKind.Absolute)).DistinctBy(item => item.ExternalId).ToArray();

    public static ProviderMetadata Detail(string html, Uri uri, MetadataSearchResult result)
    {
        string code = ProviderParsing.Labeled(html, "番號", "番号", "識別碼") ?? ProviderParsing.FindCode(html) ?? result.Code;
        string? image = ProviderParsing.Meta(html, "og:image");
        return new("JavDB", uri.ToString(), ProviderParsing.NormalizeCode(code), ProviderParsing.Meta(html, "og:title") ?? ProviderParsing.Heading(html),
            ProviderParsing.Meta(html, "description"), ProviderParsing.Labeled(html, "導演", "監督"),
            ProviderParsing.Labeled(html, "片商", "メーカー"), ProviderParsing.Labeled(html, "發行", "レーベル"),
            ProviderParsing.Labeled(html, "系列", "シリーズ"), ProviderParsing.Minutes(ProviderParsing.Labeled(html, "時長", "収録時間")),
            ProviderParsing.Date(ProviderParsing.Labeled(html, "日期", "発売日")), uri.ToString(),
            ProviderParsing.Links(html, "tags", "genre"), ProviderParsing.Links(html, "actors", "actor"),
            string.IsNullOrWhiteSpace(image) ? [] : [new("Poster", image)]);
    }

    public static IReadOnlyList<RemoteMetadataResult> Remote(string html, Uri baseUri) =>
        Item.Matches(html).Select(match => {
            string body = match.Groups["body"].Value;
            string code = ProviderParsing.ClassText(body, "uid") ?? ProviderParsing.FindCode(body) ?? "";
            string? poster = Regex.Match(body, @"(?:data-src|src)=[""']([^""']+)").Groups[1].Value;
            string url = ProviderParsing.Absolute(match.Groups["url"].Value, baseUri) ?? "";
            return new RemoteMetadataResult("JavDB", url, code, ProviderParsing.ClassText(body, "video-title"),
                ProviderParsing.Absolute(poster, baseUri), [], [], url, false, false);
        }).Where(item => item.SourceUrl.Length > 0).Take(50).ToArray();
}

internal static class ProviderParsing
{
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Code = new(@"(?<![A-Z0-9])([A-Z]{2,10})[-_\s]?(\d{2,7})(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static string NormalizeCode(string value) { string decoded = WebUtility.HtmlDecode(value) ?? ""; Match match = Code.Match(decoded.ToUpperInvariant()); return match.Success ? $"{match.Groups[1].Value}-{match.Groups[2].Value}" : decoded.Trim().ToUpperInvariant().Replace('_', '-'); }
    public static string Comparable(string value) => NormalizeCode(value).Replace("-", "").Replace(" ", "");
    public static string? FindCode(string value) => Code.Match(Text(value)) is { Success: true } match ? NormalizeCode(match.Value) : null;
    public static string Text(string value) => Regex.Replace(WebUtility.HtmlDecode(Tags.Replace(value ?? "", " ")), @"\s+", " ").Trim();
    public static string? Meta(string html, string key) => Match(html, $@"<meta[^>]*(?:property|name)=[""']{Regex.Escape(key)}[""'][^>]*content=[""']([^""']+)[""']");
    public static string? Heading(string html) => Text(Match(html, @"<h1[^>]*>([\s\S]*?)</h1>") ?? Match(html, @"<title[^>]*>([\s\S]*?)</title>") ?? "") is { Length: > 0 } value ? value : null;
    public static string? Labeled(string html, params string[] labels)
    {
        foreach (string label in labels) {
            string? value = Match(html, $@"(?:<[^>]+>\s*)?{Regex.Escape(label)}\s*[:：]?\s*(?:</[^>]+>\s*)*(?:<[^>]+>)*([^<\r\n]+)");
            value = Text(value ?? "");
            if (value.Length > 0) return value;
        }
        return null;
    }
    public static IReadOnlyList<string> Links(string html, params string[] fragments) =>
        Regex.Matches(html, @"<a[^>]*href=[""']([^""']+)[""'][^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase)
            .Where(match => fragments.Any(fragment => match.Groups[1].Value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .Select(match => Text(match.Groups[2].Value)).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static string? ClassText(string html, string className) => Text(Match(html, $@"class=[""'][^""']*{Regex.Escape(className)}[^""']*[""'][^>]*>([\s\S]*?)</") ?? "") is { Length: > 0 } value ? value : null;
    public static string? AttributeOrText(string html, string attribute) => Match(html, $@"{attribute}=[""']([^""']+)[""']") is { Length: > 0 } value ? Text(value) : Text(html);
    public static string? Absolute(string? value, Uri baseUri) => string.IsNullOrWhiteSpace(value) ? null : Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri.ToString() : new Uri(baseUri, WebUtility.HtmlDecode(value)).ToString();
    public static int? Minutes(string? value) => Regex.Match(value ?? "", @"\d+") is { Success: true } match && int.TryParse(match.Value, out int minutes) ? minutes * 60 : null;
    public static string? Date(string? value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) && date.Year > 1900 ? date.ToString("yyyy-MM-dd") : null;
    public static bool Useful(ProviderMetadata value) => !string.IsNullOrWhiteSpace(value.Title) || value.Actors.Count > 0 || value.Genres.Count > 0 || value.Images.Count > 0;
    public static bool LooksBlocked(string html) => Regex.IsMatch(html, "captcha|cf-chl|cloudflare|turnstile|年齢認証|age.?check|sign.?in|login|not-available-in-your-region|This content is not available in your region|region", RegexOptions.IgnoreCase);
    public static string StatusMessage(string provider, HttpStatusCode status) => status switch {
        HttpStatusCode.Forbidden => $"{provider} 请求被拒绝，请检查 Cookie、代理或地区网络。",
        (HttpStatusCode)429 => $"{provider} 请求过于频繁，请稍后重试。",
        HttpStatusCode.NotFound => $"{provider} 未找到结果。",
        _ => $"{provider} 请求失败：HTTP {(int)status}。",
    };
    private static string? Match(string input, string pattern) => Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline) is { Success: true } match ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : null;
}
