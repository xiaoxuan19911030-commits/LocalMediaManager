using SkiaSharp;

namespace LocalMediaManager.Bridge;

public sealed record ScreenshotQuality(double Brightness, double BlackRatio, double Sharpness, ulong PerceptualHash);

public static class ScreenshotQualityAnalyzer
{
    public static ScreenshotQuality Analyze(string path)
    {
        using SKBitmap? source = SKBitmap.Decode(path);
        if (source is null) throw new InvalidDataException("候选截图无法解码。");
        using SKBitmap bitmap = source.Resize(new SKImageInfo(64, 64), SKSamplingOptions.Default)
            ?? throw new InvalidDataException("候选截图无法分析。");
        double sum = 0;
        int black = 0;
        double laplacian = 0;
        var gray = new double[64, 64];
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            SKColor color = bitmap.GetPixel(x, y);
            double value = (0.2126 * color.Red + 0.7152 * color.Green + 0.0722 * color.Blue) / 255d;
            gray[y, x] = value;
            sum += value;
            if (value < 0.05) black++;
        }
        for (int y = 1; y < 63; y++)
        for (int x = 1; x < 63; x++)
            laplacian += Math.Abs((4 * gray[y, x]) - gray[y - 1, x] - gray[y + 1, x] - gray[y, x - 1] - gray[y, x + 1]);
        ulong hash = 0;
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            hash <<= 1;
            if (gray[y * 8, x * 7] > gray[y * 8, x * 7 + 1]) hash |= 1;
        }
        return new(sum / 4096, black / 4096d, laplacian / (62 * 62), hash);
    }

    public static int HashDistance(ulong left, ulong right) => System.Numerics.BitOperations.PopCount(left ^ right);
}
