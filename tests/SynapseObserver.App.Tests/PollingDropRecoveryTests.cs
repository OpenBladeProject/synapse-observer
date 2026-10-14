using System.Reflection;
using SynapseObserver.App;
using SynapseObserver.Backend;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class PollingDropRecoveryTests
{
    [Fact]
    public async Task PersistentPollingFaultRetainsKnownAndLaterStopDropCounts()
    {
        var model = new CollectorViewModel(true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        var observer = new FaultedObserver();
        model.SetOwnedTestObserver(observer, startPump: true);
        var cleanup = (Task)typeof(CollectorViewModel).GetField("stopTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model)!;
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("9 recording gaps", model.ObservationSummary);
        var evidence = model.Evidence!;
        var drops = evidence.Markers.Where(marker => marker.Label == "Observer queue dropped events").ToArray();
        Assert.Equal(new[] { "count-5", "count-7" }, drops.Select(marker => marker.Value));
        Assert.Contains(ContributionDraftBuilder.CreateQualityReceipt(evidence).Gaps,
            gap => gap.Code == "RecordingObservationLoss");
        Assert.Equal(1, observer.Stops);
        Assert.Equal(1, observer.Disposals);
        Assert.Contains("partial evidence", model.Status);
    }

    private static async Task Run(AsyncCommand command)
    {
        Assert.True(command.CanExecute(null)); command.Execute(null); await command.CurrentExecution;
    }
    private sealed class FaultedObserver : INativeObservationSession
    {
        public int Stops { get; private set; }
        public int Disposals { get; private set; }
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public NativeSessionStatus GetStatus() => new(true, Stops > 0, 0, Stops > 0 ? 7 : 5, "Owned cleanup complete");
        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default)
            => throw new System.IO.IOException("Owned persistent unreadable ring");
        public Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            Stops++; return Task.FromResult(GetStatus());
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
