using System.Globalization;
using System.Text.RegularExpressions;

namespace LocalMediaManager.Bridge;

public enum SearchComparison
{
    Equal,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
}

public sealed record NumericSearchCondition(SearchComparison Comparison, double Value);
public sealed record YearSearchCondition(SearchComparison Comparison, int Value);

public sealed record SearchCriteria(
    IReadOnlyList<string> Keywords,
    bool? Favorite,
    bool? Watched,
    bool Unrated,
    NumericSearchCondition? Rating,
    IReadOnlyList<string> Actors,
    IReadOnlyList<string> Directors,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> CustomTags,
    IReadOnlyList<string> Series,
    IReadOnlyList<string> Studios,
    IReadOnlyList<string> Libraries,
    YearSearchCondition? Year);

public static partial class SearchQueryParser
{
    public static SearchCriteria Parse(string query)
    {
        bool? favorite = null;
        bool? watched = null;
        bool unrated = false;
        NumericSearchCondition? rating = null;
        YearSearchCondition? year = null;
        var keywords = new List<string>();
        var actors = new List<string>();
        var directors = new List<string>();
        var tags = new List<string>();
        var customTags = new List<string>();
        var series = new List<string>();
        var studios = new List<string>();
        var libraries = new List<string>();

        foreach (string token in Tokenize(query)) {
            string normalized = token.Trim();
            string lower = normalized.ToLowerInvariant();

            if (TryParseBoolean(normalized, lower, "收藏", "favorite", out bool favoriteValue)) {
                favorite = favoriteValue;
                continue;
            }
            if (TryParseBoolean(normalized, lower, "已观看", "watched", out bool watchedValue)
                || TryParseBoolean(normalized, lower, "观看", "played", out watchedValue)) {
                watched = watchedValue;
                continue;
            }
            if (normalized is "已观看" or "看过" or "已播放" || lower is "watched" or "played") {
                watched = true;
                continue;
            }
            if (normalized is "未观看" or "未播放" || lower is "unwatched" or "unplayed") {
                watched = false;
                continue;
            }
            if (normalized is "未评分" || lower is "unrated") {
                unrated = true;
                continue;
            }
            if (normalized is "收藏" or "已收藏" || lower is "favorite" or "fav") {
                favorite = true;
                continue;
            }
            if (normalized is "未收藏" || lower is "unfavorite" or "notfavorite") {
                favorite = false;
                continue;
            }
            if (TryParseRating(normalized, out NumericSearchCondition? parsedRating)) {
                rating = parsedRating;
                continue;
            }
            if (TryParseYear(normalized, out YearSearchCondition? parsedYear)) {
                year = parsedYear;
                continue;
            }
            if (TryParseField(normalized, out string field, out string value)) {
                switch (NormalizeField(field)) {
                    case "actor": actors.Add(value); continue;
                    case "director": directors.Add(value); continue;
                    case "tag": tags.Add(value); continue;
                    case "customtag": customTags.Add(value); continue;
                    case "series": series.Add(value); continue;
                    case "studio": studios.Add(value); continue;
                    case "library": libraries.Add(value); continue;
                }
            }

            keywords.Add(normalized);
        }

        return new(SearchTerms(keywords), favorite, watched, unrated, rating, actors, directors, tags, customTags, series, studios, libraries, year);
    }

    private static IReadOnlyList<string> SearchTerms(IReadOnlyList<string> values) =>
        values.Select(NormalizeText).Where(value => value.Length > 0).ToList();

    private static IEnumerable<string> Tokenize(string query) =>
        WhitespaceRegex().Replace(query.Replace('\u3000', ' '), " ")
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeText(string value) => WhitespaceRegex().Replace(value.Replace('\u3000', ' '), " ").Trim();

    private static bool TryParseField(string token, out string field, out string value)
    {
        field = "";
        value = "";
        int index = token.IndexOfAny([':', '：']);
        if (index <= 0 || index == token.Length - 1) return false;
        field = token[..index].Trim();
        value = token[(index + 1)..].Trim();
        return field.Length > 0 && value.Length > 0;
    }

    private static string NormalizeField(string field) => field.Trim().ToLowerInvariant() switch
    {
        "演员" or "actor" or "actress" => "actor",
        "导演" or "director" => "director",
        "标签" or "tag" => "tag",
        "自定义标签" or "customtag" or "custom-tag" or "custom_tag" => "customtag",
        "系列" or "series" => "series",
        "厂商" or "制作方" or "studio" or "maker" => "studio",
        "媒体库" or "library" => "library",
        _ => "",
    };

    private static bool TryParseBoolean(string token, string lower, string chineseField, string englishField, out bool value)
    {
        value = false;
        foreach (string separator in new[] { ":", "：" }) {
            if (token.StartsWith(chineseField + separator, StringComparison.Ordinal)) {
                return bool.TryParse(token[(chineseField.Length + separator.Length)..], out value);
            }
            if (lower.StartsWith(englishField + separator, StringComparison.Ordinal)) {
                return bool.TryParse(lower[(englishField.Length + separator.Length)..], out value);
            }
        }
        return false;
    }

    private static bool TryParseRating(string token, out NumericSearchCondition? condition)
    {
        condition = null;
        string normalized = token.Trim()
            .Replace("＞", ">")
            .Replace("＜", "<")
            .Replace("＝", "=");

        if (normalized.EndsWith("星", StringComparison.Ordinal) &&
            double.TryParse(normalized[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double stars)) {
            condition = new(SearchComparison.Equal, stars);
            return true;
        }

        Match match = RatingRegex().Match(normalized);
        if (!match.Success) return false;
        if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return false;
        condition = new(ParseComparison(match.Groups["op"].Value), value);
        return true;
    }

    private static bool TryParseYear(string token, out YearSearchCondition? condition)
    {
        condition = null;
        string normalized = token.Trim()
            .Replace("＞", ">")
            .Replace("＜", "<")
            .Replace("＝", "=");
        Match match = YearRegex().Match(normalized);
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return false;
        condition = new(ParseComparison(match.Groups["op"].Value), value);
        return true;
    }

    private static SearchComparison ParseComparison(string value) => value switch
    {
        ">" => SearchComparison.GreaterThan,
        ">=" => SearchComparison.GreaterThanOrEqual,
        "<" => SearchComparison.LessThan,
        "<=" => SearchComparison.LessThanOrEqual,
        _ => SearchComparison.Equal,
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(?:评分|rating)\s*(?<op>>=|<=|>|<|=|:)?\s*(?<value>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex RatingRegex();

    [GeneratedRegex(@"^(?:年份|year)\s*(?<op>>=|<=|>|<|=|:)?\s*(?<value>\d{4})$", RegexOptions.IgnoreCase)]
    private static partial Regex YearRegex();
}
