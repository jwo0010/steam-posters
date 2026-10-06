using System;
using System.Collections.ObjectModel;
using SteamPosters.Artwork;
using SteamPosters.Core.Steam;
using steamposters.ViewModels;

namespace steamposters.Services;

/// <summary>Everything the wizard talks to, built once in App and replaced by fakes in tests.</summary>
public sealed record AppServices(
    SteamLocator Locator,
    IApiKeyStore KeyStore,
    Func<string, IArtworkProvider> CreateProvider,
    ImageCache Images);

/// <summary>State shared between wizard steps.</summary>
public sealed class WizardSession
{
    public SteamAccount? Account { get; set; }

    public IArtworkProvider? Provider { get; set; }

    /// <summary>The account the games below were loaded for (reloaded when the account changes).</summary>
    public string? LoadedAccountId { get; set; }

    public ObservableCollection<GameItemViewModel> Games { get; } = new();
}
