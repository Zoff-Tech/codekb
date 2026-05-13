using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodeKb.Scanner.Roslyn.Detection;

public sealed record ConfigMatch(
    string FilePath,
    string FileFormat,
    string Key,
    string? Value,
    bool ValueRedacted,
    string JsonPath,
    int LineStart,
    int LineEnd);

public interface IConfigFileScanner
{
    IReadOnlyList<ConfigMatch> Scan(string relativePath, string content, IReadOnlyList<string> searchTerms);
}

public sealed class ConfigFileScanner : IConfigFileScanner
{
    private static readonly Regex FeatureSectionRegex = new(@"feature|flag|toggle", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AppSettingsRegex = new(@"appsettings.*\.(json|ya?ml)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<ConfigMatch> Scan(string relativePath, string content, IReadOnlyList<string> searchTerms)
    {
        var normalized = relativePath.Replace('\\', '/');
        var ext = Path.GetExtension(normalized).ToLowerInvariant();
        return ext switch
        {
            ".env" => ScanEnv(normalized, content, searchTerms),
            ".json" => ScanJson(normalized, content, searchTerms),
            _ => Array.Empty<ConfigMatch>(),
        };
    }

    public static bool IsAppSettingsFile(string path)
        => AppSettingsRegex.IsMatch(path) || path.Contains("appsettings", StringComparison.OrdinalIgnoreCase);

    public static bool IsFeatureSection(string section) => FeatureSectionRegex.IsMatch(section);

    internal static IReadOnlyList<ConfigMatch> ScanEnv(string path, string content, IReadOnlyList<string> searchTerms)
    {
        var matches = new List<ConfigMatch>();
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line.Substring(0, eq).Trim();
            // Always redact .env values
            bool keyInTerms = searchTerms.Any(t => string.Equals(t, key, StringComparison.Ordinal));
            if (keyInTerms || searchTerms.Count == 0)
            {
                matches.Add(new ConfigMatch(
                    FilePath: path,
                    FileFormat: "env",
                    Key: key,
                    Value: null,
                    ValueRedacted: true,
                    JsonPath: $"$.{key}",
                    LineStart: i + 1,
                    LineEnd: i + 1));
            }
        }
        return matches;
    }

    internal static IReadOnlyList<ConfigMatch> ScanJson(string path, string content, IReadOnlyList<string> searchTerms)
    {
        var matches = new List<ConfigMatch>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(content); }
        catch { return matches; }

        var isAppSettings = IsAppSettingsFile(path);
        WalkJson(doc.RootElement, "$", path, content, searchTerms, isAppSettings, parentInFeatureSection: false, matches);
        return matches;
    }

    private static void WalkJson(JsonElement el, string jsonPath, string path, string content, IReadOnlyList<string> searchTerms, bool isAppSettings, bool parentInFeatureSection, List<ConfigMatch> matches)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                var childPath = jsonPath + "." + prop.Name;
                var inFeatureSection = parentInFeatureSection || IsFeatureSection(prop.Name);
                bool emit = false;
                bool searchTermHit = searchTerms.Any(t => string.Equals(t, prop.Name, StringComparison.Ordinal));

                if (prop.Value.ValueKind != JsonValueKind.Object && prop.Value.ValueKind != JsonValueKind.Array)
                {
                    // Leaf key: emit if parent section was feature/flag/toggle, OR if appsettings file, OR if a --search term matches the key
                    if (parentInFeatureSection || isAppSettings || searchTermHit)
                        emit = true;
                }

                if (emit)
                {
                    var value = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        _ => prop.Value.GetRawText(),
                    };
                    matches.Add(new ConfigMatch(
                        FilePath: path,
                        FileFormat: "json",
                        Key: prop.Name,
                        Value: value,
                        ValueRedacted: false,
                        JsonPath: childPath,
                        LineStart: 1,
                        LineEnd: 1));
                }

                WalkJson(prop.Value, childPath, path, content, searchTerms, isAppSettings, inFeatureSection, matches);
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var item in el.EnumerateArray())
            {
                WalkJson(item, jsonPath + $"[{i}]", path, content, searchTerms, isAppSettings, parentInFeatureSection, matches);
                i++;
            }
        }
    }
}
