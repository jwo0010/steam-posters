using System.Text;

namespace SteamPosters.Core.Steam;

/// <summary>
/// The legacy shortcut id: crc32(Exe + AppName) | 0x80000000. Current Steam stores its own
/// <c>appid</c> in shortcuts.vdf that does not follow this formula (verified on a real library),
/// so this is only a fallback for entries that have no stored appid.
/// </summary>
public static class ShortcutId
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(string exe, string appName) =>
        Crc32(Encoding.UTF8.GetBytes(exe + appName)) | 0x80000000u;

    public static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
