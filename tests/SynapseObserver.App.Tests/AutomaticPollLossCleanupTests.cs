using System.Reflection;
using SynapseObserver.App;
using SynapseObserver.Backend;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class AutomaticPollLossCleanupTests
{
    [Fact]
    public async Task RepeatedRecoverablePollLoss_RecordsOneMarkerAndEveryRejectedRecord()
    {
        var model = await Recording();
        var observer = new RepeatedPollLossObserver([2, 1, 2]);
        model.SetOwnedTestObserver(observer, startPump: true);

        await observer.ThirdPollReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Cleanup(model).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("5 recording gaps", model.ObservationSummary);
        Assert.Single(model.Evidence!.Markers,
            marker => marker.Kind == EvidenceMarkerKind.Failure
                && marker.ActivityId == "observer"
                && marker.Provenance == EvidenceProvenance.Observer
                && marker.Label == "Malformed observer records rejected");
        Assert.Single(model.Evidence.Markers,
            marker => marker.Kind == EvidenceMarkerKind.RecordingStopped);
        Assert.Equal(1, observer.Stops);
        Assert.Equal(1, observer.Disposals);
    }

    [Fact]
    public async Task FinishedStatusAfterFirstTypedLoss_DoesNotPublishExpiryBeforeAutomaticCleanup()
    {
        var model = await Recording();
        var statuses = new List<string>();
        model.PropertyChanged += (_, _) => statuses.Add(model.Status);
        var observer = new FinishedPollLossObserver();

        model.SetOwnedTestObserver(observer, startPump: true);
        await Cleanup(model).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(statuses,
            status => status.Contains("time limit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(statuses,
            status => status.Contains("Stop and save", StringComparison.Ordinal));
        Assert.Contains("Recording stopped with gaps", model.Status);
        Assert.Contains("Save the partial evidence ZIP", model.Status);
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

    private sealed class RepeatedPollLossObserver(IReadOnlyList<int> rejectedByPoll) : INativeObservationSession
    {
        private readonly TaskCompletionSource thirdPollReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int polls;
        private int stops;
        private int disposals;
        private bool finished;

        public TaskCompletionSource ThirdPollReached => thirdPollReached;
        public int Stops => Volatile.Read(ref stops);
        public int Disposals => Volatile.Read(ref disposals);
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();

        public NativeSessionStatus GetStatus() =>
            new(true, Volatile.Read(ref finished), 0, 0, "Owned observer state");

        public async Task<IReadOnlyList<NativeObservationEvent>> PollAsync(
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var poll = Interlocked.Increment(ref polls);
            if (poll >= 3) thirdPollReached.TrySetResult();
            if (poll <= rejectedByPoll.Count)
            {
                throw new NativeObservationPollException([], rejectedByPoll[poll - 1]);
            }
            return [];
        }

        public async Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref stops);
            await thirdPollReached.Task.WaitAsync(cancellationToken);
            Volatile.Write(ref finished, true);
            return GetStatus();
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposals);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FinishedPollLossObserver : INativeObservationSession
    {
        private int polls;
        private int stops;
        private int disposals;

        public int Stops => Volatile.Read(ref stops);
        public int Disposals => Volatile.Read(ref disposals);
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public NativeSessionStatus GetStatus() => new(true, true, 0, 0, "Owned observer state");

        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Interlocked.Increment(ref polls) == 1
                ? Task.FromException<IReadOnlyList<NativeObservationEvent>>(
                    new NativeObservationPollException([], 1))
                : Task.FromResult<IReadOnlyList<NativeObservationEvent>>([]);
        }

        public Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref stops);
            return Task.FromResult(GetStatus());
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposals);
            return ValueTask.CompletedTask;
        }
    }
}
