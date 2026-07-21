namespace LocalMediaManager.Bridge;

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
    string? Trailer);

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
