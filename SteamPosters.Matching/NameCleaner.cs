using System.Text;
using System.Text.RegularExpressions;

namespace SteamPosters.Matching;

/// <summary>
/// Cleans raw names (app names, exe and folder names) into searchable text: strips extensions and
/// "- Shortcut", bracketed tags and version numbers, splits CamelCase and separators, and drops
/// words that say nothing about which game it is.
/// </summary>
public static partial class NameCleaner
{
    /// <summary>Whole words dropped from every name, case-insensitively. Extend here.</summary>
    public static readonly IReadOnlySet<string> NoiseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "launcher", "shipping", "win64", "win32", "x64", "x86", "x64vk", "dx11", "dx12", "vulkan",
        "binaries", "bin", "retail", "release", "publish", "canary", "game", "shortcut", "exe",
        "debug", "portable", "msvc", "qt",
    };

    /// <summary>
    /// Store, repack or scene names that get appended to folder names with a dash
    /// ("Starfield-AnkerGames"). Only these exact names are stripped, to stay conservative.
    /// </summary>
    public static readonly IReadOnlySet<string> DistributorTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AnkerGames", "FitGirl", "DODI", "ElAmigos", "Repack", "GOG", "SteamRip", "Empress", "CODEX",
        "PLAZA", "SKIDROW", "RUNE", "TENOKE", "Razor1911", "CPY", "FLT", "DARKSiDERS", "KaOs",
    };

    /// <summary>Cleans one raw name. Returns "" when nothing meaningful is left.</summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim().Trim('"');
        s = ShortcutSuffix().Replace(s, "");
        s = Extension().Replace(s, "");
        s = StripDistributorSuffix(s);
        s = Brackets().Replace(s, " ");                // [FitGirl Repack], (x64), [0100F2C0115B6000]
        s = VersionToken().Replace(s, " ");            // v1.2.3, 1.3.287, v0
        // Drop noise both before and after CamelCase splitting ("Win64" whole, "MyGame" -> "My").
        var words = Separators().Split(s)
            .Where(w => w.Length > 0 && !NoiseWords.Contains(w))
            .SelectMany(w => SplitCamelCase(w).Split(' '))
            .Where(w => w.Length > 0 && !NoiseWords.Contains(w));
        return string.Join(' ', words);
    }

    /// <summary>"Starfield-AnkerGames" -> "Starfield". Leaves "Half-Life" and unknown suffixes alone.</summary>
    public static string StripDistributorSuffix(string s)
    {
        var dash = s.LastIndexOf('-');
        if (dash <= 0) return s;
        var tag = s[(dash + 1)..].Trim();
        return DistributorTags.Contains(tag) ? s[..dash].TrimEnd() : s;
    }

    /// <summary>"ACBlackFlag" -> "AC Black Flag", "Cyberpunk2077" -> "Cyberpunk 2077"; "TotK" stays whole.</summary>
    public static string SplitCamelCase(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i > 0)
            {
                var prev = s[i - 1];
                var next = i + 1 < s.Length ? s[i + 1] : '\0';
                var boundary =
                    (char.IsLower(prev) && char.IsUpper(c) && char.IsLower(next)) ||    // fooBar, but not TotK
                    (char.IsUpper(prev) && char.IsUpper(c) && char.IsLower(next)) ||    // ACBlack
                    (char.IsLetter(prev) && char.IsDigit(c)) ||                         // Cyberpunk2077
                    (char.IsDigit(prev) && char.IsLetter(c));                           // 4Dead
                if (boundary) sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"\s*-\s*shortcut\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ShortcutSuffix();

    [GeneratedRegex(@"\.(exe|lnk|bat|cmd|url)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Extension();

    [GeneratedRegex(@"[\[\(\{][^\]\)\}]*[\]\)\}]")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(v\d+(\.\d+){0,3}|\d+(\.\d+){1,3})(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex VersionToken();

    [GeneratedRegex(@"[_.+\-]|\s+")]
    private static partial Regex Separators();
}
