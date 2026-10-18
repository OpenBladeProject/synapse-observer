using System.ComponentModel;
using SynapseObserver.Core;

namespace SynapseObserver.App;

internal sealed record SettingChoice(string Label, string? DetailsLabel = null, string? DetailsHint = null, bool IsOther = false)
{
    public override string ToString() => Label;
}

// Suggestions describe the user's Synapse UI. They do not admit hardware commands.
internal sealed class SettingEntry : INotifyPropertyChanged
{
    private SettingChoice? selectedChoice;
    private string details = string.Empty;
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<SettingChoice> Choices { get; private set; } = [];
    public SettingChoice? SelectedChoice
    {
        get => selectedChoice;
        set
        {
            if (value == selectedChoice) return;
            selectedChoice = value;
            details = string.Empty;
            Notify();
        }
    }
    public string Details
    {
        get => details;
        set { details = value; Notify(); }
    }
    public bool ShowDetails => selectedChoice?.DetailsLabel is not null;
    public string DetailsLabel => selectedChoice?.DetailsLabel ?? string.Empty;
    public string DetailsHint => selectedChoice?.DetailsHint ?? string.Empty;
    public string Value => selectedChoice is null ? string.Empty
        : selectedChoice.IsOther ? details.Trim()
        : ShowDetails && !string.IsNullOrWhiteSpace(details) ? $"{selectedChoice.Label}: {details.Trim()}"
        : selectedChoice.Label;
    public bool HasValue => selectedChoice is not null && (!ShowDetails || !string.IsNullOrWhiteSpace(details))
        && Value.Length <= 80 && EvidenceText.IsDisplayText(Value);
    public string ValidationHint => selectedChoice is null ? "Choose the current setting or observed state."
        : ShowDetails && string.IsNullOrWhiteSpace(details) ? "Add the displayed value or observed details."
        : Value.Length > 80 ? "Keep the setting and details within 80 characters."
        : Value.IndexOfAny(['/', '\\']) >= 0 ? "Use words instead of slash or backslash characters."
        : Value.Any(char.IsControl) ? "Use a single line without control characters."
        : !EvidenceText.IsDisplayText(Value) ? "Use a short displayed value without personal details or paths."
        : string.Empty;

    public void Reset(int activity)
    {
        var activities = CaptureActivityCatalog.Create();
        Reset(activity >= 0 && activity < activities.Length ? activities[activity] : null);
    }

    public void Reset(CaptureActivity? activity)
    {
        Choices = activity is not null
            ? activity.Choices
            : [new("Other state…", "State or value shown (required)", "Use a short description without personal information.", true)];
        selectedChoice = null;
        details = string.Empty;
        Notify();
    }

    internal void SetReportedText(string value)
    {
        selectedChoice = Choices.FirstOrDefault(choice => !choice.IsOther && choice.DetailsLabel is null && choice.Label == value)
            ?? Choices.Last();
        details = selectedChoice.IsOther ? value : string.Empty;
        Notify();
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
