namespace LocalMediaManager.Bridge;

public sealed record ActorImageMetadata(string Name, string ImageUrl);

public sealed record MovieMetadata(
    string Provider,
    string? ExternalId,
    string Code,
    string? Title,
    string? OriginalTitle,
    string? Description,
    IReadOnlyList<string> Actors,
    string? Director,
    string? Studio,
    string? Series,
    IReadOnlyList<string> Tags,
    string? Country,
    string? ReleaseDate,
    int? DurationSeconds,
    decimal? Rating,
    string? Poster,
    string? Thumb,
    string? Fanart,
    IReadOnlyList<string> ExtraFanart,
    string? Trailer,
    IReadOnlyList<ActorImageMetadata> ActorImages);

public sealed record MdcNgScrapeCommand(
    string MoviePath,
    string? Code = null,
    string? TargetFolder = null,
    int? TimeoutSeconds = null);

public sealed record MdcNgScrapeResult(
    string Provider,
    bool Success,
    string Status,
    string? JobId,
    string? TaskId,
    MovieMetadata? Metadata,
    string RawJson,
    string Message);

public sealed record MetadataSyncRequest(
    string Code,
    string? MediaPath = null,
    string? ProviderId = null,
    long? MovieId = null,
    bool Overwrite = false);

public sealed record MetadataSyncResult(
    bool Success,
    MovieMetadata? Metadata,
    string ProviderId,
    long ElapsedMilliseconds,
    IReadOnlyList<string> Warnings,
    string? ErrorCode,
    string? ErrorMessage,
    long? ImportTaskId = null,
    string? ImportSummary = null,
    MetadataSyncImageResult? Images = null);

public sealed record MetadataSyncImageResult(
    int MovieImagesDownloaded,
    int ActorImagesDownloaded,
    IReadOnlyList<string> Warnings);
