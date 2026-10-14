using System.Reflection;
using SynapseObserver.App;
using SynapseObserver.Backend;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class RecordingGapLifetimeTests
{
    private static readonly LocalHidCandidate Candidate = new("owned-fixture", "fixture-interface", "Blade fixture", 0x1532, 0xAAAA, null,
        1, 2, new HidReportGeometry(0, 0, 91), CollectionState.Collected, null, null, null, null, null, null, null);

    [Fact]
    public async Task SuccessiveRecordingsCountFreshNativeDropsWithoutDiscardingSessionEvidence()
    {
        var model = await Recording();
        model.SetOwnedTestObserver(new OwnedObserver(dropped: 5));
        await Poll(model);
        await Run(model.StopCommand);
        var first = model.Evidence!;
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        model.StartingSetting = "Balanced";
        await Run(model.MarkCommand);
        model.SetOwnedTestObserver(new OwnedObserver(dropped: 3,
            events: [new(1, 100, "HidD_SetFeature", "set-feature", true, 0, [0], [], null, 1, 0, 0, 101)]), [Candidate]);
        await Poll(model);
        model.ChangedSetting = "Silent";
        await Run(model.MarkCommand);
        await Run(model.MarkCommand);
        var evidence = model.Evidence!;
        Assert.Equal(first.StartedAtUtc, evidence.StartedAtUtc);
        Assert.Contains("8 recording gaps", model.ObservationSummary);
        Assert.Contains("3 events were dropped", model.CollectionDetails);
        Assert.Contains("during this recording", model.CollectionDetails);
        var drops = evidence.Markers.Where(marker => marker.Label == "Observer queue dropped events").ToArray();
        Assert.Equal(new[] { "count-5", "count-3" }, drops.Select(marker => marker.Value));
        Assert.Equal(2, evidence.Markers.Count(marker => marker.Kind == EvidenceMarkerKind.RecordingStarted));
        var coverage = Assert.Single(evidence.Coverage, item => item.ActivityId == "performance");
        Assert.Equal(CoverageState.CompletedWithGaps, coverage.State);
        Assert.Equal(CoverageReason.CaptureFailed, coverage.Reason);
        await Run(model.StopCommand);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeFailureAddsOneGapWhetherPumpOrStopSeesItFirst(bool autonomous)
    {
        var model = await Recording();
        var observer = new OwnedObserver(failure: 170) { Finished = autonomous };
        model.SetOwnedTestObserver(observer, startPump: autonomous);
        if (autonomous) await Pump(model);
        await Run(model.StopCommand);
        Assert.Contains("1 recording gaps", model.ObservationSummary);
        Assert.Contains("ended with a gap", model.Status);
        Assert.Contains(model.Evidence!.Markers, marker => marker.Label == "Recording cleanup has a gap");
        Assert.Equal(1, observer.Stops);
        Assert.Equal(1, observer.Disposals);
    }

    [Fact]
    public async Task NativeFailureDeduplicationIsPerRecording()
    {
        var model = await Recording();
        for (var iteration = 0; iteration < 2; iteration++)
        {
            if (iteration > 0)
            {
                await Run(model.CollectCommand);
                model.AttachConsent = true;
                await Run(model.StartCommand);
            }
            model.SetOwnedTestObserver(new OwnedObserver(failure: 170) { Finished = true }, startPump: true);
            await Pump(model);
            await Run(model.StopCommand);
        }
        Assert.Contains("2 recording gaps", model.ObservationSummary);
        Assert.Equal(2, model.Evidence!.Markers.Count(marker => marker.Label == "Native observer failed"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PollingFailureKeepsExactBaselineInStatusAndInstructions(int phase)
    {
        var model = await Recording();
        model.StartingSetting = "Silent";
        await Run(model.MarkCommand);
        if (phase == 2)
        {
            model.ChangedSetting = "Balanced";
            await Run(model.MarkCommand);
        }
        var observer = new OwnedObserver { ThrowFirstPoll = true };
        model.SetOwnedTestObserver(observer, startPump: true);
        await Pump(model);
        Assert.Contains("Restore Silent", model.Status);
        Assert.Contains("Restore the starting setting: Silent", model.StepInstructions);
        Assert.DoesNotContain("Change one available setting", model.StepInstructions);
        Assert.DoesNotContain("Mark restoration", model.StepInstructions);
        Assert.False(model.MarkCommand.CanExecute(null));
        Assert.True(model.StopCommand.CanExecute(null));
        await Run(model.StopCommand);
        Assert.Contains("Restore Silent", model.Status);
        Assert.Contains("1 recording gaps", model.ObservationSummary);
        Assert.Equal(1, observer.Disposals);
    }

    [Fact]
    public async Task DisposeFailureAlsoMakesCleanupGapVisibleInSummary()
    {
        var model = await Recording();
        model.SetOwnedTestObserver(new OwnedObserver { ThrowDispose = true });
        await Run(model.StopCommand);
        Assert.Contains("1 recording gaps", model.ObservationSummary);
        Assert.Contains("ended with a gap", model.Status);
        Assert.Contains(model.Evidence!.Markers, marker => marker.Label == "Recording cleanup has a gap");
    }

    private static async Task<CollectorViewModel> Recording()
    {
        var model = new CollectorViewModel(true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        return model;
    }
    private static Task Poll(CollectorViewModel model) => (Task)typeof(CollectorViewModel)
        .GetMethod("PollOnceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(model, null)!;
    private static Task Pump(CollectorViewModel model) => (Task)typeof(CollectorViewModel)
        .GetField("pump", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model)!;
    private static async Task Run(AsyncCommand command)
    {
        Assert.True(command.CanExecute(null)); command.Execute(null); await command.CurrentExecution;
    }

    private sealed class OwnedObserver(int dropped = 0, int failure = 0, IReadOnlyList<NativeObservationEvent>? events = null) : INativeObservationSession
    {
        public bool Finished { get; set; }
        public bool ThrowFirstPoll { get; set; }
        public bool ThrowDispose { get; set; }
        private bool polled;
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public NativeSessionStatus GetStatus() => new(true, Finished, failure, dropped, "Owned observer state");
        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default)
        {
            var first = !polled; polled = true;
            if (first && ThrowFirstPoll) throw new System.IO.InvalidDataException("Owned malformed record");
            return Task.FromResult(first ? events ?? [] : (IReadOnlyList<NativeObservationEvent>)[]);
        }
        public Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            Stops++; Finished = true; return Task.FromResult(GetStatus());
        }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            if (ThrowDispose) throw new System.IO.IOException("Owned dispose failure");
            return ValueTask.CompletedTask;
        }
    }
}
