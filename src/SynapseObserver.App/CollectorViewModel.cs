using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Security.Cryptography;
using SynapseObserver.Core;
using SynapseObserver.Backend;

namespace SynapseObserver.App;

internal sealed partial class CollectorViewModel : INotifyPropertyChanged
{
    public IReadOnlyList<CaptureActivity> Activities { get; } = CaptureActivityCatalog.Create();
    private readonly bool preview;
    private long sessionOriginTicks = Stopwatch.GetTimestamp();
    private double RelativeMilliseconds => (Stopwatch.GetTimestamp() - sessionOriginTicks) * 1000.0 / Stopwatch.Frequency;
    private readonly WindowsInventorySource inventorySource = new();
    private IReadOnlyList<LocalHidCandidate> controlCandidates = [];
    private IReadOnlyList<LocalHidCandidate> inputCandidates = [];
    private BackendCandidate? backendCandidate;
    private Func<Task<INativeObservationSession>>? startObserverForTest;
    private INativeObservationSession? observer;
    private CancellationTokenSource? pumpCancellation;
    private Task? pump;
    private Task? stopTask;
    private bool closing;
    private double observerOrigin;
    private int stepStartCount;
    private int observedCalls;
    private int failedCalls;
    private bool stopping;
    private bool nativeFailureGapCounted;
    private bool malformedLossMarkerEmitted;
    private bool cleanupRequested;
    private bool readerFaulted;
    private int gaps;
    private int droppedEvents;
    private int stepStartGaps;
    private string retentionGapDetails = string.Empty;
    private bool observerEnded;
    private int recordingStartGaps;
    private int recordingNumber;
    private int dropSegmentNumber;
    private bool busy;
    private EvidenceSession? session;
    private bool collecting;
    private bool recording;
    private bool consent;
    private int step;
    private int stepPhase;
    private string startingSetting = string.Empty;
    public SettingEntry StartingEntry { get; } = new();
    public SettingEntry ChangedEntry { get; } = new();
    private string deviceSummary = "Ready to identify your laptop. Device information stays on this computer until you save a ZIP.";
    private string recorderStatus = "Device information is available even if recording cannot start.";
    private string collectionDetails = "The observer collects selected laptop feature calls. An observed response is labeled as a user-mode returned buffer, not firmware confirmation.";
    private string status = "Start with your laptop's device information.";
    private EvidenceCollectionReview? collectionReview;
    public string ReviewSummary => collectionReview?.Summary ?? string.Empty;
    public string ReviewNotes => collectionReview is null ? string.Empty
        : string.Join("\n\n", collectionReview.Notes);

    private async Task UpdateCollectionReviewAsync()
    {
        if (session is null) return;
        var wasBusy = busy;
        busy = true;
        RefreshCommands();
        try
        {
            using var snapshot = session.Snapshot();
            collectionReview = await Task.Run(() => EvidenceCollectionReview.Create(snapshot));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A full capture volume can also prevent temporary review files.
            // Cleanup and export to another volume must remain available.
            collectionReview = new EvidenceCollectionReview(
                "The collection review could not be prepared. Collected information is still available.",
                ["Check free disk space and save the partial evidence ZIP to another location. The export will prepare its review there."]);
        }
        finally
        {
            busy = wasBusy;
            RefreshCommands();
        }
    }

    public CollectorViewModel(bool preview)
    {
        this.preview = preview;
        StartingEntry.Reset(0);
        ChangedEntry.Reset(0);
        StartingEntry.PropertyChanged += (_, _) => RefreshCommands();
        ChangedEntry.PropertyChanged += (_, _) =>
        {
            Changed(nameof(StepStatus));
            RefreshCommands();
        };
        CollectCommand = new AsyncCommand(() => GuardAsync(CollectAsync), () => !closing && !capacityReached && !busy && !collecting && !recording && !IsProbingSpecialKey);
        StartCommand = new AsyncCommand(() => GuardAsync(StartAsync), () => !closing && !busy && !IsProbingSpecialKey && !capacityReached && HasInventory && consent && !recording && (preview || backendCandidate is not null));
        StopCommand = new AsyncCommand(() => GuardAsync(StopAsync), () => recording && !busy && !closing);
        MarkCommand = new AsyncCommand(() => GuardAsync(MarkAsync), () => !closing && !busy && !IsProbingSpecialKey && !capacityReached && HasInventory && HasCurrentStep
            && (CurrentActivity.IsReadOnly || (recording && !observerEnded))
            && (stepPhase == 2 || (stepPhase == 0 ? StartingEntry.HasValue : HasDistinctChangedSetting)));
        SkipCommand = new AsyncCommand(() => GuardAsync(() => SkipAsync(false)), () => CanSelectActivity && HasCurrentStep);
        UnavailableCommand = new AsyncCommand(() => GuardAsync(() => SkipAsync(true)), () => CanSelectActivity && HasCurrentStep);
        SaveCommand = new AsyncCommand(() => GuardAsync(SaveAsync), CanSaveEvidence);
        MarkCommand.CanExecuteChanged += (_, _) => SaveCommand.Refresh();
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
    internal void SetOwnedTestObserver(INativeObservationSession syntheticObserver, IReadOnlyList<LocalHidCandidate>? syntheticCandidates = null, bool startPump = false)
    {
        observer = syntheticObserver;
        if (syntheticCandidates is not null) controlCandidates = syntheticCandidates;
        if (startPump)
        {
            pumpCancellation = new CancellationTokenSource();
            StartPump();
        }
    }
    internal void SetOwnedStartForTest(BackendCandidate syntheticCandidate, Func<Task<INativeObservationSession>> start)
    {
        backendCandidate = syntheticCandidate;
        startObserverForTest = start;
        RefreshCommands();
    }
    public bool HasInventory => session is not null;
    public bool ShowInventoryScreen => !HasInventory;
    public bool ShowExport => HasInventory && !recording;
    public bool IsRecording => recording;
    public bool HasCurrentStep => step >= 0 && step < Activities.Count;
    public bool CanSelectActivity => !closing && !capacityReached && HasInventory && !busy && !IsProbingSpecialKey && (!recording || !observerEnded) && stepPhase == 0;
    public int SelectedActivityIndex
    {
        get => HasCurrentStep ? step : -1;
        set
        {
            if (!CanSelectActivity || value < 0 || value >= Activities.Count || value == step) return;
            step = value;
            ResetEntries();
            Refresh();
        }
    }
    private CaptureActivity CurrentActivity => Activities[step];
    private bool HasDistinctChangedSetting => ChangedEntry.HasValue
        && !SettingValueComparison.AreEquivalent(startingSetting, ChangedEntry.Value);
    public string CoverageSummary => $"{Activities.Count(activity => activity.State != CoverageState.Pending)} of {Activities.Count} items handled. Choose any item; stop and save whenever you are ready.";
    public string StartingValueLabel => HasCurrentStep && CurrentActivity.IsReadOnly ? "Current state or known behavior" : "Starting setting";
    public string ActivityGuidance => HasCurrentStep ? CurrentActivity.Guidance : string.Empty;
    public bool ShowStartingValue => HasCurrentStep && stepPhase == 0;
    public bool ShowChangedValue => HasCurrentStep && stepPhase == 1;
    public bool ShowAttach => !recording;
    public string CollectButtonText => HasInventory ? "Refresh device information" : "Collect device information";
    public string VersionLabel => "v" + BuildVersion.Split('+')[0];
    public string BuildVersion => typeof(CollectorViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "Unknown";
    public string DeviceSummary => deviceSummary;
    public string RecorderStatus => recorderStatus;
    public string CollectionDetails => string.IsNullOrEmpty(retentionGapDetails) ? collectionDetails : $"{collectionDetails}\n{retentionGapDetails}";
    public string Status => status;
    public string StepTitle => HasCurrentStep ? $"{step + 1} of {Activities.Count} · {CurrentActivity.Title}" : "Checklist finished";
    public string StepInstructions => capacityReached ? CapacityInstructions : HasCurrentStep
        ? CurrentActivity.IsReadOnly ? "Report the current state or behavior you have already observed. Save the report without changing a setting."
        : !recording ? "Start recording before changing this setting, or choose a current-state report to continue without recording."
        : observerEnded ? EndedRecordingInstructions
        : stepPhase == 0 ? "Wait for the original controls to finish updating. Select the setting currently shown, then begin this step."
        : stepPhase == 1 ? "Change one available setting in its usual controls. Wait until those controls finish updating and show the new value, then select it below and mark the change."
        : $"Wait for any syncing to finish. Restore the starting setting: {startingSetting}, then wait for the controls to finish updating and show that value before marking restoration."
        : "Stop recording and save the evidence ZIP.";
    private string EndedRecordingInstructions => (stepPhase > 0
        ? $"Restore the starting setting: {startingSetting}. " : "Recording is unavailable. ")
        + (stopping || cleanupRequested ? "Wait for recording cleanup, then save the partial evidence."
            : "Stop recording and save the partial evidence.");
    public string StepStatus => capacityReached ? "Recording stopped early. Save the partial evidence after cleanup." : !HasCurrentStep ? "Your setting changes and restoration markers are recorded." : CurrentActivity.IsReadOnly ? "This is your report, not a hardware measurement or firmware confirmation."
        : !recording ? "Current-state reports are available without observing Synapse. Setting changes need a recording."
        : observerEnded ? "Recording is unavailable. Restore any unfinished setting and save the evidence after cleanup."
        : stepPhase == 0 ? "If Synapse shows \"Syncing Profiles\", wait before changing a setting. Skip anything you cannot test."
        : stepPhase == 1 && ChangedEntry.HasValue && !HasDistinctChangedSetting ? "Choose a value different from the starting setting."
        : stepPhase == 1 ? "Wait for Synapse to finish syncing before restoring. If it stays stuck, stop recording and save partial evidence."
        : "This records your observation of the restored setting. If Synapse stays stuck syncing, stop recording and save partial evidence.";
    public string MarkButtonText => HasCurrentStep && CurrentActivity.IsReadOnly ? "Save current state" : stepPhase == 0 ? "Begin this step" : stepPhase == 1 ? "I changed it" : "I restored it";
    public string ObservationSummary => $"{observedCalls} observed calls · {failedCalls} failed calls · {gaps} recording gaps";
    public string ExportSummary => $"The ZIP contains device information, {observedCalls} observed calls, recording coverage, and your action markers. Unproven or sensitive payloads are omitted. Review it before sharing.";

    public bool AttachConsent
    {
        get => consent;
        set { consent = value; Changed(); RefreshCommands(); }
    }

    public string StartingSetting
    {
        get => StartingEntry.Value;
        set => StartingEntry.SetReportedText(value);
    }

    public string ChangedSetting
    {
        get => ChangedEntry.Value;
        set => ChangedEntry.SetReportedText(value);
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
                status = RecorderDiscoveryDiagnostics.AvailabilityStatus(preview || backendCandidate is not null, consent, recorderStatus);
                return;
            }

            var inventory = preview ? PreviewInventory() : await Task.Run(() => new InventoryCollector(inventorySource).CollectAsync());
            session = new EvidenceSession(inventory);
            sessionOriginTicks = Stopwatch.GetTimestamp();
            var identities = inventory.MachineIdentity.Value;
            var identity = identities.FirstOrDefault();
            deviceSummary = $"{identity?.ProductName ?? "Laptop identity unavailable"} · BIOS {identity?.BiosVersion ?? "unavailable"}\n{inventory.RazerHidInterfaces.Value.Count} Razer HID interfaces · {inventory.SynapseProcesses.Value.Count} related software processes";
            Marker(EvidenceMarkerKind.SessionStarted, "session", "Device information collected", EvidenceProvenance.Collector);
            foreach (var activity in Activities)
            {
                session.SetCoverage(new CoverageItem(activity.Id, activity.Title, CoverageState.Pending, CoverageReason.None));
            }

            await DiscoverRecorderAsync();
            await UpdateCollectionReviewAsync();
            status = "Device information collected. You can save it now or add a recording.";
            if (session.StorageFailure is not null)
                ReachCapacity("Local evidence storage is unavailable. Check free disk space; device information can still be exported.");
        }
        finally
        {
            collecting = false;
            busy = false;
            if (capacityReached) status = CapacityInstructions;
            Refresh();
        }
    }

    private async Task DiscoverRecorderAsync()
    {
        backendCandidate = null;
        inputCandidates = [];
        if (preview)
        {
            recorderStatus = "Synapse is ready. Start a short recording when you are ready to change settings.";
            return;
        }

        recorderStatus = "Checking the native observer's availability.";
        var candidates = await Task.Run(() => inventorySource.DiscoverLocalControlCandidates());
        var eligibleInputs = candidates.Value.Where(candidate => SpecialKeyProbeParser.IsInputCandidate(candidate)
            && candidate.UsbParentProductName?.Contains("Blade", StringComparison.OrdinalIgnoreCase) == true).ToArray();
        if (SpecialKeyProbeParser.IsSingleBladeRoot(eligibleInputs)) inputCandidates = eligibleInputs;
        controlCandidates = candidates.Value.Where(BackendDiscovery.IsEligibleControlCandidate).ToArray();
        if (controlCandidates.Count is < 1 or > 8
            || controlCandidates.Select(c => c.UsbParentDeviceInstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            recorderStatus = "The laptop's feature-report interfaces could not be identified unambiguously. Save device information so the maintainer can investigate.";
            collectionDetails = recorderStatus;
            Marker(EvidenceMarkerKind.Failure, "observer", "Native observer unavailable", EvidenceProvenance.Collector);
            return;
        }

        var processes = await BackendDiscovery.DiscoverAsync();
        var eligible = processes.Where(candidate => candidate.Eligible).ToArray();
        if (eligible.Length == 1)
        {
            backendCandidate = eligible[0];
            recorderStatus = $"Ready to observe {controlCandidates.Count} feature-report interfaces on your laptop. Razer publisher and process identity verified.";
        }
        else
        {
            recorderStatus = processes.Count == 0 ? "Start Synapse normally and open your laptop's controls. Then check recording availability. You can save device information now."
                : eligible.Length > 1 ? "More than one verified Synapse engine is running. Close duplicate Synapse instances, then check recording availability."
                : RecorderDiscoveryDiagnostics.UnavailableSummary(processes);
        }

        collectionDetails = RecorderDiscoveryDiagnostics.Format(processes);
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
        status = "Starting recording.";
        recorderStatus = "Checking the recording target and starting the observer.";
        Refresh();
        try
        {
            if (!preview || startObserverForTest is not null)
            {
                // The backend performs fresh process, payload and device checks
                // on every start. Reuse the UI candidate without another HID scan.
                if (!preview && backendCandidate is null)
                {
                    status = recorderStatus;
                    return;
                }

                try
                {
                    observer = startObserverForTest is null
                        ? await NativeObservationSession.StartAsync(backendCandidate!, controlCandidates.Select(candidate => candidate.InterfacePath).ToArray())
                        : await startObserverForTest();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    observer = null;
                    consent = false;
                    backendCandidate = null;
                    startObserverForTest = null;
                    session.AddMarker(new EvidenceMarker(RelativeMilliseconds, EvidenceMarkerKind.Failure, "observer", "Recording start failed", EvidenceProvenance.Collector));
                    recorderStatus = "Recording could not start. Check recording availability for the next step. Allow observation again when recording becomes available.";
                    status = "Recording did not start. Collected information is still available. Check recording availability or restart Synapse normally.";
                    return;
                }
                // Use the observer's actual QPC origin, not the time at which its
                // Task.Run was queued. User markers and events share this clock.
                observerOrigin = (observer.OriginTimestampTicks - sessionOriginTicks) * 1000.0 / Stopwatch.Frequency;
            }

            stopTask = null;
            // Native counters belong to each observer instance; session evidence
            // and accumulated loss remain intact across successive recordings.
            droppedEvents = 0;
            nativeFailureGapCounted = false;
            malformedLossMarkerEmitted = false;
            cleanupRequested = false;
            readerFaulted = false;
            recordingStartGaps = gaps;
            recordingNumber++;
            dropSegmentNumber = 0;
            recording = true;
            observerEnded = false;
            collectionDetails = "The observer collects selected laptop feature calls. An observed response is labeled as a user-mode returned buffer, not firmware confirmation.";
            status = "Recording is active. Stop when ready.";
        }
        finally
        {
            busy = false;
            Refresh();
        }

        if (!HasCurrentStep || CurrentActivity.State != CoverageState.Pending)
        {
            NextStep();
        }
        Marker(EvidenceMarkerKind.RecordingStarted, "session", "Recording started", EvidenceProvenance.Observer);
        recorderStatus = preview ? "Recording selected laptop feature calls. Preview data only." : "Recording feature calls for this laptop. Change one setting at a time in Synapse.";
        if (observer is not null)
        {
            pumpCancellation = new CancellationTokenSource();
            StartPump();
        }

        Refresh();
    }

    internal Func<ValueTask<PowerSourceKind>>? ActionPowerSourceForTest { get; set; }

    private async ValueTask<PowerSourceKind> ReadActionPowerSourceAsync()
    {
        if (ActionPowerSourceForTest is not null) return await ActionPowerSourceForTest();
        if (preview) return PowerSourceKind.Ac;
        try
        {
            var result = await inventorySource.CollectPowerSourceAsync(CancellationToken.None);
            return result.Value?.Source ?? PowerSourceKind.Unknown;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return PowerSourceKind.Unknown;
        }
    }

    private async Task MarkAsync()
    {
        var actionSession = session;
        var actionStep = step;
        var activity = CurrentActivity;
        var phase = stepPhase;
        var startingValue = StartingEntry.Value;
        var startingValueIsValid = StartingEntry.HasValue;
        var changedValue = ChangedEntry.Value;
        var changedValueIsValid = ChangedEntry.HasValue;
        var reportedStartingValue = startingSetting;

        if ((activity.IsReadOnly || phase == 0) && !startingValueIsValid)
        {
            status = StartingEntry.ValidationHint;
            Refresh();
            return;
        }
        if (!activity.IsReadOnly && phase == 1
            && (!changedValueIsValid || SettingValueComparison.AreEquivalent(reportedStartingValue, changedValue)))
        {
            status = "Choose a changed value that differs from the starting setting.";
            Refresh();
            return;
        }

        busy = true;
        RefreshCommands();
        try
        {
            var actionPower = await ReadActionPowerSourceAsync();
            // Discovery yields to the reader and cleanup. Discard late actions.
            if (closing || capacityReached || !ReferenceEquals(session, actionSession) || step != actionStep || stepPhase != phase
                || !activity.IsReadOnly && (!recording || observerEnded || stopping)) return;
            if (activity.IsReadOnly)
            {
                if (!Marker(EvidenceMarkerKind.UserReportedCurrentState, activity.Id, "User reported current state or known behavior", EvidenceProvenance.UserReported, startingValue, actionPower)) return;
                SetCurrentCoverage(CoverageState.Completed, CoverageReason.None);
                NextStep();
                if (!recording) await UpdateCollectionReviewAsync();
                Refresh();
                return;
            }

            if (phase == 0)
            {
                if (!Marker(EvidenceMarkerKind.UserReportedStartingValue, activity.Id, "Starting setting reported", EvidenceProvenance.UserReported, startingValue, actionPower)) return;
                startingSetting = startingValue;
                stepStartCount = observedCalls;
                stepStartGaps = gaps;
                stepPhase = 1;
            }
            else if (phase == 1)
            {
                if (!Marker(EvidenceMarkerKind.UserReportedTargetValue, activity.Id, "Changed setting reported", EvidenceProvenance.UserReported, changedValue, actionPower)) return;
                if (!Marker(EvidenceMarkerKind.UserActionConfirmed, activity.Id, "User marked the setting change", EvidenceProvenance.UserReported, powerSource: actionPower)) return;
                stepPhase = 2;
            }
            else
            {
                if (!Marker(EvidenceMarkerKind.UserReportedRestoration, activity.Id, "User reported restoration of starting setting", EvidenceProvenance.UserReported, reportedStartingValue, actionPower)) return;
                SetCurrentCoverage(observedCalls > stepStartCount && gaps == stepStartGaps ? CoverageState.Completed : CoverageState.CompletedWithGaps,
                    gaps > stepStartGaps ? CoverageReason.CaptureFailed : observedCalls <= stepStartCount ? CoverageReason.NoMatchingEvents : CoverageReason.None);
                NextStep();
            }

        }
        finally
        {
            busy = false;
            Refresh();
        }
    }

    private async Task SkipAsync(bool unavailable)
    {
        if (!Marker(EvidenceMarkerKind.ItemSkipped, CurrentActivity.Id, unavailable ? "User reported this item unavailable" : "User skipped this item", EvidenceProvenance.UserReported)) return;
        SetCurrentCoverage(unavailable ? CoverageState.Unavailable : CoverageState.Skipped, CoverageReason.UserSkipped);
        NextStep();
        if (!recording) await UpdateCollectionReviewAsync();
        Refresh();
        return;
    }

    private void NextStep()
    {
        var next = Enumerable.Range(1, Activities.Count).Select(offset => (step + offset) % Activities.Count)
            .FirstOrDefault(index => Activities[index].State == CoverageState.Pending, -1);
        step = next;
        ResetEntries();
    }

    private void SetCurrentCoverage(CoverageState state, CoverageReason reason)
    {
        session!.SetCoverage(new CoverageItem(CurrentActivity.Id, CurrentActivity.Title, state, reason));
        CurrentActivity.SetState(state);
    }

    private void ResetEntries()
    {
        stepPhase = 0;
        startingSetting = string.Empty;
        StartingEntry.Reset(step);
        ChangedEntry.Reset(step);
    }

    private Task StopAsync() => stopTask ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        stopping = true;
        busy = true;
        status = WithRestorationReminder("Stopping recording and retaining completed observations.");
        recorderStatus = "Stopping recording.";
        Refresh();
        NativeSessionStatus? nativeStatus = null;
        Exception? stopFailure = null;
        try
        {
            if (observer is not null)
            {
                // Keep the one reader running while native hooks stop and the
                // engine queue drains into the smaller shared ring.
                nativeStatus = await observer.StopAsync();
                if (UpdateDroppedEvents(nativeStatus)) NotifyObservationSummaries();
                if (!nativeStatus.Finished)
                {
                    // Dispose previously paid a second wait without retaining its
                    // result. Keep the sole reader alive for that bounded wait so
                    // a late finish publishes its final events and queue totals.
                    nativeStatus = await observer.StopAsync();
                    if (UpdateDroppedEvents(nativeStatus)) NotifyObservationSummaries();
                }
                pumpCancellation?.Cancel();
                if (pump is not null) await pump;
                await PollOnceAsync();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            stopFailure = ex;
            if (ex is not NativeObservationPollException) gaps++;
            session?.AddMarker(new EvidenceMarker(RelativeMilliseconds, EvidenceMarkerKind.Failure, "observer", "Final observer drain failed", EvidenceProvenance.Observer, ex.GetType().Name));
        }
        finally
        {
            pumpCancellation?.Cancel();
            if (pump is not null)
            {
                try { await pump; }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { stopFailure ??= ex; }
            }
            if (observer is not null)
            {
                if (readerFaulted || stopFailure is not null || nativeStatus is not { Finished: true, Failure: 0, ReuseAllowed: true })
                    observer.InvalidateReuse();
                try
                {
                    await observer.DisposeAsync();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    if (stopFailure is null) gaps++;
                    stopFailure ??= ex;
                    observer.InvalidateReuse();
                }

                observer = null;
            }

            pumpCancellation?.Dispose();
            pumpCancellation = null;
            pump = null;
            recording = false;
            stopping = false;
            busy = false;
            var cleanStop = !readerFaulted && stopFailure is null
                && (nativeStatus is { Finished: true, Failure: 0, ReuseAllowed: true }
                    || preview && nativeStatus is null);
            if (!cleanStop)
            {
                consent = false;
                backendCandidate = null;
            }

            var unfinished = HasCurrentStep && stepPhase > 0;
            if (unfinished)
            {
                SetCurrentCoverage(CoverageState.CompletedWithGaps, CoverageReason.IncompleteRestoration);
            }

            session?.AddMarker(new EvidenceMarker(RelativeMilliseconds, EvidenceMarkerKind.RecordingStopped, "session", "Recording stopped", EvidenceProvenance.Observer));
            recorderStatus = !cleanStop
                ? "Recording stopped after an error. Restart Synapse normally before another recording."
                : nativeStatus?.Diagnostic ?? "Recording stopped. Gaps and user-reported restoration remain labeled in the evidence.";
            if (nativeStatus is { Failure: not 0 }) CountNativeFailureGap();
            if (!cleanStop)
            {
                session?.AddMarker(new EvidenceMarker(RelativeMilliseconds, EvidenceMarkerKind.Failure, "observer", "Recording cleanup has a gap", EvidenceProvenance.Observer));
                status = "Recording ended with a gap. Save the partial evidence; restart Synapse manually before another session.";
            }
            else if (gaps > recordingStartGaps)
            {
                status = "Recording stopped with gaps. Save the partial evidence ZIP when ready.";
            }
            else
            {
                status = "Recording stopped. Save the evidence ZIP when ready.";
            }

            status = WithRestorationReminder(status);

            CheckMarkerCapacity();
            if (capacityReached) status = CapacityInstructions;

            // Preserve the incomplete-restoration evidence and reminder, but
            // permit subsequent inventory-only reports after recording ends.
            ResetEntries();
            await UpdateCollectionReviewAsync();

            Refresh();
        }
    }

    private void StartPump()
    {
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = PumpAsync(pumpCancellation!.Token, () => stopRequested.TrySetResult());
        pump = reader;
        // A recoverable rejection requests cleanup while this sole reader keeps
        // draining. A fatal fault requests cleanup after the reader completes.
        _ = StopAfterReaderFailureAsync(reader, stopRequested.Task);
    }

    private async Task StopAfterReaderFailureAsync(Task<bool> reader, Task stopRequested)
    {
        await Task.WhenAny(reader, stopRequested);
        var failed = stopRequested.IsCompleted || await reader;
        if (failed && ReferenceEquals(pump, reader) && recording && !stopping) await GuardAsync(StopAsync);
    }

    private async Task<bool> PumpAsync(CancellationToken cancellationToken, Action requestStop)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NativeSessionStatus? nativeStatus;
                try { nativeStatus = await PollOnceAsync(); }
                catch (NativeObservationPollException)
                {
                    // Rejected slots have been released and valid events retained.
                    // The ring remains readable; keep one reader through stop so
                    // buffered events do not overflow its smaller shared ring.
                    readerFaulted = true;
                    observer?.InvalidateReuse();
                    observerEnded = true;
                    cleanupRequested = true;
                    if (!malformedLossMarkerEmitted)
                    {
                        malformedLossMarkerEmitted = true;
                        Marker(EvidenceMarkerKind.Failure, "observer", "Malformed observer records rejected", EvidenceProvenance.Observer);
                    }
                    if (!stopping)
                    {
                        recorderStatus = "Recording had malformed records. Requesting observer cleanup.";
                        status = WithRestorationReminder("Recording encountered a gap. The observer is stopping; save the partial evidence after cleanup.");
                        Refresh();
                    }
                    requestStop();
                    nativeStatus = observer!.GetStatus();
                }
                if (capacityReached && !stopping && !cleanupRequested)
                {
                    observerEnded = true;
                    cleanupRequested = true;
                    status = CapacityInstructions;
                    recorderStatus = "Collection stopped early. Cleaning up the observer and retaining recording-gap counts.";
                    Refresh();
                    requestStop();
                }
                if (nativeStatus is { Finished: true })
                {
                    observerEnded = true;
                    // A requested stop owns its status until cleanup completes.
                    // Native failure and normal expiry have different evidence.
                    if (!stopping && !cleanupRequested)
                    {
                        if (nativeStatus.Failure != 0)
                        {
                            CountNativeFailureGap();
                            Marker(EvidenceMarkerKind.Failure, "observer", "Native observer failed", EvidenceProvenance.Observer);
                            recorderStatus = nativeStatus.Diagnostic;
                            status = "The observer encountered an error. Stop and save the partial evidence; restart Synapse manually before another recording.";
                        }
                        else
                        {
                            recorderStatus = "The native observer reached the recording time limit.";
                            status = "The recording time limit was reached. Stop and save the evidence.";
                        }
                        status = WithRestorationReminder(status);
                        // StopCoreAsync owns the single RecordingStopped marker.
                        Refresh();
                    }
                    return false;
                }

                // The shared ring has 32 slots. Multi-row lighting frames can
                // exhaust it within 100 ms; drain at 20 ms while keeping the
                // native ten-minute bound and explicit dropped-event accounting.
                await Task.Delay(20, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            readerFaulted = true;
            observer?.InvalidateReuse();
            observerEnded = true;
            cleanupRequested = true;
            gaps++;
            Marker(EvidenceMarkerKind.Failure, "observer", "Observer polling failed", EvidenceProvenance.Observer, ex.GetType().Name);
            recorderStatus = "The recording reader failed. Requesting observer cleanup.";
            status = WithRestorationReminder("Recording encountered a gap. The observer is stopping; save the partial evidence after cleanup.");
            Refresh();
            return true;
        }
        return false;
    }

    private string WithRestorationReminder(string message) => HasCurrentStep && stepPhase > 0
        ? $"Restore {startingSetting} in the original controls before leaving this setting. " + message
        : message;

    private void CountNativeFailureGap()
    {
        if (nativeFailureGapCounted) return;
        nativeFailureGapCounted = true;
        gaps++;
    }

    private async Task<NativeSessionStatus?> PollOnceAsync()
    {
        if (observer is null)
        {
            return null;
        }

        var nativeStatus = observer.GetStatus();
        var visibleStateChanged = UpdateDroppedEvents(nativeStatus);
        IReadOnlyList<NativeObservationEvent> batch = [];
        NativeObservationPollException? parseLoss = null;
        var scopeRejections = 0;
        try
        {
            try { batch = await observer.PollAsync(); }
            catch (NativeObservationPollException ex)
            {
                // The ring reader has already released rejected slots. Retain every
                // valid event in its batch before stopping this partial recording.
                batch = ex.Events;
                parseLoss = ex;
                gaps += ex.RejectedRecords;
            }
            visibleStateChanged |= batch.Count > 0 || parseLoss is not null;
            foreach (var observed in batch)
            {
                if (observed.InterfaceIndex >= controlCandidates.Count)
                {
                    // Reject only this out-of-scope record; preserve valid events
                    // elsewhere in the already-consumed batch.
                    gaps++;
                    scopeRejections++;
                    continue;
                }

                var device = controlCandidates[(int)observed.InterfaceIndex];
                var callStatus = observed.CompletionGap == "pending-completion-not-observed" ? ObservationStatus.Pending
                    : observed.Result ? ObservationStatus.Succeeded : ObservationStatus.Failed;
                var operationId = $"recording-{recordingNumber}-op-{observed.Sequence}";
                var at = observerOrigin + observed.RelativeMilliseconds;
                var completedAt = observerOrigin + observed.CompletionRelativeMilliseconds;
                var returnedLengthUnknown = observed.Result && observed.Api == "HidD_GetFeature"
                    && observed.CompletionGap == "return-length-unknown";
                // HidD exposes no actual byte count. This is an API caveat,
                // independent of operation failure or lost capture data.
                var error = returnedLengthUnknown ? null : observed.CompletionGap;
                // HidD_GetFeature returns BOOL, not an output byte count. Only
                // DeviceIoControl supplies an actual returned-length value.
                uint? actualReturnedBytes = observed.Api.StartsWith("DeviceIoControl.", StringComparison.Ordinal)
                    ? observed.ReturnedBytes : null;
                AddControlObservation(new ControlObservation(operationId, at, ObservationSource.UserModeCall, observed.Api, ObservationDirection.Input,
                    callStatus, unchecked((int)observed.NativeError), error, device.VendorId, device.ProductId, device.InterfaceLabel,
                    device.UsagePage, device.Usage, observed.RequestBytes.Length > 0 ? observed.RequestBytes[0] : null,
                    observed.RequestBytes, PayloadDisposition.ProvenFeatureControl, completedAt, observed.OutputBufferCapacity,
                    observed.RequestedBytes, actualReturnedBytes));
                if (observed.ResponseBytes.Length > 0)
                {
                    AddControlObservation(new ControlObservation(operationId, completedAt, ObservationSource.ReturnedBuffer, observed.Api, ObservationDirection.Output,
                        callStatus, unchecked((int)observed.NativeError), error, device.VendorId, device.ProductId, device.InterfaceLabel,
                        device.UsagePage, device.Usage,
                        observed.Api == "DeviceIoControl.HID_GET_FEATURE" && observed.ReturnedBytes == 90
                            && observed.ResponseBytes.Length == 90 && observed.OutputBufferCapacity >= 91
                            && device.ReportGeometry?.FeatureBytes == 91 ? null : observed.ResponseBytes[0], observed.ResponseBytes, PayloadDisposition.ProvenFeatureControl, completedAt, observed.OutputBufferCapacity,
                        observed.RequestedBytes, actualReturnedBytes)
                    { ReturnedLengthUnknown = returnedLengthUnknown });
                }

                observedCalls++;
                if (callStatus == ObservationStatus.Failed) failedCalls++;
                if (error is not null)
                {
                    gaps++;
                }
            }
        }
        finally
        {
            // Include unprocessed records when scope or projection fails.
            foreach (var observed in batch)
            {
                CryptographicOperations.ZeroMemory(observed.RequestBytes);
                CryptographicOperations.ZeroMemory(observed.ResponseBytes);
            }
            CheckMarkerCapacity();
            if (visibleStateChanged) NotifyObservationSummaries();
        }

        if (parseLoss is not null) throw parseLoss;
        if (scopeRejections > 0) throw new NativeObservationPollException([], scopeRejections);
        return nativeStatus;
    }

    private bool UpdateDroppedEvents(NativeSessionStatus nativeStatus)
    {
        var dropped = nativeStatus.DroppedEvents;
        if (dropped <= droppedEvents) return false;
        collectionDetails = $"{dropped} events were dropped by the bounded observer queue during this recording. Completed-call observations cannot establish firmware application. Pending asynchronous completions were not observed.";
        gaps += dropped - droppedEvents;
        droppedEvents = dropped;
        session?.RecordObserverDroppedEvents(RelativeMilliseconds, recordingNumber, dropped, dropSegmentNumber);
        return true;
    }

    private void NotifyObservationSummaries()
    {
        // Empty unchanged polls need no binding or command notifications.
        Changed(nameof(ObservationSummary));
        Changed(nameof(CollectionDetails));
        Changed(nameof(ExportSummary));
        Changed(nameof(CapacitySummary));
    }

    private void AddControlObservation(ControlObservation observation)
    {
        var result = session!.AddObservation(observation);
        if (result is EvidenceAddResult.RejectedStorage)
        {
            gaps++;
            retentionGapDetails = "Local evidence storage failed. Only complete reports saved before the failure can be exported; later reports are counted as gaps.";
            ReachCapacity("Local evidence storage could not continue. Check free disk space and save the partial ZIP to another location.");
        }
        else if (result is EvidenceAddResult.RejectedInvalid or EvidenceAddResult.RejectedLimit)
        {
            gaps++;
            retentionGapDetails = "An observation could not be retained. Its rejection is counted as a recording gap in the evidence.";
        }
    }

    private Task SaveAsync() => ExportEvidenceAsync();

    public async Task CloseAsync()
    {
        closing = true;
        RefreshCommands();
        await CollectCommand.CurrentExecution;
        await StartCommand.CurrentExecution;
        await SaveCommand.CurrentExecution;
        await MarkCommand.CurrentExecution;
        if (recording || observer is not null)
        {
            await GuardAsync(StopAsync);
        }
        session?.Dispose();
    }

    private bool Marker(EvidenceMarkerKind kind, string activity, string label, EvidenceProvenance provenance, string? value = null, PowerSourceKind? powerSource = null)
    {
        var result = session?.AddMarker(new EvidenceMarker(RelativeMilliseconds, kind, activity, label, provenance, value)
        { PowerSourceAtMarker = powerSource });
        if (result == EvidenceAddResult.RejectedInvalid)
        {
            throw new InvalidDataException("The marker was rejected. Use a short setting value without personal details or paths.");
        }
        if (result == EvidenceAddResult.RejectedLimit)
        {
            ReachCapacity("Checklist space is full.");
            retentionGapDetails = $"The session reached its {EvidenceLimits.MaxMarkers} action-marker limit. Further markers are counted as rejected in the ZIP. Stop and save the partial evidence ZIP.";
            status = "This action could not be recorded because the session is full. " + CapacityInstructions;
            Refresh();
            return false;
        }
        if (result == EvidenceAddResult.Added && kind == EvidenceMarkerKind.UserReportedStartingValue) dropSegmentNumber++;
        return result == EvidenceAddResult.Added;
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
            session?.AddMarker(new EvidenceMarker(RelativeMilliseconds, EvidenceMarkerKind.Failure, "session", "Collector step failed", EvidenceProvenance.Collector, ex.GetType().Name));
            Refresh();
        }
        finally
        {
            CheckMarkerCapacity();
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
        if (stage >= 2)
        {
            recording = true;
            startingSetting = "Balanced";
            StartingEntry.SetReportedText("Balanced");
            ChangedEntry.SetReportedText("Silent");
            stepPhase = 1;
            recorderStatus = "Recording selected laptop feature calls. Example data only.";
            if (stage == 3)
            {
                StartingEntry.SelectedChoice = StartingEntry.Choices.Single(choice => choice.Label == "Custom");
                StartingEntry.Details = "CPU Boost, GPU High";
                stepPhase = 0;
            }
        }

        if (stage == 4)
        {
            startingSetting = "Custom: CPU Boost, GPU High";
            ReachCapacity("Local evidence storage could not continue. Check free disk space and save the partial ZIP to another location.");
            recording = false;
            ResetEntries();
            status = CapacityInstructions;
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
        ProbeSpecialKeyCommand?.Refresh(); CancelSpecialKeyProbeCommand?.Refresh();
        CollectCommand.Refresh(); StartCommand.Refresh(); StopCommand.Refresh(); MarkCommand.Refresh();
        SkipCommand.Refresh(); UnavailableCommand.Refresh(); SaveCommand.Refresh();
    }

    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
