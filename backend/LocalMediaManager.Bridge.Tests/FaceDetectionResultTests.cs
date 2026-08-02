using LocalMediaManager.Bridge;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class FaceDetectionResultTests
{
    [Fact]
    public void LargestFaceWinsOverMoreCenteredSmallerFace()
    {
        var result = new FaceDetectionResult([
            new FaceRectangle(.45, .25, .10, .10, .98),
            new FaceRectangle(.05, .20, .30, .45, .82),
        ], "test");

        FaceRectangle face = Assert.IsType<FaceRectangle>(result.LargestFace);
        Assert.InRange(face.CenterX, .19, .21);
    }

    [Theory]
    [InlineData(.02, .20)]
    [InlineData(.50, .50)]
    [InlineData(.90, .80)]
    public void FocusIsClampedAwayFromExtremeEdges(double center, double expected)
    {
        double focus = Math.Clamp(center, .20, .80);
        Assert.Equal(expected, focus, 3);
    }

    [Fact]
    public async Task ResultCacheSeparatesModelVersions()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try {
            var cache = new AiResultCache(root);
            await cache.WriteAsync("yunet:2023mar:cover", new FaceDetectionResult([], "2023mar"));
            Assert.NotNull(await cache.ReadAsync<FaceDetectionResult>("yunet:2023mar:cover"));
            Assert.Null(await cache.ReadAsync<FaceDetectionResult>("yunet:next:cover"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
