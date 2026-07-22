using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record MetaTubeSettingsDto(
    bool Enabled,
    string BaseUrl,
    int TimeoutSeconds,
    bool DownloadImages,
    bool WriteNfo,
    bool AutoExecute,
    bool NonDestructive);

public sealed record MdcNgSettingsDto(
    bool Enabled,
    string ServiceUrl,
    string ApiUrl,
    int TimeoutSeconds,
    string ApiKey = "",
    bool DownloadImages = true,
    IReadOnlyList<MdcNgPathMappingDto>? PathMappings = null);
public sealed record MdcNgPathMappingDto(string LocalPathPrefix, string ProviderPathPrefix, bool Enabled = true, int Order = 0);

public sealed record JavBusSettingsDto(
    bool Enabled,
    int Priority,
    string BaseUrl,
    int TimeoutSeconds,
    int RetryCount,
    string Cookie,
    bool DownloadImages,
    bool FillMissingOnly,
    IReadOnlyList<string>? MirrorUrls = null);

public sealed record WebMetadataSettingsDto(
    bool Enabled, int Priority, string BaseUrl, int TimeoutSeconds, int RetryCount,
    string Cookie, bool DownloadImages, bool FillMissingOnly, IReadOnlyList<string>? MirrorUrls = null);

public sealed record ProviderNetworkSettingsDto(string ProxyMode, string ProxyUrl, string Username = "", string Password = "")
{
    public static ProviderNetworkSettingsDto Default => new("System", "");
}

public sealed record MetadataProviderContext(MetaTubeSettingsDto MetaTube, JavBusSettingsDto JavBus, string? PreferredSource = null,
    WebMetadataSettingsDto? Dmm = null, WebMetadataSettingsDto? JavDb = null, ProviderNetworkSettingsDto? Network = null)
{
    public MdcNgSettingsDto MdcNg { get; init; } = SettingsDefaults.MdcNg;
    public string? CurrentMoviePath { get; init; }
    public Func<string, string, CancellationToken, Task>? ProviderLog { get; init; }
    public Func<string, string, CancellationToken, Task>? ProviderDebugLog { get; init; }
    public ProviderNetworkSettingsDto NetworkSettings => Network ?? ProviderNetworkSettingsDto.Default;

    public int TimeoutSeconds(string provider) =>
        provider.Equals("JavBus", StringComparison.OrdinalIgnoreCase) ? JavBus.TimeoutSeconds
        : provider.Equals("MDC-NG", StringComparison.OrdinalIgnoreCase) ? MdcNg.TimeoutSeconds
        : provider.Equals("DMM", StringComparison.OrdinalIgnoreCase) ? (Dmm ?? SettingsDefaults.Dmm).TimeoutSeconds
        : provider.Equals("JavDB", StringComparison.OrdinalIgnoreCase) ? (JavDb ?? SettingsDefaults.JavDb).TimeoutSeconds
        : MetaTube.TimeoutSeconds;

    public bool DownloadImages(string provider) =>
        provider.Equals("JavBus", StringComparison.OrdinalIgnoreCase) ? JavBus.DownloadImages
        : provider.Equals("MDC-NG", StringComparison.OrdinalIgnoreCase) ? MdcNg.DownloadImages
        : provider.Equals("DMM", StringComparison.OrdinalIgnoreCase) ? (Dmm ?? SettingsDefaults.Dmm).DownloadImages
        : provider.Equals("JavDB", StringComparison.OrdinalIgnoreCase) ? (JavDb ?? SettingsDefaults.JavDb).DownloadImages
        : MetaTube.DownloadImages;
}

public sealed record ProviderConnectionResult(bool Success, string Provider, string Message, long ElapsedMilliseconds);

public sealed class MetadataProviderSettingsService(string databasePath)
{
    public async Task<MetaTubeSettingsDto> ReadMetaTubeAsync()
    {
        MetaTubeSettingsDto defaults = SettingsDefaults.Unified.MetaTube;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE 'metadata.metatube.%'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values[reader.GetString(0)] = reader.GetString(1);
        return new(
            Bool(values, "metadata.metatube.enabled", defaults.Enabled),
            NormalizeBaseUrl(Text(values, "metadata.metatube.baseUrl", defaults.BaseUrl)),
            Math.Clamp(Int(values, "metadata.metatube.timeoutSeconds", defaults.TimeoutSeconds), 15, 180),
            Bool(values, "metadata.metatube.downloadImages", defaults.DownloadImages),
            true,
            Bool(values, "metadata.metatube.autoExecute", defaults.AutoExecute),
            Bool(values, "metadata.metatube.nonDestructive", defaults.NonDestructive));
    }

    public async Task<JavBusSettingsDto> ReadJavBusAsync()
    {
        JavBusSettingsDto defaults = SettingsDefaults.JavBus;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE 'metadata.javbus.%'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values[reader.GetString(0)] = reader.GetString(1);
        return new(
            Bool(values, "metadata.javbus.enabled", defaults.Enabled),
            Math.Clamp(Int(values, "metadata.javbus.priority", defaults.Priority), 1, 99),
            NormalizeBaseUrl(Text(values, "metadata.javbus.baseUrl", defaults.BaseUrl)),
            Math.Clamp(Int(values, "metadata.javbus.timeoutSeconds", defaults.TimeoutSeconds), 10, 180),
            Math.Clamp(Int(values, "metadata.javbus.retryCount", defaults.RetryCount), 0, 3),
            Text(values, "metadata.javbus.cookie", defaults.Cookie),
            Bool(values, "metadata.javbus.downloadImages", defaults.DownloadImages),
            true,
            UrlList(values, "metadata.javbus.mirrorUrls", defaults.MirrorUrls));
    }

    public async Task<MdcNgSettingsDto> ReadMdcNgAsync()
    {
        MdcNgSettingsDto defaults = SettingsDefaults.MdcNg;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE 'metadata.mdcNg.%'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values[reader.GetString(0)] = reader.GetString(1);
        return NormalizeMdcNg(new(
            Bool(values, "metadata.mdcNg.enabled", defaults.Enabled),
            Text(values, "metadata.mdcNg.serviceUrl", defaults.ServiceUrl),
            Text(values, "metadata.mdcNg.apiUrl", defaults.ApiUrl),
            Int(values, "metadata.mdcNg.timeoutSeconds", defaults.TimeoutSeconds),
            Text(values, "metadata.mdcNg.apiKey", defaults.ApiKey),
            Bool(values, "metadata.mdcNg.downloadImages", defaults.DownloadImages),
            JsonValue(values, "metadata.mdcNg.pathMappings", defaults.PathMappings ?? Array.Empty<MdcNgPathMappingDto>())));
    }

    public async Task<ProviderNetworkSettingsDto> ReadNetworkAsync()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE 'metadata.network.%'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values[reader.GetString(0)] = reader.GetString(1);
        return NormalizeNetwork(new(
            Text(values, "metadata.network.proxyMode", ProviderNetworkSettingsDto.Default.ProxyMode),
            Text(values, "metadata.network.proxyUrl", ProviderNetworkSettingsDto.Default.ProxyUrl),
            Text(values, "metadata.network.username", ""),
            Text(values, "metadata.network.password", "")));
    }

    public Task<WebMetadataSettingsDto> ReadDmmAsync() => ReadWebAsync("dmm", SettingsDefaults.Dmm);
    public Task<WebMetadataSettingsDto> ReadJavDbAsync() => ReadWebAsync("javdb", SettingsDefaults.JavDb);
    public Task<WebMetadataSettingsDto> ReadMinnanoAsync() => ReadWebAsync("minnano", SettingsDefaults.Minnano);
    public Task<WebMetadataSettingsDto> ReadWikipediaJpAsync() => ReadWebAsync("wikipediaJp", SettingsDefaults.WikipediaJp);

    public async Task<MetaTubeSettingsDto> SaveMetaTubeAsync(MetaTubeSettingsDto input)
    {
        if (!Uri.TryCreate(input.BaseUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("MetaTube 地址必须是有效的 HTTP 或 HTTPS URL。");
        var clean = input with {
            BaseUrl = NormalizeBaseUrl(uri.ToString()),
            TimeoutSeconds = Math.Clamp(input.TimeoutSeconds, 15, 180),
            WriteNfo = true,
            NonDestructive = true,
        };
        await using var connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        await using var transaction = await connection.BeginTransactionAsync();
        await StoreAsync(connection, transaction, "metadata.metatube.enabled", clean.Enabled, "boolean");
        await StoreAsync(connection, transaction, "metadata.metatube.baseUrl", clean.BaseUrl, "string");
        await StoreAsync(connection, transaction, "metadata.metatube.timeoutSeconds", clean.TimeoutSeconds, "integer");
        await StoreAsync(connection, transaction, "metadata.metatube.downloadImages", clean.DownloadImages, "boolean");
        await StoreAsync(connection, transaction, "metadata.metatube.writeNfo", clean.WriteNfo, "boolean");
        await StoreAsync(connection, transaction, "metadata.metatube.autoExecute", clean.AutoExecute, "boolean");
        await StoreAsync(connection, transaction, "metadata.metatube.nonDestructive", true, "boolean");
        await transaction.CommitAsync();
        return clean;
    }

    public async Task<JavBusSettingsDto> SaveJavBusAsync(JavBusSettingsDto input)
    {
        JavBusSettingsDto clean = NormalizeJavBus(input);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadWrite);
        await using var transaction = await connection.BeginTransactionAsync();
        await StoreJavBusAsync(connection, transaction, clean);
        await transaction.CommitAsync();
        return clean;
    }

    public static JavBusSettingsDto NormalizeJavBus(JavBusSettingsDto input)
    {
        if (!Uri.TryCreate(input.BaseUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("JavBus 地址必须是有效的 HTTP 或 HTTPS URL。");
        return input with {
            Priority = Math.Clamp(input.Priority, 1, 99),
            BaseUrl = NormalizeBaseUrl(uri.ToString()),
            TimeoutSeconds = Math.Clamp(input.TimeoutSeconds, 10, 180),
            RetryCount = Math.Clamp(input.RetryCount, 0, 3),
            Cookie = input.Cookie?.Trim() ?? "",
            FillMissingOnly = true,
            MirrorUrls = NormalizeUrlList(input.MirrorUrls ?? []),
        };
    }

    public static MdcNgSettingsDto NormalizeMdcNg(MdcNgSettingsDto input)
    {
        string serviceUrl = NormalizeMdcNgServiceUrl(input.ServiceUrl);
        string apiUrl = NormalizeOptionalHttpUrl(input.ApiUrl, SettingsDefaults.MdcNg.ApiUrl, "MDC-NG API 地址");
        return input with {
            ServiceUrl = serviceUrl,
            ApiUrl = apiUrl,
            TimeoutSeconds = Math.Clamp(input.TimeoutSeconds, 10, 600),
            ApiKey = input.ApiKey?.Trim() ?? "",
            DownloadImages = input.DownloadImages,
            PathMappings = MdcNgPathMapper.Normalize(input.PathMappings ?? []),
        };
    }

    private static string NormalizeOptionalHttpUrl(string? value, string fallback, string label)
    {
        string text = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text)) text = fallback;
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException($"{label}必须是有效的 HTTP 或 HTTPS URL。");
        return NormalizeBaseUrl(uri.ToString());
    }

    private static string NormalizeMdcNgServiceUrl(string? value)
    {
        return NormalizeOptionalHttpUrl(value, SettingsDefaults.MdcNg.ServiceUrl, "MDC-NG 服务地址");
    }

    public static async Task StoreMdcNgAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, MdcNgSettingsDto clean)
    {
        await StoreAsync(connection, transaction, "metadata.mdcNg.enabled", clean.Enabled, "boolean");
        await StoreAsync(connection, transaction, "metadata.mdcNg.serviceUrl", clean.ServiceUrl, "string");
        await StoreAsync(connection, transaction, "metadata.mdcNg.apiUrl", clean.ApiUrl, "string");
        await StoreAsync(connection, transaction, "metadata.mdcNg.timeoutSeconds", clean.TimeoutSeconds, "integer");
        await StoreAsync(connection, transaction, "metadata.mdcNg.apiKey", clean.ApiKey, "secret");
        await StoreAsync(connection, transaction, "metadata.mdcNg.downloadImages", clean.DownloadImages, "boolean");
        await StoreAsync(connection, transaction, "metadata.mdcNg.pathMappings", clean.PathMappings ?? [], "json");
    }

    public static async Task StoreJavBusAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, JavBusSettingsDto clean)
    {
        await StoreAsync(connection, transaction, "metadata.javbus.enabled", clean.Enabled, "boolean");
        await StoreAsync(connection, transaction, "metadata.javbus.priority", clean.Priority, "integer");
        await StoreAsync(connection, transaction, "metadata.javbus.baseUrl", clean.BaseUrl, "string");
        await StoreAsync(connection, transaction, "metadata.javbus.timeoutSeconds", clean.TimeoutSeconds, "integer");
        await StoreAsync(connection, transaction, "metadata.javbus.retryCount", clean.RetryCount, "integer");
        await StoreAsync(connection, transaction, "metadata.javbus.cookie", clean.Cookie, "secret");
        await StoreAsync(connection, transaction, "metadata.javbus.downloadImages", clean.DownloadImages, "boolean");
        await StoreAsync(connection, transaction, "metadata.javbus.fillMissingOnly", true, "boolean");
        await StoreAsync(connection, transaction, "metadata.javbus.mirrorUrls", clean.MirrorUrls ?? [], "json");
    }

    public static WebMetadataSettingsDto NormalizeWeb(WebMetadataSettingsDto input, string provider)
    {
        if (!Uri.TryCreate(input.BaseUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException($"{provider} 地址必须是有效的 HTTP 或 HTTPS URL。");
        return input with {
            Priority = Math.Clamp(input.Priority, 1, 99),
            BaseUrl = NormalizeBaseUrl(uri.ToString()),
            TimeoutSeconds = Math.Clamp(input.TimeoutSeconds, 10, 180),
            RetryCount = Math.Clamp(input.RetryCount, 0, 3),
            Cookie = input.Cookie?.Trim() ?? "",
            FillMissingOnly = true,
            MirrorUrls = NormalizeUrlList(input.MirrorUrls ?? []),
        };
    }

    public static ProviderNetworkSettingsDto NormalizeNetwork(ProviderNetworkSettingsDto input)
    {
        string mode = input.ProxyMode?.Trim().ToLowerInvariant() switch {
            "direct" => "Direct",
            "manual" => "Manual",
            _ => "System",
        };
        string proxyUrl = (input.ProxyUrl ?? "").Trim();
        if (mode == "Manual" && (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out Uri? proxy) || proxy.Scheme is not ("http" or "https")))
            throw new ArgumentException("手动代理地址必须是有效的 http 或 https URL。");
        return new(mode, mode == "Manual" ? proxyUrl : "", input.Username?.Trim() ?? "", input.Password?.Trim() ?? "");
    }

    public static async Task StoreWebAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction,
        string id, WebMetadataSettingsDto clean)
    {
        string prefix = $"metadata.{id}.";
        await StoreAsync(connection, transaction, prefix + "enabled", clean.Enabled, "boolean");
        await StoreAsync(connection, transaction, prefix + "priority", clean.Priority, "integer");
        await StoreAsync(connection, transaction, prefix + "baseUrl", clean.BaseUrl, "string");
        await StoreAsync(connection, transaction, prefix + "timeoutSeconds", clean.TimeoutSeconds, "integer");
        await StoreAsync(connection, transaction, prefix + "retryCount", clean.RetryCount, "integer");
        await StoreAsync(connection, transaction, prefix + "cookie", clean.Cookie, "secret");
        await StoreAsync(connection, transaction, prefix + "downloadImages", clean.DownloadImages, "boolean");
        await StoreAsync(connection, transaction, prefix + "fillMissingOnly", true, "boolean");
        await StoreAsync(connection, transaction, prefix + "mirrorUrls", clean.MirrorUrls ?? [], "json");
    }

    public static async Task StoreNetworkAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction,
        ProviderNetworkSettingsDto clean)
    {
        await StoreAsync(connection, transaction, "metadata.network.proxyMode", clean.ProxyMode, "string");
        await StoreAsync(connection, transaction, "metadata.network.proxyUrl", clean.ProxyUrl, "string");
        await StoreAsync(connection, transaction, "metadata.network.username", clean.Username, "secret");
        await StoreAsync(connection, transaction, "metadata.network.password", clean.Password, "secret");
    }

    public static async Task StoreAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction,
        string key, object value, string type)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO AppSettings(Key,ValueJson,ValueType,UpdatedAt) VALUES($key,$value,$type,$at)
            ON CONFLICT(Key) DO UPDATE SET ValueJson=excluded.ValueJson,ValueType=excluded.ValueType,UpdatedAt=excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value));
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenAsync(SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = databasePath, Mode = mode, Cache = SqliteCacheMode.Shared,
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private async Task<WebMetadataSettingsDto> ReadWebAsync(string id, WebMetadataSettingsDto defaults)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await OpenAsync(SqliteOpenMode.ReadOnly);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key,ValueJson FROM AppSettings WHERE Key LIKE $prefix";
        command.Parameters.AddWithValue("$prefix", $"metadata.{id}.%");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values[reader.GetString(0)] = reader.GetString(1);
        string key = $"metadata.{id}.";
        return new(Bool(values, key + "enabled", defaults.Enabled), Math.Clamp(Int(values, key + "priority", defaults.Priority), 1, 99),
            NormalizeBaseUrl(Text(values, key + "baseUrl", defaults.BaseUrl)), Math.Clamp(Int(values, key + "timeoutSeconds", defaults.TimeoutSeconds), 10, 180),
            Math.Clamp(Int(values, key + "retryCount", defaults.RetryCount), 0, 3), Text(values, key + "cookie", defaults.Cookie),
            Bool(values, key + "downloadImages", defaults.DownloadImages), true, UrlList(values, key + "mirrorUrls", defaults.MirrorUrls));
    }

    private static string NormalizeBaseUrl(string value) => value.Trim().TrimEnd('/') + "/";
    private static IReadOnlyList<string> NormalizeUrlList(IEnumerable<string> values) =>
        values.Select(value => (value ?? "").Trim()).Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" ? NormalizeBaseUrl(uri.ToString()) : "")
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    private static IReadOnlyList<string> UrlList(IReadOnlyDictionary<string, string> values, string key, IReadOnlyList<string>? fallback)
    {
        if (!values.TryGetValue(key, out string? raw)) return fallback ?? [];
        try { return NormalizeUrlList(JsonSerializer.Deserialize<string[]>(raw) ?? []); } catch (JsonException) { return fallback ?? []; }
    }
    private static bool Bool(IReadOnlyDictionary<string, string> values, string key, bool fallback) =>
        values.TryGetValue(key, out string? raw) && JsonSerializer.Deserialize<bool>(raw) is bool value ? value : fallback;
    private static int Int(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out string? raw) && int.TryParse(raw, out int value) ? value : fallback;
    private static string Text(IReadOnlyDictionary<string, string> values, string key, string fallback) {
        if (!values.TryGetValue(key, out string? raw)) return fallback;
        try { return JsonSerializer.Deserialize<string>(raw) ?? fallback; } catch (JsonException) { return fallback; }
    }
    private static IReadOnlyList<T> JsonValue<T>(IReadOnlyDictionary<string, string> values, string key, IReadOnlyList<T> fallback) {
        if (!values.TryGetValue(key, out string? raw)) return fallback;
        try { return JsonSerializer.Deserialize<T[]>(raw) ?? fallback; } catch (JsonException) { return fallback; }
    }
}
