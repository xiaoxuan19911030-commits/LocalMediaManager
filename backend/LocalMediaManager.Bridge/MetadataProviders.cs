using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
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
    Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetaTubeSettingsDto settings, CancellationToken cancellationToken);
    Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetaTubeSettingsDto settings, CancellationToken cancellationToken);
    Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken);
    Task<ProviderConnectionResult> TestConnectionAsync(MetaTubeSettingsDto settings, CancellationToken cancellationToken);
}

public sealed class MetaTubeProvider(IHttpClientFactory clients) : IMetadataProvider
{
    private static readonly string[] ProviderPreference = ["FANZA", "MGS", "JavBus", "JAV321", "AVBASE"];
    public string Name => "MetaTube";

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetaTubeSettingsDto settings, CancellationToken cancellationToken)
    {
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

    public async Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetaTubeSettingsDto settings, CancellationToken cancellationToken)
    {
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

    public async Task<ProviderConnectionResult> TestConnectionAsync(MetaTubeSettingsDto settings, CancellationToken cancellationToken)
    {
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
