namespace LocalMediaManager.Bridge;

public enum LibraryType
{
    Standard,
    Local,
}

public enum ScreenshotStatus
{
    None,
    Pending,
    Processing,
    Completed,
    Failed,
}

public enum CoverSource
{
    None,
    Uploaded,
    Screenshot,
    Scraped,
}

public interface IVideoScreenshotService
{
    Task QueueAsync(long movieId, CancellationToken cancellationToken = default);
}

public static class MediaLibraryType
{
    public static LibraryType Parse(string? value) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out LibraryType result)
            ? result
            : throw new ArgumentException("媒体库类型只支持 Standard 或 Local。");
}
