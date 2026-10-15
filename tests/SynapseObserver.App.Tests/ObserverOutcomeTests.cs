using System.ComponentModel;
using System.Reflection;
using SynapseObserver.App;
using SynapseObserver.Backend;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class ObserverOutcomeTests
{
    private static readonly LocalHidCandidate Candidate = new("owned-fixture", "fixture-interface", "Blade fixture", 0x1532, 0xAAAA, null,
        1, 2, new HidReportGeometry(0, 0, 91), CollectionState.Collected, null, null, null, null, null, null, null);

    [Theory]
    [InlineData(true, "return-length-unknown", 0)]
    [InlineData(false, null, 1)]
    public async Task CompletedReadsAndCapturedFailuresDoNotBecomeCollectionLoss(bool success, string? note, int failed)
    {
        var model = await BeginStep();
        var response = success ? new byte[91] : [];
        var item = new NativeObservationEvent(1, 100, "HidD_GetFeature", "get-feature", success, success ? 0u : 5u,
            [0], response, note, 91, success ? 91u : 0u, 0, 101, 91);
        var observer = new OwnedObserver([item]);
        model.SetOwnedTestObserver(observer, [Candidate]);
        await Poll(model);
        var evidence = model.Evidence!;
        Assert.All(evidence.Observations, observation =>
        {
            Assert.Null(observation.ErrorCategory);
            Assert.Null(observation.ReturnedBytes);
            Assert.Equal(success ? ObservationStatus.Succeeded : ObservationStatus.Failed, observation.Status);
        });
        if (success)
            Assert.True(Assert.Single(evidence.Observations, item => item.Source == ObservationSource.ReturnedBuffer).ReturnedLengthUnknown);
        else
            Assert.Equal(5, Assert.Single(evidence.Observations).NativeStatus);
        Assert.Contains($"{failed} failed calls", model.ObservationSummary);
        Assert.Contains("0 recording gaps", model.ObservationSummary);
        await CompleteStep(model);
        var coverage = Assert.Single(model.Evidence!.Coverage, item => item.ActivityId == "performance");
        Assert.Equal(CoverageState.Completed, coverage.State);
        Assert.Equal(CoverageReason.None, coverage.Reason);
        await Run(model.StopCommand);
    }

    [Fact]
    public async Task NativeHidDCaveatMappingRetainsAReviewedResponseThroughTheRealSessionPipeline()
    {
        var model = await Recording();
        using var syntheticSession = new EvidenceSession(SyntheticPreview.CreateInventory());
        typeof(CollectorViewModel).GetField("session", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(model, syntheticSession);
        var candidate = Candidate with { InterfaceLabel = "hid-preview-01", ProductId = 0xFFFF };
        var bytes = new byte[91];
        bytes[1] = 2; bytes[2] = 0x1F; bytes[6] = 4; bytes[7] = 0x0D; bytes[8] = 0x82;
        bytes[9] = 1; bytes[10] = 1; bytes[11] = 5;
        for (var index = 3; index <= 88; index++) bytes[89] ^= bytes[index];
        model.SetOwnedTestObserver(new OwnedObserver([new(1, 100, "HidD_GetFeature", "get-feature", true, 0,
            [0], bytes, "return-length-unknown", 91, 91, 0, 101, 91)]), [candidate]);
        await Poll(model);
        var retained = Assert.Single(model.Evidence!.Observations, item => item.Source == ObservationSource.ReturnedBuffer);
        Assert.Equal(FeaturePayloadProjectionKind.ReviewedThermalMode, retained.PayloadProjectionKind);
        Assert.NotNull(retained.PayloadHex);
        Assert.True(retained.ReturnedLengthUnknown);
        Assert.Null(retained.ReturnedBytes);
        Assert.Null(retained.ErrorCategory);
        Assert.All(bytes, item => Assert.Equal((byte)0, item));
        Assert.Contains("0 recording gaps", model.ObservationSummary);
        await Run(model.StopCommand);
    }

    [Fact]
    public async Task PendingCompletionRemainsAnObservationGap()
    {
        var model = await BeginStep();
        model.SetOwnedTestObserver(new OwnedObserver([new(1, 100, "DeviceIoControl.HID_GET_FEATURE", "get-feature", false, 997,
            [0], [], "pending-completion-not-observed", 1, 0, 0, 101, 91)]), [Candidate]);
        await Poll(model);
        Assert.Equal(ObservationStatus.Pending, Assert.Single(model.Evidence!.Observations).Status);
        Assert.Contains("0 failed calls", model.ObservationSummary);
        Assert.Contains("1 recording gaps", model.ObservationSummary);
        await CompleteStep(model);
        Assert.Equal(CoverageReason.CaptureFailed, Assert.Single(model.Evidence!.Coverage, item => item.ActivityId == "performance").Reason);
        await Run(model.StopCommand);
    }

    [Fact]
    public async Task ZeroObservationQueueLossTakesPrecedenceOverNoMatchingEvents()
    {
        var model = await BeginStep();
        var observer = new OwnedObserver([]) { Dropped = 3 };
        model.SetOwnedTestObserver(observer);
        await Poll(model);
        await CompleteStep(model);
        Assert.Empty(model.Evidence!.Observations);
        Assert.Equal(CoverageReason.CaptureFailed, Assert.Single(model.Evidence.Coverage, item => item.ActivityId == "performance").Reason);
        Assert.Contains(model.Evidence.Markers, item => item.Label == "Observer queue dropped events");
        await Run(model.StopCommand);
    }

    [Fact]
    public async Task EmptyPollsNotifyNothingAndRealChangesNotifyOnlySummaries()
    {
        var model = await Recording();
        var observer = new OwnedObserver([]);
        model.SetOwnedTestObserver(observer, [Candidate]);
        var notifications = new List<string?>();
        model.PropertyChanged += (_, item) => notifications.Add(item.PropertyName);
        var commandNotifications = 0;
        model.MarkCommand.CanExecuteChanged += (_, _) => commandNotifications++;
        model.StopCommand.CanExecuteChanged += (_, _) => commandNotifications++;
        for (int index = 0; index < 50; index++) await Poll(model);
        Assert.Empty(notifications);
        Assert.Equal(0, commandNotifications);
        observer.Events = [new(1, 100, "HidD_SetFeature", "set-feature", true, 0, [0], [], null, 1, 0, 0, 101)];
        await Poll(model);
        Assert.Equal(new[] { "ObservationSummary", "CollectionDetails", "ExportSummary" }, notifications);
        Assert.Equal(0, commandNotifications);
        notifications.Clear();
        observer.Dropped = 2;
        await Poll(model);
        Assert.Equal(new[] { "ObservationSummary", "CollectionDetails", "ExportSummary" }, notifications);
        Assert.Contains("2 recording gaps", model.ObservationSummary);
        notifications.Clear();
        await Poll(model);
        Assert.Empty(notifications);
        await Run(model.StopCommand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(170)]
    public async Task AutonomousEndDistinguishesExpiryFromFailureAndKeepsRestorationReminder(int failure)
    {
        var model = await BeginStep();
        var observer = new OwnedObserver([]) { Finished = true, Failure = failure };
        model.SetOwnedTestObserver(observer, startPump: true);
        await Pump(model);
        Assert.Contains("Restore Balanced", model.Status);
        if (failure == 0)
        {
            Assert.Contains("time limit", model.Status);
            Assert.DoesNotContain(model.Evidence!.Markers, marker => marker.Label == "Native observer failed");
        }
        else
        {
            Assert.Contains("observer encountered an error", model.Status);
            Assert.DoesNotContain("time limit", model.Status);
            Assert.Contains(model.Evidence!.Markers, marker => marker.Label == "Native observer failed");
        }
        Assert.False(model.MarkCommand.CanExecute(null));
        await Run(model.StopCommand);
        Assert.Equal(1, observer.Disposals);
    }

    [Fact]
    public async Task ManualStopNeverPublishesExpiryWhilePumpFinishes()
    {
        var model = await BeginStep();
        var observer = new OwnedObserver([]) { GateStop = true };
        model.SetOwnedTestObserver(observer, startPump: true);
        var statuses = new List<string>();
        model.PropertyChanged += (_, _) => statuses.Add(model.Status);
        model.StopCommand.Execute(null);
        await observer.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Pump(model).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("Stopping recording", model.Status);
        Assert.DoesNotContain(statuses, status => status.Contains("time limit", StringComparison.Ordinal));
        observer.ReleaseStop.SetResult();
        await model.StopCommand.CurrentExecution;
        Assert.Contains("Restore Balanced", model.Status);
        Assert.DoesNotContain(statuses, status => status.Contains("time limit", StringComparison.Ordinal));
        Assert.Single(model.Evidence!.Markers, marker => marker.Kind == EvidenceMarkerKind.RecordingStopped);
        Assert.Equal(1, observer.Stops);
        Assert.Equal(1, observer.Disposals);
    }

    private static async Task<CollectorViewModel> Recording()
    {
        var model = new CollectorViewModel(true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        return model;
    }
    private static async Task<CollectorViewModel> BeginStep()
    {
        var model = await Recording();
        model.StartingSetting = "Balanced";
        await Run(model.MarkCommand);
        return model;
    }
    private static async Task CompleteStep(CollectorViewModel model)
    {
        model.ChangedSetting = "Silent";
        await Run(model.MarkCommand);
        await Run(model.MarkCommand);
    }
    private static Task Poll(CollectorViewModel model) => (Task)typeof(CollectorViewModel)
        .GetMethod("PollOnceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(model, null)!;
    private static Task Pump(CollectorViewModel model) => (Task)typeof(CollectorViewModel)
        .GetField("pump", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model)!;
    private static async Task Run(AsyncCommand command)
    {
        Assert.True(command.CanExecute(null)); command.Execute(null); await command.CurrentExecution;
    }

    private sealed class OwnedObserver(IReadOnlyList<NativeObservationEvent> events) : INativeObservationSession
    {
        public IReadOnlyList<NativeObservationEvent> Events { get; set; } = events;
        public bool Finished { get; set; }
        public int Failure { get; set; }
        public int Dropped { get; set; }
        public bool GateStop { get; set; }
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public NativeSessionStatus GetStatus() => new(true, Finished, Failure, Dropped, "Owned observer state");
        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default)
        {
            var result = Events; Events = []; return Task.FromResult(result);
        }
        public async Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            Stops++; Finished = true; StopEntered.TrySetResult();
            if (GateStop) await ReleaseStop.Task;
            return GetStatus();
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
