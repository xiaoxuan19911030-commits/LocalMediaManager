namespace LocalMediaManager.Bridge;

public sealed record MdcNgPathMappingResult(string OriginalPath, string ProviderPath, MdcNgPathMappingDto Rule);

public static class MdcNgPathMapper
{
    public static IReadOnlyList<MdcNgPathMappingDto> Normalize(IEnumerable<MdcNgPathMappingDto> mappings)
    {
        if (!mappings.Any()) return Array.Empty<MdcNgPathMappingDto>();
        var result = new List<MdcNgPathMappingDto>();
        foreach (MdcNgPathMappingDto item in mappings.OrderBy(value => value.Order)) {
            string local = NormalizeLocalPrefix(item.LocalPathPrefix);
            string provider = NormalizeProviderPrefix(item.ProviderPathPrefix);
            if (result.Any(value => value.LocalPathPrefix.Equals(local, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Duplicate MDC-NG local path prefix: {local}");
            result.Add(new(local, provider, item.Enabled, result.Count));
        }
        return result;
    }

    public static MdcNgPathMappingResult? Map(string path, IEnumerable<MdcNgPathMappingDto> mappings)
    {
        string full = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
        foreach (MdcNgPathMappingDto rule in Normalize(mappings).Where(value => value.Enabled).OrderByDescending(value => value.LocalPathPrefix.Length)) {
            string prefix = rule.LocalPathPrefix;
            if (!full.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                && !full.StartsWith(prefix.EndsWith(Path.DirectorySeparatorChar) ? prefix : prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            string relative = full.Length == prefix.Length ? "" : full[(prefix.Length + (prefix.EndsWith(Path.DirectorySeparatorChar) ? 0 : 1))..];
            string mapped = rule.ProviderPathPrefix + (relative.Length == 0 ? "" : "/" + relative.Replace('\\', '/'));
            return new(path, mapped, rule);
        }
        return null;
    }

    private static string NormalizeLocalPrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)) throw new ArgumentException("MDC-NG Windows path prefix must be an absolute path.");
        string full = Path.GetFullPath(value.Replace('/', Path.DirectorySeparatorChar));
        string root = Path.GetPathRoot(full)!;
        return full.Equals(root, StringComparison.OrdinalIgnoreCase) ? root : full.TrimEnd(Path.DirectorySeparatorChar);
    }

    private static string NormalizeProviderPrefix(string value)
    {
        string clean = (value ?? "").Trim().Replace('\\', '/');
        if (!clean.StartsWith('/')) throw new ArgumentException("MDC-NG provider path prefix must be an absolute Unix path.");
        return clean == "/" ? clean : clean.TrimEnd('/');
    }
}
