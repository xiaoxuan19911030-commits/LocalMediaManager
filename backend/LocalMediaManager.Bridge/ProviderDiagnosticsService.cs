using System.Collections.Concurrent;

namespace LocalMediaManager.Bridge;

public sealed record ProviderDiagnosticResult(string Provider, bool Reachable, string Scope, string Recommendation,
    string Message, string TestedAt, long ElapsedMilliseconds);

public sealed class ProviderDiagnosticsService(
    MetadataProviderSettingsService settings,
    MdcNgProvider mdcNg,
    MetaTubeProvider metaTube,
    JavBusProvider javBus,
    bool enableNetworkFiltering = true) : BackgroundService
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

        return context with {
            MdcNg = context.MdcNg with { Enabled = allowed("MDC-NG", context.MdcNg.Enabled) },
            MetaTube = context.MetaTube with { Enabled = allowed("MetaTube", context.MetaTube.Enabled) },
            JavBus = context.JavBus with { Enabled = allowed("JavBus", context.JavBus.Enabled) },
        };
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

    private static ProviderDiagnosticResult ToDiagnostic(string scope, ProviderConnectionResult result) =>
        new(result.Provider, result.Success, scope,
            result.Success ? "当前无需处理" : "当前不可用；正式同步会暂时跳过该来源",
            result.Message, DateTimeOffset.UtcNow.ToString("O"), result.ElapsedMilliseconds);

    private static string ProviderNameFromScope(string scope) =>
        scope.Contains("预览图", StringComparison.OrdinalIgnoreCase) ? "MDC-NG"
        : scope.Contains("导演", StringComparison.OrdinalIgnoreCase) ? "JavBus"
        : "MetaTube";
}
