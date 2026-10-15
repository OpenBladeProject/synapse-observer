using SynapseObserver.App;
using SynapseObserver.Backend;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class LateStopCompletionTests
{
    private static readonly LocalHidCandidate Candidate = new("owned-fixture", "fixture-interface", "Blade fixture", 0x1532, 0xAAAA, null,
        1, 2, new HidReportGeometry(0, 0, 91), CollectionState.Collected, null, null, null, null, null, null, null);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnconfirmedFirstWaitRetainsLateEventsAndFinalCountsOrExplicitFailure(bool lateFinish)
    {
        var model = new CollectorViewModel(true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        var observer = new LateObserver(lateFinish);
        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);
        await Run(model.StopCommand).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, observer.StopRequests);
        Assert.Equal(1, observer.Disposals);
        Assert.Equal(1, observer.MaximumConcurrentPolls);
        Assert.Equal(lateFinish ? 128 : 0, model.Evidence!.Observations.Count);
        Assert.Contains(lateFinish ? "7 recording gaps" : "8 recording gaps", model.ObservationSummary);
        Assert.Contains(model.Evidence.Markers, marker => marker.Label == "Observer queue dropped events" && marker.Value == "count-7");
        Assert.Contains("partial evidence", model.Status);
        Assert.Single(model.Evidence.Markers, marker => marker.Kind == EvidenceMarkerKind.RecordingStopped);
        if (lateFinish)
        {
            Assert.DoesNotContain(model.Evidence.Markers, marker => marker.Label == "Recording cleanup has a gap");
            Assert.DoesNotContain("restart Synapse", model.Status);
        }
        else Assert.Contains(model.Evidence.Markers, marker => marker.Label == "Recording cleanup has a gap");
        Assert.All(observer.Buffers, buffer => Assert.All(buffer, value => Assert.Equal((byte)0, value)));
    }
    private static async Task Run(AsyncCommand command)
    {
        Assert.True(command.CanExecute(null)); command.Execute(null); await command.CurrentExecution;
    }
    private sealed class LateObserver(bool lateFinish) : INativeObservationSession
    {
        private int retained, activePolls;
        private bool finished;
        public List<byte[]> Buffers { get; } = [];
        public int StopRequests { get; private set; }
        public int Disposals { get; private set; }
        public int MaximumConcurrentPolls { get; private set; }
        private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public NativeSessionStatus GetStatus() => new(true, finished, 0, StopRequests >= 2 ? 7 : 0, "Owned stop state");
        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default)
        {
            MaximumConcurrentPolls = Math.Max(MaximumConcurrentPolls, ++activePolls);
            try
            {
                var batch = new List<NativeObservationEvent>();
                if (lateFinish && StopRequests >= 2)
                    for (var index = 0; index < 32 && retained < 128; index++)
                    {
                        var bytes = new byte[] { 0, 1 }; Buffers.Add(bytes);
                        batch.Add(new((ulong)++retained, retained, "HidD_SetFeature", "set-feature", true, 0, bytes, [], null, 2, 0, 0, retained + 1));
                    }
                if (retained == 128) drained.TrySetResult();
                return Task.FromResult<IReadOnlyList<NativeObservationEvent>>(batch);
            }
            finally { activePolls--; }
        }
        public async Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            StopRequests++;
            if (StopRequests == 1 || !lateFinish)
                return GetStatus() with { Failure = 170, Diagnostic = "Owned stop unconfirmed" };
            await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
            finished = true;
            return GetStatus();
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
