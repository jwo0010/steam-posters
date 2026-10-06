using System;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SteamPosters.Artwork;
using SteamPosters.Artwork.SteamGridDb;
using SteamPosters.Core.IO;
using SteamPosters.Core.Steam;
using steamposters.Services;
using steamposters.ViewModels;

namespace steamposters;

public partial class App : Application
{
    private static readonly HttpClient Http = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel(CreateServices(), new GitHubUpdateService());
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            _ = viewModel.CheckForUpdatesAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static AppServices CreateServices()
    {
        IApiKeyStore keyStore = OperatingSystem.IsWindows() ? new ApiKeyStore() : new InMemoryKeyStore();
        return new AppServices(
            new SteamLocator(),
            keyStore,
            key => new SteamGridDbProvider(Http, key),
            new ImageCache(Http),
            new BackupService(),
            steamPath => new SteamProcess(steamPath));
    }

    /// <summary>Non-Windows fallback (v1 is Windows-only): the key lasts until the app closes.</summary>
    private sealed class InMemoryKeyStore : IApiKeyStore
    {
        private string? _key;

        public string? Load() => _key;

        public void Save(string apiKey) => _key = apiKey;

        public void Delete() => _key = null;
    }
}
