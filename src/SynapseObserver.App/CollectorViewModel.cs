using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using SynapseObserver.Core;
using SynapseObserver.Backend;

namespace SynapseObserver.App;

internal sealed class CollectorViewModel : INotifyPropertyChanged
{
    private static readonly string[] Activities = ["performance", "fans", "charge-limit", "lighting"];
    private static readonly string[] Titles = ["Performance mode", "Fan setting", "Battery charge limit", "Keyboard lighting"];
    private readonly bool preview;
    private readonly Stopwatch elapsed = new();
    private readonly WindowsInventorySource inventorySource = new();
    private IReadOnlyList<LocalHidCandidate> controlCandidates = [];
    private BackendCandidate? backendCandidate;
    private INativeObservationSession? observer;
    private CancellationTokenSource? pumpCancellation;
    private Task? pump;
    private double observerOrigin;
    private int stepStartCount;
    private int observedCalls;
    private int gaps;
    private int droppedEvents;
    private int stepStartGaps;
    private bool observerEnded;
    private bool busy;
    private EvidenceSession? session;
    private bool collecting;
    private bool recording;
    private bool consent;
    private int step;
    private int stepPhase;
    private string startingSetting = string.Empty;
    private string changedSetting = string.Empty;
    private string deviceSummary = "Ready to identify your laptop. Device information stays on this computer until you save a ZIP.";
    private string recorderStatus = "Device information is available even if recording cannot start.";
    private string collectionDetails = "The observer collects selected laptop feature calls. An observed response is labeled as a user-mode returned buffer, not firmware confirmation.";
    private string status = "Start with your laptop's device information.";

    public CollectorViewModel(bool preview)
    {
        this.preview = preview;
        CollectCommand = new AsyncCommand(() => GuardAsync(CollectAsync), () => !busy && !collecting && !recording);
        StartCommand = new AsyncCommand(() => GuardAsync(StartAsync), () => !busy && HasInventory && consent && !recording && (preview || backendCandidate is not null));
        StopCommand = new AsyncCommand(() => GuardAsync(StopAsync), () => recording);
        MarkCommand = new AsyncCommand(() => GuardAsync(MarkAsync), () => recording && !observerEnded && step < Activities.Length && (stepPhase == 2 || (stepPhase == 0 ? !string.IsNullOrWhiteSpace(startingSetting) : !string.IsNullOrWhiteSpace(changedSetting))));
        SkipCommand = new AsyncCommand(() => GuardAsync(() => SkipAsync(false)), () => recording && !observerEnded && step < Activities.Length && stepPhase == 0);
        UnavailableCommand = new AsyncCommand(() => GuardAsync(() => SkipAsync(true)), () => recording && !observerEnded && step < Activities.Length && stepPhase == 0);
        SaveCommand = new AsyncCommand(() => GuardAsync(SaveAsync), () => !preview && !busy && HasInventory && !recording);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncCommand CollectCommand { get; }
    public AsyncCommand StartCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand MarkCommand { get; }
    public AsyncCommand SkipCommand { get; }
    public AsyncCommand UnavailableCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public bool IsCollecting => collecting;
    public bool IsBusy => busy;
    internal EvidenceSessionSnapshot? Evidence => session?.Snapshot();
    internal void SetOwnedTestObserver(INativeObservationSession syntheticObserver) => observer = syntheticObserver;
    public bool HasInventory => session is not null;
    public bool ShowInventoryScreen => !HasInventory;
    public bool ShowExport => HasInventory && !recording;
    public bool IsRecording => recording;
    public bool ShowStartingValue => stepPhase == 0;
    public bool ShowChangedValue => stepPhase == 1;
    public bool ShowAttach => !recording;
    public string CollectButtonText => HasInventory ? "Refresh device information" : "Collect device information";
    public string DeviceSummary => deviceSummary;
    public string RecorderStatus => recorderStatus;
    public string CollectionDetails => collectionDetails;
    public string Status => status;
    public string StepTitle => step < Titles.Length ? $"{step + 1} of {Titles.Length} · {Titles[step]}" : "Recording steps finished";
    public string StepInstructions => step < Titles.Length
        ? stepPhase == 0 ? "Enter the setting currently shown in Synapse. Then begin this step."
        : stepPhase == 1 ? "Choose a different available setting in Synapse. Enter its value below, then mark the change."
        : $"Restore the starting setting in Synapse: {startingSetting}. Mark restoration after Synapse shows that value."
        : "Stop recording and save the evidence ZIP.";
    public string StepStatus => stepPhase == 0 ? "Skip any setting you do not want to change."
        : stepPhase == 1 ? "The collector observes Synapse. It does not change a setting for you."
        : "Restoration will be recorded as your observation of Synapse's display.";
    public string MarkButtonText => stepPhase == 0 ? "Begin this step" : stepPhase == 1 ? "I changed it" : "I restored it";
    public string ObservationSummary => $"{observedCalls} observed calls · {gaps} recording gaps";
    public string ExportSummary => $"The ZIP contains device information, {observedCalls} observed calls, recording coverage, and your action markers. Unproven or sensitive payloads are omitted. Review it before sharing.";

    public bool AttachConsent
    {
        get => consent;
        set { consent = value; Changed(); RefreshCommands(); }
    }

    public string StartingSetting
    {
        get => startingSetting;
        set { startingSetting = value; Changed(); RefreshCommands(); }
    }

    public string ChangedSetting
    {
        get => changedSetting;
        set { changedSetting = value; Changed(); RefreshCommands(); }
    }

    private async Task CollectAsync()
    {
        collecting = true;
        busy = true;
        Refresh();
        try
        {
            if (session is not null)
            {
                await DiscoverRecorderAsync();
                status = "Recording availability checked. Your collected evidence was preserved.";
                return;
            }

            var inventory = preview ? PreviewInventory() : await Task.Run(() => new InventoryCollector(inventorySource).CollectAsync());
            session = new EvidenceSession(inventory);
            elapsed.Restart();
            var identities = inventory.MachineIdentity.Value;
            var identity = identities.FirstOrDefault();
            deviceSummary = $"{identity?.ProductName ?? "Laptop identity unavailable"} · BIOS {identity?.BiosVersion ?? "unavailable"}\n{inventory.RazerHidInterfaces.Value.Count} Razer HID interfaces · {inventory.SynapseProcesses.Value.Count} related software processes";
            Marker(EvidenceMarkerKind.SessionStarted, "session", "Device information collected", EvidenceProvenance.Collector);
            foreach (var id in Activities)
            {
                session.SetCoverage(new CoverageItem(id, Titles[Array.IndexOf(Activities, id)], CoverageState.Pending, CoverageReason.None));
            }

            await DiscoverRecorderAsync();
            status = "Device information collected. You can save it now or add a recording.";
        }
        finally
        {
            collecting = false;
            busy = false;
            Refresh();
        }
    }

    private async Task DiscoverRecorderAsync()
    {
        backendCandidate = null;
        if (preview)
        {
            recorderStatus = "Synapse is ready. Start a short recording when you are ready to change settings.";
            return;
        }

        recorderStatus = "Checking the native observer's availability.";
        var candidates = await Task.Run(() => inventorySource.DiscoverLocalControlCandidates());
        controlCandidates = candidates.Value.Where(BackendDiscovery.IsEligibleControlCandidate).ToArray();
        if (controlCandidates.Count is < 1 or > 8
            || controlCandidates.Select(c => c.UsbParentDeviceInstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            recorderStatus = "The laptop's feature-report interfaces could not be identified unambiguously. Save device information so the maintainer can investigate.";
            return;
        }

        var processes = await BackendDiscovery.DiscoverAsync();
        var eligible = processes.Where(candidate => candidate.Eligible).ToArray();
        if (eligible.Length == 1)
        {
            backendCandidate = eligible[0];
            recorderStatus = $"Ready to observe {controlCandidates.Count} feature-report interfaces on your laptop. This prototype supports the reviewed Synapse build only.";
        }
        else
        {
            recorderStatus = processes.Count == 0 ? "Start Synapse normally and open your laptop's controls. Then check recording availability. You can save device information now."
                : eligible.Length > 1 ? "More than one reviewed Synapse engine is running. Close duplicate Synapse instances, then check recording availability."
                : processes[0].Diagnostic;
        }

        collectionDetails = string.Join("\n", processes.Select(candidate => candidate.Diagnostic));
        if (backendCandidate is null)
        {
            Marker(EvidenceMarkerKind.Failure, "observer", "Native observer unavailable", EvidenceProvenance.Collector);
        }
    }

    private async Task StartAsync()
    {
        if (!consent || session is null)
        {
            return;
        }

        busy = true;
        Refresh();
        try
        {
            if (!preview)
            {
                if (backendCandidate is null)
                {
                    return;
                }

                observerOrigin = elapsed.Elapsed.TotalMilliseconds;
                observer = await NativeObservationSession.StartAsync(backendCandidate, controlCandidates.Select(candidate => candidate.InterfacePath).ToArray());
            }

            recording = true;
            observerEnded = false;
        }
        finally
        {
            busy = false;
            Refresh();
        }

        step = 0;
        stepPhase = 0;
        Marker(EvidenceMarkerKind.RecordingStarted, "session", "Recording started", EvidenceProvenance.Observer);
        recorderStatus = preview ? "Recording selected laptop feature calls. Preview data only." : "Recording feature calls for this laptop. Change one setting at a time in Synapse.";
        if (observer is not null)
        {
            pumpCancellation = new CancellationTokenSource();
            pump = PumpAsync(pumpCancellation.Token);
        }

        Refresh();
    }

    private Task MarkAsync()
    {
        if (stepPhase == 0)
        {
            Marker(EvidenceMarkerKind.UserReportedStartingValue, Activities[step], "Starting setting reported", EvidenceProvenance.UserReported, startingSetting);
            stepStartCount = observedCalls;
            stepStartGaps = gaps;
            stepPhase = 1;
        }
        else if (stepPhase == 1)
        {
            Marker(EvidenceMarkerKind.UserReportedTargetValue, Activities[step], "Changed setting reported", EvidenceProvenance.UserReported, changedSetting);
            Marker(EvidenceMarkerKind.UserActionConfirmed, Activities[step], "User marked the Synapse change", EvidenceProvenance.UserReported);
            stepPhase = 2;
        }
        else
        {
            Marker(EvidenceMarkerKind.UserReportedRestoration, Activities[step], "User reported restoration in Synapse", EvidenceProvenance.UserReported, startingSetting);
            session!.SetCoverage(new CoverageItem(Activities[step], Titles[step], observedCalls > stepStartCount && gaps == stepStartGaps ? CoverageState.Completed : CoverageState.CompletedWithGaps, observedCalls > stepStartCount ? CoverageReason.None : CoverageReason.NoMatchingEvents));
            NextStep();
        }

        Refresh();
        return Task.CompletedTask;
    }

    private Task SkipAsync(bool unavailable)
    {
        Marker(EvidenceMarkerKind.ItemSkipped, Activities[step], unavailable ? "User could not find the setting in Synapse" : "User skipped this item", EvidenceProvenance.UserReported);
        session!.SetCoverage(new CoverageItem(Activities[step], Titles[step], unavailable ? CoverageState.Unavailable : CoverageState.Skipped, CoverageReason.UserSkipped));
        NextStep();
        Refresh();
        return Task.CompletedTask;
    }

    private void NextStep()
    {
        step++;
        stepPhase = 0;
        startingSetting = string.Empty;
        changedSetting = string.Empty;
    }

    private async Task StopAsync()
    {
        NativeSessionStatus? nativeStatus = null;
        Exception? stopFailure = null;
        try
        {
            if (observer is not null)
            {
                // Stop the single reader before the final drain, preserving a
                // response completed while native hooks were being disabled.
                pumpCancellation?.Cancel();
                if (pump is not null)
                {
                    await pump;
                }

                nativeStatus = await observer.StopAsync();
                await PollOnceAsync();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            stopFailure = ex;
            gaps++;
            session?.AddMarker(new EvidenceMarker(elapsed.ElapsedMilliseconds, EvidenceMarkerKind.Failure, "observer", "Final observer drain failed", EvidenceProvenance.Observer, ex.GetType().Name));
        }
        finally
        {
            if (observer is not null)
            {
                try
                {
                    await observer.DisposeAsync();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    stopFailure ??= ex;
                }

                observer = null;
            }

            pumpCancellation?.Dispose();
            pumpCancellation = null;
            pump = null;
            recording = false;
            consent = false;
            backendCandidate = null;

            var unfinished = step < Activities.Length && stepPhase > 0;
            if (unfinished)
            {
                session!.SetCoverage(new CoverageItem(Activities[step], Titles[step], CoverageState.CompletedWithGaps, CoverageReason.IncompleteRestoration));
            }

            session?.AddMarker(new EvidenceMarker(elapsed.ElapsedMilliseconds, EvidenceMarkerKind.RecordingStopped, "session", "Recording stopped", EvidenceProvenance.Observer));
            recorderStatus = nativeStatus?.Diagnostic ?? "Recording stopped. Gaps and user-reported restoration remain labeled in the evidence.";
            if (nativeStatus is { Failure: not 0 } || stopFailure is not null)
            {
                session?.AddMarker(new EvidenceMarker(elapsed.ElapsedMilliseconds, EvidenceMarkerKind.Failure, "observer", "Recording cleanup has a gap", EvidenceProvenance.Observer));
                status = "Recording ended with a gap. Save the partial evidence; restart Synapse manually before another session.";
            }
            else
            {
                status = "Recording stopped. Save the evidence ZIP when ready.";
            }

            if (unfinished)
            {
                status = $"Restore {startingSetting} in Synapse before leaving this setting. " + status;
            }

            Refresh();
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await PollOnceAsync();
                var nativeStatus = observer!.GetStatus();
                if (nativeStatus.Finished)
                {
                    observerEnded = true;
                    recorderStatus = "The native observer has stopped. Restore the starting setting in Synapse if needed, then stop and save this session.";
                    Marker(EvidenceMarkerKind.RecordingStopped, "observer", "Native observer reached its bounded lifetime", EvidenceProvenance.Observer);
                    status = "The recording time limit was reached. Stop and save the evidence. Restore the starting setting in Synapse if this step is unfinished.";
                    Refresh();
                    return;
                }

                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            observerEnded = true;
            gaps++;
            Marker(EvidenceMarkerKind.Failure, "observer", "Observer polling failed", EvidenceProvenance.Observer, ex.GetType().Name);
            status = "Recording encountered a gap. Stop recording and save the partial evidence.";
            Refresh();
        }
    }

    private async Task PollOnceAsync()
    {
        if (observer is null)
        {
            return;
        }

        foreach (var observed in await observer.PollAsync())
        {
            if (observed.InterfaceIndex >= controlCandidates.Count)
            {
                throw new InvalidDataException("Observer interface index is outside the selected device scope.");
            }

            var device = controlCandidates[(int)observed.InterfaceIndex];
            var callStatus = observed.CompletionGap == "pending-completion-not-observed" ? ObservationStatus.Pending
                : observed.Result ? ObservationStatus.Succeeded : ObservationStatus.Failed;
            var operationId = $"op-{observed.Sequence}";
            var at = observerOrigin + observed.RelativeMilliseconds;
            var completedAt = observerOrigin + observed.CompletionRelativeMilliseconds;
            var error = observed.CompletionGap;
            session!.AddObservation(new ControlObservation(operationId, at, ObservationSource.UserModeCall, observed.Api, ObservationDirection.Input,
                callStatus, unchecked((int)observed.NativeError), error, device.VendorId, device.ProductId, device.InterfaceLabel,
                device.UsagePage, device.Usage, observed.RequestBytes.Length > 0 ? observed.RequestBytes[0] : null,
                observed.RequestBytes, PayloadDisposition.ProvenFeatureControl, completedAt));
            if (observed.ResponseBytes.Length > 0)
            {
                session.AddObservation(new ControlObservation(operationId, completedAt, ObservationSource.ReturnedBuffer, observed.Api, ObservationDirection.Output,
                    callStatus, unchecked((int)observed.NativeError), error, device.VendorId, device.ProductId, device.InterfaceLabel,
                    device.UsagePage, device.Usage, observed.ResponseBytes[0], observed.ResponseBytes, PayloadDisposition.ProvenFeatureControl, completedAt));
            }

            observedCalls++;
            if (error is not null || !observed.Result)
            {
                gaps++;
            }
        }

        var dropped = observer.GetStatus().DroppedEvents;
        if (dropped > 0)
        {
            collectionDetails = $"{dropped} events were dropped by the bounded observer queue. Completed-call observations cannot establish firmware application. Pending asynchronous completions were not observed.";
            if (dropped > droppedEvents)
            {
                gaps += dropped - droppedEvents;
                droppedEvents = dropped;
                Marker(EvidenceMarkerKind.Failure, "observer", "Observer queue dropped events", EvidenceProvenance.Observer, $"count-{dropped}");
            }
        }

        Refresh();
    }

    private async Task SaveAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save device evidence",
            Filter = "Evidence ZIP (*.zip)|*.zip",
            FileName = $"blade-evidence-{DateTime.UtcNow:yyyyMMdd-HHmm}.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        busy = true;
        Refresh();
        try
        {
            await EvidenceBundleWriter.WriteAsync(session!, dialog.FileName, CancellationToken.None);
            status = "Evidence ZIP saved. Attach it to your support request.";
        }
        finally
        {
            busy = false;
            Refresh();
        }
    }

    public async Task CloseAsync()
    {
        await CollectCommand.CurrentExecution;
        await StartCommand.CurrentExecution;
        await SaveCommand.CurrentExecution;
        if (recording || observer is not null)
        {
            await GuardAsync(StopAsync);
        }
    }

    private void Marker(EvidenceMarkerKind kind, string activity, string label, EvidenceProvenance provenance, string? value = null)
    {
        if (session?.AddMarker(new EvidenceMarker(elapsed.ElapsedMilliseconds, kind, activity, label, provenance, value)) is EvidenceAddResult.RejectedInvalid or EvidenceAddResult.RejectedLimit)
        {
            throw new InvalidDataException("The marker was rejected. Use a short setting value without personal details or paths.");
        }
    }

    private async Task GuardAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            status = $"This step could not finish ({ex.GetType().Name}). Collected information is still available.";
            session?.AddMarker(new EvidenceMarker(elapsed.ElapsedMilliseconds, EvidenceMarkerKind.Failure, "session", "Collector step failed", EvidenceProvenance.Collector, ex.GetType().Name));
            Refresh();
        }
    }

    internal void SetPreviewStage(int stage)
    {
        if (stage == 0)
        {
            return;
        }

        session = new EvidenceSession(PreviewInventory());
        deviceSummary = "Razer Blade · example device\nBIOS 1.00 · 3 Razer HID interfaces · Synapse detected";
        recorderStatus = "Synapse is ready. Start a short recording when you are ready to change settings.";
        status = "Device information collected. You can save it now or add a recording.";
        if (stage == 2)
        {
            recording = true;
            startingSetting = "Balanced";
            changedSetting = "Silent";
            stepPhase = 1;
            recorderStatus = "Recording selected laptop feature calls. Example data only.";
        }

        Refresh();
    }

    private static InventorySnapshot PreviewInventory() => new(
        InventorySnapshot.CurrentSchemaVersion,
        DateTimeOffset.UtcNow,
        SectionResult<IReadOnlyList<MachineIdentity>>.Collected([new("64-bit", "Razer", "Razer Blade · example device", null, "1.00", null)]),
        SectionResult<PowerSourceEvidence?>.Collected(new(PowerSourceKind.Ac, 80, true)),
        SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Collected([]),
        SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Collected([]));

    private void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        CollectCommand.Refresh(); StartCommand.Refresh(); StopCommand.Refresh(); MarkCommand.Refresh();
        SkipCommand.Refresh(); UnavailableCommand.Refresh(); SaveCommand.Refresh();
    }

    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
