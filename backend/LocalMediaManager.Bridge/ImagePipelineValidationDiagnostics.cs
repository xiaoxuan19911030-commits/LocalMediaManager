using System.Security.Cryptography;
using SkiaSharp;

namespace LocalMediaManager.Bridge;

// Enabled only by the isolated validation harness. Normal Bridge processes always use Full.
public enum ImagePipelineValidationMode
{
    Full,
    DownloadAndDrainOnly,
    DownloadToBufferOnly,
    DownloadAndProbeOnly,
    DownloadAndDecodeOnly,
    DownloadDecodeAndHash,
    FullWithoutFileWrite,
    FullEarlyResponseDispose,
}

public sealed record ImagePipelineValidationCounters(
    string Mode, long DownloadedImages, long ReadBytes, long ProbedImages, long DecodedImages,
    long HashedImages, long WrittenImages, long RequestsCreated, long RequestsDisposed,
    long ResponsesCreated, long ResponsesDisposed, long StreamsOpened, long StreamsDisposed,
    long ActiveRequests, long ActiveResponses, long ActiveStreams, long PeakActiveResponses, long PeakActiveStreams);

public static class ImagePipelineValidationDiagnostics
{
    private static long downloadedImages;
    private static long readBytes;
    private static long probedImages;
    private static long decodedImages;
    private static long hashedImages;
    private static long writtenImages;
    private static long requestsCreated, requestsDisposed, responsesCreated, responsesDisposed, streamsOpened, streamsDisposed;
    private static long activeRequests, activeResponses, activeStreams, peakActiveResponses, peakActiveStreams, ignoredPeak;

    public static bool IsEnabled => Environment.GetEnvironmentVariable("LMM_VALIDATION_DIAGNOSTICS") == "1";

    public static ImagePipelineValidationMode Mode => IsEnabled
        && Enum.TryParse(Environment.GetEnvironmentVariable("LMM_VALIDATION_IMAGE_MODE"), true, out ImagePipelineValidationMode mode)
            ? mode
            : ImagePipelineValidationMode.Full;

    public static string RequestUrl(string originalUrl)
    {
        string? overrideUrl = IsEnabled ? Environment.GetEnvironmentVariable("LMM_VALIDATION_IMAGE_FAILURE_URL") : null;
        return Uri.TryCreate(overrideUrl, UriKind.Absolute, out Uri? uri) && uri.IsLoopback ? uri.ToString() : originalUrl;
    }

    public static ImagePipelineValidationCounters Snapshot() => new(Mode.ToString(),
        Interlocked.Read(ref downloadedImages), Interlocked.Read(ref readBytes), Interlocked.Read(ref probedImages),
        Interlocked.Read(ref decodedImages), Interlocked.Read(ref hashedImages), Interlocked.Read(ref writtenImages),
        Interlocked.Read(ref requestsCreated), Interlocked.Read(ref requestsDisposed), Interlocked.Read(ref responsesCreated),
        Interlocked.Read(ref responsesDisposed), Interlocked.Read(ref streamsOpened), Interlocked.Read(ref streamsDisposed),
        Interlocked.Read(ref activeRequests), Interlocked.Read(ref activeResponses), Interlocked.Read(ref activeStreams),
        Interlocked.Read(ref peakActiveResponses), Interlocked.Read(ref peakActiveStreams));

    public static void Downloaded() => Interlocked.Increment(ref downloadedImages);
    public static void Read(long bytes) => Interlocked.Add(ref readBytes, bytes);
    public static void Probed() => Interlocked.Increment(ref probedImages);
    public static void Decoded() => Interlocked.Increment(ref decodedImages);
    public static void Hashed() => Interlocked.Increment(ref hashedImages);
    public static void Written() => Interlocked.Increment(ref writtenImages);
    public static IDisposable TrackRequest() => Track(ref requestsCreated, ref activeRequests, ref ignoredPeak, LeaseKind.Request);
    public static IDisposable TrackResponse() => Track(ref responsesCreated, ref activeResponses, ref peakActiveResponses, LeaseKind.Response);
    public static IDisposable TrackStream() => Track(ref streamsOpened, ref activeStreams, ref peakActiveStreams, LeaseKind.Stream);

    private static IDisposable Track(ref long created, ref long active, ref long peak, LeaseKind kind)
    {
        if (!IsEnabled) return EmptyLease.Instance;
        Interlocked.Increment(ref created);
        long current = Interlocked.Increment(ref active);
        while (current > Volatile.Read(ref peak)) Interlocked.CompareExchange(ref peak, current, Volatile.Read(ref peak));
        return new Lease(kind);
    }

    private enum LeaseKind { Request, Response, Stream }
    private sealed class Lease(LeaseKind kind) : IDisposable {
        private int disposed;
        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (kind == LeaseKind.Request) { Interlocked.Increment(ref requestsDisposed); Interlocked.Decrement(ref activeRequests); }
            else if (kind == LeaseKind.Response) { Interlocked.Increment(ref responsesDisposed); Interlocked.Decrement(ref activeResponses); }
            else { Interlocked.Increment(ref streamsDisposed); Interlocked.Decrement(ref activeStreams); }
        }
    }
    private sealed class EmptyLease : IDisposable { public static readonly EmptyLease Instance = new(); public void Dispose() { } }

    public static async Task DrainAsync(Stream source, CancellationToken token)
    {
        byte[] buffer = new byte[81920];
        while (true) {
            int read = await source.ReadAsync(buffer, token);
            if (read == 0) return;
            Read(read);
        }
    }

    public static async Task<byte[]> BufferAsync(Stream source, CancellationToken token)
    {
        using var target = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true) {
            int read = await source.ReadAsync(buffer, token);
            if (read == 0) return target.ToArray();
            Read(read);
            await target.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }

    public static void Probe(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using SKCodec? codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            throw new InvalidDataException("Validation image probe failed.");
        Probed();
    }

    public static void Decode(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
            throw new InvalidDataException("Validation image decode failed.");
        Decoded();
    }

    public static void Hash(byte[] bytes)
    {
        _ = SHA256.HashData(bytes);
        Hashed();
    }
}
