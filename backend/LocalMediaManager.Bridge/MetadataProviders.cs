using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace LocalMediaManager.Bridge;

public sealed record MetadataSearchResult(string Provider, string ExternalId, string Code, string? Title);
public sealed record MetadataImage(string Type, string Url, string? Provider = null);
public sealed record ProviderMetadata(
    string Provider, string ExternalId, string Code, string? Title, string? Description,
    string? Director, string? Studio, string? Publisher, string? Series, int? DurationSeconds,
    string? ReleaseDate, string? WebUrl, IReadOnlyList<string> Genres, IReadOnlyList<string> Actors,
    IReadOnlyList<MetadataImage> Images, decimal? Rating = null, string? OriginalTitle = null,
    string? Country = null, IReadOnlyList<ActorImageMetadata>? ActorImages = null,
    double Confidence = 0, IReadOnlyDictionary<string, string>? FieldSources = null,
    string? RawResponseHash = null, string? ErrorCode = null, string? ErrorMessage = null);

public static class ProviderMetadataEvidence
{
    public static ProviderMetadata Attach(ProviderMetadata metadata, string source, string rawResponse, double confidence)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Code"] = source };
        Add(fields, "Title", metadata.Title, source); Add(fields, "OriginalTitle", metadata.OriginalTitle, source);
        Add(fields, "Plot", metadata.Description, source); Add(fields, "Director", metadata.Director, source);
        Add(fields, "Studio", metadata.Studio, source); Add(fields, "Publisher", metadata.Publisher, source);
        Add(fields, "Series", metadata.Series, source); Add(fields, "ReleaseDate", metadata.ReleaseDate, source);
        Add(fields, "Country", metadata.Country, source);
        if (metadata.DurationSeconds is > 0) fields["Duration"] = source;
        if (metadata.Rating is > 0) fields["Rating"] = source;
        if (metadata.Actors.Count > 0) fields["Actors"] = source;
        if (metadata.Genres.Count > 0) fields["Genres"] = source;
        foreach (string type in metadata.Images.Where(image => !string.IsNullOrWhiteSpace(image.Url))
                     .Select(image => MediaStoragePathResolver.NormalizeResourceType(image.Type)).Distinct(StringComparer.OrdinalIgnoreCase))
            fields[type] = source;
        return metadata with {
            Confidence = Math.Clamp(confidence, 0, 1),
            FieldSources = fields,
            RawResponseHash = Sha256(rawResponse),
        };
    }

    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""))).ToLowerInvariant();

    private static void Add(Dictionary<string, string> fields, string name, string? value, string source)
    {
        if (!string.IsNullOrWhiteSpace(value)) fields[name] = source;
    }
}

public interface IMetadataProvider
{
    string Name { get; }
    Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext settings, CancellationToken cancellationToken);
    Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext settings, CancellationToken cancellationToken);
    Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken);
    Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext settings, CancellationToken cancellationToken);
}

public sealed record MdcNgToolStatusDto(
    string ServiceUrl,
    string ApiUrl,
    bool ServiceReachable,
    bool ApiReachable,
    string? Version,
    string LastCheckedAt,
    string Message);

public sealed class MdcNgProvider(IHttpClientFactory? clients = null) : IMetadataProvider
{
    private const string DefaultPreviewTargetFolder = "/config/data";
    public string Name => "MDC-NG";

    public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        if (!settings.MdcNg.Enabled || string.IsNullOrWhiteSpace(settings.CurrentMoviePath))
            return Task.FromResult<IReadOnlyList<MetadataSearchResult>>([]);

        string normalizedCode = JavBusCode.Normalize(code);
        return Task.FromResult<IReadOnlyList<MetadataSearchResult>>([
            new(Name, settings.CurrentMoviePath, normalizedCode, normalizedCode)
        ]);
    }

    public async Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.CurrentMoviePath)) return null;
        bool windowsPath = Path.IsPathFullyQualified(settings.CurrentMoviePath);
        if (windowsPath && !File.Exists(settings.CurrentMoviePath)) throw new FileNotFoundException($"[MDC-NG] Windows media file does not exist: {RedactPath(settings.CurrentMoviePath)}");
        MdcNgPathMappingResult? mapping = windowsPath
            ? MdcNgPathMapper.Map(settings.CurrentMoviePath, settings.MdcNg.PathMappings ?? [])
            : new(settings.CurrentMoviePath, settings.CurrentMoviePath.Replace('\\', '/'), new("/", "/", true, 0));
        if (mapping is null) {
            int enabled = (settings.MdcNg.PathMappings ?? []).Count(value => value.Enabled);
            await (settings.ProviderLog?.Invoke(Name, $"未找到媒体路径映射：{RedactPath(settings.CurrentMoviePath)}；已启用规则：{enabled}", cancellationToken) ?? Task.CompletedTask);
            throw new InvalidOperationException($"MDC-NG path mapping is not configured for {RedactPath(settings.CurrentMoviePath)}");
        }
        await (settings.ProviderLog?.Invoke(Name, $"路径映射已命中：Windows={RedactPath(settings.CurrentMoviePath)}；Provider={RedactPath(mapping.ProviderPath)}", cancellationToken) ?? Task.CompletedTask);
        MdcNgScrapeResult scrape = await ScrapeAsync(
            new(mapping.ProviderPath, result.Code, TimeoutSeconds: settings.MdcNg.TimeoutSeconds),
            settings.MdcNg,
            cancellationToken,
            (message, token) => settings.ProviderDebugLog?.Invoke(Name, message, token) ?? Task.CompletedTask);
        if (!scrape.Success || scrape.Metadata is null)
            throw new InvalidOperationException(scrape.Message);
        if (scrape.Message.Contains("file operation failed", StringComparison.OrdinalIgnoreCase))
            await (settings.ProviderLog?.Invoke(Name, scrape.Message, cancellationToken) ?? Task.CompletedTask);

        if (settings.ResponseCapture is not null)
            await settings.ResponseCapture(Name, "json", scrape.RawJson, cancellationToken);
        return ToProviderMetadata(scrape.Metadata, scrape.RawJson);
    }

    public Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken) =>
        Task.FromResult(metadata.Images);

    private static ProviderMetadata ToProviderMetadata(MovieMetadata metadata, string rawResponse)
    {
        var images = new List<MetadataImage>();
        AddImage(images, "Poster", metadata.Poster);
        AddImage(images, "Thumb", metadata.Thumb);
        AddImage(images, "Fanart", metadata.Fanart);
        foreach (string image in metadata.ExtraFanart) AddImage(images, "Preview", image);
        AddImage(images, "Trailer", metadata.Trailer);

        ProviderMetadata result = new(metadata.Provider, metadata.ExternalId ?? metadata.Code, metadata.Code, metadata.Title,
            metadata.Description, metadata.Director, metadata.Studio, null, metadata.Series,
            metadata.DurationSeconds > 0 ? metadata.DurationSeconds : null, metadata.ReleaseDate, null,
            metadata.Tags, metadata.Actors, images, metadata.Rating, metadata.OriginalTitle, metadata.Country, metadata.ActorImages);
        return ProviderMetadataEvidence.Attach(result, "MDC-NG", rawResponse, 0.95);
    }

    private static void AddImage(List<MetadataImage> images, string type, string? url)
    {
        if (!string.IsNullOrWhiteSpace(url)) images.Add(new(type, url, "MDC-NG"));
    }

    public async Task<MdcNgScrapeResult> ScrapeAsync(MdcNgScrapeCommand command, MdcNgSettingsDto settings, CancellationToken cancellationToken,
        Func<string, CancellationToken, Task>? diagnosticLog = null)
    {
        if (string.IsNullOrWhiteSpace(command.MoviePath))
            throw new ArgumentException("MoviePath is required.", nameof(command));
        MdcNgSettingsDto normalized = MetadataProviderSettingsService.NormalizeMdcNg(settings);
        int timeoutSeconds = Math.Clamp(command.TimeoutSeconds ?? normalized.TimeoutSeconds, 10, 600);
        using HttpClient client = clients?.CreateClient("MDC-NG") ?? new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(Math.Min(timeoutSeconds, 60));
        Uri apiRoot = await ResolveApiRootAsync(client, normalized, cancellationToken);
        await DebugAsync(diagnosticLog, $"ServiceUrl={normalized.ServiceUrl}; ApiUrl={normalized.ApiUrl}; ResolvedApiRoot={apiRoot}", cancellationToken);

        JsonElement beforeJobs = await ReadJsonElementAsync(client, new Uri(apiRoot, "api/manual-jobs?page=1&page_size=20"), cancellationToken);
        long beforeMaxJobId = MaxId(beforeJobs);
        Dictionary<string, object> payload = new() {
            ["source_pathes"] = new[] { command.MoviePath },
            ["pathes"] = new[] { command.MoviePath },
            ["target_dir"] = string.IsNullOrWhiteSpace(command.TargetFolder) ? DefaultPreviewTargetFolder : command.TargetFolder,
            ["target_folder"] = string.IsNullOrWhiteSpace(command.TargetFolder) ? DefaultPreviewTargetFolder : command.TargetFolder,
            ["link_mode"] = 3,
            ["delete_empty_parent_after_move"] = false,
        };
        Uri jobUri = new Uri(apiRoot, "api/manual-jobs");
        await DebugAsync(diagnosticLog, $"POST {jobUri}; Query=none; ContentType=application/json; Body={RedactJson(JsonSerializer.Serialize(payload))}; SourcePath={RedactPath(command.MoviePath)}; Code={command.Code}", cancellationToken);
        await PostManualJobAsync(client, jobUri, payload, diagnosticLog, cancellationToken);

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        JsonElement latest = beforeJobs;
        string? jobId = null;
        string? taskId = null;
        string status = "created";
        while (DateTimeOffset.UtcNow < deadline) {
            JsonElement jobs = await ReadJsonElementAsync(client, new Uri(apiRoot, "api/manual-jobs?page=1&page_size=20"), cancellationToken);
            await DebugAsync(diagnosticLog, $"GET {new Uri(apiRoot, "api/manual-jobs?page=1&page_size=20")}; Response={RedactJson(jobs.GetRawText())}", cancellationToken);
            JsonElement? job = FindManualJob(jobs, command.MoviePath, beforeMaxJobId);
            if (job is not null) {
                latest = job.Value;
                jobId ??= FirstScalar(latest, "id", "job_id", "jobId");
                status = FirstScalar(latest, "status", "state") ?? status;
            }

            JsonElement tasks = await ReadJsonElementAsync(client, new Uri(apiRoot, "api/tasks_full?page=1&page_size=20"), cancellationToken);
            await DebugAsync(diagnosticLog, $"GET {new Uri(apiRoot, "api/tasks_full?page=1&page_size=20")}; Response={RedactJson(tasks.GetRawText())}", cancellationToken);
            JsonElement? task = FindTask(tasks, command.MoviePath, command.Code, jobId);
            bool latestIsTask = task is not null;
            if (task is not null) {
                latest = task.Value;
                taskId ??= FirstScalar(latest, "id", "task_id", "taskId");
                jobId ??= FirstScalar(latest, "manual_job_id", "manualJobId", "job_id", "jobId");
            }

            status = FirstScalar(latest, "status", "state") ?? status;
            if (TryFindProperty(latest, "metadata", out JsonElement metadataValue) && metadataValue.ValueKind == JsonValueKind.Object) {
                MovieMetadata metadata = MdcNgAdapter.ToMovieMetadata(latest, command.Code);
                string? fileError = FirstScalar(latest, "error_message", "errorMessage");
                string message = string.IsNullOrWhiteSpace(fileError)
                    ? "MDC-NG scrape task returned metadata."
                    : $"MDC-NG metadata returned; provider file operation failed: {fileError}";
                return new(Name, true, status, jobId, taskId, metadata, latest.GetRawText(), message);
            }
            if (!latestIsTask && IsSuccessStatus(latest, status) && FirstScalar(latest, "total_count", "totalCount") is not "0") {
                await Task.Delay(250, cancellationToken);
                continue;
            }
            if (IsSuccessStatus(latest, status))
                return new(Name, false, status, jobId, taskId, null, latest.GetRawText(), "MDC-NG scrape task finished without metadata.");
            if (IsFailureStatus(latest, status)) {
                return new(Name, false, status, jobId, taskId, null, latest.GetRawText(), "MDC-NG scrape task failed before returning metadata.");
            }
            await Task.Delay(250, cancellationToken);
        }
        return new(Name, false, status, jobId, taskId, null, latest.GetRawText(), "MDC-NG scrape task timed out before returning metadata.");
    }

    public async Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        MdcNgToolStatusDto status = await StatusAsync(settings.MdcNg, cancellationToken);
        watch.Stop();
        return new(status.ApiReachable, Name, status.Message, watch.ElapsedMilliseconds);
    }

    public async Task<MdcNgToolStatusDto> StatusAsync(MdcNgSettingsDto settings, CancellationToken cancellationToken)
    {
        using HttpClient client = clients?.CreateClient("MDC-NG") ?? new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 10, 600));
        bool serviceReachable = await IsReachableAsync(client, new Uri(new Uri(settings.ServiceUrl), "settings/common"), cancellationToken);
        string apiUrl = settings.ApiUrl;
        (bool apiReachable, string? version) = await ReadVersionAsync(client, new Uri(new Uri(apiUrl), "api/version"), cancellationToken);
        if (!apiReachable && !settings.ApiUrl.Equals(settings.ServiceUrl, StringComparison.OrdinalIgnoreCase)) {
            (apiReachable, version) = await ReadVersionAsync(client, new Uri(new Uri(settings.ServiceUrl), "api/version"), cancellationToken);
            if (apiReachable) apiUrl = settings.ServiceUrl;
        }
        string message = apiReachable
            ? $"MDC-NG API connected: {apiUrl}"
            : serviceReachable
                ? $"MDC-NG web is reachable, but API is not reachable. Map 9207:9207 and confirm API URL {settings.ApiUrl}."
                : $"MDC-NG is not reachable. Check service URL {settings.ServiceUrl} and API URL {settings.ApiUrl}.";
        return new(settings.ServiceUrl, apiUrl, serviceReachable, apiReachable, version, DateTimeOffset.Now.ToString("O"), message);
    }

    private static async Task<bool> IsReachableAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        try {
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode;
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException) {
            return false;
        }
    }

    private static async Task<Uri> ResolveApiRootAsync(HttpClient client, MdcNgSettingsDto settings, CancellationToken cancellationToken)
    {
        Uri configured = new(settings.ApiUrl);
        (bool reachable, _) = await ReadVersionAsync(client, new Uri(configured, "api/version"), cancellationToken);
        if (reachable) return configured;
        Uri service = new(settings.ServiceUrl);
        if (service.Equals(configured)) return configured;
        (bool serviceApiReachable, _) = await ReadVersionAsync(client, new Uri(service, "api/version"), cancellationToken);
        return serviceApiReachable ? service : configured;
    }

    private static async Task<(bool Reachable, string? Version)> ReadVersionAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        try {
            string version = (await client.GetStringAsync(uri, cancellationToken)).Trim();
            return (true, string.IsNullOrWhiteSpace(version) ? null : version);
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException) {
            return (false, null);
        }
    }

    private static async Task PostManualJobAsync(HttpClient client, Uri uri, object payload, Func<string, CancellationToken, Task>? diagnosticLog, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(uri, payload, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        await DebugAsync(diagnosticLog, $"POST Response: HTTP {(int)response.StatusCode} {response.ReasonPhrase}; Body={RedactJson(body)}; MDC-NG Version=unknown", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"MDC-NG request failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}; response={RedactJson(body)}");
    }

    private static Task DebugAsync(Func<string, CancellationToken, Task>? diagnosticLog, string message, CancellationToken token) =>
        diagnosticLog is null ? Task.CompletedTask : diagnosticLog(message, token);

    private static string RedactPath(string value)
    {
        string normalized = value.Replace('/', '\\');
        string fileName = Path.GetFileName(normalized);
        if (normalized.StartsWith("\\\\", StringComparison.Ordinal))
            return string.IsNullOrWhiteSpace(fileName) ? "\\\\<redacted>" : $"\\\\<redacted>\\…\\{fileName}";
        string? root = Path.GetPathRoot(normalized);
        if (!string.IsNullOrWhiteSpace(root))
            return string.IsNullOrWhiteSpace(fileName) ? $"{root}<redacted>" : $"{root}<redacted>\\{fileName}";
        return string.IsNullOrWhiteSpace(fileName) ? "<provider-path>" : $"<provider-path>/{fileName}";
    }
    private static string RedactJson(string value) => value.Length <= 1200 ? value : value[..1200] + "…";

    private static async Task<JsonElement> ReadJsonElementAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"MDC-NG poll failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private static long MaxId(JsonElement source)
    {
        return EnumerateDataItems(source)
            .Select(item => long.TryParse(FirstScalar(item, "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static JsonElement? FindManualJob(JsonElement source, string moviePath, long afterId)
    {
        return EnumerateDataItems(source)
            .Where(item => long.TryParse(FirstScalar(item, "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && id > afterId)
            .Where(item => ContainsText(item, moviePath))
            .OrderByDescending(item => long.Parse(FirstScalar(item, "id")!, CultureInfo.InvariantCulture))
            .Select(item => (JsonElement?)item.Clone())
            .FirstOrDefault();
    }

    private static JsonElement? FindTask(JsonElement source, string moviePath, string? code, string? jobId)
    {
        IEnumerable<JsonElement> items = EnumerateDataItems(source);
        if (!string.IsNullOrWhiteSpace(jobId)) {
            return items
                .Where(item => FirstScalar(item, "manual_job_id", "manualJobId", "job_id", "jobId") == jobId)
                .Select(item => (JsonElement?)item.Clone())
                .FirstOrDefault();
        }
        return EnumerateDataItems(source)
            .Where(item => ContainsText(item, moviePath) || (!string.IsNullOrWhiteSpace(code) && ContainsText(item, code)))
            .Select(item => (JsonElement?)item.Clone())
            .FirstOrDefault();
    }

    private static IEnumerable<JsonElement> EnumerateDataItems(JsonElement source)
    {
        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
            return data.EnumerateArray();
        if (source.ValueKind == JsonValueKind.Array) return source.EnumerateArray();
        return [];
    }

    private static bool ContainsText(JsonElement source, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (source.ValueKind == JsonValueKind.String)
            return source.GetString()?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
        if (source.ValueKind == JsonValueKind.Object)
            return source.EnumerateObject().Any(property => ContainsText(property.Value, text));
        if (source.ValueKind == JsonValueKind.Array)
            return source.EnumerateArray().Any(item => ContainsText(item, text));
        return false;
    }

    private static string? FirstScalar(JsonElement source, params string[] names)
    {
        foreach (string name in names) {
            if (TryFindProperty(source, name, out JsonElement value)) {
                string? text = value.ValueKind switch {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
        }
        return null;
    }

    private static bool TryFindProperty(JsonElement source, string name, out JsonElement value)
    {
        if (source.ValueKind == JsonValueKind.Object) {
            foreach (JsonProperty property in source.EnumerateObject()) {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                    value = property.Value;
                    return true;
                }
            }
            foreach (JsonProperty property in source.EnumerateObject()) {
                if (TryFindProperty(property.Value, name, out value)) return true;
            }
        } else if (source.ValueKind == JsonValueKind.Array) {
            foreach (JsonElement item in source.EnumerateArray()) {
                if (TryFindProperty(item, name, out value)) return true;
            }
        }
        value = default;
        return false;
    }

    private static bool IsSuccessStatus(JsonElement source, string status)
    {
        if (int.TryParse(status, CultureInfo.InvariantCulture, out int number) && number is 2 or 1000) return true;
        string normalized = status.Trim().ToLowerInvariant();
        return normalized is "finished" or "complete" or "completed" or "success" or "succeeded" or "done"
            || FirstScalar(source, "finished_at", "finishedAt", "completed_at", "completedAt") is not null;
    }

    private static bool IsFailureStatus(JsonElement source, string status)
    {
        if (int.TryParse(status, CultureInfo.InvariantCulture, out int number) && number < 0) return true;
        string normalized = status.Trim().ToLowerInvariant();
        return normalized is "failed" or "failure" or "error" or "abort" or "aborted" or "cancelled" or "canceled"
            || FirstScalar(source, "error", "exception") is not null;
    }
}

public sealed class MetadataNoResultException(string message) : InvalidOperationException(message);
public sealed class MetadataProviderBlockedException(string message) : InvalidOperationException(message);

public static class MetadataProviderCapabilities
{
    public static IReadOnlySet<string> For(string provider) => ProviderCatalog.Fields(provider);

    public static bool SupportsAny(string provider, IReadOnlySet<string>? requested)
    {
        if (requested is null || requested.Count == 0) return true;
        HashSet<string> normalized = Normalize(requested);
        return normalized.Count == 0 || normalized.Overlaps(For(provider));
    }

    public static bool Satisfies(ProviderMetadata metadata, IReadOnlySet<string>? requested)
    {
        if (requested is null || requested.Count == 0) return false;
        foreach (string field in Normalize(requested)) {
            bool present = field switch {
                "Title" => !string.IsNullOrWhiteSpace(metadata.Title),
                "OriginalTitle" => !string.IsNullOrWhiteSpace(metadata.OriginalTitle),
                "Actors" => metadata.Actors.Count > 0,
                "Genres" => metadata.Genres.Count > 0,
                "Director" => !string.IsNullOrWhiteSpace(metadata.Director),
                "Studio" => !string.IsNullOrWhiteSpace(metadata.Studio) || !string.IsNullOrWhiteSpace(metadata.Publisher),
                "Publisher" => !string.IsNullOrWhiteSpace(metadata.Publisher),
                "Series" => !string.IsNullOrWhiteSpace(metadata.Series),
                "ReleaseDate" => !string.IsNullOrWhiteSpace(metadata.ReleaseDate),
                "Duration" => metadata.DurationSeconds is > 0,
                "Description" => !string.IsNullOrWhiteSpace(metadata.Description),
                "Poster" or "Fanart" or "Preview" or "Screenshot" => metadata.Images.Any(image => MediaStoragePathResolver.NormalizeResourceType(image.Type).Equals(field, StringComparison.OrdinalIgnoreCase)),
                _ => true,
            };
            if (!present) return false;
        }
        return true;
    }

    private static HashSet<string> Normalize(IEnumerable<string> fields) => fields
        .Select(field => field.Equals("Tags", StringComparison.OrdinalIgnoreCase) ? "Genres" : field)
        .Where(field => !field.Equals("NFO", StringComparison.OrdinalIgnoreCase))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

}
public sealed class MetaTubeProvider(IHttpClientFactory clients) : IMetadataProvider
{
    private static readonly string[] ProviderPreference = ["FANZA", "MGS", "JavBus", "JAV321", "AVBASE"];
    public string Name => "MetaTube";

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        MetaTubeSettingsDto settings = context.MetaTube;
        using HttpClient client = CreateClient(settings, context.NetworkSettings);
        using JsonDocument document = await GetJsonAsync(client,
            new Uri(new Uri(settings.BaseUrl), $"v1/movies/search?q={Uri.EscapeDataString(NormalizeCode(code))}&fallback=True"), cancellationToken, notFoundAsEmpty: true);
        if (context.ResponseCapture is not null)
            await context.ResponseCapture(Name, "json", document.RootElement.GetRawText(), cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            return [];
        var results = new List<MetadataSearchResult>();
        foreach (JsonElement item in data.EnumerateArray()) {
            string provider = String(item, "provider");
            string id = String(item, "id");
            string number = String(item, "number");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(provider)) continue;
            results.Add(new(provider, id, JavBusCode.Normalize(NormalizeCode(string.IsNullOrWhiteSpace(number) ? code : number)), NullableString(item, "title")));
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
        using HttpClient client = CreateClient(settings, context.NetworkSettings);
        Uri detailUrl = new(new Uri(settings.BaseUrl), $"v1/movies/{Uri.EscapeDataString(result.Provider)}/{Uri.EscapeDataString(result.ExternalId)}?lazy=True");
        using JsonDocument document = await GetJsonAsync(client, detailUrl, cancellationToken);
        if (context.ResponseCapture is not null)
            await context.ResponseCapture(Name, "json", document.RootElement.GetRawText(), cancellationToken);
        JsonElement root = document.RootElement;
        JsonElement movie = root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object ? data : root;
        string code = JavBusCode.Normalize(NormalizeCode(String(movie, "number")));
        if (string.IsNullOrWhiteSpace(code)) code = result.Code;
        string? releaseDate = NormalizeDate(NullableString(movie, "release_date"));
        int? runtimeMinutes = NullableInt(movie, "runtime");
        string? primary = null;
        if (!string.IsNullOrWhiteSpace(result.Provider) && !string.IsNullOrWhiteSpace(result.ExternalId))
            primary = new Uri(new Uri(settings.BaseUrl), $"v1/images/primary/{Uri.EscapeDataString(result.Provider)}/{Uri.EscapeDataString(result.ExternalId)}").ToString();
        primary ??= FirstUrl(movie, "big_cover_url", "cover_url", "big_thumb_url", "thumb_url");
        var images = new List<MetadataImage>();
        string evidenceSource = $"MetaTube/{result.Provider}";
        if (!string.IsNullOrWhiteSpace(primary)) images.Add(new("Poster", StripQuery(primary), evidenceSource));
        string? fanart = FirstUrl(movie, "backdrop_url", "fanart_url", "background_url", "landscape_url")
            ?? FirstUrl(movie, "big_cover_url", "cover_url");
        if (!string.IsNullOrWhiteSpace(fanart)) images.Add(new("Fanart", StripQuery(fanart), evidenceSource));
        MetadataImage[] previewImages = movie.TryGetProperty("preview_images", out JsonElement previews) && previews.ValueKind == JsonValueKind.Array
            ? previews.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()).Where(IsHttpUrl).Select(value => new MetadataImage("Preview", StripQuery(value!), evidenceSource)).Take(30).ToArray()
            : [];
        if (string.IsNullOrWhiteSpace(fanart)) {
            string? fallbackFanart = previewImages.FirstOrDefault()?.Url ?? primary;
            if (!string.IsNullOrWhiteSpace(fallbackFanart)) images.Add(new("Fanart", fallbackFanart, evidenceSource));
        }
        images.AddRange(previewImages);
        var metadata = new ProviderMetadata(
            result.Provider, result.ExternalId, code, NullableString(movie, "title"), NullableString(movie, "summary"),
            NullableString(movie, "director"), NullableString(movie, "maker"), NullableString(movie, "label"),
            NullableString(movie, "series"), runtimeMinutes is > 0 ? runtimeMinutes * 60 : null, releaseDate,
            NullableString(movie, "homepage") ?? detailUrl.ToString(), Strings(movie, "genres"), ActorNames(movie),
            images.DistinctBy(image => $"{image.Type}\u0000{image.Url}", StringComparer.OrdinalIgnoreCase).ToArray(),
            ActorImages: ActorImages(movie));
        metadata = ProviderMetadataEvidence.Attach(metadata, evidenceSource, movie.GetRawText(),
            Comparable(code) == Comparable(result.Code) ? 0.98 : 0.85);
        return HasUsefulMetadata(metadata) ? metadata : null;
    }

    public Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken) =>
        Task.FromResult(metadata.Images);

    public async Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext context, CancellationToken cancellationToken)
    {
        MetaTubeSettingsDto settings = context.MetaTube;
        var watch = Stopwatch.StartNew();
        try {
            Uri uri = new(new Uri(settings.BaseUrl), "v1/movies/search?q=ABP-001&fallback=False");
            string trace = await ProviderNetworkDiagnostics.ProbeAsync(Name, uri, settings.TimeoutSeconds,
                ProviderNetworkDiagnostics.JsonHeaders, cancellationToken, context.NetworkSettings);
            watch.Stop();
            bool success = trace.Contains("HTTP 200", StringComparison.OrdinalIgnoreCase);
            return new(success, Name, trace, watch.ElapsedMilliseconds);
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException) {
            watch.Stop();
            return new(false, Name, error is TaskCanceledException ? "连接超时。" : error.Message, watch.ElapsedMilliseconds);
        }
    }

    private HttpClient CreateClient(MetaTubeSettingsDto settings, ProviderNetworkSettingsDto network)
    {
        HttpClient client = network.ProxyMode.Equals("System", StringComparison.OrdinalIgnoreCase)
            ? clients.CreateClient("MetaTube")
            : ProviderHttpClients.Create(settings.TimeoutSeconds, network);
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LocalMediaManager", "0.7.6"));
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
        string trace = await ProviderNetworkDiagnostics.ProbeAsync("MetaTube", uri, (int)Math.Max(15, client.Timeout.TotalSeconds),
            ProviderNetworkDiagnostics.JsonHeaders, CancellationToken.None);
        throw new ProviderNetworkException("MetaTube", uri, $"MetaTube 连续请求 3 次均失败。最后错误：{lastError?.Message ?? "未知错误"}。{trace}", lastError);
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
    private static IReadOnlyList<string> ActorNames(JsonElement source) {
        if (!source.TryGetProperty("actors", out JsonElement values) || values.ValueKind != JsonValueKind.Array) return [];
        return values.EnumerateArray().Select(value => value.ValueKind switch {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object => FirstString(value, "name", "jp_name", "ja_name", "display_name", "title"),
            _ => null,
        }).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static IReadOnlyList<ActorImageMetadata> ActorImages(JsonElement source) {
        var result = new List<ActorImageMetadata>();
        if (source.TryGetProperty("actors", out JsonElement actors) && actors.ValueKind == JsonValueKind.Array) {
            foreach (JsonElement actor in actors.EnumerateArray()) {
                if (actor.ValueKind != JsonValueKind.Object) continue;
                AddActorImage(result, actor, "name", "jp_name", "ja_name", "display_name", "title");
            }
        }
        foreach (string groupName in new[] { "actor_images", "actorImages", "actor_avatars", "actorAvatars" }) {
            if (!source.TryGetProperty(groupName, out JsonElement values) || values.ValueKind != JsonValueKind.Array) continue;
            foreach (JsonElement item in values.EnumerateArray()) {
                if (item.ValueKind == JsonValueKind.Object) AddActorImage(result, item, "name", "actor", "actress", "jp_name", "ja_name", "display_name", "title");
            }
        }
        return result.DistinctBy(value => $"{value.Name}\u0000{value.ImageUrl}", StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static void AddActorImage(List<ActorImageMetadata> result, JsonElement source, params string[] nameKeys) {
        string? name = FirstString(source, nameKeys);
        string? image = FirstUrl(source, "image_url", "avatar_url", "photo_url", "portrait_url", "thumbnail_url", "thumb_url", "url", "image", "avatar", "photo", "portrait", "thumbnail");
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(image))
            result.Add(new(name.Trim(), StripQuery(image)));
    }
    private static string? FirstString(JsonElement source, params string[] names) =>
        names.Select(name => NullableString(source, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static string? FirstUrl(JsonElement source, params string[] names) => names.Select(name => NullableString(source, name)).FirstOrDefault(IsHttpUrl);
    private static bool IsHttpUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https";
    private static string StripQuery(string value) { int index = value.IndexOfAny(['?', '#']); return index >= 0 ? value[..index] : value; }
    private static string? NormalizeDate(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime date) && date.Year > 1900
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }
}

public sealed class CompositeMetadataProvider(MdcNgProvider mdcNg, MetaTubeProvider metaTube, JavBusProvider javBus,
    IMovieNumberExtractor? movieNumberExtractor = null) : IMetadataProvider
{
    private readonly ProviderManager providerManager = new(
        new IMetadataProvider[] { mdcNg, metaTube, javBus }, movieNumberExtractor);
    public string Name => "Metadata";

    public CompositeMetadataProvider(ProviderManager providerManager, MdcNgProvider mdcNg, MetaTubeProvider metaTube,
        JavBusProvider javBus, IMovieNumberExtractor? movieNumberExtractor = null)
        : this(mdcNg, metaTube, javBus, movieNumberExtractor)
    {
        this.providerManager = providerManager;
    }

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        if (!Ordered(settings).Any())
            throw new MetadataProviderBlockedException("当前没有可用于此影片的元数据来源；请检查 Provider 健康状态或 MDC-NG 路径映射。");
        if (string.IsNullOrWhiteSpace(settings.PreferredSource))
            return [new("Composite", code, code, null)];
        var providers = Ordered(settings).ToArray();
        if (providers.Length == 0)
            throw new InvalidOperationException("当前网络检测没有可用的元数据来源，本次同步已跳过不可达 Provider。");
        var results = new List<MetadataSearchResult>();
        foreach (IProviderSdk provider in providers) {
            try {
                IReadOnlyList<MetadataSearchResult> found = await provider.SearchAsync(code, settings, cancellationToken);
                results.AddRange(found);
                if (found.Count > 0 && !string.IsNullOrWhiteSpace(settings.PreferredSource)) break;
            } catch (Exception error) when (error is not OperationCanceledException && string.IsNullOrWhiteSpace(settings.PreferredSource)) {
                results.Add(new($"__error:{provider.Descriptor.Name}", error.Message, code, null));
            }
        }
        MetadataSearchResult[] errors = results.Where(value => value.Provider.StartsWith("__error:", StringComparison.Ordinal)).ToArray();
        results.RemoveAll(value => value.Provider.StartsWith("__error:", StringComparison.Ordinal));
        if (results.Count == 0 && errors.Length > 0)
            throw new HttpRequestException("All metadata providers failed: " + string.Join(" | ", errors.Select(value => $"{value.Provider[8..]}: {value.ExternalId}")));
        return results;
    }

    public async Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        IProviderSdk provider = providerManager.Resolve(ResolveSourceName(result.Provider));
        if (result.Provider.Equals("Composite", StringComparison.OrdinalIgnoreCase))
            return await CompositeAsync(result.Code, settings, cancellationToken);
        if (provider.Descriptor.Name.Equals("JavBus", StringComparison.OrdinalIgnoreCase) || !settings.JavBus.Enabled || !string.IsNullOrWhiteSpace(settings.PreferredSource))
            return await provider.GetDetailAsync(result, settings, cancellationToken);

        ProviderMetadata? primary = null;
        Exception? primaryError = null;
        try {
            primary = await metaTube.GetMetadataAsync(result, settings, cancellationToken);
        } catch (Exception error) when (error is not OperationCanceledException) {
            primaryError = error;
        }

        if (primary is not null && !NeedsJavBusSupplement(primary))
            return primary;

        ProviderMetadata? fallback = await TryJavBusAsync(result.Code, settings, cancellationToken);
        if (primary is null) {
            if (fallback is not null) return fallback;
            if (primaryError is not null) throw primaryError;
            return null;
        }
        return fallback is null ? primary : Merge(primary, fallback);
    }

    public Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken) =>
        Task.FromResult(metadata.Images);

    public Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext settings, CancellationToken cancellationToken) =>
        metaTube.TestConnectionAsync(settings, cancellationToken);

    private IEnumerable<IProviderSdk> Ordered(MetadataProviderContext settings) =>
        providerManager.OrderedProviders(settings);

    private static string ResolveSourceName(string provider) =>
        provider.Equals("MDC-NG", StringComparison.OrdinalIgnoreCase) ? "MDC-NG"
        : provider.Equals("JavBus", StringComparison.OrdinalIgnoreCase) ? "JavBus"
        : "MetaTube";

    private async Task<ProviderMetadata?> CompositeAsync(string code, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        ProviderMetadata? merged = null;
        int providerFailures = 0;
        int completedSearches = 0;
        foreach (IProviderSdk source in Ordered(settings)) {
            string sourceName = source.Descriptor.Name;
            if (!MetadataProviderCapabilities.SupportsAny(sourceName, settings.RequestedFields)) {
                await LogAsync(settings, sourceName, "跳过：该 Provider 不负责本次目标字段", cancellationToken);
                continue;
            }
            await LogAsync(settings, sourceName, "开始", cancellationToken);
            IReadOnlyList<MetadataSearchResult> results;
            try {
                results = await source.SearchAsync(code, settings, cancellationToken);
                completedSearches++;
                await LogAsync(settings, sourceName, $"搜索结束：候选 {results.Count} 个", cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException) {
                providerFailures++;
                await (settings.ProviderFailure?.Invoke(sourceName, error, cancellationToken) ?? Task.CompletedTask);
                await LogAsync(settings, sourceName, $"失败：{error.Message}", cancellationToken);
                continue;
            }
            bool accepted = false;
            foreach (MetadataSearchResult result in results.Take(2)) {
                try {
                    ProviderMetadata? candidate = await source.GetDetailAsync(result, settings, cancellationToken);
                    if (candidate is null) {
                        await LogAsync(settings, sourceName, "详情为空，继续下一候选", cancellationToken);
                        continue;
                    }
                    if (!(movieNumberExtractor?.AreEquivalent(code, candidate.Code)
                        ?? JavBusCode.Normalize(candidate.Code).Equals(JavBusCode.Normalize(code), StringComparison.OrdinalIgnoreCase))) {
                        await LogAsync(settings, sourceName, $"番号不匹配：期望 {code}，实际 {candidate.Code}", cancellationToken);
                        continue;
                    }
                    string returned = Describe(candidate);
                    ProviderMetadata next = merged is null ? candidate : Merge(merged, candidate);
                    string added = merged is null ? Describe(candidate) : DescribeAdded(merged, next);
                    merged = next;
                    accepted = true;
                    await (settings.ProviderSuccess?.Invoke(sourceName, cancellationToken) ?? Task.CompletedTask);
                    await LogAsync(settings, sourceName, $"返回：{returned}", cancellationToken);
                    await LogAsync(settings, sourceName, $"补全：{added}", cancellationToken);
                    break;
                }
                catch (Exception error) when (error is not OperationCanceledException) {
                    await (settings.ProviderFailure?.Invoke(sourceName, error, cancellationToken) ?? Task.CompletedTask);
                    await LogAsync(settings, sourceName, $"详情失败：{error.Message}", cancellationToken);
                }
            }
            await LogAsync(settings, sourceName, accepted ? "结束：已参与合并" : "结束：无可合并结果", cancellationToken);
            if (merged is not null && MetadataProviderCapabilities.Satisfies(merged, settings.RequestedFields)) {
                await LogAsync(settings, "Metadata Router", "目标字段已满足，停止调用后续 Provider", cancellationToken);
                break;
            }
            if (merged is not null
                && (settings.RequestedFields is null || settings.RequestedFields.Count == 0)
                && sourceName.Equals("MetaTube", StringComparison.OrdinalIgnoreCase)
                && !NeedsJavBusSupplement(merged)) {
                await LogAsync(settings, "Metadata Router", "MetaTube 已覆盖 JavBus 可补字段，停止调用 JavBus", cancellationToken);
                break;
            }
        }
        if (merged is null && providerFailures > 0 && completedSearches == 0)
            throw new HttpRequestException("所有已启用 Provider 均发生网络或解析异常；详见任务日志。");
        if (merged is not null)
            await LogAsync(settings, "Metadata Merge", $"最终字段：{Describe(merged)}", cancellationToken);
        return merged;
    }

    private async Task<ProviderMetadata?> TryJavBusAsync(string code, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        try {
            MetadataProviderContext javBusOnly = settings with { PreferredSource = "JavBus" };
            MetadataSearchResult? result = (await javBus.SearchAsync(code, javBusOnly, cancellationToken)).FirstOrDefault();
            return result is null ? null : await javBus.GetMetadataAsync(result, javBusOnly, cancellationToken);
        } catch (Exception error) when (error is HttpRequestException or InvalidDataException or InvalidOperationException or TaskCanceledException) {
            if (error is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
            return null;
        }
    }

    private static bool NeedsJavBusSupplement(ProviderMetadata metadata) =>
        string.IsNullOrWhiteSpace(metadata.Title)
        || string.IsNullOrWhiteSpace(metadata.Director)
        || string.IsNullOrWhiteSpace(metadata.Studio)
        || string.IsNullOrWhiteSpace(metadata.Publisher)
        || string.IsNullOrWhiteSpace(metadata.Series)
        || metadata.DurationSeconds is null or <= 0
        || string.IsNullOrWhiteSpace(metadata.ReleaseDate)
        || metadata.Actors.Count == 0
        || metadata.Genres.Count == 0
        || !HasImageType(metadata.Images, "Poster")
        || !HasImageType(metadata.Images, "Preview");

    private static ProviderMetadata Merge(ProviderMetadata primary, ProviderMetadata fallback) => primary with {
        Title = FirstText(primary.Title, fallback.Title),
        OriginalTitle = FirstText(primary.OriginalTitle, fallback.OriginalTitle),
        Description = FirstText(primary.Description, fallback.Description),
        Director = FirstText(primary.Director, fallback.Director),
        Studio = FirstText(primary.Studio, fallback.Studio),
        Publisher = FirstText(primary.Publisher, fallback.Publisher),
        Series = FirstText(primary.Series, fallback.Series),
        DurationSeconds = primary.DurationSeconds is > 0 ? primary.DurationSeconds : fallback.DurationSeconds,
        ReleaseDate = FirstText(primary.ReleaseDate, fallback.ReleaseDate),
        WebUrl = FirstText(primary.WebUrl, fallback.WebUrl),
        Country = FirstText(primary.Country, fallback.Country),
        Rating = primary.Rating is > 0 ? primary.Rating : fallback.Rating,
        Genres = FillMissingValues(primary.Genres, fallback.Genres),
        Actors = FillMissingValues(primary.Actors, fallback.Actors),
        Images = MergeImages(primary.Images, fallback.Images),
        ActorImages = FillMissingActorImages(primary.ActorImages ?? [], fallback.ActorImages ?? []),
        Confidence = Math.Max(primary.Confidence, fallback.Confidence),
        FieldSources = MergeFieldSources(primary, fallback),
        RawResponseHash = MergeHashes(primary.RawResponseHash, fallback.RawResponseHash),
    };

    private static bool HasImageType(IReadOnlyList<MetadataImage> images, string type) =>
        images.Any(image => image.Type.Equals(type, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(image.Url));

    private static IReadOnlyList<MetadataImage> MergeImages(IReadOnlyList<MetadataImage> primary,
        IReadOnlyList<MetadataImage> fallback)
    {
        var result = primary.Where(image => !string.IsNullOrWhiteSpace(image.Url)).ToList();
        foreach (IGrouping<string, MetadataImage> group in fallback.Where(image => !string.IsNullOrWhiteSpace(image.Url))
                     .GroupBy(image => image.Type, StringComparer.OrdinalIgnoreCase)) {
            if (!HasImageType(result, group.Key))
                result.AddRange(group.DistinctBy(candidate => candidate.Url, StringComparer.OrdinalIgnoreCase));
        }
        return result.DistinctBy(image => $"{image.Type}\u0000{image.Url}", StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> FillMissingValues(IReadOnlyList<string> primary, IReadOnlyList<string> fallback) =>
        (primary.Count > 0 ? primary : fallback).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static IReadOnlyList<ActorImageMetadata> FillMissingActorImages(IReadOnlyList<ActorImageMetadata> primary,
        IReadOnlyList<ActorImageMetadata> fallback) =>
        (primary.Count > 0 ? primary : fallback).Where(value => !string.IsNullOrWhiteSpace(value.Name) && !string.IsNullOrWhiteSpace(value.ImageUrl))
            .DistinctBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    private static IReadOnlyDictionary<string, string> MergeFieldSources(ProviderMetadata primary, ProviderMetadata fallback)
    {
        var result = new Dictionary<string, string>(primary.FieldSources ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        foreach ((string field, string source) in fallback.FieldSources ?? new Dictionary<string, string>()) {
            if (!result.ContainsKey(field) && !HasField(primary, field)) {
                result[field] = source;
            }
        }
        return result;
    }

    private static bool HasField(ProviderMetadata metadata, string field) => field switch {
        "Code" => !string.IsNullOrWhiteSpace(metadata.Code),
        "Title" => !string.IsNullOrWhiteSpace(metadata.Title),
        "OriginalTitle" => !string.IsNullOrWhiteSpace(metadata.OriginalTitle),
        "Plot" or "Description" => !string.IsNullOrWhiteSpace(metadata.Description),
        "Director" => !string.IsNullOrWhiteSpace(metadata.Director),
        "Studio" => !string.IsNullOrWhiteSpace(metadata.Studio),
        "Publisher" => !string.IsNullOrWhiteSpace(metadata.Publisher),
        "Series" => !string.IsNullOrWhiteSpace(metadata.Series),
        "ReleaseDate" => !string.IsNullOrWhiteSpace(metadata.ReleaseDate),
        "Country" => !string.IsNullOrWhiteSpace(metadata.Country),
        "Duration" => metadata.DurationSeconds is > 0,
        "Rating" => metadata.Rating is > 0,
        "Actors" => metadata.Actors.Count > 0,
        "Genres" => metadata.Genres.Count > 0,
        _ => HasImageType(metadata.Images, field),
    };

    private static string? MergeHashes(string? primary, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(primary)) return fallback;
        if (string.IsNullOrWhiteSpace(fallback)) return primary;
        return ProviderMetadataEvidence.Sha256($"{primary}:{fallback}");
    }

    private static string? FirstText(string? primary, string? fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static Task LogAsync(MetadataProviderContext settings, string provider, string message, CancellationToken token) =>
        settings.ProviderLog?.Invoke(provider, message, token) ?? Task.CompletedTask;

    private static string Describe(ProviderMetadata value)
    {
        var fields = new List<string>();
        var empty = new List<string>();
        AddState(fields, empty, "标题", value.Title); AddState(fields, empty, "番号", value.Code);
        AddState(fields, empty, "日期", value.ReleaseDate); AddState(fields, empty, "时长", value.DurationSeconds is > 0);
        AddState(fields, empty, "导演", value.Director); AddState(fields, empty, "厂商", value.Studio);
        AddState(fields, empty, "系列", value.Series); AddState(fields, empty, "标签", value.Genres.Count > 0, value.Genres.Count);
        AddState(fields, empty, "演员", value.Actors.Count > 0, value.Actors.Count);
        AddState(fields, empty, "Poster", HasImageType(value.Images, "Poster"));
        AddState(fields, empty, "Fanart", HasImageType(value.Images, "Fanart"));
        AddState(fields, empty, "Preview", HasImageType(value.Images, "Preview"), value.Images.Count(image => image.Type.Equals("Preview", StringComparison.OrdinalIgnoreCase)));
        AddState(fields, empty, "Screenshot", HasImageType(value.Images, "Screenshot"), value.Images.Count(image => image.Type.Equals("Screenshot", StringComparison.OrdinalIgnoreCase)));
        AddState(fields, empty, "简介", value.Description); AddState(fields, empty, "评分", value.Rating is > 0);
        empty.Add("NFO(由同步流程生成，Provider 不直接返回)");
        return $"有 {fields.Count} 项：{(fields.Count == 0 ? "无" : string.Join("、", fields))}；空：{string.Join("、", empty)}";
    }

    private static string DescribeAdded(ProviderMetadata before, ProviderMetadata after)
    {
        var fields = new List<string>();
        Added(fields, "标题", before.Title, after.Title); Added(fields, "日期", before.ReleaseDate, after.ReleaseDate);
        if (before.DurationSeconds is null or <= 0 && after.DurationSeconds is > 0) fields.Add("时长");
        Added(fields, "导演", before.Director, after.Director); Added(fields, "厂商", before.Studio, after.Studio);
        Added(fields, "发行商", before.Publisher, after.Publisher); Added(fields, "系列", before.Series, after.Series);
        Added(fields, "简介", before.Description, after.Description); Added(fields, "原始标题", before.OriginalTitle, after.OriginalTitle);
        Added(fields, "国家", before.Country, after.Country);
        if (before.Rating is null or <= 0 && after.Rating is > 0) fields.Add("评分");
        int genres = after.Genres.Count - before.Genres.Count; if (genres > 0) fields.Add($"标签(+{genres})");
        int actors = after.Actors.Count - before.Actors.Count; if (actors > 0) fields.Add($"演员(+{actors})");
        foreach (IGrouping<string, MetadataImage> images in after.Images.Except(before.Images).GroupBy(image => image.Type, StringComparer.OrdinalIgnoreCase))
            fields.Add($"{images.Key}(+{images.Count()})");
        int actorImages = (after.ActorImages?.Count ?? 0) - (before.ActorImages?.Count ?? 0);
        if (actorImages > 0) fields.Add($"演员头像(+{actorImages})");
        return fields.Count == 0 ? "无新增，保留现有值" : string.Join("、", fields);
    }

    private static void AddState(List<string> fields, List<string> empty, string name, string? value) =>
        AddState(fields, empty, name, !string.IsNullOrWhiteSpace(value));
    private static void AddState(List<string> fields, List<string> empty, string name, bool present, int count = 0) {
        if (present) fields.Add(count > 0 ? $"{name}({count})" : name); else empty.Add(name);
    }
    private static void Added(List<string> fields, string name, string? before, string? after) {
        if (string.IsNullOrWhiteSpace(before) && !string.IsNullOrWhiteSpace(after)) fields.Add(name);
    }

}

public sealed class JavBusProvider(IHttpClientFactory clients) : IMetadataProvider
{
    private const int MaximumPageCacheEntries = 64;
    private static readonly TimeSpan PageCacheTtl = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, CachedPage> pageCache = new(StringComparer.OrdinalIgnoreCase);
    public const string DefaultBaseUrl = "https://www.javbus.com/";
    public string Name => "JavBus";

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        string normalized = JavBusCode.Normalize(code);
        if (string.IsNullOrWhiteSpace(normalized) || JavBusCode.IsUnsupported(normalized)) return [];
        JavBusSettingsDto settings = context.JavBus;
        string urlCode = Uri.EscapeDataString(normalized);
        Exception? last = null;
        foreach (JavBusSettingsDto candidate in Candidates(settings)) {
            try {
                Uri uri = new(new Uri(candidate.BaseUrl), urlCode);
                using HttpClient client = CreateClient(candidate, context.NetworkSettings);
                string html = await GetHtmlAsync(client, uri, candidate, cancellationToken, notFoundAsEmpty: true);
                if (context.ResponseCapture is not null && !string.IsNullOrWhiteSpace(html))
                    await context.ResponseCapture(Name, "html", html, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) continue;
                JavBusPageGuard.EnsureMoviePage(html);
                string parsedCode = JavBusParser.Code(html) ?? normalized;
                if (Comparable(parsedCode) == Comparable(normalized)) {
                    StorePage(uri, candidate, html);
                    return [new("JavBus", uri.ToString(), parsedCode, JavBusParser.Title(html))];
                }
            } catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
                last = error;
            }
        }
        if (last is not null) throw last;
        return [];
    }

    public async Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        JavBusSettingsDto settings = context.JavBus;
        Uri uri = Uri.TryCreate(result.ExternalId, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(new Uri(settings.BaseUrl), Uri.EscapeDataString(JavBusCode.Normalize(result.ExternalId)));
        JavBusSettingsDto effective = settings with { BaseUrl = uri.GetLeftPart(UriPartial.Authority) + "/" };
        bool cacheHit = TryTakePage(uri, effective, out string html);
        if (!cacheHit) {
            using HttpClient client = CreateClient(effective, context.NetworkSettings);
            html = await GetHtmlAsync(client, uri, effective, cancellationToken);
        }
        if (context.ResponseCapture is not null && !cacheHit)
            await context.ResponseCapture(Name, "html", html, cancellationToken);
        JavBusPageGuard.EnsureMoviePage(html);
        ProviderMetadata parsed = JavBusParser.Parse(html, uri.ToString(), result.Code);
        ProviderMetadata metadata = ProviderMetadataEvidence.Attach(parsed with {
            Images = parsed.Images.Select(image => image with { Provider = "JavBus" }).ToArray(),
        }, "JavBus", html, 0.96);
        return HasUsefulMetadata(metadata) ? metadata : null;
    }

    public Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken) =>
        Task.FromResult(metadata.Images);

    public async Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext context, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try {
            using HttpClient client = CreateClient(context.JavBus, context.NetworkSettings);
            Uri uri = new(context.JavBus.BaseUrl);
            string trace = await ProviderNetworkDiagnostics.ProbeAsync(Name, uri, context.JavBus.TimeoutSeconds,
                request => ProviderNetworkDiagnostics.BrowserHeaders(request, context.JavBus.Cookie, context.JavBus.BaseUrl), cancellationToken, context.NetworkSettings);
            watch.Stop();
            bool success = trace.Contains("HTTP 200", StringComparison.OrdinalIgnoreCase)
                && !trace.Contains("Cloudflare: detected", StringComparison.OrdinalIgnoreCase)
                && !trace.Contains("Blocked Page:", StringComparison.OrdinalIgnoreCase);
            return new(success, Name, trace, watch.ElapsedMilliseconds);
        } catch (Exception error) when (error is HttpRequestException or TaskCanceledException) {
            watch.Stop();
            return new(false, Name, error is TaskCanceledException ? "JavBus 请求超时，请检查网络或代理。" : $"JavBus 网络错误：{error.Message}", watch.ElapsedMilliseconds);
        }
    }

    private HttpClient CreateClient(JavBusSettingsDto settings, ProviderNetworkSettingsDto network)
    {
        HttpClient client = network.ProxyMode.Equals("System", StringComparison.OrdinalIgnoreCase)
            ? clients.CreateClient("JavBus")
            : ProviderHttpClients.Create(settings.TimeoutSeconds, network);
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9,en-US;q=0.7,en;q=0.5");
        if (!string.IsNullOrWhiteSpace(settings.Cookie))
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", settings.Cookie);
        return client;
    }

    private static IReadOnlyList<JavBusSettingsDto> Candidates(JavBusSettingsDto settings) =>
        new[] { settings.BaseUrl }.Concat(settings.MirrorUrls ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(url => settings with { BaseUrl = url })
            .ToArray();

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
        string trace = await ProviderNetworkDiagnostics.ProbeAsync("JavBus", uri, settings.TimeoutSeconds,
            request => ProviderNetworkDiagnostics.BrowserHeaders(request, settings.Cookie, settings.BaseUrl), CancellationToken.None);
        throw new ProviderNetworkException("JavBus", uri,
            lastError is TaskCanceledException ? $"JavBus 请求超时，请检查网络或代理。{trace}" : $"JavBus 网络错误：{lastError?.Message ?? "未知错误"}。{trace}", lastError);
    }

    private static bool HasUsefulMetadata(ProviderMetadata value) =>
        !string.IsNullOrWhiteSpace(value.Title) || value.Images.Count > 0 || value.Actors.Count > 0;
    private static string Comparable(string value) => value.Replace("-", "").Replace("_", "").Replace(" ", "").Trim();

    private void StorePage(Uri uri, JavBusSettingsDto settings, string html)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach ((string key, CachedPage value) in pageCache.Where(value => value.Value.ExpiresAt <= now))
            pageCache.TryRemove(key, out _);
        if (pageCache.Count >= MaximumPageCacheEntries) {
            foreach (string key in pageCache.OrderBy(value => value.Value.ExpiresAt)
                         .Take(pageCache.Count - MaximumPageCacheEntries + 1).Select(value => value.Key))
                pageCache.TryRemove(key, out _);
        }
        pageCache[PageKey(uri, settings)] = new(html, now.Add(PageCacheTtl));
    }

    private bool TryTakePage(Uri uri, JavBusSettingsDto settings, out string html)
    {
        if (pageCache.TryRemove(PageKey(uri, settings), out CachedPage? cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow) {
            html = cached.Html;
            return true;
        }
        html = "";
        return false;
    }

    private static string PageKey(Uri uri, JavBusSettingsDto settings) =>
        $"{uri.AbsoluteUri}|{ProviderMetadataEvidence.Sha256(settings.Cookie ?? "")}";

    private sealed record CachedPage(string Html, DateTimeOffset ExpiresAt);
}

internal static class JavBusPageGuard
{
    public static void EnsureMoviePage(string html)
    {
        if (ContainsAny(html, "cf-chl-", "challenge-platform", "cf-turnstile", "Just a moment..."))
            throw new InvalidOperationException("JavBus Cloudflare challenge detected.");
        if (Regex.IsMatch(html, @"<link\b[^>]*rel\s*=\s*[""']canonical[""'][^>]*driver-verify", RegexOptions.IgnoreCase)
            || Regex.IsMatch(html, @"<form\b[^>]*action\s*=\s*[""'][^""']*driver-verify", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("JavBus authentication required: verification page returned. Configure a valid Cookie.");
    }

    private static bool ContainsAny(string value, params string[] markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
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
    private static readonly Regex ActorLink = new(@"<a\b[^>]*href\s*=\s*[""'][^""']*/star/[^""']+[""'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Image = new(@"<img\b[^>]*\bsrc\s*=\s*[""']([^""']+)[""'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

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
            images,
            ActorImages: ActorImages(html, webUrl));
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
    private static IReadOnlyList<ActorImageMetadata> ActorImages(string html, string webUrl) =>
        ActorLink.Matches(html)
            .Select(match => {
                string body = match.Groups[1].Value;
                string? image = Absolute(First(Image, body), webUrl);
                string name = Html(body);
                return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(image)
                    ? new ActorImageMetadata(name, image)
                    : null;
            })
            .Where(value => value is not null)
            .Cast<ActorImageMetadata>()
            .DistinctBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
