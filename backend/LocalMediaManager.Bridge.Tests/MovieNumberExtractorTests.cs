using Xunit;
using System.Text.Json.Nodes;

namespace LocalMediaManager.Bridge.Tests;

public sealed class MovieNumberExtractorTests
{
    public static IEnumerable<object?[]> Cases => [
        ["ABC-123_UN.mp4", "ABC-123", "UncensoredSuffix", null],
        ["ABC-123-UN.mp4", "ABC-123", "UncensoredSuffix", null],
        ["ABC-123J.mp4", "ABC-123", "EditionSuffix", null],
        ["ABC-123-1.mp4", "ABC-123", "NumericPart", 1],
        ["ABC-123A.mp4", "ABC-123", "EditionSuffix", null],
        ["ABC-123B.mp4", "ABC-123", "EditionSuffix", null],
        ["ABC-123-C.mp4", "ABC-123", "EditionSuffix", null],
        ["ABC-123-4K.mp4", "ABC-123", "QualitySuffix", null],
        ["ABC-123-2K.mp4", "ABC-123", "QualitySuffix", null],
        ["ABC-123-U.mp4", "ABC-123", "UncensoredSuffix", null],
        ["ABC-123-UC.mp4", "ABC-123", "UncensoredSuffix", null],
        ["ABC-123CH.mp4", "ABC-123", "SubtitleSuffix", null],
        ["ABC-123C.mp4", "ABC-123", "EditionSuffix", null],
        ["ABC-123-AI.mp4", "ABC-123", "QualitySuffix", null],
        ["[4K][中文字幕] SONE-454.mp4", "SONE-454", "StandardSeparated", null],
        ["hhd800.com@SONE-454.mp4", "SONE-454", "StandardSeparated", null],
        ["SONE454.mp4", "SONE-454", "StandardCompact", null],
        ["SONE-454-CD1.mp4", "SONE-454", "CdPart", 1],
        ["FC2PPV1234567.mp4", "FC2-PPV-1234567", "Fc2Ppv", null],
        ["1pondo-123456_789.mp4", "1PONDO-123456_789", "OnePondo", null],
        ["259LUXU1234.mp4", "259LUXU-1234", "Luxu", null],
        ["CARIB-123456-789.mp4", "CARIB-123456-789", "Caribbean", null],
        ["HEYDOUGA-1234-567.mp4", "HEYDOUGA-1234-567", "Heydouga", null],
        ["WAAA-448+五星+娇小可爱清纯+持续输出+高颜值+小坂七香.mp4", "WAAA-448", "StandardSeparated", null],
        ["WAAA-448 - 五星 - 小坂七香.mp4", "WAAA-448", "StandardSeparated", null],
        ["WAAA-448_五星_小坂七香.mp4", "WAAA-448", "StandardSeparated", null],
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExtractsConfiguredFormats(string source, string expected, string rule, int? part)
    {
        MovieNumberExtractionResult result = Create().Extract(source);

        Assert.Equal(source, result.OriginalFileName);
        Assert.Equal(expected, result.NormalizedNumber);
        Assert.Contains(rule, result.MatchedRule ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(part, result.PartIndex);
        Assert.True(result.Confidence >= 0.8);
    }

    [Theory]
    [InlineData("普通中文标题.mp4")]
    [InlineData("2026-07-23-test-video.mp4")]
    public void LeavesNonNumbersUnrecognized(string source)
    {
        MovieNumberExtractionResult result = Create().Extract(source);
        Assert.Null(result.NormalizedNumber);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public void LowersConfidenceForMultipleCandidates()
    {
        MovieNumberExtractionResult result = Create().Extract("SONE-454 ABW-001.mp4");
        Assert.True(result.Confidence < 0.7);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("MultipleCandidates", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtendsSuffixRulesByConfigurationOnly()
    {
        string sourceRules = FindRuleFile();
        string temporaryRules = Path.Combine(Path.GetTempPath(), $"lmm-number-rules-{Guid.NewGuid():N}.json");
        try {
            JsonNode root = JsonNode.Parse(File.ReadAllText(sourceRules))!;
            root["suffixRules"]!.AsArray().Add(new JsonObject {
                ["id"] = "FutureEditionSuffix",
                ["pattern"] = "(?i)^[\\s._-]*(?:SPECIALCUT)(?=$|[^A-Z0-9])",
            });
            File.WriteAllText(temporaryRules, root.ToJsonString());

            MovieNumberExtractionResult result = new MovieNumberExtractor(temporaryRules).Extract("ABC-123-SPECIALCUT.mp4");

            Assert.Equal("ABC-123", result.NormalizedNumber);
            Assert.Contains("FutureEditionSuffix", result.MatchedRule);
        } finally {
            if (File.Exists(temporaryRules)) File.Delete(temporaryRules);
        }
    }

    private static MovieNumberExtractor Create() => new(FindRuleFile());

    private static string FindRuleFile()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent) {
            string file = Path.Combine(current.FullName, "backend", "LocalMediaManager.Bridge", "movie-number-rules.json");
            if (File.Exists(file)) return file;
        }
        throw new FileNotFoundException("movie-number-rules.json");
    }
}
