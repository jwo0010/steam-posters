using SteamPosters.Core.IO;
using SteamPosters.Core.Vdf;

namespace SteamPosters.Core.Steam;

/// <summary>
/// One non-Steam game in shortcuts.vdf. A thin view over its VDF map: setters change only the
/// targeted field, and every other field stays untouched in <see cref="Fields"/>.
/// </summary>
public sealed class Shortcut
{
    internal Shortcut(string index, VdfMap fields)
    {
        Index = index;
        Fields = fields;
    }

    /// <summary>The entry's key in the shortcuts map ("0", "1", ...).</summary>
    public string Index { get; }

    /// <summary>All fields, including ones this class has no property for.</summary>
    public VdfMap Fields { get; }

    /// <summary>The appid Steam stored for this entry, or null for old entries without one.</summary>
    public uint? StoredAppId => Fields.GetUInt32("appid");

    /// <summary>The legacy crc-based id. Only meaningful when <see cref="StoredAppId"/> is missing.</summary>
    public uint ComputedAppId => ShortcutId.Compute(Exe, AppName);

    /// <summary>The id artwork files are named after: the stored appid, else the computed one.</summary>
    public uint AppId => StoredAppId ?? ComputedAppId;

    public string AppName { get => Get("AppName"); set => Fields.SetString("AppName", value); }

    public string Exe { get => Get("Exe"); set => Fields.SetString("Exe", value); }

    public string StartDir { get => Get("StartDir"); set => Fields.SetString("StartDir", value); }

    public string Icon { get => Get("icon"); set => Fields.SetString("icon", value); }

    public string LaunchOptions { get => Get("LaunchOptions"); set => Fields.SetString("LaunchOptions", value); }

    public bool IsHidden
    {
        get => Fields.GetUInt32("IsHidden") is > 0;
        set => Fields.SetUInt32("IsHidden", value ? 1u : 0u);
    }

    private string Get(string key) => Fields.GetString(key) ?? "";

    public override string ToString() => $"[{Index}] {AppName} ({AppId})";
}

/// <summary>shortcuts.vdf: Steam's list of non-Steam games for one account.</summary>
public sealed class ShortcutsFile
{
    private ShortcutsFile(BinaryVdfDocument document, VdfMap shortcutsMap)
    {
        Document = document;
        ShortcutsMap = shortcutsMap;
        Shortcuts = shortcutsMap
            .Where(e => e.Type == VdfType.Map)
            .Select(e => new Shortcut(e.Key, e.Map!))
            .ToList();
    }

    public BinaryVdfDocument Document { get; }

    public VdfMap ShortcutsMap { get; }

    public IReadOnlyList<Shortcut> Shortcuts { get; }

    public static ShortcutsFile Parse(ReadOnlySpan<byte> data)
    {
        var document = BinaryVdfDocument.Parse(data);
        var map = document.Root.GetMap("shortcuts")
            ?? throw new InvalidDataException("shortcuts.vdf has no 'shortcuts' map.");
        return new ShortcutsFile(document, map);
    }

    public static ShortcutsFile Load(string path) => Parse(File.ReadAllBytes(path));

    public byte[] ToBytes() => Document.ToBytes();

    /// <summary>
    /// Writes the file safely: temp file in the same folder, re-parsed and compared before it
    /// replaces the original. Steam must be closed, or it will overwrite the file on exit.
    /// </summary>
    public void Save(string path)
    {
        var bytes = ToBytes();
        SafeFileWriter.Write(path, bytes, written =>
        {
            var reparsed = Parse(written);
            return reparsed.ToBytes().AsSpan().SequenceEqual(written)
                && reparsed.Shortcuts.Count == Shortcuts.Count;
        });
    }

    /// <summary>Entries with the same Exe and AppName (Steam allows this; they can still have different appids).</summary>
    public IReadOnlyList<IReadOnlyList<Shortcut>> FindDuplicates() =>
        Shortcuts.GroupBy(s => (s.Exe, s.AppName))
                 .Where(g => g.Count() > 1)
                 .Select(g => (IReadOnlyList<Shortcut>)g.ToList())
                 .ToList();

    /// <summary>
    /// Entries that would share artwork files because they resolve to the same id. Happens when
    /// duplicates have no stored appid; such entries need a stored appid before art can differ.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Shortcut>> FindArtworkIdConflicts() =>
        Shortcuts.GroupBy(s => s.AppId)
                 .Where(g => g.Count() > 1)
                 .Select(g => (IReadOnlyList<Shortcut>)g.ToList())
                 .ToList();
}
