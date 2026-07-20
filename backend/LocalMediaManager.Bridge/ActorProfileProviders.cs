using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public interface IActorProfileProvider
{
    string Name { get; }
    Task<IReadOnlyList<ActorProfileCandidate>> SearchAsync(string name, IReadOnlyList<string> aliases, WebMetadataSettingsDto settings, CancellationToken cancellationToken);
    Task<ProviderConnectionResult> TestConnectionAsync(WebMetadataSettingsDto settings, CancellationToken cancellationToken);
}

public sealed record ActorProfilePreview(long ActorId, IReadOnlyList<ActorProfileCandidate> Candidates, IReadOnlyList<string> Warnings);

public sealed class ActorProfileProviderService(string databasePath, MetadataProviderSettingsService settings, MinnanoActorProfileProvider minnano,
    WikipediaJpActorProfileProvider wikipedia, ActorProfileService profiles)
{
    public async Task<ActorProfilePreview> PreviewAsync(long actorId, string? source, CancellationToken cancellationToken)
    {
        if (actorId <= 0) throw new ArgumentOutOfRangeException(nameof(actorId));
        (string Name, IReadOnlyList<string> Aliases) actor = await ReadActorAsync(actorId, cancellationToken);
        var candidates = new List<ActorProfileCandidate>();
        var warnings = new List<string>();
        foreach ((IActorProfileProvider provider, WebMetadataSettingsDto providerSettings) in await ProvidersAsync(source)) {
            if (!providerSettings.Enabled) continue;
            try {
                candidates.AddRange((await provider.SearchAsync(actor.Name, actor.Aliases, providerSettings, cancellationToken))
                    .Where(candidate => candidate.Confidence >= 0.85));
            } catch (Exception error) when (error is not OperationCanceledException) {
                warnings.Add($"{provider.Name}: {SafeError(error)}");
            }
        }
        return new(actorId, candidates.OrderByDescending(value => value.Confidence).ToArray(), warnings);
    }

    public Task<ActorProfileMergeResult> ApplyAsync(long actorId, ActorProfileCandidate candidate, CancellationToken cancellationToken) =>
        profiles.MergeAsync(actorId, candidate, cancellationToken);

    private async Task<IReadOnlyList<(IActorProfileProvider, WebMetadataSettingsDto)>> ProvidersAsync(string? source)
    {
        var values = new List<(IActorProfileProvider, WebMetadataSettingsDto)> {
            (minnano, await settings.ReadMinnanoAsync()),
            (wikipedia, await settings.ReadWikipediaJpAsync()),
        };
        return string.IsNullOrWhiteSpace(source) ? values : values.Where(item => item.Item1.Name.Equals(source, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private async Task<(string, IReadOnlyList<string>)> ReadActorAsync(long actorId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name,Alias FROM Actors WHERE Id=$id AND Id>0";
        command.Parameters.AddWithValue("$id", actorId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException($"Actor {actorId} was not found.");
        string name = reader.GetString(0);
        string[] aliases = reader.IsDBNull(1) ? [] : reader.GetString(1).Split([',', '，', '/', '／'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return (name, aliases);
    }

    private static string SafeError(Exception error) => error switch {
        TaskCanceledException => "请求超时，请检查代理后重试。",
        InvalidDataException => "页面结构已变化，当前结果未写入。",
        InvalidOperationException => error.Message,
        _ => "网络请求失败，完整错误已写入 Debug 日志。",
    };
}

public sealed class MinnanoActorProfileProvider(IHttpClientFactory clients) : IActorProfileProvider
{
    public const string DefaultBaseUrl = "https://www.minnano-av.com/";
    public string Name => "Minnano";
    public async Task<IReadOnlyList<ActorProfileCandidate>> SearchAsync(string name, IReadOnlyList<string> aliases, WebMetadataSettingsDto settings, CancellationToken cancellationToken)
    {
        using HttpClient client = ActorHttp(clients, Name, settings);
        Uri search = new(new Uri(settings.BaseUrl), $"search_result.php?search_word={Uri.EscapeDataString(name)}");
        string html = await GetTextAsync(client, search, cancellationToken);
        var names = new[] { name }.Concat(aliases).Select(NormalizeName).Where(value => value.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<ActorProfileCandidate>();
        foreach ((string url, string candidateName) in MinnanoParser.Results(html, search)) {
            if (!names.Contains(NormalizeName(candidateName))) continue;
            string detail = await GetTextAsync(client, new Uri(url), cancellationToken);
            ActorProfileData profile = MinnanoParser.Profile(detail);
            candidates.Add(new(Name, candidateName, url, NormalizeName(candidateName) == NormalizeName(name) ? 0.98 : 0.9, profile));
            if (candidates.Count >= 3) break;
        }
        return candidates;
    }
    public Task<ProviderConnectionResult> TestConnectionAsync(WebMetadataSettingsDto settings, CancellationToken cancellationToken) =>
        TestAsync(clients, Name, settings, cancellationToken);

    internal static HttpClient ActorHttp(IHttpClientFactory clients, string name, WebMetadataSettingsDto settings)
    {
        HttpClient client = clients.CreateClient(name);
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9,en-US;q=0.7,en;q=0.5");
        if (!string.IsNullOrWhiteSpace(settings.Cookie)) client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", settings.Cookie);
        return client;
    }
    internal static async Task<string> GetTextAsync(HttpClient client, Uri uri, CancellationToken token)
    {
        try {
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ProviderParsing.StatusMessage(uri.Host, response.StatusCode));
            string text = await response.Content.ReadAsStringAsync(token);
            if (ProviderParsing.LooksBlocked(text)) throw new InvalidOperationException("来源返回了验证或登录页面。");
            return text;
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException) {
            string trace = await ProviderNetworkDiagnostics.ProbeAsync(uri.Host, uri, 30,
                request => ProviderNetworkDiagnostics.BrowserHeaders(request, null, uri.GetLeftPart(UriPartial.Authority) + "/"), CancellationToken.None);
            throw new ProviderNetworkException(uri.Host, uri, $"{error.Message}. {trace}", error);
        }
    }
    internal static async Task<ProviderConnectionResult> TestAsync(IHttpClientFactory clients, string name, WebMetadataSettingsDto settings, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        try {
            Uri uri = new(settings.BaseUrl);
            string trace = await ProviderNetworkDiagnostics.ProbeAsync(name, uri, settings.TimeoutSeconds,
                request => ProviderNetworkDiagnostics.BrowserHeaders(request, settings.Cookie, settings.BaseUrl), token);
            bool success = trace.Contains("HTTP 200", StringComparison.OrdinalIgnoreCase)
                && !trace.Contains("Cloudflare: detected", StringComparison.OrdinalIgnoreCase)
                && !trace.Contains("Blocked Page:", StringComparison.OrdinalIgnoreCase);
            return new(success, name, trace, watch.ElapsedMilliseconds);
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException) {
            return new(false, name, error is TaskCanceledException ? $"{name} HTTP Timeout" : $"{name} 网络请求失败：{error.Message}", watch.ElapsedMilliseconds);
        }
    }
    private static string NormalizeName(string value) => Regex.Replace(value ?? "", @"[\s・･·]", "").ToUpperInvariant();
}

public sealed class WikipediaJpActorProfileProvider(IHttpClientFactory clients) : IActorProfileProvider
{
    public const string DefaultBaseUrl = "https://ja.wikipedia.org/";
    public string Name => "Wikipedia JP";
    public async Task<IReadOnlyList<ActorProfileCandidate>> SearchAsync(string name, IReadOnlyList<string> aliases, WebMetadataSettingsDto settings, CancellationToken cancellationToken)
    {
        using HttpClient client = MinnanoActorProfileProvider.ActorHttp(clients, Name, settings);
        Uri uri = new(new Uri(settings.BaseUrl), $"w/api.php?action=query&generator=search&gsrsearch={Uri.EscapeDataString(name)}&gsrlimit=5&prop=extracts|info&exintro=1&explaintext=1&inprop=url&format=json&formatversion=2");
        string json = await MinnanoActorProfileProvider.GetTextAsync(client, uri, cancellationToken);
        return WikipediaJpParser.Parse(json, name, aliases);
    }
    public Task<ProviderConnectionResult> TestConnectionAsync(WebMetadataSettingsDto settings, CancellationToken cancellationToken) =>
        MinnanoActorProfileProvider.TestAsync(clients, Name, settings, cancellationToken);
}

internal static class MinnanoParser
{
    public static IReadOnlyList<(string Url, string Name)> Results(string html, Uri baseUri) =>
        Regex.Matches(html, @"<a[^>]*href=[""']([^""']*(?:actress|av_actress)[^""']*)[""'][^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase)
            .Select(match => (ProviderParsing.Absolute(match.Groups[1].Value, baseUri)!, ProviderParsing.Text(match.Groups[2].Value)))
            .Where(value => value.Item1 is not null && value.Item2.Length > 0).Distinct().ToArray();

    public static ActorProfileData Profile(string html)
    {
        string? birthday = ProviderParsing.Date(ProviderParsing.Labeled(html, "生年月日", "誕生日"));
        int? height = Number(ProviderParsing.Labeled(html, "身長"));
        string? cup = Regex.Match(ProviderParsing.Labeled(html, "カップ", "罩杯") ?? "", @"\b([A-N])\b", RegexOptions.IgnoreCase) is { Success: true } match ? match.Groups[1].Value.ToUpperInvariant() : null;
        string[] aliases = ProviderParsing.Links(html, "alias", "actress").Take(12).ToArray();
        string? avatar = ProviderParsing.Meta(html, "og:image");
        return new(birthday, height, cup, null, ProviderParsing.Labeled(html, "デビュー", "活動期間"), null, aliases, avatar);
    }
    private static int? Number(string? value) => Regex.Match(value ?? "", @"\d{3}") is { Success: true } match && int.TryParse(match.Value, out int number) && number is >= 100 and <= 250 ? number : null;
}

internal static class WikipediaJpParser
{
    public static IReadOnlyList<ActorProfileCandidate> Parse(string json, string name, IReadOnlyList<string> aliases)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("query", out JsonElement query) || !query.TryGetProperty("pages", out JsonElement pages) || pages.ValueKind != JsonValueKind.Array) return [];
        var names = new[] { name }.Concat(aliases).Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<ActorProfileCandidate>();
        foreach (JsonElement page in pages.EnumerateArray()) {
            string title = page.TryGetProperty("title", out JsonElement titleValue) ? titleValue.GetString() ?? "" : "";
            string extract = page.TryGetProperty("extract", out JsonElement extractValue) ? extractValue.GetString() ?? "" : "";
            if (!names.Contains(Normalize(title)) || IsDisambiguation(extract)) continue;
            string url = page.TryGetProperty("fullurl", out JsonElement urlValue) ? urlValue.GetString() ?? "" : "";
            string? birth = Regex.Match(extract, @"(?<y>19\d{2}|20\d{2})年(?<m>\d{1,2})月(?<d>\d{1,2})日") is { Success: true } date
                ? $"{date.Groups["y"].Value}-{int.Parse(date.Groups["m"].Value):00}-{int.Parse(date.Groups["d"].Value):00}" : null;
            int? height = Regex.Match(extract, @"身長\s*(\d{3})\s*cm", RegexOptions.IgnoreCase) is { Success: true } heightMatch ? int.Parse(heightMatch.Groups[1].Value) : null;
            string? birthplace = Regex.Match(extract, @"(?:出身地|出生地)[は:：\s]*([^。、\n]{2,40})") is { Success: true } place ? place.Groups[1].Value.Trim() : null;
            string description = extract.Length <= 500 ? extract : extract[..500];
            results.Add(new("Wikipedia JP", title, url, 0.96, new(birth, height, null, birthplace, null, description, [])));
        }
        return results;
    }
    private static string Normalize(string value) => Regex.Replace(value ?? "", @"[\s・･·（）()]", "").ToUpperInvariant();
    private static bool IsDisambiguation(string value) => value.Contains("曖昧さ回避", StringComparison.Ordinal) || value.Contains("同名", StringComparison.Ordinal) && value.Length < 150;
}
