using SynapseObserver.App;
using SynapseObserver.Core;
using SynapseObserver.Backend;

namespace SynapseObserver.App.Tests;

public sealed class CollectorWorkflowTests
{
    [Fact]
    public async Task StartingValueIsRequiredAndCannotBeSkippedAfterChangeBegins()
    {
        var model = await RecordingAsync();
        Assert.False(model.MarkCommand.CanExecute(null));
        model.StartingSetting = "Silent";
        await Run(model.MarkCommand);
        Assert.False(model.ShowStartingValue);
        Assert.True(model.ShowChangedValue);
        Assert.False(model.SkipCommand.CanExecute(null));
        Assert.False(model.UnavailableCommand.CanExecute(null));
        Assert.False(model.MarkCommand.CanExecute(null));
        model.ChangedSetting = "Balanced";
        Assert.True(model.MarkCommand.CanExecute(null));
    }

    [Fact]
    public async Task InvalidPersonalPathCannotAdvanceStartingMarker()
    {
        var model = await RecordingAsync();
        model.StartingSetting = @"C:\Users\person";
        await Run(model.MarkCommand);
        Assert.True(model.ShowStartingValue);
        Assert.Equal(1, model.Evidence!.RejectedMarkerCount);
        Assert.DoesNotContain(model.Evidence.Markers, marker => marker.Kind == EvidenceMarkerKind.UserReportedStartingValue);
    }

    [Fact]
    public async Task RestorationUsesOriginalReportedValueAndRetainsMissingObservationGap()
    {
        var model = await RecordingAsync();
        model.StartingSetting = "Silent";
        await Run(model.MarkCommand);
        model.ChangedSetting = "Performance";
        await Run(model.MarkCommand);
        Assert.Contains("Silent", model.StepInstructions);
        await Run(model.MarkCommand);
        var evidence = model.Evidence!;
        var restore = Assert.Single(evidence.Markers, marker => marker.Kind == EvidenceMarkerKind.UserReportedRestoration);
        Assert.Equal("Silent", restore.Value);
        Assert.Equal(EvidenceProvenance.UserReported, restore.Provenance);
        var performance = Assert.Single(evidence.Coverage, item => item.ActivityId == "performance");
        Assert.Equal(CoverageState.CompletedWithGaps, performance.State);
        Assert.Equal(CoverageReason.NoMatchingEvents, performance.Reason);
    }

    [Fact]
    public async Task StopDuringChangePreservesIncompleteRestorationAndStartingValue()
    {
        var model = await RecordingAsync();
        model.StartingSetting = "Silent";
        await Run(model.MarkCommand);
        await Run(model.StopCommand);
        Assert.False(model.IsRecording);
        Assert.Contains("Restore Silent", model.Status);
        Assert.Contains(model.Evidence!.Coverage, item => item.Reason == CoverageReason.IncompleteRestoration);
        Assert.DoesNotContain(model.Evidence.Markers, marker => marker.Kind == EvidenceMarkerKind.UserReportedRestoration);
    }

    [Fact]
    public async Task MalformedFinalDrainStillDisposesAndPreservesRestorationReminder()
    {
        var model = await RecordingAsync();
        model.StartingSetting = "Silent";
        await Run(model.MarkCommand);
        var observer = new MalformedOwnedObserver();
        model.SetOwnedTestObserver(observer);
        await Run(model.StopCommand);
        Assert.True(observer.Stopped);
        Assert.True(observer.Disposed);
        Assert.False(model.IsRecording);
        Assert.Contains("Restore Silent", model.Status);
        Assert.Contains(model.Evidence!.Coverage, item => item.Reason == CoverageReason.IncompleteRestoration);
        Assert.Contains(model.Evidence.Markers, marker => marker.Label == "Final observer drain failed");
    }

    [Fact]
    public async Task RefreshPreservesEarlierEvidence()
    {
        var model = await RecordingAsync();
        await Run(model.SkipCommand);
        await Run(model.StopCommand);
        var before = model.Evidence!;
        await Run(model.CollectCommand);
        Assert.Equal(before.StartedAtUtc, model.Evidence!.StartedAtUtc);
        Assert.Equal(before.Markers, model.Evidence.Markers);
        Assert.Contains(model.Evidence.Coverage, item => item.ActivityId == "performance" && item.State == CoverageState.Skipped);
    }

    [Fact]
    public async Task UnavailableSettingRemainsUserReportedAndSyntheticPreviewCannotExport()
    {
        var model = await RecordingAsync();
        await Run(model.UnavailableCommand);
        var skipped = Assert.Single(model.Evidence!.Markers, marker => marker.Kind == EvidenceMarkerKind.ItemSkipped);
        Assert.Equal(EvidenceProvenance.UserReported, skipped.Provenance);
        await Run(model.StopCommand);
        Assert.False(model.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task CommandTracksExecutionAndRejectsReentry()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var command = new AsyncCommand(async () => { calls++; await completion.Task; });
        command.Execute(null);
        command.Execute(null);
        Assert.False(command.CanExecute(null));
        Assert.Equal(1, calls);
        completion.SetResult();
        await command.CurrentExecution;
    }

    private static async Task<CollectorViewModel> RecordingAsync()
    {
        var model = new CollectorViewModel(true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        Assert.True(model.IsRecording);
        return model;
    }

    private static async Task Run(AsyncCommand command)
    {
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        await command.CurrentExecution;
    }

    private sealed class MalformedOwnedObserver : INativeObservationSession
    {
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public NativeSessionStatus GetStatus() => new(true, Stopped, 0, 0, "Owned fixture");
        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default) =>
            throw new System.IO.InvalidDataException("Owned malformed record fixture");
        public Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            Stopped = true;
            return Task.FromResult(GetStatus());
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
