using System.Reflection;
using SynapseObserver.App;
using SynapseObserver.Backend;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class ReaderFailureRecoveryTests
{
    private static readonly LocalHidCandidate Candidate = new("owned-fixture", "fixture-interface", "Blade fixture", 0x1532, 0xAAAA, null,
        1, 2, new HidReportGeometry(0, 0, 91), CollectionState.Collected, null, null, null, null, null, null, null);

    [Theory]
    [InlineData(0)]
    [InlineData(170)]
    public async Task ReaderFailureRequestsBoundedCleanupAndCloseJoinsWithoutAnotherReader(int failure)
    {
        var model = await Recording();
        model.StartingSetting = "Silent";
        await Run(model.MarkCommand);
        var late = Event(2);
        var observer = new OwnedObserver { ThrowFirstPoll = true, GateStop = true, Failure = failure, Events = [late] };
        EvidenceSessionSnapshot? finalSnapshot = null;
        model.PropertyChanged += (_, _) =>
        {
            if (!model.IsRecording && !model.IsBusy) finalSnapshot ??= model.Evidence;
        };
        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);
        await observer.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsBusy);
        Assert.False(model.MarkCommand.CanExecute(null));
        Assert.Contains("Stopping", model.RecorderStatus);
        Assert.Contains("Restore Silent", model.Status);
        Assert.DoesNotContain("observer has stopped", model.StepInstructions);
        var close = model.CloseAsync();
        Assert.False(close.IsCompleted);
        observer.ReleaseStop.SetResult();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(failure == 0 ? 1 : 2, observer.Stops);
        Assert.Equal(1, observer.Disposals);
        Assert.Equal(1, observer.MaximumConcurrentPolls);
        Assert.False(model.IsRecording);
        Assert.Contains("partial evidence", model.Status);
        Assert.Contains("Restore Silent", model.Status);
        using var evidence = Assert.IsType<EvidenceSessionSnapshot>(finalSnapshot);
        Assert.Single(evidence.Observations);
        Assert.Single(evidence.Markers, marker => marker.Kind == EvidenceMarkerKind.RecordingStopped);
        Assert.All(late.RequestBytes, value => Assert.Equal((byte)0, value));
        Assert.Contains("Restart Synapse", model.RecorderStatus);
        Assert.Contains("restart Synapse", model.Status);
        Assert.True(observer.Invalidations > 0);
        Assert.False(model.AttachConsent);
    }

    [Fact]
    public async Task MalformedBatchRetainsValidEventsAndCountsRejectedSlotsBeforeCleanup()
    {
        var model = await Recording();
        var valid = Event(1);
        var observer = new OwnedObserver { ParseLoss = new NativeObservationPollException([valid], 2) };
        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);
        await Cleanup(model).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(model.Evidence!.Observations);
        Assert.Contains("2 recording gaps", model.ObservationSummary);
        Assert.Contains("partial evidence", model.Status);
        Assert.All(valid.RequestBytes, value => Assert.Equal((byte)0, value));
        Assert.Equal(1, observer.Stops);
        Assert.Equal(1, observer.Disposals);
        Assert.True(observer.Invalidations > 0);
        Assert.Contains("Restart Synapse", model.RecorderStatus);
        Assert.False(model.AttachConsent);
    }

    [Fact]
    public async Task DisposalFailureInvalidatesReuseEvenAfterCleanNativeStop()
    {
        var model = await Recording();
        var observer = new OwnedObserver { ThrowDispose = true };
        model.SetOwnedTestObserver(observer, [Candidate]);
        await Run(model.StopCommand);
        Assert.True(observer.Invalidations > 0);
        Assert.Contains("Restart Synapse", model.RecorderStatus);
        Assert.Contains("restart Synapse", model.Status);
        Assert.False(model.AttachConsent);
    }

    [Fact]
    public async Task CallPairsHaveDistinctRecordingPrefixesAndCleanLaterStopDoesNotInheritOldLoss()
    {
        var model = await Recording();
        using var evidence = new EvidenceSession(SyntheticPreview.CreateInventory());
        var sessionField = typeof(CollectorViewModel).GetField("session", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((EvidenceSession)sessionField.GetValue(model)!).Dispose();
        sessionField.SetValue(model, evidence);
        var candidate = Candidate with { InterfaceLabel = "hid-preview-01", ProductId = 0xFFFF };
        for (var iteration = 1; iteration <= 2; iteration++)
        {
            if (iteration > 1)
            {
                await Run(model.StartCommand);
                Assert.DoesNotContain("events were dropped", model.CollectionDetails);
            }
            model.SetOwnedTestObserver(new OwnedObserver { Events = [Event(1) with { RequestBytes = new byte[91], ResponseBytes = new byte[91], RequestedBytes = 91, OutputBufferCapacity = 91 }], Dropped = iteration == 1 ? 1 : 0 }, [candidate]);
            await Poll(model);
            await Run(model.StopCommand);
            if (iteration == 1) Assert.Contains("partial evidence", model.Status);
            else Assert.DoesNotContain("partial evidence", model.Status);
        }
        var events = model.Evidence!.Observations;
        Assert.Equal(4, events.Count);
        Assert.Equal(new[] { "recording-1-op-1", "recording-2-op-1" }, events.Select(item => item.OperationId).Distinct());
        Assert.All(events.GroupBy(item => item.OperationId), pair =>
        {
            Assert.Equal(2, pair.Count());
            Assert.Contains(pair, item => item.Source == ObservationSource.UserModeCall);
            Assert.Contains(pair, item => item.Source == ObservationSource.ReturnedBuffer);
        });
        var later = events.Last();
        Assert.Equal("recording-1-op-1", later.PayloadComparison!.PreviousOperationId);
        Assert.Contains("1 recording gaps", model.ObservationSummary);
    }

    private static NativeObservationEvent Event(ulong sequence) => new(sequence, 100, "HidD_SetFeature", "set-feature", true, 0, [0, 1], [], null, 2, 0, 0, 101);
    private static async Task<CollectorViewModel> Recording()
    {
        var model = new CollectorViewModel(true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        return model;
    }
    private static async Task Cleanup(CollectorViewModel model)
    {
        var field = typeof(CollectorViewModel).GetField("stopTask", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (field.GetValue(model) is not Task)
        {
            Assert.True(DateTime.UtcNow < deadline, "The asynchronous reader did not publish its cleanup task.");
            await Task.Delay(1);
        }
        await (Task)field.GetValue(model)!;
    }
    private static Task Poll(CollectorViewModel model) => (Task)typeof(CollectorViewModel)
        .GetMethod("PollOnceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(model, null)!;
    private static async Task Run(AsyncCommand command)
    {
        Assert.True(command.CanExecute(null)); command.Execute(null); await command.CurrentExecution;
    }
    private sealed class OwnedObserver : INativeObservationSession
    {
        public IReadOnlyList<NativeObservationEvent> Events { get; set; } = [];
        public bool ThrowFirstPoll { get; set; }
        public NativeObservationPollException? ParseLoss { get; set; }
        public bool GateStop { get; set; }
        public int Failure { get; set; }
        public int Dropped { get; set; }
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public int Invalidations { get; private set; }
        public bool ThrowDispose { get; set; }
        public int MaximumConcurrentPolls { get; private set; }
        private int polls, activePolls;
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public NativeSessionStatus GetStatus() => new(true, Stops > 0 && !GateStop && Failure == 0, Failure, Dropped,
            Failure == 0 ? "Hooks stopped." : "Stop was requested but detachment was not confirmed.");
        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default)
        {
            MaximumConcurrentPolls = Math.Max(MaximumConcurrentPolls, ++activePolls);
            try
            {
                if (polls++ == 0)
                {
                    if (ParseLoss is not null) throw ParseLoss;
                    if (ThrowFirstPoll) throw new System.IO.InvalidDataException("Owned reader failure");
                }
                var batch = Events; Events = []; return Task.FromResult(batch);
            }
            finally { activePolls--; }
        }
        public async Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            Stops++; StopEntered.TrySetResult();
            if (GateStop) await ReleaseStop.Task;
            GateStop = false;
            return GetStatus();
        }
        public void InvalidateReuse() => Invalidations++;
        public ValueTask DisposeAsync()
        {
            Disposals++;
            if (ThrowDispose) throw new System.IO.IOException("Owned disposal failure");
            return ValueTask.CompletedTask;
        }
    }
}
