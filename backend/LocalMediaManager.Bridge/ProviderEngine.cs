using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalMediaManager.Bridge;

public enum ProviderHealthStatus
{
    Available,
    Partial,
    Offline,
    AuthenticationRequired,
    RateLimited,
    ConfigurationError,
    ParserBroken,
    Unsupported,
}

public enum ProviderFailureCategory
{
    Network,
    Timeout,
    NotFound,
    RateLimited,
    Parser,
    Cloudflare,
    Authentication,
    EmptyResult,
    NoMatch,
    Configuration,
    Unsupported,
    Unknown,
}

public sealed record ProviderDescriptor(
    string Name,
    int Priority,
    int MinimumDelayMilliseconds,
    IReadOnlySet<string> Capabilities,
    bool RequiresAuthentication = false,
    bool RequiresPath = false,
    bool RequiresMedia = false);

public sealed record ProviderRawResponse(string Provider, string Format, string Content);
public sealed record ProviderStageTrace(string Stage, string Status, long ElapsedMilliseconds, string? Message = null);
public sealed record ProviderCacheInfo(bool Hit, string Key, string? ExpiresAt);
public sealed record ProviderMergePreview(IReadOnlyList<string> ContributedFields, ProviderMetadata? Result);
public sealed record ProviderPlaygroundRequest(string Provider, string Code, bool BypassCache = false);
public sealed record ProviderPlaygroundResult(
    string Provider,
    string Code,
    ProviderHealthStatus Status,
    int? HttpStatus,
    long ElapsedMilliseconds,
    bool ParserSucceeded,
    int FieldCount,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<ProviderRawResponse> RawResponses,
    ProviderMetadata? ProviderResult,
    ProviderMergePreview MergePreview,
    IReadOnlyList<ProviderStageTrace> Diagnostics,
    ProviderCacheInfo Cache,
    ProviderFailureCategory? FailureCategory = null,
    string? Error = null);

public sealed record ProviderBenchmarkSnapshot(
    string Provider,
    long RequestCount,
    long SuccessCount,
    long FailureCount,
    double SuccessRate,
    double ParserSuccessRate,
    double AverageElapsedMilliseconds,
    string? LastRequestAt,
    string? LastSuccessAt,
    string? LastError,
    ProviderFailureCategory? LastFailureCategory);

public sealed record ProviderDashboardItem(
    ProviderDescriptor Descriptor,
    bool Enabled,
    ProviderHealthStatus Status,
    ProviderBenchmarkSnapshot Benchmark);

public interface IProviderSdk
{
    ProviderDescriptor Descriptor { get; }
    Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext context, CancellationToken cancellationToken);
    Task<ProviderMetadata?> GetDetailAsync(MetadataSearchResult result, MetadataProviderContext context, CancellationToken cancellationToken);
    Task<ProviderConnectionResult> HealthAsync(MetadataProviderContext context, CancellationToken cancellationToken);
}

public static class ProviderCatalog
{
    private static readonly IReadOnlyDictionary<string, ProviderDescriptor> Descriptors =
        new Dictionary<string, ProviderDescriptor>(StringComparer.OrdinalIgnoreCase) {
            ["MDC-NG"] = Describe("MDC-NG", 1, 250,
                ["Title", "OriginalTitle", "Actors", "Genres", "Director", "Studio", "Publisher", "Series", "ReleaseDate", "Duration", "Description", "Plot", "Poster", "Fanart", "Preview", "Rating", "NFO", "SourceURL", "SearchByCode", "SearchByFile", "SearchByPath"],
                requiresPath: true, requiresMedia: true),
            ["MetaTube"] = Describe("MetaTube", 2, 150,
                ["Title", "OriginalTitle", "Actors", "Genres", "Director", "Studio", "Publisher", "Series", "ReleaseDate", "Duration", "Description", "Plot", "Poster", "Fanart", "Preview", "Rating", "NFO", "SourceURL", "SearchByCode", "SearchByTitle"]),
            ["JavBus"] = Describe("JavBus", 3, 900,
                ["Title", "Actors", "Genres", "Director", "Studio", "Publisher", "Series", "ReleaseDate", "Duration", "Poster", "Preview", "SourceURL", "SearchByCode", "Detail"]),
            ["Mock"] = Describe("Mock", 1000, 0,
                ["Title", "Actors", "Genres", "Director", "Studio", "Series", "ReleaseDate", "Duration", "Description", "Plot", "Poster", "Fanart", "Preview", "Rating", "NFO", "SourceURL", "SearchByCode"]),
        };

    public static IReadOnlyList<ProviderDescriptor> All => Descriptors.Values.OrderBy(value => value.Priority).ToArray();

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultPriorities { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["Actors"] = ["JavBus", "MetaTube", "MDC-NG"],
            ["Genres"] = ["JavBus", "MetaTube", "MDC-NG"],
            ["Poster"] = ["MetaTube", "JavBus", "MDC-NG"],
            ["Fanart"] = ["MetaTube", "MDC-NG"],
            ["NFO"] = ["MetaTube", "MDC-NG"],
            ["Description"] = ["MDC-NG", "MetaTube"],
            ["Series"] = ["MetaTube", "MDC-NG", "JavBus"],
            ["Director"] = ["JavBus", "MetaTube", "MDC-NG"],
            ["Studio"] = ["JavBus", "MetaTube", "MDC-NG"],
            ["ReleaseDate"] = ["JavBus", "MetaTube", "MDC-NG"],
        };

    public static ProviderDescriptor Get(string provider) => Descriptors.TryGetValue(provider, out ProviderDescriptor? descriptor)
        ? descriptor
        : throw new KeyNotFoundException($"Provider is not registered: {provider}");

    public static bool Supports(string provider, string field) => Get(provider).Capabilities.Contains(NormalizeField(field));

    public static IReadOnlySet<string> Fields(string provider) => Get(provider).Capabilities
        .Where(value => !value.StartsWith("SearchBy", StringComparison.OrdinalIgnoreCase) && value is not "Detail" and not "NFO" and not "SourceURL")
        .Select(NormalizeField).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string NormalizeField(string field) => field.Equals("Tags", StringComparison.OrdinalIgnoreCase) ? "Genres"
        : field.Equals("Plot", StringComparison.OrdinalIgnoreCase) ? "Description"
        : field;

    private static ProviderDescriptor Describe(string name, int priority, int delay, string[] capabilities,
        bool requiresAuthentication = false, bool requiresPath = false, bool requiresMedia = false) =>
        new(name, priority, delay, capabilities.ToHashSet(StringComparer.OrdinalIgnoreCase),
            requiresAuthentication, requiresPath, requiresMedia);
}

public sealed class MetadataProviderSdk(IMetadataProvider provider) : IProviderSdk
{
    public IMetadataProvider Provider => provider;
    public ProviderDescriptor Descriptor => ProviderCatalog.Get(provider.Name);
    public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext context, CancellationToken cancellationToken) =>
        provider.SearchAsync(code, context, cancellationToken);
    public Task<ProviderMetadata?> GetDetailAsync(MetadataSearchResult result, MetadataProviderContext context, CancellationToken cancellationToken) =>
        provider.GetMetadataAsync(result, context, cancellationToken);
    public Task<ProviderConnectionResult> HealthAsync(MetadataProviderContext context, CancellationToken cancellationToken) =>
        provider.TestConnectionAsync(context, cancellationToken);
}

public sealed class MockMetadataProvider : IMetadataProvider
{
    public string Name => "Mock";

    public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = JavBusCode.Normalize(code);
        return Task.FromResult<IReadOnlyList<MetadataSearchResult>>([new(Name, normalized, normalized, $"Mock {normalized}")]);
    }

    public async Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext context, CancellationToken cancellationToken)
    {
        const string raw = "{\"provider\":\"Mock\",\"offline\":true}";
        if (context.ResponseCapture is not null)
            await context.ResponseCapture(Name, "json", raw, cancellationToken);
        ProviderMetadata metadata = new(Name, result.ExternalId, result.Code, $"Mock {result.Code}", "Offline deterministic fixture",
            "Mock Director", "Mock Studio", null, "Mock Series", 5400, "2026-01-01", "https://example.invalid/mock",
            ["Mock Genre"], ["Mock Actor"], [new("Poster", "https://example.invalid/poster.jpg", Name), new("Fanart", "https://example.invalid/fanart.jpg", Name)],
            8.5m, Country: "JP");
        return ProviderMetadataEvidence.Attach(metadata, Name, raw, 1);
    }

    public Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken) =>
        Task.FromResult(metadata.Images);

    public Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderConnectionResult(true, Name, "Offline deterministic provider is available.", 0));
}

public sealed class ProviderManager
{
    private const int MaximumCacheEntries = 256;
    private const int MaximumHistoryEntries = 200;
    private readonly IReadOnlyDictionary<string, IProviderSdk> providers;
    private readonly IMovieNumberExtractor? movieNumberExtractor;
    private readonly TimeSpan cacheTtl;
    private readonly Func<DateTimeOffset> clock;
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProviderMetrics> metrics = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProviderHealthStatus> health = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<ProviderPlaygroundResult> history = new();

    public ProviderManager(IEnumerable<IMetadataProvider> providers, IMovieNumberExtractor? movieNumberExtractor = null,
        TimeSpan? cacheTtl = null, Func<DateTimeOffset>? clock = null)
        : this(providers.Select(value => (IProviderSdk)new MetadataProviderSdk(value)), movieNumberExtractor, cacheTtl, clock)
    {
    }

    public ProviderManager(IEnumerable<IProviderSdk> providers, IMovieNumberExtractor? movieNumberExtractor = null,
        TimeSpan? cacheTtl = null, Func<DateTimeOffset>? clock = null)
    {
        this.providers = providers.ToDictionary(value => value.Descriptor.Name, StringComparer.OrdinalIgnoreCase);
        this.movieNumberExtractor = movieNumberExtractor;
        this.cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(10);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<ProviderDescriptor> Registered => providers.Values.Select(value => value.Descriptor)
        .OrderBy(value => value.Priority).ToArray();

    public IMetadataProvider ResolveMetadataProvider(string name)
    {
        if (!providers.TryGetValue(name, out IProviderSdk? sdk)) throw new KeyNotFoundException($"Provider is not registered: {name}");
        return sdk is MetadataProviderSdk adapter
            ? adapter.Provider
            : throw new InvalidOperationException($"Provider adapter is unavailable: {name}");
    }

    public IProviderSdk Resolve(string name) => providers.TryGetValue(name, out IProviderSdk? sdk)
        ? sdk
        : throw new KeyNotFoundException($"Provider is not registered: {name}");

    public IReadOnlyList<IProviderSdk> OrderedProviders(MetadataProviderContext context)
    {
        IEnumerable<string> names = Registered.Where(value => value.Name != "Mock").Select(value => value.Name);
        if (!string.IsNullOrWhiteSpace(context.PreferredSource)) names = names.Where(value => value.Equals(context.PreferredSource, StringComparison.OrdinalIgnoreCase));
        return names.Where(name => IsEnabled(name, context)).Select(Resolve).ToArray();
    }

    public IReadOnlyList<IMetadataProvider> OrderedMetadataProviders(MetadataProviderContext context)
    {
        return OrderedProviders(context).Select(value => value is MetadataProviderSdk adapter
            ? adapter.Provider
            : throw new InvalidOperationException($"Provider does not implement the legacy metadata adapter: {value.Descriptor.Name}"))
            .ToArray();
    }

    public bool IsEnabled(string provider, MetadataProviderContext context) => provider.ToUpperInvariant() switch {
        "MDC-NG" => context.MdcNg.Enabled,
        "METATUBE" => context.MetaTube.Enabled,
        "JAVBUS" => context.JavBus.Enabled,
        "MOCK" => true,
        _ => false,
    };

    public async Task<ProviderPlaygroundResult> ExecuteAsync(string provider, string code, MetadataProviderContext context,
        bool bypassCache, CancellationToken cancellationToken)
    {
        if (!providers.TryGetValue(provider, out IProviderSdk? sdk))
            return Failure(provider, code, ProviderFailureCategory.Unsupported, "Provider is not registered.", 0, []);
        string normalized = NormalizeCode(code);
        string cacheKey = $"{sdk.Descriptor.Name}:{normalized}";
        if (!bypassCache && cache.TryGetValue(cacheKey, out CacheEntry? existing) && existing.ExpiresAt > clock()) {
            ProviderPlaygroundResult hit = existing.Value with { Cache = new(true, cacheKey, existing.ExpiresAt.ToString("O")) };
            Record(hit);
            return hit;
        }

        var raw = new List<ProviderRawResponse>();
        var traces = new List<ProviderStageTrace>();
        MetadataProviderContext scoped = context with {
            PreferredSource = sdk.Descriptor.Name,
            ResponseCapture = (name, format, content, token) => {
                token.ThrowIfCancellationRequested();
                raw.Add(new(name, NormalizeFormat(format), ProviderSecretRedactor.Redact(content)));
                return Task.CompletedTask;
            },
        };
        var total = Stopwatch.StartNew();
        try {
            var stage = Stopwatch.StartNew();
            IReadOnlyList<MetadataSearchResult> results = await sdk.SearchAsync(normalized, scoped, cancellationToken);
            stage.Stop();
            traces.Add(new("Request", results.Count > 0 ? "Completed" : "EmptyResult", stage.ElapsedMilliseconds, $"Candidates={results.Count}"));
            if (results.Count == 0) {
                ProviderPlaygroundResult empty = Failure(provider, normalized, ProviderFailureCategory.EmptyResult, "Provider returned no search result.", total.ElapsedMilliseconds, traces, raw);
                Record(empty);
                return empty;
            }

            ProviderMetadata? metadata = null;
            stage.Restart();
            foreach (MetadataSearchResult result in results.Take(3)) {
                ProviderMetadata? candidate = await sdk.GetDetailAsync(result, scoped, cancellationToken);
                if (candidate is null) continue;
                if (movieNumberExtractor is null || movieNumberExtractor.AreEquivalent(normalized, candidate.Code, candidate.ExternalId)) { metadata = candidate; break; }
            }
            stage.Stop();
            traces.Add(new("Parser", metadata is null ? "NoMatch" : "Completed", stage.ElapsedMilliseconds));
            if (metadata is null) {
                ProviderPlaygroundResult noMatch = Failure(provider, normalized, ProviderFailureCategory.NoMatch, "No detail matched the requested number.", total.ElapsedMilliseconds, traces, raw);
                Record(noMatch);
                return noMatch;
            }
            traces.Add(new("ProviderResult", "Completed", total.ElapsedMilliseconds, $"Fields={CountFields(metadata)}"));
            IReadOnlyList<string> contributed = ContributedFields(metadata);
            traces.Add(new("Merge", "PreviewOnly", 0, $"Contributed={contributed.Count}"));
            traces.Add(new("Writer", "Skipped", 0, "Provider Playground is read-only."));
            total.Stop();
            var success = new ProviderPlaygroundResult(sdk.Descriptor.Name, normalized, ProviderHealthStatus.Available, 200,
                total.ElapsedMilliseconds, true, CountFields(metadata), sdk.Descriptor.Capabilities.Order().ToArray(), raw.ToArray(), metadata,
                new(contributed, metadata), traces, new(false, cacheKey, clock().Add(cacheTtl).ToString("O")));
            AddCache(cacheKey, success);
            Record(success);
            return success;
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
            total.Stop();
            ProviderFailureCategory category = ProviderFailureClassifier.Classify(error);
            traces.Add(new("Request", "Failed", total.ElapsedMilliseconds, ProviderSecretRedactor.Redact(error.Message)));
            traces.Add(new("Writer", "Skipped", 0, "Provider Playground is read-only."));
            ProviderPlaygroundResult failure = Failure(provider, normalized, category, error.Message, total.ElapsedMilliseconds, traces, raw);
            Record(failure);
            return failure;
        }
    }

    public IReadOnlyList<ProviderDashboardItem> Dashboard(MetadataProviderContext context) => Registered.Select(descriptor => {
        ProviderBenchmarkSnapshot benchmark = Snapshot(descriptor.Name);
        bool enabled = IsEnabled(descriptor.Name, context);
        ProviderHealthStatus status = !enabled ? ProviderHealthStatus.Unsupported
            : health.TryGetValue(descriptor.Name, out ProviderHealthStatus known) ? known
            : benchmark.RequestCount == 0 ? ProviderHealthStatus.Partial
            : benchmark.LastFailureCategory is null ? ProviderHealthStatus.Available
            : HealthFor(benchmark.LastFailureCategory.Value);
        return new ProviderDashboardItem(descriptor, enabled, status, benchmark);
    }).ToArray();

    public IReadOnlyList<ProviderPlaygroundResult> Recent(int limit = 20) => history.Reverse().Take(Math.Clamp(limit, 1, 100)).ToArray();

    public int ClearCache()
    {
        int count = cache.Count;
        cache.Clear();
        return count;
    }

    public void ReportHealth(string provider, ProviderHealthStatus status)
    {
        if (providers.ContainsKey(provider)) health[provider] = status;
    }

    private string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Movie number is required.", nameof(code));
        string? extracted = movieNumberExtractor?.Extract(code).NormalizedNumber;
        return extracted ?? JavBusCode.Normalize(code);
    }

    private void AddCache(string key, ProviderPlaygroundResult result)
    {
        if (cache.Count >= MaximumCacheEntries) {
            string? oldest = cache.OrderBy(value => value.Value.CreatedAt).Select(value => value.Key).FirstOrDefault();
            if (oldest is not null) cache.TryRemove(oldest, out _);
        }
        DateTimeOffset now = clock();
        cache[key] = new(result, now, now.Add(cacheTtl));
    }

    private void Record(ProviderPlaygroundResult result)
    {
        health[result.Provider] = result.Status;
        ProviderMetrics metric = metrics.GetOrAdd(result.Provider, _ => new());
        metric.Record(result);
        history.Enqueue(result);
        while (history.Count > MaximumHistoryEntries) history.TryDequeue(out _);
    }

    private ProviderBenchmarkSnapshot Snapshot(string provider) => metrics.TryGetValue(provider, out ProviderMetrics? value)
        ? value.Snapshot(provider)
        : new(provider, 0, 0, 0, 0, 0, 0, null, null, null, null);

    private static ProviderPlaygroundResult Failure(string provider, string code, ProviderFailureCategory category, string error,
        long elapsed, IReadOnlyList<ProviderStageTrace> traces, IReadOnlyList<ProviderRawResponse>? raw = null) =>
        new(provider, code, HealthFor(category), ProviderFailureClassifier.HttpStatus(error), elapsed, false, 0,
            ProviderCatalog.All.FirstOrDefault(value => value.Name.Equals(provider, StringComparison.OrdinalIgnoreCase))?.Capabilities.Order().ToArray() ?? [],
            raw ?? [], null, new([], null), traces, new(false, $"{provider}:{code}", null), category, ProviderSecretRedactor.Redact(error));

    private static ProviderHealthStatus HealthFor(ProviderFailureCategory category) => category switch {
        ProviderFailureCategory.Authentication => ProviderHealthStatus.AuthenticationRequired,
        ProviderFailureCategory.RateLimited => ProviderHealthStatus.RateLimited,
        ProviderFailureCategory.Configuration => ProviderHealthStatus.ConfigurationError,
        ProviderFailureCategory.Parser => ProviderHealthStatus.ParserBroken,
        ProviderFailureCategory.Unsupported => ProviderHealthStatus.Unsupported,
        ProviderFailureCategory.EmptyResult or ProviderFailureCategory.NoMatch or ProviderFailureCategory.NotFound => ProviderHealthStatus.Partial,
        _ => ProviderHealthStatus.Offline,
    };

    private static string NormalizeFormat(string format) => format.Trim().ToLowerInvariant() switch { "html" => "html", "xml" => "xml", _ => "json" };

    public static IReadOnlyList<string> ContributedFields(ProviderMetadata value)
    {
        var fields = new List<string> { "Code" };
        Add(fields, "Title", value.Title); Add(fields, "OriginalTitle", value.OriginalTitle); Add(fields, "Description", value.Description);
        Add(fields, "Director", value.Director); Add(fields, "Studio", value.Studio); Add(fields, "Publisher", value.Publisher);
        Add(fields, "Series", value.Series); Add(fields, "ReleaseDate", value.ReleaseDate); Add(fields, "SourceURL", value.WebUrl);
        if (value.DurationSeconds is > 0) fields.Add("Duration");
        if (value.Rating is > 0) fields.Add("Rating");
        if (value.Actors.Count > 0) fields.Add("Actors");
        if (value.Genres.Count > 0) fields.Add("Genres");
        fields.AddRange(value.Images.Select(image => MediaStoragePathResolver.NormalizeResourceType(image.Type)).Distinct(StringComparer.OrdinalIgnoreCase));
        return fields;
    }

    public static int CountFields(ProviderMetadata value) => ContributedFields(value).Count;
    private static void Add(List<string> fields, string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) fields.Add(name); }

    private sealed record CacheEntry(ProviderPlaygroundResult Value, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

    private sealed class ProviderMetrics
    {
        private readonly object gate = new();
        private long requests, successes, failures, parserSuccesses, elapsed;
        private string? lastRequestAt, lastSuccessAt, lastError;
        private ProviderFailureCategory? lastCategory;

        public void Record(ProviderPlaygroundResult result)
        {
            lock (gate) {
                requests++; elapsed += result.ElapsedMilliseconds; lastRequestAt = DateTimeOffset.UtcNow.ToString("O");
                if (result.ProviderResult is not null) { successes++; parserSuccesses++; lastSuccessAt = lastRequestAt; lastError = null; lastCategory = null; }
                else { failures++; lastError = result.Error; lastCategory = result.FailureCategory; }
            }
        }

        public ProviderBenchmarkSnapshot Snapshot(string provider)
        {
            lock (gate) return new(provider, requests, successes, failures,
                requests == 0 ? 0 : successes * 100d / requests,
                requests == 0 ? 0 : parserSuccesses * 100d / requests,
                requests == 0 ? 0 : elapsed / (double)requests,
                lastRequestAt, lastSuccessAt, lastError, lastCategory);
        }
    }
}

public sealed class ProviderPlaygroundService(ProviderManager manager, MetadataProviderSettingsService settings)
{
    public async Task<IReadOnlyList<ProviderDashboardItem>> DashboardAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return manager.Dashboard(await ContextAsync());
    }

    public async Task<ProviderPlaygroundResult> TestAsync(ProviderPlaygroundRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Provider)) throw new ArgumentException("Provider is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Code)) throw new ArgumentException("Movie number is required.", nameof(request));
        return await manager.ExecuteAsync(request.Provider, request.Code, await ContextAsync(), request.BypassCache, cancellationToken);
    }

    public IReadOnlyList<ProviderPlaygroundResult> Recent(int limit) => manager.Recent(limit);
    public object ClearCache() => new { clearedEntries = manager.ClearCache(), clearedAt = DateTimeOffset.UtcNow.ToString("O") };

    private async Task<MetadataProviderContext> ContextAsync() => new(
        await settings.ReadMetaTubeAsync(),
        await settings.ReadJavBusAsync(),
        Network: await settings.ReadNetworkAsync()) {
        MdcNg = await settings.ReadMdcNgAsync(),
    };
}

public static class ProviderFailureClassifier
{
    public static ProviderFailureCategory Classify(Exception error)
    {
        string value = $"{error.GetType().Name} {error.Message}";
        if (error is TaskCanceledException or TimeoutException || Contains(value, "timeout", "timed out")) return ProviderFailureCategory.Timeout;
        if (Contains(value, "cloudflare", "captcha", "challenge")) return ProviderFailureCategory.Cloudflare;
        if (Contains(value, "401", "403", "unauthorized", "forbidden", "authentication", "login")) return ProviderFailureCategory.Authentication;
        if (Contains(value, "429", "rate limit", "too many requests")) return ProviderFailureCategory.RateLimited;
        if (Contains(value, "404", "not found", "未找到")) return ProviderFailureCategory.NotFound;
        if (error is JsonException or InvalidDataException || Contains(value, "parse", "parser", "invalid json", "无法解析")) return ProviderFailureCategory.Parser;
        if (error is ArgumentException || Contains(value, "configuration", "not configured", "path mapping", "base url")) return ProviderFailureCategory.Configuration;
        if (error is NotSupportedException) return ProviderFailureCategory.Unsupported;
        if (error is HttpRequestException || Contains(value, "network", "connection", "dns", "socket")) return ProviderFailureCategory.Network;
        return ProviderFailureCategory.Unknown;
    }

    public static int? HttpStatus(string? value)
    {
        Match match = Regex.Match(value ?? "", @"\bHTTP\s*(?<status>\d{3})\b", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["status"].Value, out int status) ? status : null;
    }

    private static bool Contains(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

public static partial class ProviderSecretRedactor
{
    [GeneratedRegex("(?i)(authorization|cookie|password|token|api[-_]?key)([\\\"']?\\s*[:=]\\s*[\\\"']?)([^\\s,;\\\"']+)")]
    private static partial Regex SecretPattern();

    public static string Redact(string value)
    {
        string clean = SecretPattern().Replace(value ?? "", "$1$2<redacted>");
        return clean.Length <= 200_000 ? clean : clean[..200_000] + "\n<truncated>";
    }
}
