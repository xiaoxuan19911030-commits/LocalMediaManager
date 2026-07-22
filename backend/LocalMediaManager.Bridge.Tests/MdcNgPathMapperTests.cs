using LocalMediaManager.Bridge;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class MdcNgPathMapperTests
{
    [Theory]
    [InlineData(@"E:\Media\THU-063.mp4", "/config/media/THU-063.mp4")]
    [InlineData(@"e:/media/2026/THU-063.mp4", "/config/media/2026/THU-063.mp4")]
    public void MapsCaseInsensitivePathsAndPreservesSubdirectories(string input, string expected)
    {
        MdcNgPathMappingResult? result = MdcNgPathMapper.Map(input, [new(@"E:\Media\", "/config/media/")]);
        Assert.NotNull(result); Assert.Equal(expected, result.ProviderPath);
    }

    [Fact]
    public void LongestEnabledDirectoryPrefixWins()
    {
        MdcNgPathMappingResult? result = MdcNgPathMapper.Map(@"E:\Media\Special\A.mp4", [
            new(@"E:\Media", "/config/media", true, 0), new(@"E:\Media\Special", "/config/special", true, 1)]);
        Assert.Equal("/config/special/A.mp4", result?.ProviderPath);
    }

    [Fact]
    public void SimilarDirectoryDoesNotMatchAndDisabledRulesAreIgnored()
    {
        Assert.Null(MdcNgPathMapper.Map(@"E:\MediaBackup\A.mp4", [new(@"E:\Media", "/config/media")]));
        Assert.Null(MdcNgPathMapper.Map(@"E:\Media\A.mp4", [new(@"E:\Media", "/config/media", false)]));
    }

    [Fact]
    public void RejectsRelativeUnixAndDuplicateLocalPrefixes()
    {
        Assert.Throws<ArgumentException>(() => MdcNgPathMapper.Normalize([new(@"E:\Media", "config/media")]));
        Assert.Throws<ArgumentException>(() => MdcNgPathMapper.Normalize([new(@"E:\Media", "/a"), new(@"e:/media/", "/b")]));
    }
}
