using SteamPosters.Core.Vdf;

namespace SteamPosters.Core.Steam;

public sealed record SteamAccount(
    string AccountId,
    ulong SteamId64,
    string? PersonaName,
    bool MostRecent,
    string UserDataPath,
    int ShortcutCount)
{
    public string ConfigPath => Path.Combine(UserDataPath, "config");
    public string ShortcutsPath => Path.Combine(ConfigPath, "shortcuts.vdf");
    public string GridPath => Path.Combine(ConfigPath, "grid");
}

/// <summary>Finds the Steam install and the accounts that have used it on this PC.</summary>
public sealed class SteamLocator
{
    public const ulong SteamId64Base = 76561197960265728;
    public const string DefaultSteamPath = @"C:\Program Files (x86)\Steam";

    private readonly Func<string?> _readRegistrySteamPath;
    private readonly string _fallbackPath;

    public SteamLocator(Func<string?>? readRegistrySteamPath = null, string fallbackPath = DefaultSteamPath)
    {
        _readRegistrySteamPath = readRegistrySteamPath ?? ReadRegistrySteamPath;
        _fallbackPath = fallbackPath;
    }

    /// <summary>The Steam folder, or null if neither the registry path nor the fallback exists.</summary>
    public string? FindSteamPath()
    {
        var fromRegistry = _readRegistrySteamPath();
        if (!string.IsNullOrWhiteSpace(fromRegistry))
        {
            var normalized = Path.GetFullPath(fromRegistry.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(normalized)) return normalized;
        }
        return Directory.Exists(_fallbackPath) ? _fallbackPath : null;
    }

    public IReadOnlyList<SteamAccount> GetAccounts(string steamPath)
    {
        var userdata = Path.Combine(steamPath, "userdata");
        if (!Directory.Exists(userdata)) return [];

        var loginUsers = ReadLoginUsers(Path.Combine(steamPath, "config", "loginusers.vdf"));
        var accounts = new List<SteamAccount>();
        foreach (var dir in Directory.EnumerateDirectories(userdata).OrderBy(d => d, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(dir);
            if (!ulong.TryParse(name, out var accountId) || accountId == 0) continue;

            var steamId64 = accountId + SteamId64Base;
            loginUsers.TryGetValue(steamId64.ToString(), out var user);
            var persona = user?.GetString("PersonaName");
            var mostRecent = user?.GetString("MostRecent") == "1";
            accounts.Add(new SteamAccount(name, steamId64, persona, mostRecent, dir, CountShortcuts(dir)));
        }
        return accounts;
    }

    /// <summary>The MostRecent account if flagged, else the first with non-Steam games, else the first.</summary>
    public static SteamAccount? GetDefaultAccount(IReadOnlyList<SteamAccount> accounts) =>
        accounts.FirstOrDefault(a => a.MostRecent)
        ?? accounts.FirstOrDefault(a => a.ShortcutCount > 0)
        ?? accounts.FirstOrDefault();

    private static Dictionary<string, TextVdfObject> ReadLoginUsers(string path)
    {
        var result = new Dictionary<string, TextVdfObject>();
        if (!File.Exists(path)) return result;
        try
        {
            var users = TextVdf.Load(path).GetObject("users");
            if (users is null) return result;
            foreach (var (id, user) in users.Children) result[id] = user;
        }
        catch (FormatException)
        {
            // A damaged loginusers.vdf only costs us persona names.
        }
        return result;
    }

    private static int CountShortcuts(string userDataPath)
    {
        var path = Path.Combine(userDataPath, "config", "shortcuts.vdf");
        if (!File.Exists(path)) return 0;
        try
        {
            return ShortcutsFile.Load(path).Shortcuts.Count;
        }
        catch (InvalidDataException)
        {
            return 0;
        }
    }

    private static string? ReadRegistrySteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") as string;
    }
}
