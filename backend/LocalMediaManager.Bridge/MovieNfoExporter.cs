namespace LocalMediaManager.Bridge;

// Coordinates SQLite-backed NFO export while leaving document safety and XML handling in NfoService.
public sealed class MovieNfoExporter(NfoService nfo)
{
    public Task<NfoPreview> PreviewAsync(long movieId, CancellationToken cancellationToken = default) =>
        nfo.PreviewExportAsync(movieId, cancellationToken);

    public Task<NfoMutationResult> ExportAsync(long movieId, string confirmationToken, bool separateWhenLocked,
        CancellationToken cancellationToken = default) =>
        nfo.ExportAsync(movieId, confirmationToken, separateWhenLocked, cancellationToken);
}
