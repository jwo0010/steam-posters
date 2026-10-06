using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using steamposters.Services;

namespace steamposters.ViewModels;

/// <summary>The wizard: a fixed list of steps with Back / Next.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(AppServices services)
    {
        var session = new WizardSession();
        Steps = [new SetupViewModel(services, session), new MatchViewModel(services, session), new ReviewViewModel(services, session), new ApplyViewModel(services, session)];
        foreach (var step in Steps) step.CanGoNextChanged += (_, _) => { NextCommand.NotifyCanExecuteChanged(); BackCommand.NotifyCanExecuteChanged(); };
        _currentStep = Steps[0];
        _currentStep.IsCurrent = true;
    }

    public IReadOnlyList<StepViewModel> Steps { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepNumber))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    private StepViewModel _currentStep;

    public string StepNumber => $"Step {Index + 1} of {Steps.Count}";

    private int Index => IndexOf(CurrentStep);

    private int IndexOf(StepViewModel step)
    {
        for (var i = 0; i < Steps.Count; i++) if (ReferenceEquals(Steps[i], step)) return i;
        return -1;
    }

    partial void OnCurrentStepChanged(StepViewModel? oldValue, StepViewModel newValue)
    {
        if (oldValue is not null) oldValue.IsCurrent = false;
        newValue.IsCurrent = true;
    }

    private bool CanGoNext() => Index < Steps.Count - 1 && CurrentStep.CanGoNext;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (!await CurrentStep.OnLeaveAsync()) return;
        CurrentStep = Steps[Index + 1];
        await CurrentStep.OnEnterAsync();
    }

    private bool CanGoBack() => Index > 0 && CurrentStep.CanGoBack;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task BackAsync()
    {
        CurrentStep = Steps[Index - 1];
        await CurrentStep.OnEnterAsync();
    }
}
