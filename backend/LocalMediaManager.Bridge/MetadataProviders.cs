using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace LocalMediaManager.Bridge;

public sealed record MetadataSearchResult(string Provider, string ExternalId, string Code, string? Title);
public sealed record MetadataImage(string Type, string Url);
public sealed record ProviderMetadata(
    string Provider, string ExternalId, string Code, string? Title, string? Description,
    string? Director, string? Studio, string? Publisher, string? Series, int? DurationSeconds,
    string? ReleaseDate, string? WebUrl, IReadOnlyList<string> Genres, IReadOnlyList<string> Actors,
    IReadOnlyList<MetadataImage> Images);

public interface IMetadataProvider
{
    string Name { get; }
    Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext settings, CancellationToken cancellationToken);
    Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext settings, CancellationToken cancellationToken);
    Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken);
    Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext settings, CancellationToken cancellationToken);
}

public sealed class MetaTubeProvider(IHttpClientFactory clients) : IMetadataProvider
{
    private static readonly string[] ProviderPreference = ["FANZA", "MGS", "JavBus", "JAV321", "AVBASE"];
    public string Name => "MetaTube";

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        MetaTubeSettingsDto settings = context.MetaTube;
        using HttpClient client = CreateClient(settings);
        using JsonDocument document = await GetJsonAsync(client,
            new Uri(new Uri(settings.BaseUrl), $"v1/movies/search?q={Uri.EscapeDataString(NormalizeCode(code))}&fallback=True"), cancellationToken, notFoundAsEmpty: true);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            return [];
        var results = new List<MetadataSearchResult>();
        foreach (JsonElement item in data.EnumerateArray()) {
            string provider = String(item, "provider");
            string id = String(item, "id");
            string number = String(item, "number");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(provider)) continue;
            results.Add(new(provider, id, NormalizeCode(string.IsNullOrWhiteSpace(number) ? code : number), NullableString(item, "title")));
        }
        string target = Comparable(code);
        return results
            .OrderByDescending(result => Comparable(result.Code) == target)
            .ThenBy(result => Array.FindIndex(ProviderPreference, provider => provider.Equals(result.Provider, StringComparison.OrdinalIgnoreCase)) is int index && index >= 0 ? index : int.MaxValue)
            .ToArray();
    }

    public async Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        MetaTubeSettingsDto settings = context.MetaTube;
        using HttpClient client = CreateClient(settings);
        Uri detailUrl = new(new Uri(settings.BaseUrl), $"v1/movies/{Uri.EscapeDataString(result.Provider)}/{Uri.EscapeDataString(result.ExternalId)}?lazy=True");
        using JsonDocument document = await GetJsonAsync(client, detailUrl, cancellationToken);
        JsonElement root = document.RootElement;
        JsonElement movie = root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object ? data : root;
        string code = NormalizeCode(String(movie, "number"));
        if (string.IsNullOrWhiteSpace(code)) code = result.Code;
        string? releaseDate = NormalizeDate(NullableString(movie, "release_date"));
        int? runtimeMinutes = NullableInt(movie, "runtime");
        string? primary = null;
        if (!string.IsNullOrWhiteSpace(result.Provider) && !string.IsNullOrWhiteSpace(result.ExternalId))
            primary = new Uri(new Uri(settings.BaseUrl), $"v1/images/primary/{Uri.EscapeDataString(result.Provider)}/{Uri.EscapeDataString(result.ExternalId)}").ToString();
        primary ??= FirstUrl(movie, "big_cover_url", "cover_url", "big_thumb_url", "thumb_url");
        var images = new List<MetadataImage>();
        if (!string.IsNullOrWhiteSpace(primary)) images.Add(new("Poster", StripQuery(primary)));
        string? fanart = FirstUrl(movie, "backdrop_url", "fanart_url", "background_url", "landscape_url");
        if (!string.IsNullOrWhiteSpace(fanart)) images.Add(new("Fanart", StripQuery(fanart)));
        if (movie.TryGetProperty("preview_images", out JsonElement previews) && previews.ValueKind == JsonValueKind.Array)
            images.AddRange(previews.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()).Where(IsHttpUrl).Select(value => new MetadataImage("Preview", StripQuery(value!))).Take(30));
        var metadata = new ProviderMetadata(
            result.Provider, result.ExternalId, code, NullableString(movie, "title"), NullableString(movie, "summary"),
            NullableString(movie, "director"), NullableString(movie, "maker"), NullableString(movie, "label"),
            NullableString(movie, "series"), runtimeMinutes is > 0 ? runtimeMinutes * 60 : null, releaseDate,
            NullableString(movie, "homepage") ?? detailUrl.ToString(), Strings(movie, "genres"), Strings(movie, "actors"),
            images.DistinctBy(image => image.Url, StringComparer.OrdinalIgnoreCase).ToArray());
        return HasUsefulMetadata(metadata) ? metadata : null;
    }

    public Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken) =>
        Task.FromResult(metadata.Images);

    public async Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext context, CancellationToken cancellationToken)
    {
        MetaTubeSettingsDto settings = context.MetaTube;
        var watch = Stopwatch.StartNew();
        try {
            using HttpClient client = CreateClient(settings);
            using HttpResponseMessage response = await client.GetAsync(new Uri(new Uri(settings.BaseUrl), "v1/movies/search?q=ABP-001&fallback=False"), cancellationToken);
            watch.Stop();
            return response.IsSuccessStatusCode
                ? new(true, Name, $"连接成功（HTTP {(int)response.StatusCode}）。", watch.ElapsedMilliseconds)
                : new(false, Name, $"服务器返回 HTTP {(int)response.StatusCode}。", watch.ElapsedMilliseconds);
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException) {
            watch.Stop();
            return new(false, Name, error is TaskCanceledException ? "连接超时。" : error.Message, watch.ElapsedMilliseconds);
        }
    }

    private HttpClient CreateClient(MetaTubeSettingsDto settings)
    {
        HttpClient client = clients.CreateClient("MetaTube");
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LocalMediaManager", "0.6.2"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, Uri uri, CancellationToken cancellationToken,
        bool notFoundAsEmpty = false)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 3; attempt++) {
            try {
                using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (notFoundAsEmpty && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return JsonDocument.Parse("{\"data\":[]}");
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"MetaTube 请求失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                try { return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken); }
                catch (JsonException error) { throw new InvalidDataException("MetaTube 返回了无法解析的 JSON。", error); }
            } catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested) {
                lastError = error;
                if (attempt < 2) await Task.Delay(attempt == 0 ? 500 : 1700, cancellationToken);
            }
        }
        throw new HttpRequestException($"MetaTube 连续请求 3 次均失败。最后错误：{lastError?.Message ?? "未知错误"}", lastError);
    }

    private static bool HasUsefulMetadata(ProviderMetadata value) =>
        !string.IsNullOrWhiteSpace(value.Title) || !string.IsNullOrWhiteSpace(value.Description) || value.Images.Count > 0;
    private static string NormalizeCode(string value) => string.Join(' ', (value ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant().Replace('_', '-');
    private static string Comparable(string value) => NormalizeCode(value).Replace("-", "").Replace(" ", "");
    private static string String(JsonElement source, string name) => NullableString(source, name) ?? "";
    private static string? NullableString(JsonElement source, string name) =>
        source.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!.Trim() : null;
    private static int? NullableInt(JsonElement source, string name) {
        if (!source.TryGetProperty(name, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : null;
    }
    private static IReadOnlyList<string> Strings(JsonElement source, string name) {
        if (!source.TryGetProperty(name, out JsonElement values) || values.ValueKind != JsonValueKind.Array) return [];
        return values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()?.Trim()).Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static string? FirstUrl(JsonElement source, params string[] names) => names.Select(name => NullableString(source, name)).FirstOrDefault(IsHttpUrl);
    private static bool IsHttpUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https";
    private static string StripQuery(string value) { int index = value.IndexOfAny(['?', '#']); return index >= 0 ? value[..index] : value; }
    private static string? NormalizeDate(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime date) && date.Year > 1900
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }
}

public sealed class CompositeMetadataProvider(MetaTubeProvider metaTube, JavBusProvider javBus) : IMetadataProvider
{
    public string Name => "Metadata";

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        var providers = Ordered(settings).ToArray();
        var results = new List<MetadataSearchResult>();
        foreach (IMetadataProvider provider in providers) {
            IReadOnlyList<MetadataSearchResult> found = await provider.SearchAsync(code, settings, cancellationToken);
            results.AddRange(found);
            if (found.Count > 0 && !string.IsNullOrWhiteSpace(settings.PreferredSource)) break;
        }
        return results;
    }

    public async Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        IMetadataProvider provider = result.Provider.Equals("JavBus", StringComparison.OrdinalIgnoreCase) ? javBus : metaTube;
        return await provider.GetMetadataAsync(result, settings, cancellationToken);
    }

    public Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken) =>
        metadata.Provider.Equals("JavBus", StringComparison.OrdinalIgnoreCase) ? javBus.GetImagesAsync(metadata, cancellationToken) : metaTube.GetImagesAsync(metadata, cancellationToken);

    public Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext settings, CancellationToken cancellationToken) =>
        metaTube.TestConnectionAsync(settings, cancellationToken);

    private IEnumerable<IMetadataProvider> Ordered(MetadataProviderContext settings)
    {
        if (settings.PreferredSource?.Equals("JavBus", StringComparison.OrdinalIgnoreCase) == true) {
            yield return javBus;
            yield break;
        }
        if (settings.PreferredSource?.Equals("MetaTube", StringComparison.OrdinalIgnoreCase) == true) {
            yield return metaTube;
            yield break;
        }
        var items = new List<(int Priority, IMetadataProvider Provider)> { (1, metaTube) };
        if (settings.JavBus.Enabled) items.Add((settings.JavBus.Priority, javBus));
        foreach ((_, IMetadataProvider provider) in items.OrderBy(item => item.Priority)) yield return provider;
    }
}

public sealed class JavBusProvider(IHttpClientFactory clients) : IMetadataProvider
{
    public const string DefaultBaseUrl = "https://www.javbus.com/";
    public string Name => "JavBus";

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        string normalized = JavBusCode.Normalize(code);
        if (string.IsNullOrWhiteSpace(normalized) || JavBusCode.IsUnsupported(normalized)) return [];
        JavBusSettingsDto settings = context.JavBus;
        string urlCode = Uri.EscapeDataString(normalized);
        using HttpClient client = CreateClient(settings);
        string html = await GetHtmlAsync(client, new Uri(new Uri(settings.BaseUrl), urlCode), settings, cancellationToken, notFoundAsEmpty: true);
        if (string.IsNullOrWhiteSpace(html)) return [];
        string parsedCode = JavBusParser.Code(html) ?? normalized;
        return Comparable(parsedCode) == Comparable(normalized)
            ? [new("JavBus", normalized, parsedCode, JavBusParser.Title(html))]
            : [];
    }

    public async Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        JavBusSettingsDto settings = context.JavBus;
        using HttpClient client = CreateClient(settings);
        Uri uri = new(new Uri(settings.BaseUrl), Uri.EscapeDataString(JavBusCode.Normalize(result.ExternalId)));
        string html = await GetHtmlAsync(client, uri, settings, cancellationToken);
        ProviderMetadata metadata = JavBusParser.Parse(html, uri.ToString(), result.Code);
        return HasUsefulMetadata(metadata) ? metadata : null;
    }

    public Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken) =>
        Task.FromResult(metadata.Images);

    public async Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext context, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try {
            using HttpClient client = CreateClient(context.JavBus);
            using HttpResponseMessage response = await client.GetAsync(new Uri(context.JavBus.BaseUrl), cancellationToken);
            watch.Stop();
            return response.IsSuccessStatusCode
                ? new(true, Name, $"连接成功（HTTP {(int)response.StatusCode}）。", watch.ElapsedMilliseconds)
                : new(false, Name, JavBusErrors.ForStatus(response.StatusCode), watch.ElapsedMilliseconds);
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException) {
            watch.Stop();
            return new(false, Name, error is TaskCanceledException ? "JavBus 请求超时，请检查网络或代理。" : $"JavBus 网络错误：{error.Message}", watch.ElapsedMilliseconds);
        }
    }

    private HttpClient CreateClient(JavBusSettingsDto settings)
    {
        HttpClient client = clients.CreateClient("JavBus");
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LocalMediaManager", "0.6.2"));
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        if (!string.IsNullOrWhiteSpace(settings.Cookie))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", settings.Cookie);
        return client;
    }

    private static async Task<string> GetHtmlAsync(HttpClient client, Uri uri, JavBusSettingsDto settings, CancellationToken cancellationToken, bool notFoundAsEmpty = false)
    {
        Exception? lastError = null;
        int attempts = Math.Max(1, settings.RetryCount + 1);
        for (int attempt = 0; attempt < attempts; attempt++) {
            try {
                using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound) {
                    if (notFoundAsEmpty) return "";
                    throw new InvalidOperationException("JavBus 未找到对应番号。");
                }
                if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
                    throw new InvalidOperationException(JavBusErrors.ForStatus(response.StatusCode));
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException(JavBusErrors.ForStatus(response.StatusCode));
                string html = await response.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(html))
                    throw new InvalidDataException("JavBus 返回空页面。");
                return html;
            } catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested) {
                lastError = error;
                if (attempt < attempts - 1) await Task.Delay(attempt == 0 ? 500 : 1500, cancellationToken);
            }
        }
        throw new HttpRequestException(lastError is TaskCanceledException ? "JavBus 请求超时，请检查网络或代理。" : $"JavBus 网络错误：{lastError?.Message ?? "未知错误"}", lastError);
    }

    private static bool HasUsefulMetadata(ProviderMetadata value) =>
        !string.IsNullOrWhiteSpace(value.Title) || value.Images.Count > 0 || value.Actors.Count > 0 || value.Genres.Count > 0;
    private static string Comparable(string value) => value.Replace("-", "").Replace("_", "").Replace(" ", "").Trim();
}

internal static class JavBusCode
{
    private static readonly Regex Fc2 = new(@"FC2(?:[-_\s]*PPV)?[-_\s]*(\d{5,8})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Code = new(@"(?<![A-Z0-9])([A-Z]{2,8})[-_\s]?(\d{2,6})(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static string Normalize(string value)
    {
        string clean = WebUtility.HtmlDecode(value ?? "").Replace('（', ' ').Replace('）', ' ').Replace('(', ' ').Replace(')', ' ').Trim();
        Match fc2 = Fc2.Match(clean);
        if (fc2.Success) return $"FC2-PPV-{fc2.Groups[1].Value}";
        Match code = Code.Match(clean.ToUpperInvariant());
        return code.Success ? $"{code.Groups[1].Value}-{code.Groups[2].Value}" : clean.ToUpperInvariant().Replace('_', '-');
    }
    public static bool IsUnsupported(string value) => value.StartsWith("FC2-", StringComparison.OrdinalIgnoreCase);
}

internal static class JavBusParser
{
    private static readonly Regex H3 = new(@"<h3[^>]*>(.*?)</h3>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BigImage = new(@"class\s*=\s*[""'][^""']*bigImage[^""']*[""'][^>]*href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Sample = new(@"class\s*=\s*[""'][^""']*sample-box[^""']*[""'][\s\S]*?href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Genre = new(@"href\s*=\s*[""'][^""']*/genre/[^""']+[""'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Actor = new(@"href\s*=\s*[""'][^""']*/star/[^""']+[""'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static ProviderMetadata Parse(string html, string webUrl, string fallbackCode)
    {
        string code = Code(html) ?? fallbackCode;
        string? title = Title(html);
        var images = new List<MetadataImage>();
        string? cover = Absolute(First(BigImage, html), webUrl);
        if (!string.IsNullOrWhiteSpace(cover)) images.Add(new("Poster", cover));
        images.AddRange(Sample.Matches(html).Select(match => Absolute(Html(match.Groups[1].Value), webUrl))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(30)
            .Select(value => new MetadataImage("Preview", value!)));
        return new("JavBus", code, code, title, null,
            Field(html, "導演", "导演"),
            Field(html, "製作商", "制作商", "メーカー"),
            Field(html, "發行商", "发行商", "レーベル"),
            Field(html, "系列", "シリーズ"),
            Duration(Field(html, "長度", "长度", "収録時間")),
            Date(Field(html, "發行日期", "发行日期", "発売日")),
            webUrl,
            Links(Genre, html),
            Links(Actor, html),
            images);
    }

    public static string? Code(string html) => Field(html, "識別碼", "识别码", "品番", "番号");
    public static string? Title(string html)
    {
        string? text = Html(First(H3, html));
        if (string.IsNullOrWhiteSpace(text)) return null;
        string? code = Code(html);
        return !string.IsNullOrWhiteSpace(code) && text.StartsWith(code, StringComparison.OrdinalIgnoreCase)
            ? text[code.Length..].Trim([' ', '\t', '-', '　'])
            : text;
    }
    private static string? Field(string html, params string[] labels)
    {
        foreach (string label in labels) {
            string pattern = $@"<span[^>]*class\s*=\s*[""']header[""'][^>]*>\s*{Regex.Escape(label)}\s*:?\s*</span>\s*(?:<a[^>]*>)?(.*?)(?:</a>)?\s*</p>";
            Match match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success) {
                string value = Html(Regex.Replace(match.Groups[1].Value, "<.*?>", " "));
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        return null;
    }
    private static IReadOnlyList<string> Links(Regex regex, string html) => regex.Matches(html).Select(match => Html(match.Groups[1].Value))
        .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string? First(Regex regex, string html) => regex.Match(html) is { Success: true } match ? Html(match.Groups[1].Value) : null;
    private static string Html(string? value) => Regex.Replace(WebUtility.HtmlDecode(value ?? ""), "<.*?>", " ").Trim();
    private static string? Absolute(string? value, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri.ToString() : Uri.TryCreate(new Uri(baseUrl), value, out uri) ? uri.ToString() : null;
    }
    private static int? Duration(string? value) => Regex.Match(value ?? "", @"\d+") is { Success: true } match && int.TryParse(match.Value, out int minutes) ? minutes * 60 : null;
    private static string? Date(string? value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) && date.Year > 1900 ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
}

internal static class JavBusErrors
{
    public static string ForStatus(HttpStatusCode status) => status switch {
        HttpStatusCode.Forbidden => "JavBus 请求被拒绝，请检查网络、代理或 Cookie。",
        (HttpStatusCode)429 => "JavBus 请求过于频繁，请稍后再试。",
        HttpStatusCode.NotFound => "JavBus 未找到对应番号。",
        _ => $"JavBus 请求失败：HTTP {(int)status} {status}",
    };
}
