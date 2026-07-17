using System.Diagnostics;

namespace LocalMediaManager.Bridge;

public sealed class PlatformCommandService
{
    public PlatformOpenResult OpenDirectory(string path)
    {
        string directory = ResolveDirectory(path);
        if (!Directory.Exists(directory))
            throw new KeyNotFoundException($"目录不存在：{directory}");

        Process.Start(new ProcessStartInfo {
            FileName = directory,
            UseShellExecute = true,
        });
        return new(directory, "已打开目录。");
    }

    public PlatformOpenResult RevealFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不能为空。");

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new KeyNotFoundException($"文件不存在：{fullPath}");

        Process.Start(new ProcessStartInfo {
            FileName = "explorer.exe",
            ArgumentList = { "/select,", fullPath },
            UseShellExecute = true,
        });
        return new(fullPath, "已定位文件。");
    }

    private static string ResolveDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不能为空。");

        string fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
            return fullPath;
        return Path.GetDirectoryName(fullPath) ?? throw new ArgumentException("无法确定目录。");
    }
}

public sealed record PlatformPathCommand(string Path);
public sealed record PlatformOpenResult(string Path, string Message);
