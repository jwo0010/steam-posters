using SteamPosters.Artwork;
using SteamPosters.Core.Steam;
using steamposters.Services;
using steamposters.ViewModels;

namespace SteamPosters.App.Tests;

/// <summary>Step 5: writing into a made-up Steam folder, with a pretend Steam client.</summary>
public class ApplyTests
{
    private static readonly ProviderGame Avowed = new("1", "Avowed", 2025, true);
    private static readonly ProviderGame Totk = new("2", "The Legend of Zelda: Tears of the Kingdom", 2023, true);

    private static FakeSteam Library() => new(
        ("Avowed", @"""E:\games\Avowed\Avowed.exe""", "", 1001u),
        ("Launcher", @"""D:\Games\The Legend of Zelda - TotK\Launcher.exe""", "", 1002u),
        ("yuzu", @"""C:\Emu\yuzu\yuzu.exe""", "", 1003u));

    /// <summary>Runs the wizard up to the Apply page.</summary>
    private static async Task<(MainWindowViewModel Main, ApplyViewModel Apply)> ToApply(FakeSteam steam)
    {
        var main = new MainWindowViewModel(steam.Services(new FakeProvider(Avowed, Totk), new FakeKeyStore("key")));
        for (var i = 0; i < 3; i++) await main.NextCommand.ExecuteAsync(null);
        return (main, Assert.IsType<ApplyViewModel>(main.CurrentStep));
    }

    private static string Grid(FakeSteam steam, string file) => Path.Combine(steam.GridFolder, file);

    /// <summary>Waits (up to 5 s) for the apply flow to reach a state; downloads finish asynchronously first.</summary>
    private static async Task WaitFor(ApplyViewModel apply, ApplyState state)
    {
        for (var i = 0; i < 500 && apply.State != state; i++) await Task.Delay(10);
        Assert.Equal(state, apply.State);
    }

    [Fact]
    public async Task SteamClosed_WritesArtAndNames_KeepsIds_AndBacksUp()
    {
        using var steam = Library();
        var original = File.ReadAllBytes(steam.ShortcutsPath);
        var (_, apply) = await ToApply(steam);
        Assert.Equal(2, apply.Plan!.Changes.Count);

        await apply.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(ApplyState.Done, apply.State);
        foreach (var file in new[] { "1001p.png", "1001.png", "1001_hero.png", "1001_logo.png", "1002p.png", "1002_hero.png" })
            Assert.Equal(TestImages.Png, File.ReadAllBytes(Grid(steam, file)));
        Assert.Equal(".png", GridArtwork.DetectExtension(File.ReadAllBytes(Grid(steam, "1001_icon.png"))));  // icons are re-encoded
        Assert.False(File.Exists(Grid(steam, "1003p.png")));      // yuzu was left alone

        var shortcuts = ShortcutsFile.Load(steam.ShortcutsPath).Shortcuts;
        Assert.Equal(new[] { "Avowed", "The Legend of Zelda: Tears of the Kingdom", "yuzu" }, shortcuts.Select(s => s.AppName));
        Assert.Equal(new uint?[] { 1001, 1002, 1003 }, shortcuts.Select(s => s.StoredAppId));
        Assert.Equal(Grid(steam, "1002_icon.png"), shortcuts[1].Icon);
        Assert.Equal("Favorites", shortcuts[1].Fields.GetMap("tags")!.GetString("0"));   // untouched fields kept
        Assert.Equal(1700000000u, shortcuts[1].Fields.GetUInt32("LastPlayTime"));
        Assert.Equal("", shortcuts[2].Icon);

        var backup = Assert.Single(new SteamPosters.Core.IO.BackupService(steam.BackupsFolder).List());
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(backup.Folder, "shortcuts.vdf")));
        Assert.Equal(0, steam.Process.Starts);                    // Steam wasn't running, so it isn't started
    }

    [Fact]
    public async Task SteamRunning_AsksFirst_ThenClosesAndReopensIt()
    {
        using var steam = Library();
        steam.Process.Running = true;
        var (main, apply) = await ToApply(steam);
        Assert.True(main.BackCommand.CanExecute(null));

        var applying = apply.ApplyCommand.ExecuteAsync(null);
        await WaitFor(apply, ApplyState.AskToClose);
        Assert.False(main.BackCommand.CanExecute(null));          // can't leave mid-apply
        Assert.False(File.Exists(Grid(steam, "1001p.png")));      // nothing written before the user answers

        apply.CloseSteamForMeCommand.Execute(null);
        await applying.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ApplyState.Done, apply.State);
        Assert.True(main.BackCommand.CanExecute(null));
        Assert.Equal(1, steam.Process.ShutdownRequests);
        Assert.Equal(1, steam.Process.Starts);
        Assert.True(File.Exists(Grid(steam, "1001p.png")));
    }

    [Fact]
    public async Task UserClosesSteamThemselves_AppWaits_AndDoesNotReopenIt()
    {
        using var steam = Library();
        steam.Process.Running = true;
        var (_, apply) = await ToApply(steam);

        var applying = apply.ApplyCommand.ExecuteAsync(null);
        await WaitFor(apply, ApplyState.AskToClose);
        apply.CloseSteamMyselfCommand.Execute(null);
        await WaitFor(apply, ApplyState.WaitingForUser);
        Assert.True(steam.Process.IsWaitingForUser);
        Assert.False(File.Exists(Grid(steam, "1001p.png")));

        steam.Process.UserClosesSteam();
        await applying.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ApplyState.Done, apply.State);
        Assert.Equal(0, steam.Process.ShutdownRequests);
        Assert.Equal(0, steam.Process.Starts);
        Assert.True(File.Exists(Grid(steam, "1001p.png")));
    }

    [Fact]
    public async Task SteamIgnoresShutdown_FallsBackToWaitingForTheUser()
    {
        using var steam = Library();
        steam.Process.Running = true;
        steam.Process.ShutdownWorks = false;
        var (_, apply) = await ToApply(steam);

        var applying = apply.ApplyCommand.ExecuteAsync(null);
        await WaitFor(apply, ApplyState.AskToClose);
        apply.CloseSteamForMeCommand.Execute(null);
        await WaitFor(apply, ApplyState.WaitingForUser);

        steam.Process.UserClosesSteam();
        await applying.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ApplyState.Done, apply.State);
    }

    [Fact]
    public async Task Cancel_ChangesNothing()
    {
        using var steam = Library();
        steam.Process.Running = true;
        var original = File.ReadAllBytes(steam.ShortcutsPath);
        var (_, apply) = await ToApply(steam);

        var applying = apply.ApplyCommand.ExecuteAsync(null);
        await WaitFor(apply, ApplyState.AskToClose);
        apply.CancelCommand.Execute(null);
        await applying.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ApplyState.Ready, apply.State);
        Assert.Equal(original, File.ReadAllBytes(steam.ShortcutsPath));
        Assert.Empty(Directory.GetFiles(steam.GridFolder));
        Assert.True(steam.Process.Running);
        Assert.True(apply.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Restore_PutsEverythingBack()
    {
        using var steam = Library();
        var original = File.ReadAllBytes(steam.ShortcutsPath);
        File.WriteAllBytes(Grid(steam, "1001p.jpg"), TestImages.Jpeg);   // art Steam already had
        var (_, apply) = await ToApply(steam);
        await apply.ApplyCommand.ExecuteAsync(null);
        Assert.False(File.Exists(Grid(steam, "1001p.jpg")));             // replaced by the new .png

        steam.Process.Running = true;                                    // user reopened Steam meanwhile
        var restoring = apply.RestoreCommand.ExecuteAsync(null);
        await WaitFor(apply, ApplyState.AskToClose);
        apply.CloseSteamForMeCommand.Execute(null);
        await restoring.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ApplyState.Restored, apply.State);
        Assert.Equal(original, File.ReadAllBytes(steam.ShortcutsPath));
        Assert.Equal(new[] { "1001p.jpg" }, Directory.GetFiles(steam.GridFolder).Select(Path.GetFileName));
        Assert.Equal(TestImages.Jpeg, File.ReadAllBytes(Grid(steam, "1001p.jpg")));
        Assert.Equal(1, steam.Process.Starts);
        Assert.False(apply.RestoreCommand.CanExecute(null));
    }

    [Fact]
    public async Task FailureWhileWriting_RollsBackEverything()
    {
        using var steam = Library();
        var (_, apply) = await ToApply(steam);
        File.WriteAllBytes(steam.ShortcutsPath, [0x00, 0x73, 0x00]);     // Steam left a damaged file behind
        var damaged = File.ReadAllBytes(steam.ShortcutsPath);

        await apply.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(ApplyState.Failed, apply.State);
        Assert.Contains("put back", apply.Status);
        Assert.Empty(Directory.GetFiles(steam.GridFolder));              // art written before the failure was removed
        Assert.Equal(damaged, File.ReadAllBytes(steam.ShortcutsPath));
    }

    [Fact]
    public async Task WebpArt_IsConvertedToPng()
    {
        using var steam = Library();
        steam.Downloads.Respond = uri => uri.AbsolutePath.Contains("Hero") ? TestImages.WebP : TestImages.Jpeg;
        var (_, apply) = await ToApply(steam);

        await apply.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(ApplyState.Done, apply.State);
        Assert.Equal(".png", GridArtwork.DetectExtension(File.ReadAllBytes(Grid(steam, "1001_hero.png"))));
        Assert.Equal(TestImages.Jpeg, File.ReadAllBytes(Grid(steam, "1001p.jpg")));        // JPEG kept as is
        Assert.Equal(".png", GridArtwork.DetectExtension(File.ReadAllBytes(Grid(steam, "1001_icon.png"))));  // icons always PNG
    }

    [Fact]
    public void OldEntryWithoutStoredAppId_GetsItPinnedBeforeRename()
    {
        using var steam = new FakeSteam(("Old Game", @"C:\old\old.exe", "", 0u));
        var account = new SteamLocator(() => steam.Root).GetAccounts(steam.Root)[0];
        var before = ShortcutsFile.Load(steam.ShortcutsPath).Shortcuts[0];
        Assert.Null(before.StoredAppId);
        var plan = new ApplyPlan(account, [new GameChange("0", before.AppId, "Old Game", "New Name", new Dictionary<ArtworkKind, ArtworkImage>())]);

        new ApplyService(new ImageCache(new HttpClient(steam.Downloads), steam.CacheFolder), new SteamPosters.Core.IO.BackupService(steam.BackupsFolder))
            .Apply(plan, []);

        var after = ShortcutsFile.Load(steam.ShortcutsPath).Shortcuts[0];
        Assert.Equal("New Name", after.AppName);
        Assert.Equal(before.ComputedAppId, after.StoredAppId);      // art stays keyed to the same id
    }

    [Fact]
    public void Converter_KeepsPngAndJpeg_ConvertsOthers_RejectsGarbage()
    {
        Assert.Same(TestImages.Png, SteamImageConverter.ToPngOrJpeg(TestImages.Png));
        Assert.Same(TestImages.Jpeg, SteamImageConverter.ToPngOrJpeg(TestImages.Jpeg));
        Assert.Equal(".png", GridArtwork.DetectExtension(SteamImageConverter.ToPngOrJpeg(TestImages.WebP)));
        Assert.Throws<InvalidDataException>(() => SteamImageConverter.ToPng([1, 2, 3, 4]));
    }
}
