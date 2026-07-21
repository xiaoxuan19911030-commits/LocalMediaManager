using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace LocalMediaManager.Bridge;

public static class MdcNgAdapter
{
    public static MovieMetadata ToMovieMetadata(JsonElement source, string? fallbackCode = null)
    {
        string code = NormalizeCode(FirstString(source, "number", "code", "num", "id", "dvdid", "movie_id") ?? fallbackCode ?? "");
        string? poster = FirstUrl(source, "poster", "poster_url", "cover_url", "big_cover_url");
        string? thumb = FirstUrl(source, "thumb", "thumbnail", "thumb_url", "small_cover_url");
        string? fanart = FirstUrl(source, "fanart", "cover", "backdrop", "backdrop_url", "background", "background_url");
        IReadOnlyList<ActorImageMetadata> actorImages = ActorImages(source);
        IReadOnlyList<string> extraFanart = Values(source, "extrafanart", "extra_fanart", "extra_fanarts", "preview_images", "sample_images", "screenshots")
            .Where(IsHttpUrl)
            .Select(StripQuery)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
        string? trailer = FirstUrl(source, "trailer", "trailer_url", "preview_video", "sample_video");

        return new MovieMetadata(
            "MDC-NG",
            FirstString(source, "external_id", "job_id", "task_id", "id"),
            code,
            FirstString(source, "title", "name"),
            FirstString(source, "original_title", "originalTitle", "originaltitle", "sorttitle"),
            FirstString(source, "summary", "plot", "outline", "description", "desc"),
            Values(source, "actors", "actor", "actress", "actresses", "stars", "cast"),
            FirstString(source, "director", "directors"),
            FirstString(source, "studio", "maker", "publisher", "label", "manufacturer"),
            FirstString(source, "series", "set"),
            Values(source, "tags", "tag", "genres", "genre", "categories", "category"),
            FirstString(source, "country", "countries"),
            NormalizeDate(FirstString(source, "release_date", "releasedate", "release", "date", "premiered")),
            NormalizeDuration(FirstString(source, "duration", "runtime", "length")),
            NormalizeRating(FirstString(source, "rating", "score", "user_rating", "UserRating")),
            poster,
            thumb,
            fanart,
            extraFanart,
            trailer,
            actorImages);
    }

    private static string? FirstString(JsonElement source, params string[] names)
    {
        foreach (string name in names) {
            if (TryFind(source, name, out JsonElement value)) {
                string? text = StringValue(value);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        return null;
    }

    private static string? FirstUrl(JsonElement source, params string[] names)
    {
        string? value = FirstString(source, names);
        return IsHttpUrl(value) ? StripQuery(value!) : null;
    }

    private static IReadOnlyList<string> Values(JsonElement source, params string[] names)
    {
        var values = new List<string>();
        foreach (string name in names) {
            if (TryFind(source, name, out JsonElement value)) CollectValues(value, values);
        }
        return values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ActorImageMetadata> ActorImages(JsonElement source)
    {
        var result = new List<ActorImageMetadata>();
        foreach (string name in new[] { "actors", "actor", "actress", "actresses", "stars", "cast" }) {
            if (TryFind(source, name, out JsonElement value)) CollectActorImages(value, result);
        }
        if (result.Count == 0) {
            IReadOnlyList<string> names = Values(source, "actors", "actor", "actress", "actresses", "stars", "cast");
            IReadOnlyList<string> photos = Values(source, "actor_photos", "actorphotos", "actor_images", "actorimages", "actor_avatars", "actoravatars");
            for (int i = 0; i < Math.Min(names.Count, photos.Count); i++) {
                if (IsHttpUrl(photos[i])) result.Add(new(names[i], StripQuery(photos[i])));
            }
        }
        return result
            .Where(value => !string.IsNullOrWhiteSpace(value.Name) && IsHttpUrl(value.ImageUrl))
            .DistinctBy(value => NormalizeActorKey(value.Name))
            .ToArray();
    }

    private static void CollectActorImages(JsonElement value, List<ActorImageMetadata> result)
    {
        switch (value.ValueKind) {
            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray()) CollectActorImages(item, result);
                break;
            case JsonValueKind.Object:
                string? name = FirstDirectString(value, "name", "title", "value", "label");
                string? image = FirstDirectUrl(value, "image", "image_url", "avatar", "avatar_url", "poster", "photo", "portrait", "thumb", "thumbnail", "url");
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(image)) result.Add(new(name, StripQuery(image)));
                break;
        }
    }

    private static void CollectValues(JsonElement value, List<string> values)
    {
        switch (value.ValueKind) {
            case JsonValueKind.String:
                string? text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text) && text.IndexOf(',') >= 0) {
                    foreach (string part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        values.Add(part);
                }
                else if (IsHttpUrl(text)) values.Add(text!);
                else AddSplitValues(text, values);
                break;
            case JsonValueKind.Number:
                values.Add(value.ToString());
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray()) CollectValues(item, values);
                break;
            case JsonValueKind.Object:
                string? name = FirstDirectString(value, "name", "title", "value", "label", "url");
                if (!string.IsNullOrWhiteSpace(name)) values.Add(name);
                break;
        }
    }

    private static string? StringValue(JsonElement value)
    {
        return value.ValueKind switch {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => value.EnumerateArray().Select(StringValue).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)),
            JsonValueKind.Object => FirstDirectString(value, "name", "title", "value", "label", "url"),
            _ => null,
        };
    }

    private static string? FirstDirectString(JsonElement source, params string[] names)
    {
        foreach (JsonProperty property in source.EnumerateObject()) {
            if (!names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) continue;
            string? value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()?.Trim() : property.Value.ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static string? FirstDirectUrl(JsonElement source, params string[] names)
    {
        foreach (JsonProperty property in source.EnumerateObject()) {
            if (!names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) continue;
            string? value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()?.Trim() : property.Value.ToString();
            if (IsHttpUrl(value)) return value;
        }
        return null;
    }

    private static bool TryFind(JsonElement source, string name, out JsonElement value)
    {
        if (source.ValueKind == JsonValueKind.Object) {
            foreach (JsonProperty property in source.EnumerateObject()) {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                    value = property.Value;
                    return true;
                }
            }
            foreach (JsonProperty property in source.EnumerateObject()) {
                if (TryFind(property.Value, name, out value)) return true;
            }
        } else if (source.ValueKind == JsonValueKind.Array) {
            foreach (JsonElement item in source.EnumerateArray()) {
                if (TryFind(item, name, out value)) return true;
            }
        }
        value = default;
        return false;
    }

    private static void AddSplitValues(string? text, List<string> values)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (string part in Regex.Split(text, @"[,;/|，、]")) {
            string value = part.Trim();
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }
    }

    private static string NormalizeCode(string value) =>
        string.Join(' ', (value ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant().Replace('_', '-');

    private static string NormalizeActorKey(string value) =>
        string.Join(' ', (value ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string? NormalizeDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime date) && date.Year > 1900
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    private static int? NormalizeDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains(':') && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan time) && time.TotalSeconds > 0)
            return (int)time.TotalSeconds;
        if (int.TryParse(Regex.Match(value, @"\d+").Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number > 0)
            return number * 60;
        return null;
    }

    private static decimal? NormalizeRating(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rating) ? rating : null;

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https";

    private static string StripQuery(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            ? new UriBuilder(uri) { Query = "", Fragment = "" }.Uri.ToString()
            : value;
}
