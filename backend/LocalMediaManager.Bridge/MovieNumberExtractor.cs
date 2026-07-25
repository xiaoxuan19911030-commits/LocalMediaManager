using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LocalMediaManager.Bridge;

public sealed record MovieNumberExtractionResult(
    string OriginalFileName,
    string? DetectedNumber,
    string? NormalizedNumber,
    string? MatchedRule,
    double Confidence,
    int? PartIndex,
    IReadOnlyList<string> Warnings);

public interface IMovieNumberExtractor
{
    double MinimumAutoSyncConfidence { get; }
    MovieNumberExtractionResult Extract(string fileName);
    bool AreEquivalent(string expected, string actual);
}

public sealed class MovieNumberExtractor : IMovieNumberExtractor
{
    private readonly MovieNumberRuleSet rules;
    private readonly IReadOnlyList<CompiledCandidateRule> candidates;
    private readonly IReadOnlyList<CompiledTextRule> noise;
    private readonly IReadOnlyList<CompiledTextRule> suffixes;
    private readonly IReadOnlyList<CompiledTextRule> parts;
    private readonly IReadOnlyList<CompiledTextRule> comparisonAliases;

    public MovieNumberExtractor(string ruleFilePath)
    {
        string json = File.ReadAllText(ruleFilePath);
        rules = JsonSerializer.Deserialize<MovieNumberRuleSet>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("番号识别规则库为空。");
        Validate(rules);
        string tail = string.Join('|', rules.SuffixRules.Concat(rules.PartRules)
            .Select(rule => $"(?:{TrimAnchors(rule.Pattern)})"));
        candidates = rules.CandidateRules.OrderByDescending(rule => rule.Priority)
            .Select(rule => new CompiledCandidateRule(rule, Compile(rule.Pattern.Replace("{{TAIL}}", tail, StringComparison.Ordinal)))).ToArray();
        noise = rules.NoiseRules.Select(rule => new CompiledTextRule(rule.Id, Compile(rule.Pattern), rule.Replacement)).ToArray();
        suffixes = rules.SuffixRules.Select(rule => new CompiledTextRule(rule.Id, Compile(rule.Pattern), string.Empty)).ToArray();
        parts = rules.PartRules.Select(rule => new CompiledTextRule(rule.Id, Compile(rule.Pattern), string.Empty)).ToArray();
        comparisonAliases = (rules.ComparisonAliasRules ?? []).Select(rule =>
            new CompiledTextRule(rule.Id, Compile(rule.Pattern), rule.Replacement)).ToArray();
    }

    public double MinimumAutoSyncConfidence => rules.MinimumAutoSyncConfidence;

    public bool AreEquivalent(string expected, string actual)
    {
        string left = JavBusCode.Normalize(expected);
        string right = JavBusCode.Normalize(actual);
        foreach (CompiledTextRule rule in comparisonAliases) {
            left = rule.Regex.Replace(left, rule.Replacement);
            right = rule.Regex.Replace(right, rule.Replacement);
        }
        return left.Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    public MovieNumberExtractionResult Extract(string fileName)
    {
        string original = Path.GetFileName(fileName ?? string.Empty);
        string source = Path.GetFileNameWithoutExtension(original).Trim();
        var warnings = new List<string>();
        foreach (CompiledTextRule rule in noise) source = rule.Regex.Replace(source, rule.Replacement);

        var matches = new List<CandidateMatch>();
        foreach (CompiledCandidateRule rule in candidates)
            foreach (Match match in rule.Regex.Matches(source))
                matches.Add(new(rule, match, match.Result(rule.Rule.Replacement).ToUpperInvariant()));

        CandidateMatch[] distinct = matches
            .GroupBy(value => value.Normalized, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(value => value.Rule.Rule.Priority).First())
            .OrderByDescending(value => value.Rule.Rule.Priority).ThenBy(value => value.Match.Index).ToArray();
        if (distinct.Length == 0)
            return new(original, null, null, null, 0, null, ["NoCandidate"]);

        CandidateMatch selected = distinct[0];
        double confidence = selected.Rule.Rule.Confidence;
        CandidateMatch[] competing = distinct.Where(value => value.Rule.Rule.Priority == selected.Rule.Rule.Priority).ToArray();
        if (competing.Length > 1) {
            confidence = Math.Min(confidence, 0.69);
            warnings.Add($"MultipleCandidates:{string.Join(',', competing.Select(value => value.Normalized))}");
        }

        string tail = source[(selected.Match.Index + selected.Match.Length)..];
        var matchedRules = new List<string> { selected.Rule.Rule.Id };
        int? partIndex = null;
        bool advanced;
        do {
            advanced = false;
            foreach (CompiledTextRule rule in suffixes.Concat(parts)) {
                Match match = rule.Regex.Match(tail);
                if (!match.Success || match.Index != 0 || match.Length == 0) continue;
                matchedRules.Add(rule.Id);
                if (match.Groups["part"].Success && int.TryParse(match.Groups["part"].Value, out int parsed)) partIndex ??= parsed;
                tail = tail[match.Length..];
                advanced = true;
                break;
            }
        } while (advanced);

        if (confidence < rules.MinimumAutoSyncConfidence) warnings.Add("BelowAutoSyncThreshold");
        return new(original, selected.Match.Value.ToUpperInvariant(), selected.Normalized,
            string.Join(" + ", matchedRules), Math.Round(confidence, 2), partIndex, warnings);
    }

    private static Regex Compile(string pattern) => new(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static string TrimAnchors(string pattern) => pattern.StartsWith("(?i)^", StringComparison.Ordinal)
        ? "(?i:" + pattern[5..] + ")"
        : pattern.StartsWith('^') ? pattern[1..] : pattern;

    private static void Validate(MovieNumberRuleSet value)
    {
        if (value.MinimumAutoSyncConfidence is < 0 or > 1) throw new InvalidOperationException("番号识别自动同步阈值无效。");
        if (value.CandidateRules.Count == 0) throw new InvalidOperationException("番号识别规则库没有候选规则。");
        IEnumerable<string> ids = value.NoiseRules.Select(x => x.Id).Concat(value.CandidateRules.Select(x => x.Id))
            .Concat(value.SuffixRules.Select(x => x.Id)).Concat(value.PartRules.Select(x => x.Id))
            .Concat((value.ComparisonAliasRules ?? []).Select(x => x.Id));
        string? duplicate = ids.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1)?.Key;
        if (duplicate is not null) throw new InvalidOperationException($"番号识别规则 ID 重复：{duplicate}");
    }

    private sealed record CompiledCandidateRule(MovieNumberCandidateRule Rule, Regex Regex);
    private sealed record CompiledTextRule(string Id, Regex Regex, string Replacement);
    private sealed record CandidateMatch(CompiledCandidateRule Rule, Match Match, string Normalized);
}

public sealed record MovieNumberRuleSet(
    double MinimumAutoSyncConfidence,
    IReadOnlyList<MovieNumberTextRule> NoiseRules,
    IReadOnlyList<MovieNumberCandidateRule> CandidateRules,
    IReadOnlyList<MovieNumberTextRule> SuffixRules,
    IReadOnlyList<MovieNumberTextRule> PartRules,
    IReadOnlyList<MovieNumberTextRule>? ComparisonAliasRules = null);
public sealed record MovieNumberTextRule(string Id, string Pattern, string Replacement = "");
public sealed record MovieNumberCandidateRule(string Id, int Priority, string Pattern, string Replacement, double Confidence);

public sealed record MovieNumberUpdateCommand(string? Number = null);
public sealed record MovieNumberUpdateResult(bool Changed, string Message, MovieNumberExtractionResult Recognition);

public sealed class MovieNumberManagementService(string databasePath, IMovieNumberExtractor extractor)
{
    public async Task<MovieNumberUpdateResult> ReidentifyAsync(long movieId, string? manualNumber = null)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        await connection.OpenAsync();
        await using var read = connection.CreateCommand();
        read.CommandText = """
            SELECT COALESCE(m.Code,''),COALESCE(f.FileName,''),COALESCE(l.LibraryType,'Standard')
              FROM Movies m
              LEFT JOIN MediaFiles f ON f.MovieId=m.Id AND f.IsPrimary=1 AND f.MediaType='Video'
              LEFT JOIN Libraries l ON l.Id=f.LibraryId
             WHERE m.Id=$id LIMIT 1
            """;
        read.Parameters.AddWithValue("$id", movieId);
        await using var reader = await read.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new KeyNotFoundException("影片不存在。");
        string current = reader.GetString(0);
        string fileName = reader.GetString(1);
        if (!reader.GetString(2).Equals("Standard", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("普通媒体库不执行番号识别。");
        await reader.DisposeAsync();

        MovieNumberExtractionResult recognition = extractor.Extract(string.IsNullOrWhiteSpace(manualNumber) ? fileName : manualNumber.Trim());
        if (string.IsNullOrWhiteSpace(recognition.NormalizedNumber)) throw new ArgumentException("未识别到可用番号。");
        if (string.IsNullOrWhiteSpace(manualNumber) && recognition.Confidence < extractor.MinimumAutoSyncConfidence)
            throw new InvalidOperationException($"识别置信度 {recognition.Confidence:0.00} 低于自动应用阈值 {extractor.MinimumAutoSyncConfidence:0.00}。");

        bool changed = !current.Equals(recognition.NormalizedNumber, StringComparison.Ordinal);
        if (changed) {
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE Movies SET Code=$code,UpdatedAt=$at WHERE Id=$id";
            update.Parameters.AddWithValue("$code", recognition.NormalizedNumber);
            update.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("$id", movieId);
            await update.ExecuteNonQueryAsync();
        }
        return new(changed, changed ? "标准番号已更新；原始文件名和路径未修改。" : "番号无需更新。", recognition);
    }
}
