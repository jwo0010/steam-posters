using System.Text;

namespace SteamPosters.Core.Tests;

/// <summary>
/// Builds binary VDF bytes by hand, independently of the code under test, so parser tests
/// don't just check the writer against itself.
/// </summary>
internal sealed class VdfBuilder
{
    private readonly MemoryStream _stream = new();

    public VdfBuilder Map(string key)
    {
        _stream.WriteByte(0x00);
        Key(key);
        return this;
    }

    public VdfBuilder Str(string key, string value) => Str(key, Encoding.UTF8.GetBytes(value));

    public VdfBuilder Str(string key, byte[] rawValue)
    {
        _stream.WriteByte(0x01);
        Key(key);
        _stream.Write(rawValue);
        _stream.WriteByte(0);
        return this;
    }

    public VdfBuilder Int(string key, uint value)
    {
        _stream.WriteByte(0x02);
        Key(key);
        _stream.Write(BitConverter.GetBytes(value));
        return this;
    }

    public VdfBuilder Typed(byte type, string key, byte[] payload)
    {
        _stream.WriteByte(type);
        Key(key);
        _stream.Write(payload);
        return this;
    }

    public VdfBuilder End()
    {
        _stream.WriteByte(0x08);
        return this;
    }

    public VdfBuilder Shortcut(string index, string appName, string exe, uint? appId, string appNameKey = "AppName")
    {
        Map(index);
        if (appId is { } id) Int("appid", id);
        Str(appNameKey, appName);
        Str("Exe", exe);
        Str("StartDir", Path.GetDirectoryName(exe.Trim('"')) + "\\");
        Str("icon", "");
        Str("ShortcutPath", "");
        Str("LaunchOptions", "");
        Int("IsHidden", 0);
        Int("AllowDesktopConfig", 1);
        Int("AllowOverlay", 1);
        Int("OpenVR", 0);
        Int("Devkit", 0);
        Str("DevkitGameID", "");
        Int("DevkitOverrideAppID", 0);
        Int("LastPlayTime", 1700000000);
        Str("FlatpakAppID", "");
        Map("tags").Str("0", "Favorites").End();
        return End();
    }

    public byte[] ToArray() => _stream.ToArray();

    /// <summary>A shortcuts.vdf with the given entries already built: root map, entries, both terminators.</summary>
    public static byte[] ShortcutsFile(Action<VdfBuilder> entries)
    {
        var b = new VdfBuilder().Map("shortcuts");
        entries(b);
        return b.End().End().ToArray();
    }

    private void Key(string key)
    {
        _stream.Write(Encoding.UTF8.GetBytes(key));
        _stream.WriteByte(0);
    }
}
