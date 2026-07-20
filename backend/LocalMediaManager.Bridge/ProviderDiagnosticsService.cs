using System.Collections.Concurrent;

namespace LocalMediaManager.Bridge;

public sealed record ProviderDiagnosticResult(string Provider, bool Reachable, string Scope, string Recommendation,
    string Message, string TestedAt, long ElapsedMilliseconds);

public sealed class ProviderDiagnosticsService(
    MetadataProviderSettingsService settings,
    MetaTubeProvider metaTube,
    JavBusProvider javBus,
    DmmProvider dmm,
    JavDbProvider javDb,
    MinnanoActorProfileProvider minnano,
    WikipediaJpActorProfileProvider wikipedia,
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
            MetaTube = context.MetaTube with { Enabled = allowed("MetaTube", configured: true) },
            JavBus = context.JavBus with { Enabled = allowed("JavBus", configured: true) },
            Dmm = (context.Dmm ?? SettingsDefaults.Dmm) with { Enabled = allowed("DMM", (context.Dmm ?? SettingsDefaults.Dmm).Enabled) },
            JavDb = (context.JavDb ?? SettingsDefaults.JavDb) with { Enabled = allowed("JavDB", (context.JavDb ?? SettingsDefaults.JavDb).Enabled) },
        };
    }

    private IReadOnlyList<ProviderDiagnosticResult> Snapshot()
    {
        string[] order = ["MetaTube", "DMM", "JavDB", "JavBus", "Minnano", "Wikipedia JP"];
        return order.Where(cache.ContainsKey).Select(provider => cache[provider]).ToArray();
    }

    private async Task RefreshAsync(CancellationToken cancellationToken, bool force = false)
    {
        await refreshLock.WaitAsync(cancellationToken);
        try {
            if (!force && !cache.IsEmpty && DateTimeOffset.UtcNow - refreshedAt < TimeSpan.FromSeconds(30))
                return;

            MetadataProviderContext context = new(await settings.ReadMetaTubeAsync(), await settings.ReadJavBusAsync(), null,
                await settings.ReadDmmAsync(), await settings.ReadJavDbAsync(), await settings.ReadNetworkAsync());
            WebMetadataSettingsDto minnanoSettings = await settings.ReadMinnanoAsync();
            WebMetadataSettingsDto wikipediaSettings = await settings.ReadWikipediaJpAsync();

            var probes = new List<(string Scope, Func<Task<ProviderConnectionResult>> Run)> {
                ("影片搜索 / 演员 / 标签 / 图片", () => metaTube.TestConnectionAsync(context, cancellationToken)),
                ("影片搜索 / 演员 / 标签 / 封面", () => dmm.TestConnectionAsync(context, cancellationToken)),
                ("番号 / 演员 / 标签搜索", () => javDb.TestConnectionAsync(context, cancellationToken)),
                ("影片标题 / 演员 / 导演 / 系列 / 标签 / 封面", () => javBus.TestConnectionAsync(context, cancellationToken)),
                ("生日 / 身高 / 罩杯", () => minnano.TestConnectionAsync(minnanoSettings, cancellationToken)),
                ("生日 / 出生地 / 活动时期 / 简介", () => wikipedia.TestConnectionAsync(wikipediaSettings, cancellationToken)),
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
            result.Success ? "当前无需处理" : "当前网络不可达；自动同步会暂时跳过该来源",
            result.Message, DateTimeOffset.UtcNow.ToString("O"), result.ElapsedMilliseconds);

    private static string ProviderNameFromScope(string scope) =>
        scope.Contains("出生地", StringComparison.OrdinalIgnoreCase) ? "Wikipedia JP"
        : scope.Contains("身高", StringComparison.OrdinalIgnoreCase) ? "Minnano"
        : scope.Contains("番号", StringComparison.OrdinalIgnoreCase) ? "JavDB"
        : scope.Contains("导演", StringComparison.OrdinalIgnoreCase) ? "JavBus"
        : scope.Contains("封面", StringComparison.OrdinalIgnoreCase) ? "DMM"
        : "MetaTube";
}
