using System.IO;
using Microsoft.Win32;
using SynapseObserver.Core;

namespace SynapseObserver.App;

internal sealed partial class CollectorViewModel
{
    // Keep space for cleanup diagnostics even when repeated user actions fill
    // the bounded checklist. Control observations are streamed separately.
    internal const int CleanupMarkerReserve = 10;
    private bool capacityReached;
    private string capacityReason = string.Empty;
    private string capacityStartingSetting = string.Empty;
    private int interruptedActivity = -1;
    private bool capacityRestorationConfirmed;
    private bool capacityRestorationReportRecorded;
    internal Func<string?>? EvidenceDestinationForTest { get; set; }
    internal Func<EvidenceSession, string, Task>? EvidenceWriterForTest { get; set; }
    public string CapacitySummary => capacityReached ? capacityReason
        : $"{session?.ObservationCount ?? 0} control reports saved locally. Finish recording, then save one evidence ZIP.";
    public bool ShowCapacityRecovery => capacityReached && !recording;
    public bool CanConfirmCapacityRestoration => !busy && !recording && !closing;
    public bool CapacityRestorationRequired => capacityStartingSetting.Length > 0;
    public string CapacityRestorationLabel => $"I restored the starting setting: {capacityStartingSetting}";
    public bool CapacityRestorationConfirmed
    {
        get => capacityRestorationConfirmed;
        set { capacityRestorationConfirmed = value; Changed(); }
    }
    private string CapacityInstructions => (CapacityRestorationRequired
        ? $"Restore the starting setting: {capacityStartingSetting} in the original controls. " : string.Empty)
        + capacityReason + (recording ? " Recording is stopping; wait for cleanup, then save the partial evidence ZIP."
            : " Save the partial evidence ZIP. Collected information remains available if saving fails.");
    private bool CanSaveEvidence() => !closing && (!preview || EvidenceDestinationForTest is not null)
        && !busy && !IsProbingSpecialKey && !MarkCommand.IsRunning && HasInventory && !recording;

    private void CheckMarkerCapacity()
    {
        if (capacityReached || session is null || session.MarkerCount < EvidenceLimits.MaxMarkers - CleanupMarkerReserve) return;
        retentionGapDetails = "The checklist has reached its action-marker limit; remaining space is reserved for cleanup diagnostics.";
        ReachCapacity("The checklist has reached its action-marker limit.");
    }

    private void ReachCapacity(string reason)
    {
        if (capacityReached) return;
        capacityReached = true;
        capacityReason = reason;
        if (HasCurrentStep && stepPhase > 0)
        {
            capacityStartingSetting = startingSetting;
            interruptedActivity = step;
        }
        CancelSpecialKeyProbe();
        status = CapacityInstructions;
        Refresh();
        if (recording && !stopping && (pump is { IsCompleted: true } || preview && observer is null)) _ = GuardAsync(StopAsync);
    }

    private async Task ExportEvidenceAsync()
    {
        var destination = EvidenceDestinationForTest is not null ? EvidenceDestinationForTest() : ChooseEvidenceDestination();
        if (destination is null) return;
        var fullPath = Path.GetFullPath(destination);
        busy = true;
        Refresh();
        try
        {
            var missingRestorationReport = false;
            if (CapacityRestorationRequired && CapacityRestorationConfirmed && !capacityRestorationReportRecorded)
            {
                // A report after recording stopped cannot complete the earlier
                // recorded action window or claim a hardware readback.
                capacityRestorationReportRecorded = Marker(EvidenceMarkerKind.UserReportedCurrentState,
                    Activities[interruptedActivity].Id, "User reported restoration after recording stopped",
                    EvidenceProvenance.UserReported, capacityStartingSetting);
                missingRestorationReport = !capacityRestorationReportRecorded;
            }
            if (EvidenceWriterForTest is not null) await EvidenceWriterForTest(session!, fullPath);
            else await EvidenceBundleWriter.WriteAsync(session!, fullPath);
            status = missingRestorationReport ? "Partial ZIP saved; the late restoration report could not be retained."
                : "Evidence ZIP saved. Attach it to your support request.";
        }
        finally
        {
            busy = false;
            Refresh();
        }
    }

    private static string? ChooseEvidenceDestination()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save device evidence",
            Filter = "Evidence ZIP (*.zip)|*.zip",
            FileName = $"blade-evidence-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip",
            DefaultExt = ".zip", AddExtension = true, OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
