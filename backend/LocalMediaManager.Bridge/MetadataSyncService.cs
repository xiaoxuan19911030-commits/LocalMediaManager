using System.Diagnostics;
using System.Text.Json;

namespace LocalMediaManager.Bridge;

public sealed class MetadataSyncService(
    MetadataProviderSettingsService settings,
    MdcNgProvider mdcNg,
    MovieMetadataImporter? importer = null,
    MovieImageImporter? imageImporter = null)
{
    public const string MdcNgProviderId = "mdc-ng";

    public async Task<MetadataSyncResult> SyncMovieAsync(long movieId, string? providerId, bool overwrite,
        CancellationToken cancellationToken)
    {
        if (importer is null) throw new InvalidOperationException("MovieMetadataImporter is not configured.");
        SyncMovie movie = await importer.ReadMovieAsync(movieId, cancellationToken);
        MetadataSyncResult result = await SyncAsync(new(movie.Code, movie.PrimaryFile, providerId, movieId, overwrite), cancellationToken);
        return result;
    }

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
            if (!scrape.Success || scrape.Metadata is null)
                return new(false, scrape.Metadata, providerId, watch.ElapsedMilliseconds, [scrape.Message],
                    "PROVIDER_FAILED", scrape.Message);

            MovieMetadataImportResult? import = null;
            MovieImageImportResult? images = null;
            if (request.MovieId.HasValue) {
                if (importer is null) throw new InvalidOperationException("MovieMetadataImporter is not configured.");
                SyncMovie movie = await importer.ReadMovieAsync(request.MovieId.Value, cancellationToken);
                if (imageImporter is not null) {
                    MdcNgSettingsDto imageSettings = await settings.ReadMdcNgAsync();
                    images = await imageImporter.PrepareAsync(movie, scrape.Metadata, imageSettings.TimeoutSeconds, request.Overwrite, cancellationToken);
                }
                import = await importer.ImportAsync(request.MovieId.Value, scrape.Metadata, request.Overwrite, images?.PreparedFiles, cancellationToken);
                if (imageImporter is not null) {
                    MdcNgSettingsDto imageSettings = await settings.ReadMdcNgAsync();
                    MovieImageImportResult actorImages = await imageImporter.ImportActorImagesAsync(scrape.Metadata, imageSettings.TimeoutSeconds, request.Overwrite, cancellationToken);
                    images = images is null
                        ? actorImages
                        : images with {
                            ActorImagesDownloaded = actorImages.ActorImagesDownloaded,
                            Warnings = images.Warnings.Concat(actorImages.Warnings).ToArray()
                        };
                }
            }
            return new(
                scrape.Success && scrape.Metadata is not null,
                scrape.Metadata,
                providerId,
                watch.ElapsedMilliseconds,
                [],
                null,
                null,
                import?.TaskId,
                import?.AppliedJson,
                images is null ? null : new(images.MovieImagesDownloaded, images.ActorImagesDownloaded, images.Warnings));
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
