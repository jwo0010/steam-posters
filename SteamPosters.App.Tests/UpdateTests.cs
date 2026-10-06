using SteamPosters.Artwork;
using steamposters.Services;
using steamposters.ViewModels;

namespace SteamPosters.App.Tests;

public class UpdateTests
{
    private sealed class FakeUpdates(Func<Task<string?>> check) : IUpdateService
    {
        public int Restarts { get; private set; }

        public Task<string?> CheckAndDownloadAsync(CancellationToken cancellationToken = default) => check();

        public void ApplyAndRestart() => Restarts++;
    }

    private static FakeSteam Library() => new(("Avowed", @"""E:\games\Avowed\Avowed.exe""", "", 1001u));

    private static FakeProvider Provider() => new(new ProviderGame("1", "Avowed", 2025, true));

    [Fact]
    public async Task UpdateReady_ShowsBanner_AndRestartInstallsIt()
    {
        using var steam = Library();
        var updates = new FakeUpdates(() => Task.FromResult<string?>("0.2.0"));
        var main = new MainWindowViewModel(steam.Services(Provider(), new FakeKeyStore("key")), updates);
        Assert.False(main.HasUpdate);

        await main.CheckForUpdatesAsync();

        Assert.True(main.HasUpdate);
        Assert.Equal("0.2.0", main.UpdateVersion);
        main.RestartToUpdateCommand.Execute(null);
        Assert.Equal(1, updates.Restarts);
    }

    [Fact]
    public async Task NoUpdate_OrCheckFailing_ShowsNothing()
    {
        using var steam = Library();
        var none = new MainWindowViewModel(steam.Services(Provider(), new FakeKeyStore("key")), new FakeUpdates(() => Task.FromResult<string?>(null)));
        var failing = new MainWindowViewModel(steam.Services(Provider(), new FakeKeyStore("key")), new FakeUpdates(() => throw new HttpRequestException("offline")));
        var devBuild = new MainWindowViewModel(steam.Services(Provider(), new FakeKeyStore("key")));

        await none.CheckForUpdatesAsync();
        await failing.CheckForUpdatesAsync();
        await devBuild.CheckForUpdatesAsync();

        Assert.False(none.HasUpdate);
        Assert.False(failing.HasUpdate);
        Assert.False(devBuild.HasUpdate);
        Assert.False(none.RestartToUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task RestartToUpdate_IsBlockedWhileApplying()
    {
        using var steam = Library();
        steam.Process.Running = true;
        var updates = new FakeUpdates(() => Task.FromResult<string?>("0.2.0"));
        var main = new MainWindowViewModel(steam.Services(Provider(), new FakeKeyStore("key")), updates);
        await main.CheckForUpdatesAsync();
        for (var i = 0; i < 3; i++) await main.NextCommand.ExecuteAsync(null);
        var apply = (ApplyViewModel)main.CurrentStep;

        var applying = apply.ApplyCommand.ExecuteAsync(null);
        for (var i = 0; i < 500 && apply.State != ApplyState.AskToClose; i++) await Task.Delay(10);
        Assert.False(main.RestartToUpdateCommand.CanExecute(null));

        apply.CancelCommand.Execute(null);
        await applying.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(main.RestartToUpdateCommand.CanExecute(null));
    }
}
