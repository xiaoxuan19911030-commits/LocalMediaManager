using LocalMediaManager.Bridge;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class SearchQueryParserTests
{
    [Fact]
    public void ParsesPlainKeywordsAndCollapsesSpaces()
    {
        SearchCriteria criteria = SearchQueryParser.Parse("  办公室　　长发  ");

        Assert.Equal(["办公室", "长发"], criteria.Keywords);
    }

    [Theory]
    [InlineData("收藏", true)]
    [InlineData("已收藏", true)]
    [InlineData("未收藏", false)]
    [InlineData("收藏:true", true)]
    [InlineData("收藏:false", false)]
    public void ParsesFavoriteConditions(string query, bool expected)
    {
        Assert.Equal(expected, SearchQueryParser.Parse(query).Favorite);
    }

    [Theory]
    [InlineData("已观看", true)]
    [InlineData("未观看", false)]
    [InlineData("已观看:true", true)]
    [InlineData("已观看:false", false)]
    public void ParsesWatchedConditions(string query, bool expected)
    {
        Assert.Equal(expected, SearchQueryParser.Parse(query).Watched);
    }

    [Theory]
    [InlineData("4星", SearchComparison.Equal, 4)]
    [InlineData("评分:4", SearchComparison.Equal, 4)]
    [InlineData("评分>=4", SearchComparison.GreaterThanOrEqual, 4)]
    [InlineData("评分>4", SearchComparison.GreaterThan, 4)]
    [InlineData("评分<=3", SearchComparison.LessThanOrEqual, 3)]
    [InlineData("评分<3", SearchComparison.LessThan, 3)]
    public void ParsesRatingConditions(string query, SearchComparison comparison, double value)
    {
        NumericSearchCondition? rating = SearchQueryParser.Parse(query).Rating;

        Assert.NotNull(rating);
        Assert.Equal(comparison, rating.Comparison);
        Assert.Equal(value, rating.Value);
    }

    [Fact]
    public void ParsesUnratedCondition()
    {
        Assert.True(SearchQueryParser.Parse("未评分").Unrated);
        Assert.True(SearchQueryParser.Parse("unrated").Unrated);
    }

    [Fact]
    public void ParsesStructuredEntityFields()
    {
        SearchCriteria criteria = SearchQueryParser.Parse("演员:三上悠亚 导演:导演A 标签:长发 自定义标签:收藏候选 系列:SONE 厂商:S1 媒体库:本地影片 年份>=2024");

        Assert.Equal(["三上悠亚"], criteria.Actors);
        Assert.Equal(["导演A"], criteria.Directors);
        Assert.Equal(["长发"], criteria.Tags);
        Assert.Equal(["收藏候选"], criteria.CustomTags);
        Assert.Equal(["SONE"], criteria.Series);
        Assert.Equal(["S1"], criteria.Studios);
        Assert.Equal(["本地影片"], criteria.Libraries);
        Assert.Equal(SearchComparison.GreaterThanOrEqual, criteria.Year!.Comparison);
        Assert.Equal(2024, criteria.Year.Value);
    }

    [Fact]
    public void MixesPlainKeywordsAndStructuredConditionsAsAndTerms()
    {
        SearchCriteria criteria = SearchQueryParser.Parse("办公室 收藏 评分>=4 演员:三上悠亚");

        Assert.Equal(["办公室"], criteria.Keywords);
        Assert.True(criteria.Favorite);
        Assert.Equal(4, criteria.Rating!.Value);
        Assert.Equal(["三上悠亚"], criteria.Actors);
    }

    [Fact]
    public void InvalidStructuredSyntaxFallsBackToPlainKeyword()
    {
        SearchCriteria criteria = SearchQueryParser.Parse("评分>>4");

        Assert.Null(criteria.Rating);
        Assert.Equal(["评分>>4"], criteria.Keywords);
    }

    [Fact]
    public void EnglishCaseIsNormalizedForOperators()
    {
        SearchCriteria criteria = SearchQueryParser.Parse("FAVORITE Rating>=4 ACTOR:Yua");

        Assert.True(criteria.Favorite);
        Assert.Equal(4, criteria.Rating!.Value);
        Assert.Equal(["Yua"], criteria.Actors);
    }

    [Fact]
    public void CodeHyphenDifferencesRemainPlainSearchTerms()
    {
        SearchCriteria criteria = SearchQueryParser.Parse("SONE104");

        Assert.Equal(["SONE104"], criteria.Keywords);
    }
}
