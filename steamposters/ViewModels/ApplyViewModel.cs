using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamPosters.Core.IO;
using SteamPosters.Core.Steam;
using steamposters.Services;

namespace steamposters.ViewModels;

public enum ApplyState
{
    Ready,
    Preparing,
    /// <summary>Steam is running; waiting for the user to choose how it gets closed.</summary>
    AskToClose,
    ClosingSteam,
    /// <summary>Waiting for the user to close Steam themselves.</summary>
    WaitingForUser,
    Applying,
    Done,
    Failed,
    Restored,
}

/// <summary>
/// Step 4: shows what will change, then writes it into Steam. Steam must be closed for that: the
/// app asks first, and either closes and reopens Steam itself or waits for the user to close it.
/// </summary>
public sealed partial class ApplyViewModel : StepViewModel
{
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(60);

    private enum CloseChoice { CloseForMe, CloseMyself, Cancel }

    private readonly AppServices _services;
    private readonly WizardSession _session;
    private readonly ApplyService _applyService;
    private TaskCompletionSource<CloseChoice>? _closeChoice;
    private CancellationTokenSource? _waitCts;

    public ApplyViewModel(AppServices services, WizardSession session)
    {
        _services = services;
        _session = session;
        _applyService = new ApplyService(services.Images, services.Backups);
    }

    public override string Title => "Apply";

    public override bool CanGoNext => false;

    public override bool CanGoBack => State is ApplyState.Ready or ApplyState.Done or ApplyState.Failed or ApplyState.Restored;

    partial void OnStateChanged(ApplyState value) => RaiseCanGoNextChanged();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private ApplyPlan? _plan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsAsking), nameof(IsWaitingForUser), nameof(IsFinished), nameof(ShowSummary))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(RestoreCommand), nameof(StartSteamCommand))]
    private ApplyState _state = ApplyState.Ready;

    /// <summary>Reopen Steam afterwards when the app closed it.</summary>
    [ObservableProperty]
    private bool _reopenSteam = true;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private BackupSet? _lastBackup;

    public ObservableCollection<string> Lines { get; } = new();

    public ObservableCollection<string> Log { get; } = new();

    public bool IsBusy => State is ApplyState.Preparing or ApplyState.ClosingSteam or ApplyState.Applying;

    public bool IsAsking => State == ApplyState.AskToClose;

    public bool IsWaitingForUser => State == ApplyState.WaitingForUser;

    public bool IsFinished => State is ApplyState.Done or ApplyState.Failed or ApplyState.Restored;

    public bool ShowSummary => State is ApplyState.Ready or ApplyState.Failed;

    public override Task OnEnterAsync()
    {
        if (State is not (ApplyState.Ready or ApplyState.Failed)) return Task.CompletedTask;
        Lines.Clear();
        if (_session.Account is null) return Task.CompletedTask;
        Plan = ApplyPlan.From(_session.Account, _session.Games);
        foreach (var change in Plan.Changes)
        {
            var parts = change.Artwork.Keys.Select(k => k.ToString().ToLowerInvariant()).ToList();
            var rename = change.NewName is null ? change.OldName : $"{change.OldName} -> {change.NewName}";
            Lines.Add(parts.Count == 0 ? $"{rename} (name only)" : $"{rename}: {string.Join(", ", parts)}");
        }
        if (Plan.IsEmpty) Lines.Add("Nothing to change.");
        return Task.CompletedTask;
    }

    private ISteamProcess Steam => _services.SteamProcessFor(_session.SteamPath ?? SteamLocator.DefaultSteamPath);

    private bool CanApply() => Plan is { IsEmpty: false } && State is ApplyState.Ready or ApplyState.Failed;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var plan = Plan!;
        Log.Clear();
        var progress = new InlineProgress(AddLog);
        try
        {
            State = ApplyState.Preparing;
            Status = "Downloading the chosen artwork...";
            var prepared = await _applyService.PrepareAsync(plan, progress);

            var closedByUs = await EnsureSteamClosedAsync();
            if (closedByUs is null)
            {
                State = ApplyState.Ready;
                Status = "Cancelled. Nothing was changed.";
                return;
            }

            State = ApplyState.Applying;
            Status = "Writing into Steam...";
            var outcome = _applyService.Apply(plan, prepared, progress);
            LastBackup = outcome.Backup;
            foreach (var warning in outcome.Warnings) AddLog(warning);

            Status = $"Done: {outcome.ImagesWritten} images written, {outcome.Renamed} games renamed. A backup is in {outcome.Backup.Folder}.";
            State = ApplyState.Done;
            ReopenIfWeClosed(closedByUs.Value);
        }
        catch (ApplyFailedException ex)
        {
            Status = ex.Message;
            State = ApplyState.Failed;
        }
        catch (Exception ex)
        {
            Status = $"Nothing was changed: {ex.Message}";
            State = ApplyState.Failed;
        }
    }

    private bool CanRestore() => LastBackup is not null && State is ApplyState.Done or ApplyState.Failed;

    /// <summary>Puts back every file from the backup taken before the last apply.</summary>
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        var backup = LastBackup!;
        var previous = State;
        var closedByUs = await EnsureSteamClosedAsync();
        if (closedByUs is null)
        {
            State = previous;
            Status = "Restore cancelled.";
            return;
        }
        try
        {
            State = ApplyState.Applying;
            Status = "Restoring...";
            _services.Backups.Restore(backup);
            AddLog($"Restored {backup.Manifest.Entries.Count} files from {backup.Folder}.");
            Status = "Restored: Steam's files are back to how they were before applying.";
            State = ApplyState.Restored;
            LastBackup = null;
            ReopenIfWeClosed(closedByUs.Value);
        }
        catch (Exception ex)
        {
            Status = $"Restore failed: {ex.Message}. The backup is still in {backup.Folder}.";
            State = previous;
        }
    }

    private bool CanStartSteam() => IsFinished;

    [RelayCommand(CanExecute = nameof(CanStartSteam))]
    private void StartSteam()
    {
        if (!Steam.IsRunning()) Steam.Start();
    }

    [RelayCommand]
    private void CloseSteamForMe() => _closeChoice?.TrySetResult(CloseChoice.CloseForMe);

    [RelayCommand]
    private void CloseSteamMyself() => _closeChoice?.TrySetResult(CloseChoice.CloseMyself);

    [RelayCommand]
    private void Cancel()
    {
        _closeChoice?.TrySetResult(CloseChoice.Cancel);
        _waitCts?.Cancel();
    }

    /// <summary>
    /// Makes sure Steam is closed. Returns true if the app closed it, false if it was already
    /// closed or the user closed it, null if the user cancelled.
    /// </summary>
    private async Task<bool?> EnsureSteamClosedAsync()
    {
        var steam = Steam;
        if (!steam.IsRunning()) return false;

        State = ApplyState.AskToClose;
        Status = "Steam needs to close to save these changes. Close it now and reopen it afterwards?";
        _closeChoice = new TaskCompletionSource<CloseChoice>();
        var choice = await _closeChoice.Task;
        _closeChoice = null;
        if (choice == CloseChoice.Cancel) return null;

        if (choice == CloseChoice.CloseForMe)
        {
            State = ApplyState.ClosingSteam;
            Status = "Closing Steam...";
            if (await steam.ShutdownAsync(ShutdownTimeout))
            {
                AddLog("Steam closed.");
                return true;
            }
            AddLog("Steam didn't close in time.");
        }

        State = ApplyState.WaitingForUser;
        Status = "Waiting for you to close Steam (Steam menu > Exit)...";
        _waitCts = new CancellationTokenSource();
        try
        {
            await steam.WaitForExitAsync(_waitCts.Token);
            AddLog("Steam closed.");
            return false;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _waitCts.Dispose();
            _waitCts = null;
        }
    }

    private void ReopenIfWeClosed(bool closedByUs)
    {
        if (!closedByUs || !ReopenSteam) return;
        Steam.Start();
        AddLog("Reopened Steam.");
    }

    private void AddLog(string line) => Log.Add(line);

    /// <summary>Reports straight away on the caller's thread (Progress&lt;T&gt; would post later).</summary>
    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
