using SteamPosters.Core.Steam;

namespace SteamPosters.Core.Tests;

public class ShortcutsFileTests
{
    private const string YuzuExe = "\"C:\\Users\\me\\AppData\\Local\\yuzu\\yuzu.exe\"";

    [Fact]
    public void Parse_ReadsFields()
    {
        var file = ShortcutsFile.Parse(VdfBuilder.ShortcutsFile(b => b
            .Shortcut("0", "Avowed", "\"F:\\games\\Avowed\\Avowed.exe\"", 3249773087)));

        var s = Assert.Single(file.Shortcuts);
        Assert.Equal("0", s.Index);
        Assert.Equal("Avowed", s.AppName);
        Assert.Equal("\"F:\\games\\Avowed\\Avowed.exe\"", s.Exe);
        Assert.Equal("F:\\games\\Avowed\\", s.StartDir);
        Assert.Equal("", s.Icon);
        Assert.False(s.IsHidden);
        Assert.Equal(3249773087u, s.StoredAppId);
        Assert.Equal(3249773087u, s.AppId);
        Assert.Equal("Favorites", s.Fields.GetMap("tags")!.GetString("0"));
    }

    [Fact]
    public void Rename_ChangesOnlyAppName_AndKeepsStoredAppId()
    {
        var original = VdfBuilder.ShortcutsFile(b => b
            .Shortcut("0", "Launcher", "\"D:\\Games\\Zelda\\Launcher.exe\"", 3702517508)
            .Shortcut("1", "Avowed", "\"F:\\Avowed.exe\"", 3249773087));
        var file = ShortcutsFile.Parse(original);

        file.Shortcuts[0].AppName = "The Legend of Zelda: Tears of the Kingdom";

        var expected = VdfBuilder.ShortcutsFile(b => b
            .Shortcut("0", "The Legend of Zelda: Tears of the Kingdom", "\"D:\\Games\\Zelda\\Launcher.exe\"", 3702517508)
            .Shortcut("1", "Avowed", "\"F:\\Avowed.exe\"", 3249773087));
        Assert.Equal(expected, file.ToBytes());

        var reparsed = ShortcutsFile.Parse(file.ToBytes());
        Assert.Equal(3702517508u, reparsed.Shortcuts[0].AppId);
        Assert.NotEqual(reparsed.Shortcuts[0].ComputedAppId, reparsed.Shortcuts[0].AppId);
    }

    [Fact]
    public void Setters_AddMissingFields_AndToggleIsHidden()
    {
        var file = ShortcutsFile.Parse(VdfBuilder.ShortcutsFile(b => b
            .Map("0").Str("AppName", "X").Str("Exe", "x.exe").End()));
        var s = file.Shortcuts[0];

        s.Icon = "C:\\icons\\x.png";
        s.IsHidden = true;

        var reparsed = ShortcutsFile.Parse(file.ToBytes()).Shortcuts[0];
        Assert.Equal("C:\\icons\\x.png", reparsed.Icon);
        Assert.True(reparsed.IsHidden);
        Assert.Equal("X", reparsed.AppName);
    }

    [Fact]
    public void MissingAppId_FallsBackToComputedId()
    {
        var file = ShortcutsFile.Parse(VdfBuilder.ShortcutsFile(b => b.Shortcut("0", "6789", "12345", appId: null)));

        var s = file.Shortcuts[0];
        Assert.Null(s.StoredAppId);
        // crc32("123456789") = 0xCBF43926 (high bit already set).
        Assert.Equal(0xCBF43926u, s.AppId);
    }

    [Fact]
    public void ShortcutId_MatchesCrc32CheckValue()
    {
        Assert.Equal(0xCBF43926u, ShortcutId.Crc32("123456789"u8));
        Assert.Equal(0x80000000u | ShortcutId.Crc32("a.exeGame"u8), ShortcutId.Compute("a.exe", "Game"));
    }

    [Fact]
    public void Duplicates_WithDifferentStoredIds_DoNotConflict()
    {
        var file = ShortcutsFile.Parse(VdfBuilder.ShortcutsFile(b => b
            .Shortcut("0", "yuzu", YuzuExe, 2852334743)
            .Shortcut("1", "Avowed", "\"F:\\Avowed.exe\"", 3249773087)
            .Shortcut("2", "yuzu", YuzuExe, 2264257811)));

        var group = Assert.Single(file.FindDuplicates());
        Assert.Equal(new[] { "0", "2" }, group.Select(s => s.Index));
        Assert.Empty(file.FindArtworkIdConflicts());
        Assert.NotEqual(file.Shortcuts[0].AppId, file.Shortcuts[2].AppId);
    }

    [Fact]
    public void Duplicates_WithoutStoredIds_ConflictOnArtwork()
    {
        var file = ShortcutsFile.Parse(VdfBuilder.ShortcutsFile(b => b
            .Shortcut("0", "yuzu", YuzuExe, appId: null)
            .Shortcut("1", "yuzu", YuzuExe, appId: null)));

        var conflict = Assert.Single(file.FindArtworkIdConflicts());
        Assert.Equal(2, conflict.Count);
    }

    [Fact]
    public void Parse_RequiresShortcutsMap()
    {
        var bytes = new VdfBuilder().Map("something").End().End().ToArray();

        Assert.Throws<InvalidDataException>(() => ShortcutsFile.Parse(bytes));
    }

    [Fact]
    public void Save_WritesFileThatReparses()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "shortcuts.vdf");
        var original = VdfBuilder.ShortcutsFile(b => b.Shortcut("0", "Old", "a.exe", 5));
        File.WriteAllBytes(path, original);

        var file = ShortcutsFile.Load(path);
        file.Shortcuts[0].AppName = "New";
        file.Save(path);

        Assert.Equal("New", ShortcutsFile.Load(path).Shortcuts[0].AppName);
        Assert.Single(Directory.GetFiles(temp.Path));
    }
}
