using System.ComponentModel;

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
    public bool HasValue => selectedChoice is not null && (!ShowDetails || !string.IsNullOrWhiteSpace(details)) && Value.Length <= 80;
    public string ValidationHint => selectedChoice is null ? "Choose the setting shown in Synapse."
        : ShowDetails && string.IsNullOrWhiteSpace(details) ? "Add the details shown in Synapse."
        : Value.Length > 80 ? "Keep the setting and details within 80 characters."
        : string.Empty;

    public void Reset(int activity)
    {
        Choices = activity switch
        {
            0 => [new("Balanced"), new("Silent"), new("Performance"), new("Gaming"), new("Creator"), new("HyperBoost"),
                new("Custom", "CPU and GPU levels (required)", "For example: CPU Boost, GPU High")],
            1 => [new("Automatic"), new("Fixed", "Fan speed (required)", "For example: 4200 RPM, or CPU 4200 RPM, GPU 4000 RPM"),
                new("Manual curve", "Curve points (required)", "Record the temperatures and speeds needed to restore your curve.")],
            2 => [new("Off"), new("50%"), new("60%"), new("70%"), new("80%"), new("90%"), new("100%")],
            3 => [new("Off"), Lighting("Static"), Lighting("Breathing"), Lighting("Spectrum cycling"), Lighting("Wave"), Lighting("Reactive"), Lighting("Starlight")],
            _ => [],
        };
        Choices = [.. Choices, new("Other state…", "Setting shown in Synapse (required)", "Use the name and any details needed to restore it. No personal information.", true)];
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

    private static SettingChoice Lighting(string name) => new(name, "Effect details (required)", "Include brightness, colors, speed or direction as shown in Synapse.");
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
