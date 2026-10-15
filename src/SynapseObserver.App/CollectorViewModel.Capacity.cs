using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using SynapseObserver.Core;

namespace SynapseObserver.App;

internal sealed partial class CollectorViewModel
{
    // Each part keeps the existing schema and memory/export bounds. Paths remain
    // local, and only the latest saved copy of each part needs overwrite protection.
    private const int MaxEvidenceParts = 256;
    // Reserve a two-marker action tail plus the one-shot queue, malformed
    // reader, native failure, final drain, stopped and cleanup diagnostics.
    internal const int CleanupMarkerReserve = 10;
    private readonly string collectionToken = "collection-" + Guid.NewGuid().ToString("N");
    private string CollectionPartLabel => $"{collectionToken}-part-{evidencePart:D3}";
    private readonly Dictionary<int, string> savedPartPaths = [];
    private int evidencePart = 1;
    private bool capacityReached;
    private string capacityReason = string.Empty;
    private string capacityStartingSetting = string.Empty;
    private int interruptedActivity = -1;
    private bool capacityRestorationConfirmed;
    internal Func<string?>? EvidenceDestinationForTest { get; set; }
    internal Func<EvidenceSession, string, Task>? EvidenceWriterForTest { get; set; }
    public AsyncCommand SaveAndContinueCommand { get; }
    public string CapacitySummary => capacityReached ? $"ZIP part {evidencePart}: {capacityReason} Save it to continue." : $"ZIP part {evidencePart}: {session?.ObservationCount ?? 0} of {EvidenceLimits.MaxObservations} control reports. A full part stops recording; save it to continue.";
    public bool ShowCapacityRecovery => capacityReached && !recording;
    public bool ShowStandardSave => !capacityReached;
    public bool CanConfirmCapacityRestoration => !busy && !recording && !closing;
    public bool CapacityRestorationRequired => capacityStartingSetting.Length > 0;
    public string CapacityRestorationLabel => $"I restored the starting setting: {capacityStartingSetting}";
    public bool CapacityRestorationConfirmed
    {
        get => capacityRestorationConfirmed;
        set { capacityRestorationConfirmed = value; Changed(); SaveAndContinueCommand.Refresh(); }
    }
    private string CapacityInstructions => (CapacityRestorationRequired
        ? $"Restore the starting setting: {capacityStartingSetting} in the original controls. " : string.Empty)
        + (recording ? "This ZIP is full. Recording is stopping; wait for cleanup, then save the ZIP."
            : evidencePart >= MaxEvidenceParts ? "The collection has reached its part limit. Save this final ZIP and share all parts together."
            : "This ZIP is full. Save it and continue in a new ZIP part. Share all saved parts together.");
    private bool CanSaveEvidence() => !closing && (!preview || EvidenceDestinationForTest is not null)
        && !busy && !IsProbingSpecialKey && HasInventory && !recording;

    private void CheckMarkerCapacity()
    {
        if (capacityReached || session is null || session.MarkerCount < EvidenceLimits.MaxMarkers - CleanupMarkerReserve) return;
        retentionGapDetails = "The action-marker capacity is reserved for cleanup. Save this ZIP and continue in a new part.";
        ReachCapacity("Checklist space is full.");
    }

    private void ReachCapacity(string reason = "Recording space is full.")
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
        // Normal expiry leaves recording=true until cleanup. A completed reader
        // can no longer see the capacity flag or signal its stop coordinator.
        // The active-reader path keeps using its existing request/drain loop.
        if (recording && !stopping && pump is { IsCompleted: true }) _ = GuardAsync(StopAsync);
    }

    private Task SaveAndContinueAsync() => ExportEvidenceAsync(continueInNewPart: true);

    private async Task ExportEvidenceAsync(bool continueInNewPart)
    {
        var destination = EvidenceDestinationForTest is not null ? EvidenceDestinationForTest() : ChooseEvidenceDestination();
        if (destination is null) return;
        var fullPath = Path.GetFullPath(destination);
        if (savedPartPaths.Any(part => part.Key != evidencePart && string.Equals(part.Value, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            status = "Choose a different filename. This file holds an earlier ZIP part; keep all parts for your support request.";
            Refresh();
            return;
        }
        var replacingCurrentPart = savedPartPaths.TryGetValue(evidencePart, out var currentPath)
            && string.Equals(currentPath, fullPath, StringComparison.OrdinalIgnoreCase);
        var allowOverwrite = evidencePart == 1 || replacingCurrentPart;
        if (!allowOverwrite && File.Exists(fullPath))
        {
            status = "Choose an unused filename for this new ZIP part. Existing files are preserved, including alternate paths to earlier parts.";
            Refresh();
            return;
        }
        busy = true;
        Refresh();
        try
        {
            var savedSession = session!;
            if (EvidenceWriterForTest is not null) await EvidenceWriterForTest(savedSession, fullPath);
            else await EvidenceBundleWriter.WriteAsync(savedSession, fullPath, CancellationToken.None, overwrite: allowOverwrite);
            savedPartPaths[evidencePart] = fullPath;
            status = "Evidence ZIP saved. Attach all saved parts to your support request.";
            if (!continueInNewPart || closing) return;

            // Rotate only after a successful atomic save. Preserve original
            // inventory provenance, but never copy old observations or completed
            // coverage into a part where those actions were not recorded.
            var next = new EvidenceSession(savedSession.Inventory);
            foreach (var activity in Activities)
                next.SetCoverage(new CoverageItem(activity.Id, activity.Title, CoverageState.Pending, CoverageReason.None));
            foreach (var activity in Activities)
                activity.ResetForNewZip(activity.SavedInEarlierZip || activity.State != CoverageState.Pending
                    && activity != (interruptedActivity >= 0 ? Activities[interruptedActivity] : null));
            session = next;
            savedSession.Dispose();
            evidencePart++;
            sessionOriginTicks = Stopwatch.GetTimestamp();
            observedCalls = failedCalls = gaps = droppedEvents = stepStartCount = stepStartGaps = recordingStartGaps = 0;
            observerOrigin = probeOrigin = 0;
            capacityReached = capacityRestorationConfirmed = observerEnded = cleanupRequested = readerFaulted = nativeFailureGapCounted = malformedLossMarkerEmitted = false;
            capacityStartingSetting = retentionGapDetails = capacityReason = string.Empty;
            stopTask = null;
            step = interruptedActivity;
            interruptedActivity = -1;
            if (step < 0) NextStep(); else ResetEntries();
            activeProbeLabel = string.Empty;
            collectionDetails = "The observer collects selected laptop feature calls. An observed response is labeled as a user-mode returned buffer, not firmware confirmation.";
            specialKeyStatus = SpecialKeySelectionGuidance(expectedSpecialKey);
            Marker(EvidenceMarkerKind.SessionStarted, "session", "New ZIP part; original device inventory reused", EvidenceProvenance.Collector, CollectionPartLabel);
            await UpdateCollectionReviewAsync();
            status = "Previous ZIP saved. Start recording when ready to continue; share all saved ZIP parts together.";
        }
        finally
        {
            busy = false;
            Refresh();
        }
    }

    private string? ChooseEvidenceDestination()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save device evidence",
            Filter = "Evidence ZIP (*.zip)|*.zip",
            FileName = $"blade-evidence-{DateTime.UtcNow:yyyyMMdd-HHmmss}-part-{evidencePart:D3}.zip",
            DefaultExt = ".zip", AddExtension = true, OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
