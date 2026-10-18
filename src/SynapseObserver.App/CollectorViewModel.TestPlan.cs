using System.IO;
using Microsoft.Win32;
using SynapseObserver.Core;

namespace SynapseObserver.App;

internal sealed partial class CollectorViewModel
{
    private CaptureTestPlan? testPlan;
    private string testPlanStatus = "Show all checks, or import a JSON plan supplied by a maintainer.";
    public AsyncCommand ImportTestPlanCommand { get; }
    public AsyncCommand ClearTestPlanCommand { get; }
    public bool HasTestPlan => testPlan is not null;
    public string TestPlanStatus => TestPlanLocked
        ? testPlanStatus + " The plan is fixed for this session. Reopen Observer to use a different plan." : testPlanStatus;
    public bool ShowTestPlanControls => !TestPlanLocked;
    private bool TestPlanLocked => stepPhase != 0 || observedCalls > 0 || recordingNumber > 0 || probeSequence > 0
        || allActivities.Any(activity => activity.State != CoverageState.Pending);
    public bool ShowRecordingGuide => HasInventory && Activities.Count > 0;
    public bool CanChangeTestPlan => !closing && !busy && !collecting && !recording
        && !IsProbingSpecialKey && !capacityReached && !TestPlanLocked
        && (session is null || session.SpecialKeyProbeCount == 0);

    private async Task ImportTestPlanAsync()
    {
        if (!CanChangeTestPlan) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import test plan", Filter = "JSON test plan (*.json)|*.json",
            CheckFileExists = true, Multiselect = false,
        };
        if (dialog.ShowDialog() != true) return;
        var selectedPath = dialog.FileName;
        busy = true;
        Refresh();
        try
        {
            byte[] bytes = await Task.Run(() =>
            {
                using var stream = new FileStream(selectedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > CaptureTestPlan.MaxBytes) throw new InvalidDataException("The test plan is too large.");
                var chunk = new byte[CaptureTestPlan.MaxBytes + 1];
                int count = stream.ReadAtLeast(chunk, chunk.Length, throwOnEndOfStream: false);
                return chunk[..count];
            });
            busy = false;
            TryImportTestPlan(bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            testPlanStatus = "The test plan could not be opened. Choose a readable JSON file up to 16 KB.";
        }
        finally
        {
            busy = false;
            Refresh();
        }
    }

    internal bool TryImportTestPlan(ReadOnlyMemory<byte> bytes)
    {
        if (!CanChangeTestPlan) return false;
        try
        {
            var parsed = CaptureTestPlan.Parse(bytes, allActivities.Select(activity => activity.Id).ToHashSet(StringComparer.Ordinal));
            session?.SetRequestedTestPlan(parsed);
            testPlan = parsed;
            selectedSpecialKeyChoices = parsed.SpecialKeys.Select(key => AllSpecialKeyChoices.Single(choice => choice.Key == key)).ToArray();
            selectedActivities = parsed.ActivityIds.Select(id => allActivities.Single(activity => activity.Id == id)).ToArray();
            if (parsed.SpecialKeys.Count > 0) ExpectedSpecialKey = parsed.SpecialKeys[0];
            MoveToActivity(Activities.Count > 0 ? 0 : -1);
            if (Activities.Count == 0) IsSpecialKeyExpanded = true;
            testPlanStatus = $"Imported plan: {Activities.Count} evidence {(Activities.Count == 1 ? "item" : "items")} and {parsed.SpecialKeys.Count} key {(parsed.SpecialKeys.Count == 1 ? "check" : "checks")}. Only these checks are shown.";
            Refresh();
            return true;
        }
        catch (InvalidDataException)
        {
            testPlanStatus = "This test plan is invalid or uses unsupported checks. Ask the maintainer for a corrected file. Your current checks are unchanged.";
            Refresh();
            return false;
        }
    }

    internal void ClearTestPlan()
    {
        if (!CanChangeTestPlan || testPlan is null) return;
        session?.SetRequestedTestPlan(null);
        testPlan = null;
        selectedSpecialKeyChoices = null;
        selectedActivities = null;
        MoveToActivity(0);
        testPlanStatus = "All evidence items and key checks are shown.";
        Refresh();
    }
}
