using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace LocalMediaManager.Bridge;

public sealed record FaceRectangle(double X, double Y, double Width, double Height, double Confidence)
{
    public double Area => Width * Height;
    public double CenterX => X + Width / 2;
}
public sealed record FaceDetectionResult(IReadOnlyList<FaceRectangle> Faces, string ModelVersion)
{
    public FaceRectangle? LargestFace => Faces.OrderByDescending(face => face.Area).FirstOrDefault();
}
public interface IFaceDetectionService { Task<FaceDetectionResult> DetectAsync(string imagePath, CancellationToken token = default); }

public sealed class YuNetFaceDetectionService(AiModelSessionManager sessions, AiInferenceQueue queue, AiResultCache cache) : IFaceDetectionService
{
    private const int InputSize = 640;
    private const float MinimumConfidence = .75f;
    public async Task<FaceDetectionResult> DetectAsync(string imagePath, CancellationToken token = default)
    {
        FileInfo file = new(imagePath);
        string key = $"{AiModelRegistry.YuNetFaceDetector.Id}:{AiModelRegistry.YuNetFaceDetector.Version}:{file.FullName}:{file.Length}:{file.LastWriteTimeUtc.Ticks}";
        FaceDetectionResult? cached = await cache.ReadAsync<FaceDetectionResult>(key, token);
        if (cached is not null) return cached;
        FaceDetectionResult result = await queue.ExecuteAsync(async cancellationToken => {
            FaceDetectionResult detected = await Task.Run(() => Detect(imagePath), cancellationToken);
            await cache.WriteAsync(key, detected, cancellationToken);
            return detected;
        }, token);
        return result;
    }
    private FaceDetectionResult Detect(string imagePath)
    {
        using SKBitmap? source = SKBitmap.Decode(imagePath);
        if (source is null) return new([], AiModelRegistry.YuNetFaceDetector.Version);
        using SKBitmap resized = source.Resize(new SKImageInfo(InputSize, InputSize, SKColorType.Bgra8888, SKAlphaType.Opaque), SKSamplingOptions.Default)
            ?? throw new InvalidDataException("Unable to resize cover for face detection.");
        var input = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
        for (int y = 0; y < InputSize; y++) for (int x = 0; x < InputSize; x++) {
            SKColor pixel = resized.GetPixel(x, y);
            input[0, 0, y, x] = pixel.Blue; input[0, 1, y, x] = pixel.Green; input[0, 2, y, x] = pixel.Red;
        }
        InferenceSession session = sessions.GetRequiredSession(AiModelRegistry.YuNetFaceDetector.Id);
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = session.Run([NamedOnnxValue.CreateFromTensor("input", input)]);
        Dictionary<string, float[]> values = outputs.ToDictionary(output => output.Name, output => output.AsTensor<float>().ToArray(), StringComparer.OrdinalIgnoreCase);
        var faces = new List<FaceRectangle>();
        foreach (int stride in new[] { 8, 16, 32 }) DecodeStride(values, stride, source.Width, source.Height, faces);
        return new(NonMaximumSuppress(faces), AiModelRegistry.YuNetFaceDetector.Version);
    }
    private static void DecodeStride(IReadOnlyDictionary<string, float[]> values, int stride, int imageWidth, int imageHeight, List<FaceRectangle> target)
    {
        if (!values.TryGetValue($"cls_{stride}", out float[]? cls) || !values.TryGetValue($"obj_{stride}", out float[]? obj) || !values.TryGetValue($"bbox_{stride}", out float[]? box)) return;
        int grid = InputSize / stride;
        int count = Math.Min(cls.Length, obj.Length);
        for (int index = 0; index < count; index++) {
            float score = MathF.Sqrt(Math.Max(0, cls[index]) * Math.Max(0, obj[index]));
            if (score < MinimumConfidence || index * 4 + 3 >= box.Length) continue;
            int gx = index % grid, gy = index / grid;
            double width = Math.Exp(Math.Clamp(box[index * 4 + 2], -8, 8)) * stride * imageWidth / InputSize;
            double height = Math.Exp(Math.Clamp(box[index * 4 + 3], -8, 8)) * stride * imageHeight / InputSize;
            double centerX = (gx + box[index * 4]) * stride * imageWidth / InputSize;
            double centerY = (gy + box[index * 4 + 1]) * stride * imageHeight / InputSize;
            double x = Math.Clamp(centerX - width / 2, 0, imageWidth), y = Math.Clamp(centerY - height / 2, 0, imageHeight);
            width = Math.Min(width, imageWidth - x); height = Math.Min(height, imageHeight - y);
            if (width > 1 && height > 1) target.Add(new(x / imageWidth, y / imageHeight, width / imageWidth, height / imageHeight, score));
        }
    }
    private static IReadOnlyList<FaceRectangle> NonMaximumSuppress(List<FaceRectangle> faces)
    {
        var kept = new List<FaceRectangle>();
        foreach (FaceRectangle candidate in faces.OrderByDescending(face => face.Confidence)) {
            if (kept.All(existing => IntersectionOverUnion(candidate, existing) < .35)) kept.Add(candidate);
        }
        return kept;
    }
    private static double IntersectionOverUnion(FaceRectangle a, FaceRectangle b)
    {
        double left = Math.Max(a.X, b.X), top = Math.Max(a.Y, b.Y), right = Math.Min(a.X + a.Width, b.X + b.Width), bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        double intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        return intersection <= 0 ? 0 : intersection / (a.Area + b.Area - intersection);
    }
}
