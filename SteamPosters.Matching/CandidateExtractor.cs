using System.Text.RegularExpressions;
using SteamPosters.Core.Steam;

namespace SteamPosters.Matching;

public enum TermSource
{
    /// <summary>A game file passed to an emulator in LaunchOptions.</summary>
    LaunchOptionsFile,
    /// <summary>The name shown in Steam (AppName).</summary>
    AppName,
    /// <summary>The executable's file name.</summary>
    ExeName,
    /// <summary>A folder the executable sits in.</summary>
    Folder,
}

/// <summary>A cleaned-up name to search for, and where it came from.</summary>
public sealed record SearchTerm(string Text, TermSource Source);

/// <summary>What to search for one shortcut, and whether it is an emulator or tool.</summary>
public sealed record Candidates(IReadOnlyList<SearchTerm> Terms, bool IsEmulatorOrTool, string? ToolName);

/// <summary>Builds ordered search terms for a non-Steam shortcut from its name, exe path and launch options.</summary>
public static partial class CandidateExtractor
{
    /// <summary>Known emulators and tools, matched against the start of the exe name (letters and digits only).</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownTools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["yuzu"] = "yuzu", ["ryujinx"] = "Ryujinx", ["cemu"] = "Cemu", ["retroarch"] = "RetroArch",
        ["dolphin"] = "Dolphin", ["pcsx2"] = "PCSX2", ["rpcs3"] = "RPCS3", ["xenia"] = "Xenia",
        ["ppsspp"] = "PPSSPP", ["duckstation"] = "DuckStation", ["citra"] = "Citra",
        ["steamrommanager"] = "Steam ROM Manager",
    };

    /// <summary>Folder names that never identify a game; skipped when walking up from the exe.</summary>
    public static readonly IReadOnlySet<string> GenericFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "binaries", "win64", "win32", "x64", "x86", "game", "games", "publish", "release", "retail",
        "windows", "steamapps", "common", "steamlibrary", "program files", "program files (x86)",
        "users", "appdata", "local", "roaming", "desktop", "downloads", "documents", "engine", "shipping",
        "epic games", "gog games", "xboxgames", "content",
    };

    private static readonly HashSet<string> NonGameExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".ini", ".cfg", ".json", ".xml", ".txt", ".log",
    };

    public const int MaxFolders = 3;

    public static Candidates Extract(Shortcut shortcut) =>
        Extract(shortcut.AppName, shortcut.Exe, shortcut.LaunchOptions);

    public static Candidates Extract(string appName, string exe, string? launchOptions = null)
    {
        var exePath = exe.Trim().Trim('"');
        var exeStem = SafeFileNameWithoutExtension(exePath);
        var tool = FindTool(exeStem);

        var terms = new List<SearchTerm>();
        void Add(string? raw, TermSource source)
        {
            var text = NameCleaner.Clean(raw);
            if (text.Length >= 2 && !terms.Any(t => string.Equals(t.Text, text, StringComparison.OrdinalIgnoreCase)))
                terms.Add(new SearchTerm(text, source));
        }

        if (tool is not null)
        {
            // An emulator's own name says nothing about the game; only the file it launches does.
            if (GameFileName(launchOptions) is { } gameFile) Add(gameFile, TermSource.LaunchOptionsFile);
            return new Candidates(terms, true, tool);
        }

        if (!LooksLikeExeName(appName, exeStem)) Add(appName, TermSource.AppName);
        Add(exeStem, TermSource.ExeName);
        foreach (var folder in MeaningfulFolders(exePath)) Add(folder, TermSource.Folder);
        return new Candidates(terms, false, null);
    }

    /// <summary>The cleaned-up name of the game file in emulator launch options, or null.</summary>
    public static string? GameFileName(string? launchOptions)
    {
        if (string.IsNullOrWhiteSpace(launchOptions)) return null;
        foreach (Match m in PathToken().Matches(launchOptions))
        {
            var path = m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["u"].Value;
            var ext = SafeExtension(path);
            if (ext.Length < 2 || NonGameExtensions.Contains(ext)) continue;

            var name = NameCleaner.Clean(SafeFileNameWithoutExtension(path));
            if (name.Length >= 2 && !name.Equals("eboot", StringComparison.OrdinalIgnoreCase)) return name;

            // e.g. ...\Game Title\PS3_GAME\USRDIR\EBOOT.BIN: use the nearest meaningful folder.
            var folder = MeaningfulFolders(path).FirstOrDefault(f => !f.Contains("USRDIR", StringComparison.OrdinalIgnoreCase)
                                                                   && !f.Contains("PS3_GAME", StringComparison.OrdinalIgnoreCase));
            if (folder is not null) return NameCleaner.Clean(folder);
        }
        return null;
    }

    private static string? FindTool(string exeStem)
    {
        var key = new string(exeStem.Where(char.IsLetterOrDigit).ToArray());
        return KnownTools.FirstOrDefault(t => key.StartsWith(t.Key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    /// <summary>True for app names that are just the exe ("Game.exe - Shortcut", "ACBlackFlag" for ACBlackFlag.exe).</summary>
    private static bool LooksLikeExeName(string appName, string exeStem)
    {
        var name = appName.Trim();
        if (ExeLike().IsMatch(name)) return true;
        return exeStem.Length > 0 && string.Equals(name, exeStem, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> MeaningfulFolders(string filePath)
    {
        string? dir;
        try { dir = Path.GetDirectoryName(filePath); }
        catch (ArgumentException) { yield break; }

        var found = 0;
        while (!string.IsNullOrEmpty(dir) && found < MaxFolders)
        {
            var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(name) || name.EndsWith(':')) yield break;   // drive root
            if (!GenericFolders.Contains(name) && NameCleaner.Clean(name).Length >= 2)
            {
                found++;
                yield return name;
            }
            dir = Path.GetDirectoryName(dir);
        }
    }

    private static string SafeFileNameWithoutExtension(string path)
    {
        try { return Path.GetFileNameWithoutExtension(path); }
        catch (ArgumentException) { return path; }
    }

    private static string SafeExtension(string path)
    {
        try { return Path.GetExtension(path); }
        catch (ArgumentException) { return ""; }
    }

    [GeneratedRegex(@"\.exe(\s*-\s*shortcut)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ExeLike();

    /// <summary>A quoted string, or an unquoted token containing a path separator.</summary>
    [GeneratedRegex(@"""(?<q>[^""]+)""|(?<u>[^\s""]*[\\/][^\s""]*)")]
    private static partial Regex PathToken();
}
