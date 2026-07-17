using System.Text.RegularExpressions;

namespace LocalMediaManager.Bridge;

public static partial class MovieCodeNormalizer
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string upper = value.ToUpperInvariant().Replace('_', '-');
        Match match = CodePattern().Match(upper);
        if (!match.Success) return null;
        string prefix = match.Groups["prefix"].Value;
        string number = match.Groups["number"].Value;
        return $"{prefix}-{number}";
    }

    [GeneratedRegex(@"(?<![A-Z0-9])(?<prefix>[A-Z]{2,10})[-\s_]*(?<number>\d{2,6})(?:[-\s_]*(?:C|4K|HD|FHD|UHD))?(?![A-Z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
