using System.Reflection;
using SynapseObserver.App;
using SynapseObserver.Backend;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class RecoverablePollLossTests
{
    private static readonly LocalHidCandidate Candidate = new("owned-fixture", "fixture-interface", "Blade fixture", 0x1532, 0xAAAA, null,
        1, 2, new HidReportGeometry(0, 0, 91), CollectionState.Collected, null, null, null, null, null, null, null);

    [Fact]
    public async Task ParseLossKeepsOneReaderDuringStopAndRetainsFinalBurstBeyondSharedRing()
    {
        var model = await Recording();
        var finalBurst = Enumerable.Range(1, 128).Select(index => Event((ulong)index)).ToArray();
        var observer = new OwnedDrainObserver(finalBurst, throwParseLossFirst: true);
        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);

        await observer.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Cleanup(model).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(128, model.Evidence!.Observations.Count);
        Assert.Contains("128 observed calls", model.ObservationSummary);
        Assert.Contains("1 recording gaps", model.ObservationSummary);
        Assert.Single(model.Evidence.Markers, marker => marker.Kind == EvidenceMarkerKind.Failure
            && marker.ActivityId == "observer" && marker.Provenance == EvidenceProvenance.Observer);
        Assert.Equal(1, observer.MaximumConcurrentPolls);
        Assert.Equal(1, observer.Stops);
        Assert.Equal(1, observer.Disposals);
        Assert.All(finalBurst, item => Assert.All(item.RequestBytes, value => Assert.Equal((byte)0, value)));
    }

    [Fact]
    public async Task InvalidSelectedInterfaceRejectsOnlyThatEventAndRetainsValidSuffix()
    {
        var first = Event(1);
        var invalid = Event(2, interfaceIndex: 1);
        var last = Event(3);
        var model = await Recording();
        var observer = new OwnedDrainObserver([], initialBatch: [first, invalid, last]);
        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);

        await observer.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Cleanup(model).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "recording-1-op-1", "recording-1-op-3" }, model.Evidence!.Observations.Select(item => item.OperationId));
        Assert.Contains("2 observed calls", model.ObservationSummary);
        Assert.Contains("1 recording gaps", model.ObservationSummary);
        Assert.Equal(1, observer.MaximumConcurrentPolls);
        Assert.Equal(1, observer.Stops);
        Assert.Equal(1, observer.Disposals);
        Assert.All(new[] { first, invalid, last }, item => Assert.All(item.RequestBytes, value => Assert.Equal((byte)0, value)));
    }

    private static NativeObservationEvent Event(ulong sequence, uint interfaceIndex = 0) =>
        new(sequence, 100 + sequence, "HidD_SetFeature", "set-feature", true, 0,
            [(byte)(sequence % byte.MaxValue), 1], [], null, 2, 0, interfaceIndex, 101 + sequence);

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
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (field.GetValue(model) is Task cleanup)
            {
                await cleanup;
                return;
            }
            await Task.Delay(10);
        }
        throw new TimeoutException("Owned observer cleanup did not start.");
    }

    private static async Task Run(AsyncCommand command)
    {
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        await command.CurrentExecution;
    }

    private sealed class OwnedDrainObserver : INativeObservationSession
    {
        private readonly object sync = new();
        private readonly Queue<NativeObservationEvent> finalEvents;
        private readonly IReadOnlyList<NativeObservationEvent> initialBatch;
        private readonly bool throwParseLossFirst;
        private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int polls;
        private int activePolls;
        private int maximumConcurrentPolls;
        private bool stopRequested;
        private bool finished;

        public OwnedDrainObserver(IEnumerable<NativeObservationEvent> finalEvents,
            bool throwParseLossFirst = false, IReadOnlyList<NativeObservationEvent>? initialBatch = null)
        {
            this.finalEvents = new Queue<NativeObservationEvent>(finalEvents);
            this.throwParseLossFirst = throwParseLossFirst;
            this.initialBatch = initialBatch ?? [];
        }

        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumConcurrentPolls => Volatile.Read(ref maximumConcurrentPolls);
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public NativeSessionStatus GetStatus() => new(true, Volatile.Read(ref finished), 0, 0, "Owned observer state");

        public async Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default)
        {
            int concurrent = Interlocked.Increment(ref activePolls);
            UpdateMaximum(concurrent);
            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                int poll = Interlocked.Increment(ref polls);
                if (poll == 1)
                {
                    if (throwParseLossFirst) throw new NativeObservationPollException([], 1);
                    return initialBatch;
                }

                lock (sync)
                {
                    if (!stopRequested) return [];
                    var batch = new List<NativeObservationEvent>(32);
                    while (batch.Count < 32 && finalEvents.TryDequeue(out var item)) batch.Add(item);
                    if (finalEvents.Count == 0) drained.TrySetResult();
                    return batch;
                }
            }
            finally { Interlocked.Decrement(ref activePolls); }
        }

        public async Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            Stops++;
            lock (sync)
            {
                stopRequested = true;
                if (finalEvents.Count == 0) drained.TrySetResult();
            }
            StopEntered.TrySetResult();
            await drained.Task.WaitAsync(cancellationToken);
            Volatile.Write(ref finished, true);
            return GetStatus();
        }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }

        private void UpdateMaximum(int concurrent)
        {
            int observed;
            do
            {
                observed = Volatile.Read(ref maximumConcurrentPolls);
                if (observed >= concurrent) return;
            }
            while (Interlocked.CompareExchange(ref maximumConcurrentPolls, concurrent, observed) != observed);
        }
    }
}
