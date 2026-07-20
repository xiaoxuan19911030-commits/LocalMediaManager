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
    WikipediaJpActorProfileProvider wikipedia)
{
    public async Task<IReadOnlyList<ProviderDiagnosticResult>> ProbeAsync(CancellationToken cancellationToken)
    {
        MetadataProviderContext context = new(await settings.ReadMetaTubeAsync(), await settings.ReadJavBusAsync(), null,
            await settings.ReadDmmAsync(), await settings.ReadJavDbAsync());
        WebMetadataSettingsDto minnanoSettings = await settings.ReadMinnanoAsync();
        WebMetadataSettingsDto wikipediaSettings = await settings.ReadWikipediaJpAsync();
        var probes = new List<(string Scope, Func<Task<ProviderConnectionResult>> Run)> {
            ("影片搜索与资料", () => metaTube.TestConnectionAsync(context, cancellationToken)),
            ("影片搜索与资料", () => dmm.TestConnectionAsync(context, cancellationToken)),
            ("番号、演员和标签搜索", () => javDb.TestConnectionAsync(context, cancellationToken)),
            ("影片搜索与资料", () => javBus.TestConnectionAsync(context, cancellationToken)),
            ("生日、身高、罩杯", () => minnano.TestConnectionAsync(minnanoSettings, cancellationToken)),
            ("生日、出生地、活动时期和简介", () => wikipedia.TestConnectionAsync(wikipediaSettings, cancellationToken)),
        };
        var results = new List<ProviderDiagnosticResult>();
        foreach ((string scope, Func<Task<ProviderConnectionResult>> run) in probes) {
            ProviderConnectionResult result = await run();
            results.Add(new(result.Provider, result.Success, scope, result.Success ? "当前无需处理" : "检查代理、Cookie 或网络后重试",
                result.Message, DateTimeOffset.UtcNow.ToString("O"), result.ElapsedMilliseconds));
        }
        return results;
    }
}
