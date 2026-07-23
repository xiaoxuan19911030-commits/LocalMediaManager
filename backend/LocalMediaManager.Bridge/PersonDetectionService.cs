using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace LocalMediaManager.Bridge;

public sealed record PersonDetectionResult(
    bool Available,
    bool HasPerson,
    int PersonCount,
    double LargestPersonAreaRatio,
    double Confidence,
    double LargestPersonCenterDistance,
    string? UnavailableReason = null);

public interface IPersonDetectionService
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    Task<PersonDetectionResult> DetectAsync(string imagePath, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class OnnxPersonDetectionService : IPersonDetectionService, IDisposable
{
    private const int InputSize = 300;
    private const float MinimumConfidence = 0.35f;
    private readonly InferenceSession? session;
    private readonly string? unavailableReason;

    public OnnxPersonDetectionService(string modelPath)
    {
        try
        {
            if (!File.Exists(modelPath))
            {
                unavailableReason = $"人物检测模型不存在：{modelPath}";
                return;
            }
            session = new InferenceSession(modelPath, new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 });
        }
        catch (Exception error)
        {
            unavailableReason = $"人物检测模型初始化失败：{error.Message}";
        }
    }

    public bool IsAvailable => session is not null;
    public string? UnavailableReason => unavailableReason;

    public async Task<PersonDetectionResult> DetectAsync(string imagePath, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (session is null) return Unavailable(unavailableReason ?? "人物检测不可用。");
        try
        {
            Task<PersonDetectionResult> work = Task.Run(() => Detect(imagePath), CancellationToken.None);
            Task delay = Task.Delay(timeout, cancellationToken);
            Task completed = await Task.WhenAny(work, delay);
            if (completed != work)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = work.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return Unavailable("人物检测超时，已降级为基础画质过滤。");
            }
            return await work;
        }
        catch (Exception error)
        {
            return Unavailable($"人物检测失败，已降级为基础画质过滤：{error.Message}");
        }
    }

    private PersonDetectionResult Detect(string imagePath)
    {
        using SKBitmap? source = SKBitmap.Decode(imagePath);
        if (source is null) throw new InvalidDataException("候选截图无法解码。");
        using var resized = source.Resize(new SKImageInfo(InputSize, InputSize, SKColorType.Rgb888x, SKAlphaType.Opaque), SKSamplingOptions.Default)
            ?? throw new InvalidDataException("候选截图无法缩放。");
        var tensor = new DenseTensor<byte>(new[] { 1, InputSize, InputSize, 3 });
        for (int y = 0; y < InputSize; y++)
        for (int x = 0; x < InputSize; x++)
        {
            SKColor color = resized.GetPixel(x, y);
            tensor[0, y, x, 0] = color.Red;
            tensor[0, y, x, 1] = color.Green;
            tensor[0, y, x, 2] = color.Blue;
        }

        string inputName = session!.InputMetadata.Keys.Single();
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = session.Run(
            new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });
        float[] boxes = Output(outputs, "detection_boxes").AsTensor<float>().ToArray();
        float[] scores = Output(outputs, "detection_scores").AsTensor<float>().ToArray();
        float[] classes = Output(outputs, "detection_classes").AsTensor<float>().ToArray();
        int count = Math.Min(scores.Length, classes.Length);
        int people = 0;
        double largestArea = 0;
        double bestConfidence = 0;
        double centerDistance = 1;
        for (int i = 0; i < count; i++)
        {
            if (scores[i] < MinimumConfidence || Math.Round(classes[i]) != 1 || i * 4 + 3 >= boxes.Length) continue;
            people++;
            double top = Math.Clamp(boxes[i * 4], 0, 1);
            double left = Math.Clamp(boxes[i * 4 + 1], 0, 1);
            double bottom = Math.Clamp(boxes[i * 4 + 2], 0, 1);
            double right = Math.Clamp(boxes[i * 4 + 3], 0, 1);
            double area = Math.Max(0, bottom - top) * Math.Max(0, right - left);
            if (area > largestArea)
            {
                largestArea = area;
                bestConfidence = scores[i];
                double dx = ((left + right) / 2) - 0.5;
                double dy = ((top + bottom) / 2) - 0.5;
                centerDistance = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / Math.Sqrt(0.5), 0, 1);
            }
        }
        return new(true, people > 0, people, largestArea, bestConfidence, centerDistance);
    }

    private static DisposableNamedOnnxValue Output(IEnumerable<DisposableNamedOnnxValue> outputs, string name) =>
        outputs.First(value => value.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static PersonDetectionResult Unavailable(string reason) => new(false, false, 0, 0, 0, 1, reason);
    public void Dispose() => session?.Dispose();
}
