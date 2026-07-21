using System.Diagnostics;
using System.Text.Json;

namespace LocalMediaManager.Bridge;

public sealed class MetadataSyncService(
    MetadataProviderSettingsService settings,
    MdcNgProvider mdcNg)
{
    public const string MdcNgProviderId = "mdc-ng";

    public async Task<MetadataSyncResult> SyncAsync(MetadataSyncRequest request, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        string providerId = NormalizeProviderId(request.ProviderId);
        try {
            if (string.IsNullOrWhiteSpace(request.Code))
                return Failure(providerId, watch, "INVALID_REQUEST", "Code is required.");
            if (providerId != MdcNgProviderId)
                return Failure(providerId, watch, "PROVIDER_UNSUPPORTED", $"Unsupported metadata provider: {providerId}.");

            MdcNgSettingsDto mdcSettings = await settings.ReadMdcNgAsync();
            if (string.IsNullOrWhiteSpace(request.ProviderId) && !mdcSettings.Enabled)
                return Failure(providerId, watch, "PROVIDER_DISABLED", "MDC-NG metadata provider is disabled.");

            MdcNgScrapeResult scrape = await mdcNg.ScrapeAsync(
                new MdcNgScrapeCommand(request.MediaPath ?? request.Code, request.Code, TimeoutSeconds: mdcSettings.TimeoutSeconds),
                mdcSettings,
                cancellationToken);
            watch.Stop();
            return new(
                scrape.Success && scrape.Metadata is not null,
                scrape.Metadata,
                providerId,
                watch.ElapsedMilliseconds,
                scrape.Success ? [] : [scrape.Message],
                scrape.Success ? null : "PROVIDER_FAILED",
                scrape.Success ? null : scrape.Message);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception error) when (error is HttpRequestException or TimeoutException or InvalidOperationException or JsonException) {
            watch.Stop();
            return Failure(providerId, watch, "PROVIDER_ERROR", error.Message);
        }
    }

    private static string NormalizeProviderId(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return MdcNgProviderId;
        string clean = providerId.Trim();
        return clean.Equals("MDC-NG", StringComparison.OrdinalIgnoreCase) ? MdcNgProviderId : clean.ToLowerInvariant();
    }

    private static MetadataSyncResult Failure(string providerId, Stopwatch watch, string code, string message)
    {
        watch.Stop();
        return new(false, null, providerId, watch.ElapsedMilliseconds, [message], code, message);
    }
}
