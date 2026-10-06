using SteamPosters.Core.IO;
using SteamPosters.Core.Steam;

namespace SteamPosters.Core.Tests;

public class FileSafetyTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9, 9];

    [Fact]
    public void SafeWrite_FailedVerification_LeavesOriginalIntact()
    {
        using var temp = new TempFolder();
        var path = temp.WriteFile("shortcuts.vdf", [1, 2, 3]);

        Assert.Throws<InvalidDataException>(() => SafeFileWriter.Write(path, [9, 9], _ => false));
        Assert.Throws<InvalidDataException>(() => SafeFileWriter.Write(path, [9, 9], _ => throw new FormatException()));

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void SafeWrite_ReplacesExistingAndCreatesNew()
    {
        using var temp = new TempFolder();
        var existing = temp.WriteFile("a.bin", [1]);
        var fresh = temp.Combine("b.bin");

        SafeFileWriter.Write(existing, [2], bytes => bytes.Length == 1);
        SafeFileWriter.Write(fresh, [3]);

        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(existing));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(fresh));
        Assert.Equal(2, Directory.GetFiles(temp.Path).Length);
    }

    [Fact]
    public void ShortcutsSave_RefusesCorruptOutput_ViaVerification()
    {
        using var temp = new TempFolder();
        var original = VdfBuilder.ShortcutsFile(b => b.Shortcut("0", "A", "a.exe", 1));
        var path = temp.WriteFile("shortcuts.vdf", original);

        // Bytes that don't parse as shortcuts.vdf must never replace the original.
        Assert.Throws<InvalidDataException>(() =>
            SafeFileWriter.Write(path, [0x00, 0x73, 0x00], b => ShortcutsFile.Parse(b) is not null));

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void GridArtwork_NamesFilesBySteamConvention()
    {
        var grid = new GridArtwork(@"C:\grid");

        Assert.Equal("3249773087p", GridArtwork.BaseName(3249773087, ArtworkKind.Poster));
        Assert.Equal("3249773087", GridArtwork.BaseName(3249773087, ArtworkKind.Wide));
        Assert.Equal("3249773087_hero", GridArtwork.BaseName(3249773087, ArtworkKind.Hero));
        Assert.Equal("3249773087_logo", GridArtwork.BaseName(3249773087, ArtworkKind.Logo));
        Assert.Equal("3249773087_icon", GridArtwork.BaseName(3249773087, ArtworkKind.Icon));
        Assert.Equal(Path.Combine(@"C:\grid", "5p.jpg"), grid.PathFor(5, ArtworkKind.Poster, "JPG"));
        Assert.Throws<ArgumentException>(() => grid.PathFor(5, ArtworkKind.Poster, ".webp"));
    }

    [Fact]
    public void GridArtwork_FindsExisting_AndWriteReplacesOtherExtension()
    {
        using var temp = new TempFolder();
        var grid = new GridArtwork(temp.Combine("grid"));
        temp.WriteFile(Path.Combine("grid", "7p.jpg"), Jpeg);
        temp.WriteFile(Path.Combine("grid", "7.png"), Png);
        temp.WriteFile(Path.Combine("grid", "7.json"), [0x7B, 0x7D]);

        Assert.Equal(new[] { grid.PathFor(7, ArtworkKind.Poster, ".jpg") }, grid.FindExisting(7, ArtworkKind.Poster));
        Assert.Equal(new[] { grid.PathFor(7, ArtworkKind.Wide, ".png") }, grid.FindExisting(7, ArtworkKind.Wide));
        Assert.Empty(grid.FindExisting(7, ArtworkKind.Hero));
        Assert.Equal(2, grid.PathsAffectedByWrite(7, ArtworkKind.Poster, Png).Count);

        var written = grid.Write(7, ArtworkKind.Poster, Png);

        Assert.Equal(grid.PathFor(7, ArtworkKind.Poster, ".png"), written);
        Assert.Equal(Png, File.ReadAllBytes(written));
        Assert.Equal(new[] { written }, grid.FindExisting(7, ArtworkKind.Poster));
        Assert.True(File.Exists(temp.Combine("grid", "7.json")));
    }

    [Fact]
    public void GridArtwork_RejectsNonImageBytes()
    {
        using var temp = new TempFolder();
        var grid = new GridArtwork(temp.Path);

        Assert.Throws<InvalidDataException>(() => grid.Write(1, ArtworkKind.Hero, [1, 2, 3]));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public void Backup_AndRestore_RoundTrip()
    {
        using var temp = new TempFolder();
        var vdf = temp.WriteFile(Path.Combine("steam", "shortcuts.vdf"), [1, 1, 1]);
        var poster = temp.WriteFile(Path.Combine("steam", "grid", "7p.jpg"), Jpeg);
        var newHero = temp.Combine("steam", "grid", "7_hero.png");
        var service = new BackupService(temp.Combine("backups"), () => new DateTime(2026, 10, 6, 3, 4, 5));

        var backup = service.Create([vdf, poster, newHero], "test apply");

        Assert.Equal("20261006-030405", Path.GetFileName(backup.Folder));
        Assert.Null(backup.Manifest.Entries.Single(e => e.OriginalPath == newHero).BackupFile);

        File.WriteAllBytes(vdf, [2, 2]);
        File.Delete(poster);
        File.WriteAllBytes(newHero, Png);

        var listed = Assert.Single(service.List());
        Assert.Equal("test apply", listed.Manifest.Description);
        service.Restore(listed);

        Assert.Equal(new byte[] { 1, 1, 1 }, File.ReadAllBytes(vdf));
        Assert.Equal(Jpeg, File.ReadAllBytes(poster));
        Assert.False(File.Exists(newHero));
    }

    [Fact]
    public void Backup_SameSecond_GetsSeparateFolders_AndListIsNewestFirst()
    {
        using var temp = new TempFolder();
        var file = temp.WriteFile("a.vdf", [1]);
        var time = new DateTime(2026, 10, 6, 3, 4, 5);
        var service = new BackupService(temp.Combine("backups"), () => time);

        var first = service.Create([file]);
        var second = service.Create([file]);
        time = time.AddMinutes(1);
        var third = service.Create([file]);

        Assert.NotEqual(first.Folder, second.Folder);
        Assert.Equal(third.Folder, service.List()[0].Folder);
    }
}
