using System.Collections.Concurrent;

namespace LocalMediaManager.Bridge;

public sealed record ProviderDiagnosticResult(string Provider, bool Reachable, string Scope, string Recommendation,
    string Message, string TestedAt, long ElapsedMilliseconds, string Status = "Unavailable",
    string? LastSuccessfulAt = null);

public sealed class ProviderDiagnosticsService(
    MetadataProviderSettingsService settings,
    MdcNgProvider mdcNg,
    MetaTubeProvider metaTube,
    JavBusProvider javBus,
    bool enableNetworkFiltering = true,
    ProviderManager? providerManager = null) : BackgroundService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, ProviderDiagnosticResult> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private DateTimeOffset refreshedAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await RefreshAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception error) { Console.Error.WriteLine($"Provider diagnostics startup probe failed: {error.Message}"); }

        using var timer = new PeriodicTimer(CacheTtl);
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try { await RefreshAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { Console.Error.WriteLine($"Provider diagnostics refresh failed: {error.Message}"); }
        }
    }

    public async Task<IReadOnlyList<ProviderDiagnosticResult>> ProbeAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken, force: true);
        return Snapshot();
    }

    public async Task<MetadataProviderContext> FilterMovieProvidersAsync(MetadataProviderContext context, CancellationToken cancellationToken)
    {
        if (!enableNetworkFiltering)
            return context;

        if (cache.IsEmpty || DateTimeOffset.UtcNow - refreshedAt > CacheTtl)
            await RefreshAsync(cancellationToken, force: false);

        bool allowed(string provider, bool configured) =>
            configured && (!cache.TryGetValue(provider, out ProviderDiagnosticResult? result) || result.Reachable);

        bool mdcNgAllowed = allowed("MDC-NG", context.MdcNg.Enabled);
        string? mdcNgBlockReason = null;
        if (mdcNgAllowed && string.IsNullOrWhiteSpace(context.CurrentMoviePath)) {
            mdcNgAllowed = false;
            mdcNgBlockReason = "PathMappingMissing: 当前影片没有可供 MDC-NG 使用的媒体路径";
        }
        else if (mdcNgAllowed && Path.IsPathFullyQualified(context.CurrentMoviePath!)) {
            if (!File.Exists(context.CurrentMoviePath!)) {
                mdcNgAllowed = false;
                mdcNgBlockReason = "PathMappingMissing: 当前媒体离线；MDC-NG 路径任务已跳过";
            }
            else if (MdcNgPathMapper.Map(context.CurrentMoviePath!, context.MdcNg.PathMappings ?? []) is null) {
                mdcNgAllowed = false;
                mdcNgBlockReason = "PathMappingMissing: 当前媒体路径没有已确认的 MDC-NG 映射";
            }
        }
        if (mdcNgBlockReason is not null && context.ProviderLog is not null)
            await context.ProviderLog("MDC-NG", $"{mdcNgBlockReason}；MetaTube/JavBus 仍按番号继续。", cancellationToken);

        return context with {
            MdcNg = context.MdcNg with { Enabled = mdcNgAllowed },
            MetaTube = context.MetaTube with { Enabled = allowed("MetaTube", context.MetaTube.Enabled) },
            JavBus = context.JavBus with { Enabled = allowed("JavBus", context.JavBus.Enabled) },
        };
    }

    public Task ReportRuntimeFailureAsync(string provider, Exception error, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string message = error.Message;
        if (message.Contains("path mapping", StringComparison.OrdinalIgnoreCase)
            || message.Contains("media file", StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask;
        string status = ReadinessStatus(new(false, provider, message, 0));
        ProviderDiagnosticResult previous = cache.TryGetValue(provider, out ProviderDiagnosticResult? current)
            ? current
            : new(provider, false, "运行时请求", "当前会话将跳过该来源", message, DateTimeOffset.UtcNow.ToString("O"), 0);
        cache[provider] = previous with {
            Reachable = false,
            Status = status,
            Recommendation = status == "RateLimited" ? "来源正在限速；稍后手动重新检测" : "运行时请求失败；本次会话后续任务将跳过该来源",
            Message = message,
            TestedAt = DateTimeOffset.UtcNow.ToString("O"),
        };
        providerManager?.ReportHealth(provider, ToEngineStatus(status));
        refreshedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task ReportRuntimeSuccessAsync(string provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string now = DateTimeOffset.UtcNow.ToString("O");
        ProviderDiagnosticResult current = cache.TryGetValue(provider, out ProviderDiagnosticResult? existing)
            ? existing
            : new(provider, true, "运行时请求", "当前无需处理", "运行时请求成功", now, 0);
        cache[provider] = current with {
            Reachable = true,
            Status = provider.Equals("MDC-NG", StringComparison.OrdinalIgnoreCase) ? "Partial" : "Available",
            Recommendation = provider.Equals("MDC-NG", StringComparison.OrdinalIgnoreCase)
                ? "API 可达；MDC-NG 仅对已验证路径映射的在线媒体启用"
                : "当前无需处理",
            TestedAt = now,
            LastSuccessfulAt = now,
        };
        providerManager?.ReportHealth(provider, provider.Equals("MDC-NG", StringComparison.OrdinalIgnoreCase)
            ? ProviderHealthStatus.Partial : ProviderHealthStatus.Available);
        refreshedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    private IReadOnlyList<ProviderDiagnosticResult> Snapshot()
    {
        string[] order = ["MDC-NG", "MetaTube", "JavBus"];
        return order.Where(cache.ContainsKey).Select(provider => cache[provider]).ToArray();
    }

    private async Task RefreshAsync(CancellationToken cancellationToken, bool force = false)
    {
        await refreshLock.WaitAsync(cancellationToken);
        try {
            if (!force && !cache.IsEmpty && DateTimeOffset.UtcNow - refreshedAt < TimeSpan.FromSeconds(30))
                return;

            MetadataProviderContext context = new(await settings.ReadMetaTubeAsync(), await settings.ReadJavBusAsync(), null,
                await settings.ReadDmmAsync(), await settings.ReadJavDbAsync(), await settings.ReadNetworkAsync()) {
                MdcNg = await settings.ReadMdcNgAsync(),
            };

            var probes = new List<(string Scope, Func<Task<ProviderConnectionResult>> Run)> {
                ("影片资料 / 演员 / 标签 / 封面 / 预览图", () => mdcNg.TestConnectionAsync(context, cancellationToken)),
                ("影片搜索 / 演员 / 标签 / 图片", () => metaTube.TestConnectionAsync(context, cancellationToken)),
                ("影片标题 / 演员 / 导演 / 系列 / 标签 / 封面", () => javBus.TestConnectionAsync(context, cancellationToken)),
            };

            ProviderDiagnosticResult[] results = await Task.WhenAll(probes.Select(async probe => {
                ProviderConnectionResult result;
                try { result = await probe.Run(); }
                catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
                    result = new(false, ProviderNameFromScope(probe.Scope), $"Probe failed: {error.Message}", 0);
                }
                return ToDiagnostic(probe.Scope, result);
            }));

            foreach (ProviderDiagnosticResult result in results)
                cache[result.Provider] = result;
            refreshedAt = DateTimeOffset.UtcNow;
        } finally {
            refreshLock.Release();
        }
    }

    private ProviderDiagnosticResult ToDiagnostic(string scope, ProviderConnectionResult result)
    {
        string status = ReadinessStatus(result);
        string? lastSuccess = result.Success
            ? DateTimeOffset.UtcNow.ToString("O")
            : cache.TryGetValue(result.Provider, out ProviderDiagnosticResult? previous) ? previous.LastSuccessfulAt : null;
        string recommendation = status switch {
            "Available" => "当前无需处理",
            "Partial" => "API 可达；MDC-NG 仅对已验证路径映射的在线媒体启用",
            "AuthenticationRequired" => "需要更新认证信息；同步会暂时跳过该来源",
            "RateLimited" => "来源正在限速；稍后重试，本次会话不重复请求",
            "ConfigurationError" => "检查 Provider 地址和配置",
            _ => "当前不可用；正式同步会暂时跳过该来源",
        };
        providerManager?.ReportHealth(result.Provider, ToEngineStatus(status));
        return new(result.Provider, result.Success, scope, recommendation, result.Message,
            DateTimeOffset.UtcNow.ToString("O"), result.ElapsedMilliseconds, status, lastSuccess);
    }

    private static string ReadinessStatus(ProviderConnectionResult result)
    {
        if (result.Success)
            return result.Provider.Equals("MDC-NG", StringComparison.OrdinalIgnoreCase) ? "Partial" : "Available";
        if (result.Message.Contains("401", StringComparison.OrdinalIgnoreCase)
            || result.Message.Contains("403", StringComparison.OrdinalIgnoreCase)
            || result.Message.Contains("login", StringComparison.OrdinalIgnoreCase)) return "AuthenticationRequired";
        if (result.Message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || result.Message.Contains("rate", StringComparison.OrdinalIgnoreCase)) return "RateLimited";
        if (result.Message.Contains("URL", StringComparison.OrdinalIgnoreCase)
            || result.Message.Contains("配置", StringComparison.OrdinalIgnoreCase)) return "ConfigurationError";
        return "Unavailable";
    }

    private static string ProviderNameFromScope(string scope) =>
        scope.Contains("预览图", StringComparison.OrdinalIgnoreCase) ? "MDC-NG"
        : scope.Contains("导演", StringComparison.OrdinalIgnoreCase) ? "JavBus"
        : "MetaTube";

    private static ProviderHealthStatus ToEngineStatus(string status) => status switch {
        "Available" => ProviderHealthStatus.Available,
        "Partial" => ProviderHealthStatus.Partial,
        "AuthenticationRequired" => ProviderHealthStatus.AuthenticationRequired,
        "RateLimited" => ProviderHealthStatus.RateLimited,
        "ConfigurationError" or "PathMappingMissing" => ProviderHealthStatus.ConfigurationError,
        "ParserBroken" => ProviderHealthStatus.ParserBroken,
        "Unsupported" => ProviderHealthStatus.Unsupported,
        _ => ProviderHealthStatus.Offline,
    };
}
