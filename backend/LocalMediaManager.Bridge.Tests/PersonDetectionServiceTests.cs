using LocalMediaManager.Bridge;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class PersonDetectionServiceTests
{
    [Fact]
    public async Task DetectsPersonInRealCocoImage()
    {
        using var service = new OnnxPersonDetectionService(ModelPath());
        PersonDetectionResult result = await service.DetectAsync(Asset("person.jpg"), TimeSpan.FromSeconds(20));
        Assert.True(result.Available, result.UnavailableReason);
        Assert.True(result.HasPerson);
        Assert.True(result.PersonCount >= 1);
        Assert.True(result.LargestPersonAreaRatio > 0.02);
        Assert.InRange(result.Confidence, 0.35, 1);
    }

    [Fact]
    public async Task MissingModelDegradesWithoutThrowing()
    {
        using var service = new OnnxPersonDetectionService(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".onnx"));
        PersonDetectionResult result = await service.DetectAsync(Asset("person.jpg"), TimeSpan.FromSeconds(1));
        Assert.False(result.Available);
        Assert.Contains("不存在", result.UnavailableReason);
    }

    [Fact]
    public async Task CorruptModelDegradesWithoutThrowing()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".onnx");
        await File.WriteAllTextAsync(path, "not an ONNX model");
        try
        {
            using var service = new OnnxPersonDetectionService(path);
            PersonDetectionResult result = await service.DetectAsync(Asset("person.jpg"), TimeSpan.FromSeconds(1));
            Assert.False(result.Available);
            Assert.Contains("初始化失败", result.UnavailableReason);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DetectionTimeoutReturnsUnavailableInsteadOfThrowing()
    {
        using var service = new OnnxPersonDetectionService(ModelPath());
        PersonDetectionResult result = await service.DetectAsync(Asset("person.jpg"), TimeSpan.FromTicks(1));
        Assert.False(result.Available);
        Assert.Contains("超时", result.UnavailableReason);
    }

    [Fact]
    public void CoverScoreRewardsCenteredSuitablePersonAndPenalizesSmallSubject()
    {
        var quality = new ScreenshotQuality(0.45, 0, 0.08, 0);
        double none = ImageGenerationTaskService.Score(quality, new(true, false, 0, 0, 0, 1));
        double small = ImageGenerationTaskService.Score(quality, new(true, true, 1, 0.01, 0.8, 0.1));
        double centered = ImageGenerationTaskService.Score(quality, new(true, true, 1, 0.2, 0.8, 0.1));
        double edge = ImageGenerationTaskService.Score(quality, new(true, true, 1, 0.2, 0.8, 0.9));
        double multiple = ImageGenerationTaskService.Score(quality, new(true, true, 3, 0.2, 0.8, 0.1));
        Assert.True(centered > small);
        Assert.True(centered > edge);
        Assert.True(small > none);
        Assert.True(multiple > centered);
    }

    private static string ModelPath() => Path.Combine(AppContext.BaseDirectory, "models", "ssd_mobilenet_v1_12-int8.onnx");
    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "assets", "person-detection", name);
}
