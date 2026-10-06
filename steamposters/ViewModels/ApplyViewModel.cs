using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using steamposters.Services;

namespace steamposters.ViewModels;

/// <summary>Step 4: summary of what will change. Writing it into Steam comes in build step 5.</summary>
public sealed partial class ApplyViewModel(WizardSession session) : StepViewModel
{
    public override string Title => "Apply";

    public override bool CanGoNext => false;

    [ObservableProperty]
    private ApplyPlan? _plan;

    public ObservableCollection<string> Lines { get; } = new();

    public override Task OnEnterAsync()
    {
        Lines.Clear();
        if (session.Account is null) return Task.CompletedTask;
        Plan = ApplyPlan.From(session.Account, session.Games);
        foreach (var change in Plan.Changes)
        {
            var parts = change.Artwork.Keys.Select(k => k.ToString().ToLowerInvariant()).ToList();
            var rename = change.NewName is null ? change.OldName : $"{change.OldName} -> {change.NewName}";
            Lines.Add(parts.Count == 0 ? $"{rename} (name only)" : $"{rename}: {string.Join(", ", parts)}");
        }
        if (Plan.IsEmpty) Lines.Add("Nothing to change.");
        return Task.CompletedTask;
    }
}
