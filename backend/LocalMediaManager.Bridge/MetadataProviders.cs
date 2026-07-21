using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;

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

public sealed class MdcNgProvider : IMetadataProvider
{
    public string Name => "MDC-NG";

    public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        MdcNgSettingsDto mdc = settings.MdcNg;
        return Task.FromResult<IReadOnlyList<MetadataSearchResult>>(
            mdc.Enabled && !string.IsNullOrWhiteSpace(mdc.CommandPath) && File.Exists(mdc.CommandPath) && !string.IsNullOrWhiteSpace(settings.CurrentMoviePath)
                ? [new(Name, code, NormalizeCode(code), null)]
                : []);
    }

    public async Task<ProviderMetadata?> GetMetadataAsync(MetadataSearchResult result, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        MdcNgSettingsDto mdc = settings.MdcNg;
        if (string.IsNullOrWhiteSpace(settings.CurrentMoviePath)) return null;
        if (string.IsNullOrWhiteSpace(mdc.CommandPath) || !File.Exists(mdc.CommandPath))
            throw new FileNotFoundException("MDC-NG 外部命令未配置。", mdc.CommandPath);
        string root = Path.Combine(Path.GetTempPath(), "lmm-mdc-ng", Guid.NewGuid().ToString("N"));
        string input = Path.Combine(root, "input");
        string output = Path.Combine(root, "output");
        string work = Path.Combine(root, "work");
        Directory.CreateDirectory(input); Directory.CreateDirectory(output); Directory.CreateDirectory(work);
        await File.WriteAllTextAsync(Path.Combine(input, $"{NormalizeCode(result.Code)}.strm"), settings.CurrentMoviePath, cancellationToken);
        try {
            ProcessStartInfo startInfo = BuildStartInfo(mdc.CommandPath, settings.CurrentMoviePath, input, output, work);
            using var process = new Process {
                StartInfo = startInfo,
            };
            process.Start();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(mdc.TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try { await process.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("MDC-NG 外部命令执行超时。");
            }
            string outText = await stdout; string errText = await stderr;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"MDC-NG 外部命令失败：exit={process.ExitCode}; {TrimLog(errText)} {TrimLog(outText)}");
            string? nfo = Directory.EnumerateFiles(output, "*.nfo", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(work, "*.nfo", SearchOption.AllDirectories))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (nfo is null) throw new InvalidDataException("MDC-NG 未输出 NFO 文件。");
            return ParseNfo(nfo, result.Code);
        } finally {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    public Task<IReadOnlyList<MetadataImage>> GetImagesAsync(ProviderMetadata metadata, CancellationToken cancellationToken) =>
        Task.FromResult(metadata.Images);

    public Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        MdcNgSettingsDto mdc = settings.MdcNg;
        if (string.IsNullOrWhiteSpace(mdc.CommandPath)) return Task.FromResult(new ProviderConnectionResult(false, Name, "未配置 MDC-NG 外部命令。", watch.ElapsedMilliseconds));
        if (!File.Exists(mdc.CommandPath)) return Task.FromResult(new ProviderConnectionResult(false, Name, $"外部命令不存在：{mdc.CommandPath}", watch.ElapsedMilliseconds));
        return Task.FromResult(new ProviderConnectionResult(true, Name, $"外部命令已配置：{mdc.CommandPath}", watch.ElapsedMilliseconds));
    }

    private static ProviderMetadata ParseNfo(string path, string fallbackCode)
    {
        XDocument doc = XDocument.Load(path);
        XElement root = doc.Root ?? throw new InvalidDataException("NFO 根节点为空。");
        string? release = First(root, "premiered", "releasedate", "release", "date");
        int? runtime = int.TryParse(First(root, "runtime"), out int minutes) && minutes > 0 ? minutes * 60 : null;
        var images = new List<MetadataImage>();
        AddImage(images, "Poster", First(root, "poster", "thumb"));
        AddImage(images, "Fanart", First(root, "fanart", "background"));
        foreach (XElement thumb in root.Elements("thumb").Skip(1).Take(30)) AddImage(images, "Preview", thumb.Value);
        string code = First(root, "id", "num", "code") ?? fallbackCode;
        return new("MDC-NG", path, NormalizeCode(code), First(root, "title"), First(root, "plot", "outline", "desc"),
            First(root, "director"), First(root, "studio", "maker"), First(root, "publisher", "label"),
            First(root, "set", "series"), runtime, NormalizeDate(release), path,
            Values(root, "genre").Concat(Values(root, "tag")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), Values(root, "actor", "name"),
            images.DistinctBy(image => image.Url, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static ProcessStartInfo BuildStartInfo(string commandPath, string moviePath, string input, string output, string work)
    {
        string extension = Path.GetExtension(commandPath);
        bool isCommandScript = extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
        return new ProcessStartInfo {
            FileName = isCommandScript ? (Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe") : commandPath,
            Arguments = isCommandScript
                ? $"/d /c {Quote(commandPath)} {Quote(moviePath)} {Quote(input)} {Quote(output)} {Quote(work)}"
                : $"{Quote(moviePath)} {Quote(input)} {Quote(output)} {Quote(work)}",
            WorkingDirectory = Path.GetDirectoryName(commandPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
    }

    private static void AddImage(List<MetadataImage> images, string type, string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https") images.Add(new(type, uri.ToString()));
    }
    private static string? First(XElement root, params string[] names) => names.Select(name => root.Element(name)?.Value.Trim()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static IReadOnlyList<string> Values(XElement root, string element, string? child = null) =>
        root.Elements(element).Select(value => child is null ? value.Value : value.Element(child)?.Value).Select(value => value?.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string NormalizeCode(string value) => string.Join(' ', (value ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant().Replace('_', '-');
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string TrimLog(string value) {
        string text = string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return text.Length > 300 ? text[..300] : text;
    }
    private static string? NormalizeDate(string? value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime date) && date.Year > 1900 ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
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
        using HttpClient client = CreateClient(settings, context.NetworkSettings);
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
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LocalMediaManager", "0.6.3"));
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
    private static string? FirstUrl(JsonElement source, params string[] names) => names.Select(name => NullableString(source, name)).FirstOrDefault(IsHttpUrl);
    private static bool IsHttpUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https";
    private static string StripQuery(string value) { int index = value.IndexOfAny(['?', '#']); return index >= 0 ? value[..index] : value; }
    private static string? NormalizeDate(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime date) && date.Year > 1900
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }
}

public sealed class CompositeMetadataProvider(MdcNgProvider mdcNg, MetaTubeProvider metaTube, JavBusProvider javBus) : IMetadataProvider
{
    public string Name => "Metadata";

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string code, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.PreferredSource))
            return [new("Composite", code, code, null)];
        var providers = Ordered(settings).ToArray();
        if (providers.Length == 0)
            throw new InvalidOperationException("当前网络检测没有可用的元数据来源，本次同步已跳过不可达 Provider。");
        var results = new List<MetadataSearchResult>();
        foreach (IMetadataProvider provider in providers) {
            try {
                IReadOnlyList<MetadataSearchResult> found = await provider.SearchAsync(code, settings, cancellationToken);
                results.AddRange(found);
                if (found.Count > 0 && !string.IsNullOrWhiteSpace(settings.PreferredSource)) break;
            } catch (Exception error) when (error is not OperationCanceledException && string.IsNullOrWhiteSpace(settings.PreferredSource)) {
                results.Add(new($"__error:{provider.Name}", error.Message, code, null));
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
        IMetadataProvider provider = result.Provider.ToLowerInvariant() switch {
            "mdc-ng" => mdcNg,
            "javbus" => javBus,
            _ => metaTube,
        };
        if (result.Provider.Equals("Composite", StringComparison.OrdinalIgnoreCase))
            return await CompositeAsync(result.Code, settings, cancellationToken);
        if (provider == javBus || !settings.JavBus.Enabled || !string.IsNullOrWhiteSpace(settings.PreferredSource))
            return await provider.GetMetadataAsync(result, settings, cancellationToken);

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
        metadata.Provider.ToLowerInvariant() switch {
            "mdc-ng" => mdcNg.GetImagesAsync(metadata, cancellationToken),
            "javbus" => javBus.GetImagesAsync(metadata, cancellationToken),
            _ => metaTube.GetImagesAsync(metadata, cancellationToken),
        };

    public Task<ProviderConnectionResult> TestConnectionAsync(MetadataProviderContext settings, CancellationToken cancellationToken) =>
        metaTube.TestConnectionAsync(settings, cancellationToken);

    private IEnumerable<IMetadataProvider> Ordered(MetadataProviderContext settings)
    {
        if (settings.PreferredSource?.Equals("JavBus", StringComparison.OrdinalIgnoreCase) == true) {
            if (settings.JavBus.Enabled) yield return javBus;
            yield break;
        }
        if (settings.PreferredSource?.Equals("MDC-NG", StringComparison.OrdinalIgnoreCase) == true) {
            if (settings.MdcNg.Enabled) yield return mdcNg;
            yield break;
        }
        if (settings.PreferredSource?.Equals("MetaTube", StringComparison.OrdinalIgnoreCase) == true) {
            if (settings.MetaTube.Enabled) yield return metaTube;
            yield break;
        }
        var items = new List<(int Priority, IMetadataProvider Provider)>();
        if (settings.MdcNg.Enabled) items.Add((1, mdcNg));
        if (settings.MetaTube.Enabled) items.Add((2, metaTube));
        if (settings.JavBus.Enabled) items.Add((3, javBus));
        foreach ((_, IMetadataProvider provider) in items.OrderBy(item => item.Priority)) yield return provider;
    }

    private async Task<ProviderMetadata?> CompositeAsync(string code, MetadataProviderContext settings, CancellationToken cancellationToken)
    {
        ProviderMetadata? merged = null;
        foreach (IMetadataProvider source in Ordered(settings)) {
            IReadOnlyList<MetadataSearchResult> results;
            try { results = await source.SearchAsync(code, settings, cancellationToken); }
            catch (Exception error) when (error is not OperationCanceledException) { continue; }
            foreach (MetadataSearchResult result in results.Take(2)) {
                try {
                    ProviderMetadata? candidate = await source.GetMetadataAsync(result, settings, cancellationToken);
                    if (candidate is null) continue;
                    if (!JavBusCode.Normalize(candidate.Code).Equals(JavBusCode.Normalize(code), StringComparison.OrdinalIgnoreCase)) continue;
                    merged = merged is null ? candidate : Merge(merged, candidate);
                    if (!NeedsJavBusSupplement(merged)) return merged;
                    break;
                } catch (Exception error) when (error is not OperationCanceledException) { }
            }
        }
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
        || metadata.Images.Count == 0;

    private static ProviderMetadata Merge(ProviderMetadata primary, ProviderMetadata fallback) => primary with {
        Title = FirstText(primary.Title, fallback.Title),
        Description = FirstText(primary.Description, fallback.Description),
        Director = FirstText(primary.Director, fallback.Director),
        Studio = FirstText(primary.Studio, fallback.Studio),
        Publisher = FirstText(primary.Publisher, fallback.Publisher),
        Series = FirstText(primary.Series, fallback.Series),
        DurationSeconds = primary.DurationSeconds is > 0 ? primary.DurationSeconds : fallback.DurationSeconds,
        ReleaseDate = FirstText(primary.ReleaseDate, fallback.ReleaseDate),
        WebUrl = FirstText(primary.WebUrl, fallback.WebUrl),
        Genres = Union(primary.Genres, fallback.Genres),
        Actors = Union(primary.Actors, fallback.Actors),
        Images = primary.Images.Count > 0 ? primary.Images : fallback.Images,
    };

    private static string? FirstText(string? primary, string? fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static IReadOnlyList<string> Union(IReadOnlyList<string> primary, IReadOnlyList<string> fallback) =>
        primary.Concat(fallback).Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
        Exception? last = null;
        foreach (JavBusSettingsDto candidate in Candidates(settings)) {
            try {
                Uri uri = new(new Uri(candidate.BaseUrl), urlCode);
                using HttpClient client = CreateClient(candidate, context.NetworkSettings);
                string html = await GetHtmlAsync(client, uri, candidate, cancellationToken, notFoundAsEmpty: true);
                if (string.IsNullOrWhiteSpace(html)) continue;
                string parsedCode = JavBusParser.Code(html) ?? normalized;
                if (Comparable(parsedCode) == Comparable(normalized))
                    return [new("JavBus", uri.ToString(), parsedCode, JavBusParser.Title(html))];
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
        using HttpClient client = CreateClient(effective, context.NetworkSettings);
        string html = await GetHtmlAsync(client, uri, effective, cancellationToken);
        ProviderMetadata metadata = JavBusParser.Parse(html, uri.ToString(), result.Code);
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
