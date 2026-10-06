namespace SteamPosters.Matching.Tests;

public class CandidateExtractorTests
{
    private static IEnumerable<(string, TermSource)> Terms(Candidates c) => c.Terms.Select(t => (t.Text, t.Source));

    [Fact]
    public void LauncherExe_FallsBackToGameFolder_AndSpellsOutKnownAbbreviation()
    {
        var c = CandidateExtractor.Extract("Launcher", @"""D:\Games\The Legend of Zelda - TotK\Launcher.exe""");

        Assert.False(c.IsEmulatorOrTool);
        Assert.Equal(new[]
        {
            ("The Legend of Zelda TotK", TermSource.Folder),
            ("The Legend of Zelda Tears of the Kingdom", TermSource.Expanded),
        }, Terms(c));
    }

    [Fact]
    public void NestedStoreFolders_AreDeduplicated_AndAppNameEqualToExeCountsAsExe()
    {
        var c = CandidateExtractor.Extract("Starfield", @"""E:\games\Starfield\Starfield-SomeStore\Starfield\Starfield.exe""");

        Assert.Equal(new[]
        {
            ("Starfield", TermSource.ExeName),
            ("Starfield Some Store", TermSource.Folder),
        }, Terms(c));
    }

    [Fact]
    public void AppNameEqualToExe_IsSkipped_AndFoldersWalkUp()
    {
        var c = CandidateExtractor.Extract("ACBlackFlag",
            @"E:\games\Assassins-Creed-Black-Flag-Resynced\Assassins-Creed-Black-Flag-Resynced\Assassins Creed Black Flag Resynced\ACBlackFlag.exe");

        Assert.Equal(new[]
        {
            ("AC Black Flag", TermSource.ExeName),
            ("Assassin's Creed Black Flag", TermSource.Expanded),
            ("Assassins Creed Black Flag Resynced", TermSource.Folder),
        }, Terms(c));
    }

    [Fact]
    public void GenericFoldersAndDriveRoot_AreSkipped()
    {
        var c = CandidateExtractor.Extract("Shooter Thing", @"C:\Program Files (x86)\Steam\steamapps\common\Cool Shooter\Binaries\Win64\CoolShooter-Win64-Shipping.exe");

        Assert.Equal(new[]
        {
            ("Shooter Thing", TermSource.AppName),
            ("Cool Shooter", TermSource.ExeName),
            ("Steam", TermSource.Folder),
        }, Terms(c));
    }

    [Theory]
    [InlineData("Ryujinx.exe - Shortcut", @"""C:\Users\me\Desktop\ryujinx-canary-1.3.287-win_x64\publish\Ryujinx.exe""", "Ryujinx")]
    [InlineData("yuzu", @"""C:\Users\me\AppData\Local\yuzu\yuzu-windows-msvc\yuzu.exe""", "yuzu")]
    [InlineData("PCSX2", @"C:\Emu\pcsx2-qt.exe", "PCSX2")]
    [InlineData("Duck", @"C:\Emu\duckstation-qt-x64-ReleaseLTCG.exe", "DuckStation")]
    [InlineData("SRM", @"C:\Tools\Steam ROM Manager.exe", "Steam ROM Manager")]
    public void Emulators_WithoutGame_HaveNoTerms(string appName, string exe, string tool)
    {
        var c = CandidateExtractor.Extract(appName, exe, launchOptions: "");

        Assert.True(c.IsEmulatorOrTool);
        Assert.Equal(tool, c.ToolName);
        Assert.Empty(c.Terms);
    }

    [Theory]
    [InlineData(@"-f -g ""D:\Roms\Switch\Super Mario Odyssey [0100000000010000][v0].nsp""", "Super Mario Odyssey")]
    [InlineData(@"-g D:\Roms\Switch\Metroid_Dread.xci", "Metroid Dread")]
    [InlineData(@"--no-gui ""D:\Roms\PS3\Demon's Souls\PS3_GAME\USRDIR\EBOOT.BIN""", "Demon's Souls")]
    [InlineData(@"-f --config ""C:\Emu\config.ini""", null)]
    [InlineData("-fullscreen", null)]
    public void Emulators_UseGameFileFromLaunchOptions(string launchOptions, string? expected)
    {
        var c = CandidateExtractor.Extract("yuzu", @"C:\Emu\yuzu\yuzu.exe", launchOptions);

        Assert.True(c.IsEmulatorOrTool);
        if (expected is null) Assert.Empty(c.Terms);
        else Assert.Equal(new[] { (expected, TermSource.LaunchOptionsFile) }, Terms(c));
    }

    [Fact]
    public void LaunchOptions_AreIgnoredForNormalGames()
    {
        var c = CandidateExtractor.Extract("Avowed", @"""E:\games\Avowed\Avowed\Avowed.exe""", @"-dx12 ""D:\mods\something.pak""");

        Assert.Equal(new[] { ("Avowed", TermSource.ExeName) }, Terms(c));
    }

    [Fact]
    public void UnknownAbbreviation_AddsSearchOnlyTermWithoutIt()
    {
        var c = CandidateExtractor.Extract("Space Hunters XyZ", @"C:\x\sh.exe");

        Assert.Equal(new[]
        {
            ("Space Hunters XyZ", TermSource.AppName),
            ("Space Hunters", TermSource.SearchOnly),
            ("sh", TermSource.ExeName),
        }, Terms(c));
    }
}