using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SynapseObserver.App;
using SynapseObserver.Backend;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class CapacityRecoveryTests
{
    private static readonly LocalHidCandidate Candidate = new(
        "owned-device-path", "owned-interface", "Razer Blade owned fixture", 0x1532, 0xFFFF, null,
        1, 2, new HidReportGeometry(0, 0, 1), CollectionState.Collected, null,
        null, null, null, "owned-root", "owned-hardware", "Razer Blade owned fixture");

    private static readonly BackendCandidate Backend = new(42, "Owned", true, "Owned fixture");

    [Fact]
    public async Task LastRequestSlotLatchesCapacityStopsCleanlyAndZeroesWholeNativeEvent()
    {
        var model = await RecordingAsync();
        model.EvidenceDestinationForTest = () => Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.zip");
        var observer = CapacityObserver.Boundary();

        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);
        await WaitForCapacityStopAsync(model, observer);

        var evidence = model.Evidence!;
        Assert.Equal(EvidenceLimits.MaxObservations, evidence.Observations.Count);
        Assert.Equal(1, evidence.RejectedObservationCount);
        var last = evidence.Observations[^1];
        Assert.Equal($"recording-1-op-{EvidenceLimits.MaxObservations}", last.OperationId);
        Assert.Equal(ObservationSource.UserModeCall, last.Source);
        Assert.DoesNotContain(evidence.Observations, item =>
            item.OperationId == last.OperationId && item.Source == ObservationSource.ReturnedBuffer);
        Assert.All(observer.RawBuffers, buffer => Assert.All(buffer, value => Assert.Equal((byte)0, value)));

        Assert.True(model.ShowCapacityRecovery);
        Assert.Contains("observation", model.CapacitySummary, StringComparison.OrdinalIgnoreCase);
        Assert.False(model.IsRecording);
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.False(model.MarkCommand.CanExecute(null));
        Assert.False(model.SkipCommand.CanExecute(null));
        Assert.False(model.UnavailableCommand.CanExecute(null));
        Assert.False(model.CanSelectActivity);
        Assert.False(model.CanProbeSpecialKeys);
        Assert.True(model.SaveCommand.CanExecute(null));
        Assert.Equal(1, observer.Stops);
        Assert.Equal(1, observer.Disposals);
        Assert.Equal(0, observer.Invalidations);
        await model.CloseAsync();
    }

    [Fact]
    public async Task CapacityDuringPendingPowerReadCannotAdvanceMarkerPhase()
    {
        var model = await RecordingAsync();
        var observer = CapacityObserver.BeforeBoundary();
        model.SetOwnedTestObserver(observer, [Candidate]);
        await Poll(model);
        Assert.Equal(EvidenceLimits.MaxObservations - 1, CurrentSession(model).ObservationCount);

        var powerRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePower = new TaskCompletionSource<PowerSourceKind>(TaskCreationOptions.RunContinuationsAsynchronously);
        model.ActionPowerSourceForTest = async () =>
        {
            powerRead.TrySetResult();
            return await releasePower.Task;
        };
        model.StartingSetting = "Balanced";
        model.MarkCommand.Execute(null);
        await powerRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        observer.EnqueueBoundary();
        model.SetOwnedTestObserver(observer, startPump: true);
        await WaitUntilAsync(() => model.ShowCapacityRecovery);
        releasePower.SetResult(PowerSourceKind.Ac);
        await model.MarkCommand.CurrentExecution;
        await WaitForCapacityStopAsync(model, observer);

        Assert.True(model.ShowStartingValue);
        Assert.False(model.CapacityRestorationRequired);
        Assert.DoesNotContain(model.Evidence!.Markers,
            marker => marker.Kind == EvidenceMarkerKind.UserReportedStartingValue);
        Assert.All(observer.RawBuffers, buffer => Assert.All(buffer, value => Assert.Equal((byte)0, value)));
        await model.CloseAsync();
    }

    [Fact]
    public async Task InterruptedSettingRequiresExplicitRestorationBeforeContinuing()
    {
        var model = await RecordingAsync();
        model.EvidenceDestinationForTest = () => Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.zip");
        model.StartingSetting = "Balanced";
        await Run(model.MarkCommand);
        var observer = CapacityObserver.Boundary();

        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);
        await WaitForCapacityStopAsync(model, observer);

        Assert.True(model.CapacityRestorationRequired);
        Assert.False(model.CapacityRestorationConfirmed);
        Assert.Contains("Balanced", model.CapacityRestorationLabel);
        Assert.Contains(model.Evidence!.Coverage, item =>
            item.ActivityId == "performance" && item.Reason == CoverageReason.IncompleteRestoration);
        Assert.True(model.SaveCommand.CanExecute(null));
        Assert.False(model.SaveAndContinueCommand.CanExecute(null));

        model.CapacityRestorationConfirmed = true;

        Assert.True(model.SaveAndContinueCommand.CanExecute(null));
        await model.CloseAsync();
    }

    [Fact]
    public async Task SuccessfulContinuationWritesTwoIndependentPartsAndRetriesInterruptedActivity()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-capacity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var firstPath = Path.Combine(directory, "part-1.zip");
            var secondPath = Path.Combine(directory, "part-2.zip");
            var destination = firstPath;
            var model = await RecordingAsync();
            model.EvidenceDestinationForTest = () => destination;

            var performance = ActivityIndex(model, "performance");
            var power = ActivityIndex(model, "power-source");
            foreach (var index in Enumerable.Range(0, model.Activities.Count)
                         .Where(index => index != performance && index != power))
            {
                model.SelectedActivityIndex = index;
                await Run(model.SkipCommand);
            }
            model.SelectedActivityIndex = power;
            model.StartingSetting = "Battery";
            await Run(model.MarkCommand);
            model.SelectedActivityIndex = performance;
            model.StartingSetting = "Balanced";
            await Run(model.MarkCommand);

            var firstObserver = CapacityObserver.Boundary();
            var secondObserver = CapacityObserver.Empty();
            var starts = 0;
            model.SetOwnedTestObserver(firstObserver, [Candidate], startPump: true);
            model.SetOwnedStartForTest(Backend, () =>
            {
                starts++;
                return Task.FromResult<INativeObservationSession>(secondObserver);
            });
            await WaitForCapacityStopAsync(model, firstObserver);
            var first = model.Evidence!;
            model.CapacityRestorationConfirmed = true;

            await Run(model.SaveAndContinueCommand);

            var fresh = model.Evidence!;
            Assert.NotEqual(first.StartedAtUtc, fresh.StartedAtUtc);
            Assert.Equal(first.Inventory.CollectedAtUtc, fresh.Inventory.CollectedAtUtc);
            Assert.Empty(fresh.Observations);
            Assert.Equal(0, fresh.RejectedObservationCount);
            Assert.Empty(fresh.SpecialKeyObservations);
            Assert.Empty(fresh.SpecialKeyProbes);
            Assert.All(fresh.Coverage, item => Assert.Equal(CoverageState.Pending, item.State));
            Assert.Equal(model.Activities.Count - 1, model.Activities.Count(item => item.SavedInEarlierZip));
            Assert.True(model.Activities[power].SavedInEarlierZip);
            Assert.False(model.Activities[performance].SavedInEarlierZip);
            Assert.Equal(performance, model.SelectedActivityIndex);
            Assert.True(model.AttachConsent);

            await Run(model.StartCommand);
            model.StartingSetting = "Balanced";
            await Run(model.MarkCommand);
            secondObserver.EnqueueSingleCall();
            await WaitUntilAsync(() => CurrentSession(model).ObservationCount == 1);
            Assert.Equal(1, starts);
            model.ChangedSetting = "Silent";
            await Run(model.MarkCommand);
            await Run(model.MarkCommand);
            Assert.False(model.HasCurrentStep);
            var laterWindow = Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(model.Evidence!).ActionWindows);
            Assert.True(laterWindow.CompleteUserReportedSequence);
            Assert.Single(laterWindow.Observations);
            Assert.Contains(laterWindow.Markers, marker => marker.Kind == EvidenceMarkerKind.UserReportedRestoration);
            await Run(model.StopCommand);
            var firstHash = SHA256.HashData(File.ReadAllBytes(firstPath));
            await Run(model.SaveCommand);
            Assert.Equal(firstHash, SHA256.HashData(File.ReadAllBytes(firstPath)));
            Assert.Contains("different filename", model.Status, StringComparison.OrdinalIgnoreCase);
            destination = secondPath;
            await Run(model.SaveCommand);

            using var firstManifest = ReadManifest(firstPath);
            using var secondManifest = ReadManifest(secondPath);
            var firstSession = firstManifest.RootElement.GetProperty("session");
            var secondSession = secondManifest.RootElement.GetProperty("session");
            Assert.Equal(firstSession.GetProperty("schemaVersion").GetString(),
                secondSession.GetProperty("schemaVersion").GetString());
            Assert.Equal(EvidenceLimits.MaxObservations,
                firstSession.GetProperty("observations").GetArrayLength());
            Assert.Single(secondSession.GetProperty("observations").EnumerateArray());
            Assert.Equal(firstSession.GetProperty("inventory").GetProperty("collectedAtUtc").GetString(),
                secondSession.GetProperty("inventory").GetProperty("collectedAtUtc").GetString());
            Assert.All(secondObserver.RawBuffers, buffer =>
                Assert.All(buffer, value => Assert.Equal((byte)0, value)));
            await model.CloseAsync();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledAndFailedContinuationKeepFullSessionIntact()
    {
        var model = await FullStoppedModelAsync();
        var original = model.Evidence!;
        model.EvidenceDestinationForTest = () => null;

        Assert.True(model.SaveAndContinueCommand.CanExecute(null),
            $"busy={model.IsBusy}; recording={model.IsRecording}; show={model.ShowCapacityRecovery}; " +
            $"restoreRequired={model.CapacityRestorationRequired}; restoreConfirmed={model.CapacityRestorationConfirmed}; " +
            $"markRunning={model.MarkCommand.IsRunning}");
        model.SaveAndContinueCommand.Execute(null);
        await model.SaveAndContinueCommand.CurrentExecution;

        Assert.Equal(original.StartedAtUtc, model.Evidence!.StartedAtUtc);
        Assert.Equal(EvidenceLimits.MaxObservations, model.Evidence.Observations.Count);
        Assert.True(model.ShowCapacityRecovery);

        model.EvidenceDestinationForTest = () => Path.Combine(Path.GetTempPath(), $"failed-{Guid.NewGuid():N}.zip");
        model.EvidenceWriterForTest = (_, _) => throw new IOException("Owned writer failure");
        await Run(model.SaveAndContinueCommand);

        Assert.Equal(original.StartedAtUtc, model.Evidence!.StartedAtUtc);
        Assert.Equal(EvidenceLimits.MaxObservations, model.Evidence.Observations.Count);
        Assert.True(model.ShowCapacityRecovery);
        Assert.Contains(nameof(IOException), model.Status);
        await model.CloseAsync();
    }

    [Fact]
    public async Task CloseWaitsForInFlightContinuationExportBeforeSessionSwap()
    {
        var model = await FullStoppedModelAsync();
        var original = model.Evidence!;
        var entered = new TaskCompletionSource<EvidenceSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.EvidenceDestinationForTest = () => Path.Combine(Path.GetTempPath(), $"gated-{Guid.NewGuid():N}.zip");
        model.EvidenceWriterForTest = async (session, _) =>
        {
            entered.TrySetResult(session);
            await release.Task;
        };

        model.SaveAndContinueCommand.Execute(null);
        var exported = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(original.StartedAtUtc, model.Evidence!.StartedAtUtc);
        Assert.Equal(EvidenceLimits.MaxObservations, exported.ObservationCount);
        var close = model.CloseAsync();
        Assert.False(close.IsCompleted);

        release.SetResult();
        await Task.WhenAll(close, model.SaveAndContinueCommand.CurrentExecution);
    }

    [Fact]
    public async Task FailedCapacityCleanupInvalidatesReuseAndRevokesConsent()
    {
        var model = await RecordingAsync();
        var observer = CapacityObserver.Boundary(cleanupFailure: true);
        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);
        model.SetOwnedStartForTest(Backend,
            () => Task.FromResult<INativeObservationSession>(CapacityObserver.SingleCall()));

        await WaitForCapacityStopAsync(model, observer);

        Assert.True(model.ShowCapacityRecovery);
        Assert.True(observer.Invalidations > 0);
        Assert.False(model.AttachConsent);
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.Contains(model.Evidence!.Markers, marker => marker.Label == "Recording cleanup has a gap");
        await model.CloseAsync();
    }

    private static async Task<CollectorViewModel> FullStoppedModelAsync()
    {
        var model = await RecordingAsync();
        var observer = CapacityObserver.Boundary();
        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);
        await WaitForCapacityStopAsync(model, observer);
        return model;
    }

    private static async Task<CollectorViewModel> RecordingAsync()
    {
        var model = new CollectorViewModel(preview: true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        return model;
    }

    private static int ActivityIndex(CollectorViewModel model, string id) =>
        model.Activities.Select((activity, index) => (activity, index))
            .Single(item => item.activity.Id == id).index;

    private static EvidenceSession CurrentSession(CollectorViewModel model) =>
        (EvidenceSession)typeof(CollectorViewModel)
            .GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(model)!;

    private static Task Poll(CollectorViewModel model) =>
        (Task)typeof(CollectorViewModel)
            .GetMethod("PollOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, null)!;

    private static JsonDocument ReadManifest(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("manifest.json")!;
        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitForCapacityStopAsync(CollectorViewModel model, CapacityObserver observer)
    {
        await observer.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stop = (Task?)typeof(CollectorViewModel)
            .GetField("stopTask", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(model);
        Assert.NotNull(stop);
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => !model.IsRecording && !model.IsBusy && model.ShowCapacityRecovery);
    }

    private static async Task Run(AsyncCommand command)
    {
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        await command.CurrentExecution;
    }

    private sealed class CapacityObserver : INativeObservationSession
    {
        private readonly object sync = new();
        private readonly Queue<IReadOnlyList<NativeObservationEvent>> batches = [];
        private readonly bool cleanupFailure;
        private bool finished;

        private CapacityObserver(bool cleanupFailure)
        {
            this.cleanupFailure = cleanupFailure;
        }

        public List<byte[]> RawBuffers { get; } = [];
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public int Invalidations { get; private set; }
        public long OriginTimestampTicks { get; } = System.Diagnostics.Stopwatch.GetTimestamp();

        public static CapacityObserver Boundary(bool cleanupFailure = false)
        {
            var observer = BeforeBoundary(cleanupFailure);
            observer.EnqueueBoundary();
            return observer;
        }

        public static CapacityObserver BeforeBoundary(bool cleanupFailure = false)
        {
            var observer = new CapacityObserver(cleanupFailure);
            observer.Enqueue(Enumerable.Range(1, EvidenceLimits.MaxObservations - 1)
                .Select(index => observer.Event((ulong)index, includeResponse: false))
                .ToArray());
            return observer;
        }

        public static CapacityObserver SingleCall()
        {
            var observer = Empty();
            observer.EnqueueSingleCall();
            return observer;
        }

        public static CapacityObserver Empty() => new(cleanupFailure: false);

        public void EnqueueSingleCall()
        {
            var at = (System.Diagnostics.Stopwatch.GetTimestamp() - OriginTimestampTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Enqueue([Event(1, includeResponse: false) with { RelativeMilliseconds = at, CompletionRelativeMilliseconds = at }]);
        }

        public void EnqueueBoundary() => Enqueue([
            Event((ulong)EvidenceLimits.MaxObservations, includeResponse: true),
        ]);

        public NativeSessionStatus GetStatus() => new(
            Started: true,
            Finished: finished,
            Failure: cleanupFailure && finished ? 170 : 0,
            DroppedEvents: 0,
            Diagnostic: "Owned capacity observer",
            ReuseAllowed: !cleanupFailure);

        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                return Task.FromResult(batches.Count == 0 ? (IReadOnlyList<NativeObservationEvent>)[] : batches.Dequeue());
            }
        }

        public Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stops++;
            finished = true;
            return Task.FromResult(GetStatus());
        }

        public void InvalidateReuse() => Invalidations++;

        public ValueTask DisposeAsync()
        {
            Disposals++;
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        private void Enqueue(IReadOnlyList<NativeObservationEvent> batch)
        {
            lock (sync) batches.Enqueue(batch);
        }

        private NativeObservationEvent Event(ulong sequence, bool includeResponse)
        {
            var request = new byte[] { 2 };
            var response = includeResponse ? new byte[] { 3 } : [];
            RawBuffers.Add(request);
            RawBuffers.Add(response);
            return new NativeObservationEvent(
                sequence,
                sequence,
                includeResponse ? "DeviceIoControl.HID_GET_FEATURE" : "HidD_SetFeature",
                includeResponse ? "get-feature" : "set-feature",
                true,
                0,
                request,
                response,
                null,
                1,
                includeResponse ? 1u : 0,
                0,
                sequence + 0.5,
                includeResponse ? 1u : 0);
        }
    }
}
