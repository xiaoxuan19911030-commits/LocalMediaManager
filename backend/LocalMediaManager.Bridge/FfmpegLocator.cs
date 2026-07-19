using System.Diagnostics;

namespace LocalMediaManager.Bridge;

public sealed record FfmpegLookupResult(bool Found, string? Path, string Source, string Message);
public sealed record FfmpegToolStatusDto(bool Found, string? Path, string? ProbePath, string Source,
    string? Version, string? ProbeVersion, string PluginDirectory, string Message);

public sealed class FfmpegLocator
{
    private readonly string appRoot;

    public FfmpegLocator(string databasePath, string appRoot)
    {
        _ = databasePath;
        this.appRoot = appRoot;
    }

    public string PluginDirectory => Path.Combine(ResolveInstallRoot(appRoot), "plugins", "ffmpeg");

    public FfmpegLookupResult Locate()
    {
        foreach ((string source, string? candidate) in Candidates()) {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return new(true, candidate, source, $"FFmpeg 已找到：{source}");
        }
        return new(false, null, "Missing", "FFmpeg 不存在。请下载 ffmpeg.exe 和 ffprobe.exe，并放入 plugins\\ffmpeg。");
    }

    public FfmpegToolStatusDto Status()
    {
        Directory.CreateDirectory(PluginDirectory);
        FfmpegLookupResult ffmpeg = Locate();
        string? probe = Candidates("ffprobe.exe").Select(item => item.Path).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        return new(
            ffmpeg.Found,
            ffmpeg.Path,
            probe,
            ffmpeg.Source,
            ffmpeg.Found && ffmpeg.Path is not null ? ReadVersion(ffmpeg.Path) : null,
            probe is not null ? ReadVersion(probe) : null,
            PluginDirectory,
            ffmpeg.Found && probe is not null ? "FFmpeg 截图工具已就绪。" : ffmpeg.Message);
    }

    private IEnumerable<(string Source, string? Path)> Candidates() => Candidates("ffmpeg.exe");

    private IEnumerable<(string Source, string? Path)> Candidates(string executable)
    {
        yield return ("插件目录", Path.Combine(PluginDirectory, executable));
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
            if (!string.IsNullOrWhiteSpace(folder))
                yield return ("PATH", Path.Combine(folder.Trim(), executable));
        }
    }

    private static string ResolveInstallRoot(string baseDirectory)
    {
        string current = Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DirectoryInfo? directory = new(current);
        if (directory.Name.Equals("bridge", StringComparison.OrdinalIgnoreCase)
            && directory.Parent?.Name.Equals("resources", StringComparison.OrdinalIgnoreCase) == true
            && directory.Parent.Parent is not null)
            return directory.Parent.Parent.FullName;
        if (directory.Name.Equals("resources", StringComparison.OrdinalIgnoreCase) && directory.Parent is not null)
            return directory.Parent.FullName;
        return current;
    }

    private static string? ReadVersion(string executable)
    {
        try {
            using var process = Process.Start(new ProcessStartInfo {
                FileName = executable,
                ArgumentList = { "-version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return null;
            string line = process.StandardOutput.ReadLine() ?? process.StandardError.ReadLine() ?? "";
            if (!process.WaitForExit(2000)) {
                try { process.Kill(); } catch { }
            }
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch {
            return null;
        }
    }
}
