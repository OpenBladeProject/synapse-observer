using System.ComponentModel;
using SynapseObserver.Core;

namespace SynapseObserver.App;

internal sealed class ManualCheckItem(TestPlanCheck definition) : INotifyPropertyChanged
{
    public TestPlanCheck Definition { get; } = definition;
    public PlanCheckReport? LastReport { get; private set; }
    public PlanCheckOutcome? Outcome => LastReport?.Outcome;
    public string DisplayLabel => Definition.Title + " · " + (Outcome?.ToString() ?? "Not checked");
    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetReport(PlanCheckReport report)
    {
        LastReport = report;
        PropertyChanged?.Invoke(this, new(nameof(DisplayLabel)));
    }
}

internal sealed partial class CollectorViewModel
{
    private IReadOnlyList<ManualCheckItem> manualChecks = [];
    private IReadOnlyList<TestPlanCheck> researchChecks = [];
    private int selectedManualCheck;
    private bool manualActive;
    private bool manualSessionStarted;
    private bool manualTestingStopped;
    private string manualBaseline = string.Empty;
    private string savedManualBaseline = string.Empty;
    private string manualDetails = string.Empty;
    private string manualCheckStatus = "These are your observations of OpenBlade, not firmware readback. Observer does not apply settings.";
    public sealed record OutcomeChoice(PlanCheckOutcome Value, string Label);
    public sealed record RestorationChoice(PlanRestoration Value, string Label);
    public IReadOnlyList<OutcomeChoice> ManualOutcomes { get; } =
    [new(PlanCheckOutcome.Worked, "Worked as described"), new(PlanCheckOutcome.Problem, "Problem"),
        new(PlanCheckOutcome.Unavailable, "Control unavailable"), new(PlanCheckOutcome.Skipped, "Skipped")];
    public IReadOnlyList<RestorationChoice> ManualRestorations { get; } =
    [new(PlanRestoration.Restored, "Restored the starting settings"), new(PlanRestoration.NoChanges, "No settings were changed"),
        new(PlanRestoration.Unconfirmed, "Restoration could not be confirmed")];
    private OutcomeChoice? manualOutcome;
    private RestorationChoice? manualRestoration;
    public AsyncCommand BeginManualCheckCommand { get; private set; } = null!;
    public AsyncCommand SaveManualCheckCommand { get; private set; } = null!;
    public IReadOnlyList<ManualCheckItem> ManualChecks => manualChecks;
    public IReadOnlyList<TestPlanCheck> ResearchChecks => researchChecks;
    public IReadOnlyList<TestPlanCoveredItem> CoveredChecks => testPlan?.Covered ?? [];
    public bool ShowManualChecks => HasInventory && manualChecks.Count > 0;
    public bool ShowResearchChecks => researchChecks.Count > 0;
    public bool ShowCoveredChecks => CoveredChecks.Count > 0;
    public bool ManualCheckActive => manualActive;
    private bool ManualTargetMatches => session is not null && testPlan?.MatchesTarget(session.Inventory) == true;
    public bool CanEnterManualBaseline => ManualTargetMatches && !manualActive && !manualTestingStopped && CurrentManualCheck is not null && manualChecks[selectedManualCheck].Outcome is null;
    public bool CanSelectManualCheck => HasInventory && !busy && !recording && !IsProbingSpecialKey && !manualActive && !manualTestingStopped;
    private TestPlanCheck? CurrentManualCheck => selectedManualCheck >= 0 && selectedManualCheck < manualChecks.Count
        ? manualChecks[selectedManualCheck].Definition : null;
    public int SelectedManualCheckIndex
    {
        get => selectedManualCheck;
        set { if (!CanSelectManualCheck || value < 0 || value >= manualChecks.Count || value == selectedManualCheck) return;
            selectedManualCheck = value; ResetManualEntry(); Refresh(); }
    }
    public string ManualCheckTitle => CurrentManualCheck?.Title ?? string.Empty;
    public IReadOnlyList<string> ManualSteps => CurrentManualCheck?.Steps ?? [];
    public string ManualExpected => CurrentManualCheck?.Expected ?? string.Empty;
    public string ManualRestore => CurrentManualCheck?.Restore ?? "No setting change is required.";
    public bool ManualNeedsRestoration => CurrentManualCheck?.RequiresRestoration == true;
    public string ManualCheckStatus => manualActive && manualRestoration?.Value == PlanRestoration.Unconfirmed
        ? "Stop testing. Choose Problem and save the unresolved restoration report, then save the partial evidence ZIP."
        : ManualTargetMatches ? manualCheckStatus
        : $"This plan is for {testPlan?.TargetModel ?? "the selected laptop"}. Collect information from that laptop before reporting checks.";
    public string ManualBaseline { get => manualBaseline; set { if (manualActive) return; manualBaseline = value; Changed(); RefreshCommands(); } }
    public string ManualDetails { get => manualDetails; set { manualDetails = value; Changed(); RefreshCommands(); } }
    public OutcomeChoice? ManualOutcome { get => manualOutcome; set { manualOutcome = value; Changed(); RefreshCommands(); } }
    public RestorationChoice? ManualRestoration { get => manualRestoration; set { manualRestoration = value; Changed(); Changed(nameof(ManualCheckStatus)); RefreshCommands(); } }
    private bool CanBeginManualCheck => ManualTargetMatches && CanSelectManualCheck && !capacityReached && !closing && observer is null && !stopping
        && stepPhase == 0 && CurrentManualCheck is not null && manualChecks[selectedManualCheck].Outcome is null
        && EvidenceText.IsDisplayText(manualBaseline);
    private bool CanSaveManualCheck => manualActive && !busy && !recording && !closing && manualOutcome is not null
        && ManualOutcomes.Contains(manualOutcome) && (manualDetails.Length == 0 || EvidenceText.IsDisplayText(manualDetails))
        && (!ManualNeedsRestoration || manualRestoration is not null && ManualRestorations.Contains(manualRestoration))
        && (manualRestoration?.Value != PlanRestoration.Unconfirmed || manualOutcome.Value == PlanCheckOutcome.Problem);

    private void ConfigureManualChecks(CaptureTestPlan? plan)
    {
        manualChecks = plan?.Checks.Where(check => check.Kind == PlanCheckKind.OpenBlade).Select(check => new ManualCheckItem(check)).ToArray() ?? [];
        researchChecks = plan?.Checks.Where(check => check.Kind == PlanCheckKind.Research).ToArray() ?? [];
        selectedManualCheck = 0;
        ResetManualEntry();
    }
    private void ResetManualEntry()
    {
        manualBaseline = string.Empty; savedManualBaseline = string.Empty; manualDetails = string.Empty;
        manualOutcome = null; manualRestoration = null;
        if (selectedManualCheck >= 0 && selectedManualCheck < manualChecks.Count && manualChecks[selectedManualCheck].LastReport is { } report)
        {
            manualBaseline = report.Baseline; manualDetails = report.Details ?? string.Empty;
            manualOutcome = ManualOutcomes.Single(item => item.Value == report.Outcome);
            manualRestoration = ManualRestorations.SingleOrDefault(item => item.Value == report.Restoration);
        }
    }
    private Task BeginManualCheckAsync()
    {
        if (!CanBeginManualCheck || session is null || CurrentManualCheck is null) return Task.CompletedTask;
        savedManualBaseline = manualBaseline.Trim();
        var report = new PlanCheckReport(RelativeMilliseconds, CurrentManualCheck.Id, PlanCheckOutcome.Started,
            savedManualBaseline, null, ManualNeedsRestoration ? PlanRestoration.Pending : PlanRestoration.NotRequired);
        if (session.AddPlanCheckReport(report) != EvidenceAddResult.Added)
        { manualCheckStatus = "This check could not be recorded. Save the partial evidence ZIP."; Refresh(); return Task.CompletedTask; }
        manualActive = true; manualSessionStarted = true;
        manualCheckStatus = "Follow this check in OpenBlade. Restore the saved starting settings before saving the result.";
        Refresh();
        return Task.CompletedTask;
    }
    private async Task SaveManualCheckAsync()
    {
        if (!CanSaveManualCheck || session is null || CurrentManualCheck is null) return;
        var restoration = ManualNeedsRestoration ? manualRestoration!.Value : PlanRestoration.NotRequired;
        var report = new PlanCheckReport(RelativeMilliseconds, CurrentManualCheck.Id, manualOutcome!.Value,
            savedManualBaseline, string.IsNullOrWhiteSpace(manualDetails) ? null : manualDetails.Trim(), restoration);
        if (session.AddPlanCheckReport(report) != EvidenceAddResult.Added)
        { manualCheckStatus = "The result could not be recorded. Keep the starting settings restored and save partial evidence."; Refresh(); return; }
        manualChecks[selectedManualCheck].SetReport(report);
        manualActive = false;
        manualTestingStopped = restoration == PlanRestoration.Unconfirmed;
        manualCheckStatus = manualTestingStopped
            ? "Stop testing. Restoration is unconfirmed. Save the partial evidence ZIP and report the unresolved setting."
            : "Your result and restoration report were saved. They are not independent firmware confirmation.";
        await UpdateCollectionReviewAsync();
        Refresh();
    }
}
