using SynapseObserver.App;
using SynapseObserver.Backend;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class CollectionReviewWorkflowTests
{
    [Fact]
    public async Task InventoryReportsAndSkipsRefreshTheVisibleReview()
    {
        var model = new CollectorViewModel(preview: true);
        await Run(model.CollectCommand);
        Assert.Contains("No Synapse recording", model.ReviewSummary);
        model.SelectedActivityIndex = model.Activities.Select((activity, index) => (activity, index))
            .Single(item => item.activity.Id == "keyboard-layout").index;
        model.StartingSetting = "ANSI US";
        await Run(model.MarkCommand);
        Assert.Contains("40 evidence items", model.ReviewNotes);
        await Run(model.SkipCommand);
        Assert.Contains("39 evidence items", model.ReviewNotes);
        Assert.False(model.IsBusy);
        await model.CloseAsync();
    }

    [Theory]
    [InlineData(SpecialKeyProbeEndReason.Timeout)]
    [InlineData(SpecialKeyProbeEndReason.FocusLost)]
    public async Task StandaloneKeyCheckRefreshesVisibleReview(SpecialKeyProbeEndReason reason)
    {
        var model = new CollectorViewModel(preview: true);
        await Run(model.CollectCommand);
        Assert.Contains("No Synapse recording or key check yet", model.ReviewSummary);
        typeof(CollectorViewModel).GetField("activeProbeLabel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(model, "probe-001");
        var notifications = new List<string?>();
        model.PropertyChanged += (_, item) => notifications.Add(item.PropertyName);

        await model.CompleteSpecialKeyProbeAsync(new(SpecialKeyExpectedKey.Fn, SpecialKeyModeContext.Unknown,
            reason, 0, 0, 0, string.Empty));

        Assert.Contains("1 key check", model.ReviewSummary);
        Assert.DoesNotContain("No Synapse recording or key check yet", model.ReviewSummary);
        Assert.Contains(nameof(CollectorViewModel.ReviewSummary), notifications);
        Assert.Contains(nameof(CollectorViewModel.ReviewNotes), notifications);
        Assert.False(model.IsBusy);
        await model.CloseAsync();
    }

    [Fact]
    public async Task EveryActionPhaseUsesFreshPowerAndStopRefreshesReview()
    {
        var model = new CollectorViewModel(preview: true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        var sources = new Queue<PowerSourceKind>([PowerSourceKind.Battery, PowerSourceKind.Ac, PowerSourceKind.Ac]);
        model.ActionPowerSourceForTest = () => ValueTask.FromResult(sources.Dequeue());
        model.StartingSetting = "Balanced";
        await Run(model.MarkCommand);
        model.ChangedSetting = "Silent";
        await Run(model.MarkCommand);
        await Run(model.MarkCommand);
        await Run(model.StopCommand);
        var window = Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(model.Evidence!).ActionWindows);
        Assert.Equal(PowerSourceKind.Unknown, window.PowerSourceAtAction);
        Assert.Equal(new[] { PowerSourceKind.Battery, PowerSourceKind.Ac, PowerSourceKind.Ac, PowerSourceKind.Ac },
            window.Markers.Select(marker => marker.PowerSourceAtMarker!.Value));
        Assert.Contains("1 complete action sequence", model.ReviewSummary);
        Assert.Contains("40 evidence items", model.ReviewNotes);
        Assert.False(model.IsBusy);
        await model.CloseAsync();
    }

    [Fact]
    public async Task CleanStopAllowsExplicitSecondRecordingAndPreservesPriorEvidence()
    {
        var model = new CollectorViewModel(preview: true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        model.StartingSetting = "Balanced";
        await Run(model.MarkCommand);
        model.ChangedSetting = "Silent";
        await Run(model.MarkCommand);
        await Run(model.MarkCommand);
        await Run(model.StopCommand);
        var first = model.Evidence!;
        Assert.True(model.AttachConsent);
        Assert.True(model.StartCommand.CanExecute(null));
        Assert.False(model.IsRecording);
        Assert.Contains("Recording stopped", model.Status);
        await Run(model.StartCommand);
        Assert.True(model.IsRecording);
        Assert.Equal("Recording is active. Stop when ready.", model.Status);
        Assert.DoesNotContain("Recording stopped", model.Status);
        Assert.DoesNotContain("Save", model.Status);
        await Run(model.StopCommand);
        var second = model.Evidence!;
        Assert.Equal(first.Observations.Count, second.Observations.Count);
        Assert.Equal(2, second.Markers.Count(item => item.Kind == EvidenceMarkerKind.RecordingStarted));
        Assert.Equal(2, second.Markers.Count(item => item.Kind == EvidenceMarkerKind.RecordingStopped));
        Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(second).ActionWindows);
        await model.CloseAsync();
    }

    [Fact]
    public async Task FailedSecondStartClearsStaleFeedbackAndRequiresAvailabilityCheck()
    {
        var model = new CollectorViewModel(preview: false);
        model.SetPreviewStage(1);
        var candidate = new BackendCandidate(42, "Owned", true, "Owned fixture");
        model.SetOwnedStartForTest(candidate, () => Task.FromResult<INativeObservationSession>(new CleanObserver()));
        model.AttachConsent = true;
        await Run(model.StartCommand);
        await Run(model.StopCommand);
        Assert.True(model.AttachConsent);
        Assert.Contains("Recording stopped", model.Status);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.SetOwnedStartForTest(
            candidate,
            async () =>
            {
                entered.SetResult();
                await release.Task;
                throw new InvalidOperationException(@"C:\private\observer.dll");
            });
        model.AttachConsent = true;

        Assert.True(model.StartCommand.CanExecute(null));
        model.StartCommand.Execute(null);
        await entered.Task;
        Assert.True(model.IsBusy);
        Assert.Equal("Starting recording.", model.Status);
        Assert.Equal("Checking the recording target and starting the observer.", model.RecorderStatus);

        release.SetResult();
        await model.StartCommand.CurrentExecution;

        Assert.False(model.IsRecording);
        Assert.False(model.AttachConsent);
        Assert.Contains("Check recording availability", model.Status);
        Assert.Contains("Allow observation again", model.RecorderStatus);
        Assert.DoesNotContain("InvalidOperationException", model.Status);
        Assert.DoesNotContain("private", model.RecorderStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Single(model.Evidence!.Markers, marker => marker.Label == "Recording start failed");
        model.AttachConsent = true;
        Assert.False(model.StartCommand.CanExecute(null));
        await model.CloseAsync();
    }

    private static async Task Run(AsyncCommand command)
    {
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        await command.CurrentExecution;
    }

    private sealed class CleanObserver : INativeObservationSession
    {
        private bool stopped;
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public NativeSessionStatus GetStatus() => new(true, stopped, 0, 0, stopped ? "Hooks stopped." : "Recording.", true);
        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NativeObservationEvent>>([]);
        public Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            stopped = true;
            return Task.FromResult(GetStatus());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
