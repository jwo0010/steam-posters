using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace steamposters.ViewModels;

/// <summary>One page of the wizard.</summary>
public abstract class StepViewModel : ObservableObject
{
    public abstract string Title { get; }

    private bool _isCurrent;

    /// <summary>Whether this is the page being shown (highlighted in the step header).</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    /// <summary>Whether the Next button is enabled.</summary>
    public virtual bool CanGoNext => true;

    /// <summary>Raised when <see cref="CanGoNext"/> may have changed.</summary>
    public event EventHandler? CanGoNextChanged;

    /// <summary>Called each time the page is shown.</summary>
    public virtual Task OnEnterAsync() => Task.CompletedTask;

    /// <summary>Called when the user presses Next; return false to stay on the page.</summary>
    public virtual Task<bool> OnLeaveAsync() => Task.FromResult(true);

    protected void RaiseCanGoNextChanged()
    {
        OnPropertyChanged(nameof(CanGoNext));
        CanGoNextChanged?.Invoke(this, EventArgs.Empty);
    }
}
