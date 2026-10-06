using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamPosters.Artwork.SteamGridDb;
using SteamPosters.Core.Steam;
using steamposters.Services;

namespace steamposters.ViewModels;

/// <summary>Step 1: find Steam, pick the account, and set up the SteamGridDB key.</summary>
public sealed partial class SetupViewModel : StepViewModel
{
    public const string GetKeyUrl = "https://www.steamgriddb.com/profile/preferences/api";

    private readonly AppServices _services;
    private readonly WizardSession _session;

    public SetupViewModel(AppServices services, WizardSession session)
    {
        _services = services;
        _session = session;

        SteamPath = services.Locator.FindSteamPath();
        if (SteamPath is not null)
        {
            foreach (var account in services.Locator.GetAccounts(SteamPath)) Accounts.Add(account);
            SelectedAccount = SteamLocator.GetDefaultAccount(Accounts);
        }

        if (services.KeyStore.Load() is { } savedKey)
        {
            // Trust a saved key until a request says otherwise; no network call at start-up.
            _session.Provider = services.CreateProvider(savedKey);
            HasValidKey = true;
            KeyStatus = "A SteamGridDB key is saved on this PC.";
        }
    }

    public override string Title => "Set up";

    public override bool CanGoNext => SelectedAccount is not null && HasValidKey && !IsCheckingKey;

    public string? SteamPath { get; }

    public bool SteamFound => SteamPath is not null;

    public ObservableCollection<SteamAccount> Accounts { get; } = new();

    [ObservableProperty]
    private SteamAccount? _selectedAccount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveKeyCommand))]
    private string _apiKeyInput = "";

    [ObservableProperty]
    private string? _keyStatus;

    [ObservableProperty]
    private bool _hasValidKey;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveKeyCommand))]
    private bool _isCheckingKey;

    partial void OnSelectedAccountChanged(SteamAccount? value) => RaiseCanGoNextChanged();

    partial void OnHasValidKeyChanged(bool value) => RaiseCanGoNextChanged();

    partial void OnIsCheckingKeyChanged(bool value) => RaiseCanGoNextChanged();

    private bool CanSaveKey() => !IsCheckingKey && !string.IsNullOrWhiteSpace(ApiKeyInput);

    /// <summary>Checks the pasted key with one search, then saves it (encrypted) if SteamGridDB accepts it.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveKey))]
    private async Task SaveKeyAsync()
    {
        var key = ApiKeyInput.Trim();
        IsCheckingKey = true;
        KeyStatus = "Checking the key with SteamGridDB...";
        try
        {
            var provider = _services.CreateProvider(key);
            await provider.SearchGamesAsync("Portal");
            _services.KeyStore.Save(key);
            _session.Provider = provider;
            ApiKeyInput = "";
            HasValidKey = true;
            KeyStatus = "Key accepted and saved (encrypted for your Windows user).";
        }
        catch (SteamGridDbAuthException)
        {
            KeyStatus = "SteamGridDB rejected that key. Copy it again from your SteamGridDB API page.";
        }
        catch (Exception ex)
        {
            KeyStatus = $"Couldn't check the key: {ex.Message}";
        }
        finally
        {
            IsCheckingKey = false;
        }
    }

    [RelayCommand]
    private void ForgetKey()
    {
        _services.KeyStore.Delete();
        _session.Provider = null;
        HasValidKey = false;
        KeyStatus = "Saved key removed.";
    }

    public override Task<bool> OnLeaveAsync()
    {
        _session.Account = SelectedAccount;
        return Task.FromResult(SelectedAccount is not null && _session.Provider is not null);
    }

}
