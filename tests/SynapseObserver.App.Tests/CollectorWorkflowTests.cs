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

    [Fact]
    public async Task PresetsNeedNoTypingAndRestoreFrozenStartingChoice()
    {
        var model = await RecordingAsync();
        model.StartingEntry.SelectedChoice = model.StartingEntry.Choices.Single(c => c.Label == "Balanced");
        Assert.True(model.MarkCommand.CanExecute(null));
        await Run(model.MarkCommand);
        model.StartingEntry.SelectedChoice = model.StartingEntry.Choices.Single(c => c.Label == "Silent");
        model.ChangedEntry.SelectedChoice = model.ChangedEntry.Choices.Single(c => c.Label == "Silent");
        await Run(model.MarkCommand);
        Assert.Contains("Balanced", model.StepInstructions);
        await Run(model.MarkCommand);
        Assert.Equal("Balanced", Assert.Single(model.Evidence!.Markers, m => m.Kind == EvidenceMarkerKind.UserReportedRestoration).Value);
        Assert.Null(model.StartingEntry.SelectedChoice);
        Assert.False(model.MarkCommand.CanExecute(null));
    }

    [Fact]
    public async Task CustomModeRequiresDetailsAndOtherStateAllowsUnfamiliarMode()
    {
        var model = await RecordingAsync();
        model.StartingEntry.SelectedChoice = model.StartingEntry.Choices.Single(c => c.Label == "Custom");
        Assert.True(model.StartingEntry.ShowDetails);
        Assert.False(model.MarkCommand.CanExecute(null));
        model.StartingEntry.Details = "CPU Boost, GPU High";
        await Run(model.MarkCommand);
        model.ChangedEntry.SelectedChoice = model.ChangedEntry.Choices.Single(c => c.IsOther);
        Assert.False(model.MarkCommand.CanExecute(null));
        model.ChangedEntry.Details = "Future mode";
        await Run(model.MarkCommand);
        await Run(model.MarkCommand);
        Assert.Equal("Custom: CPU Boost, GPU High", Assert.Single(model.Evidence!.Markers, m => m.Kind == EvidenceMarkerKind.UserReportedRestoration).Value);
        Assert.Contains(model.Evidence.Markers, m => m.Kind == EvidenceMarkerKind.UserReportedTargetValue && m.Value == "Future mode");
    }

    [Fact]
    public async Task FixedFansRequireSpeedAndChangingChoiceClearsDetails()
    {
        var model = await RecordingAsync();
        await Run(model.SkipCommand);
        model.StartingEntry.SelectedChoice = model.StartingEntry.Choices.Single(c => c.Label == "Fixed");
        Assert.False(model.MarkCommand.CanExecute(null));
        model.StartingEntry.Details = "CPU 4200 RPM, GPU 4000 RPM";
        await Run(model.MarkCommand);
        model.ChangedEntry.SelectedChoice = model.ChangedEntry.Choices.Single(c => c.Label == "Fixed");
        model.ChangedEntry.Details = "4800 RPM";
        model.ChangedEntry.SelectedChoice = model.ChangedEntry.Choices.Single(c => c.Label == "Automatic");
        Assert.False(model.ChangedEntry.ShowDetails);
        Assert.Equal(string.Empty, model.ChangedEntry.Details);
        await Run(model.MarkCommand);
        await Run(model.MarkCommand);
        Assert.Equal("Fixed: CPU 4200 RPM, GPU 4000 RPM", Assert.Single(model.Evidence!.Markers, m => m.Kind == EvidenceMarkerKind.UserReportedRestoration).Value);
    }

    [Fact]
    public async Task EveryActivityHasOtherStateAndFinishedStepsHideEditors()
    {
        var model = await RecordingAsync();
        for (var i = 0; i < 4; i++)
        {
            Assert.Single(model.StartingEntry.Choices, c => c.IsOther);
            Assert.Null(model.StartingEntry.SelectedChoice);
            await Run(model.SkipCommand);
        }
        Assert.False(model.HasCurrentStep);
        Assert.False(model.ShowStartingValue);
        Assert.False(model.ShowChangedValue);
        Assert.False(model.MarkCommand.CanExecute(null));
        Assert.True(model.StopCommand.CanExecute(null));
    }

    [Fact]
    public void OverlongCombinedStateCannotBeMarked()
    {
        var entry = new SettingEntry();
        entry.Reset(0);
        entry.SelectedChoice = entry.Choices.Single(c => c.Label == "Custom");
        entry.Details = new string('x', 80);
        Assert.False(entry.HasValue);
        Assert.Contains("80", entry.ValidationHint);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task AnotherRecordingStartsWithFreshPerformanceChoices(int skippedSteps)
    {
        var model = await RecordingAsync();
        for (var i = 0; i < skippedSteps; i++) await Run(model.SkipCommand);
        model.StartingEntry.SelectedChoice = model.StartingEntry.Choices.Last();
        model.StartingEntry.Details = "Previously reported state";
        await Run(model.StopCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        Assert.True(model.ShowStartingValue);
        Assert.Contains("Performance mode", model.StepTitle);
        Assert.Contains(model.StartingEntry.Choices, c => c.Label == "Balanced");
        Assert.Null(model.StartingEntry.SelectedChoice);
        Assert.Null(model.ChangedEntry.SelectedChoice);
        Assert.Equal(string.Empty, model.StartingEntry.Details);
        Assert.False(model.MarkCommand.CanExecute(null));
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

    [Theory]
    [InlineData(90, 91, false)]
    [InlineData(91, 91, true)]
    [InlineData(91, 375, false)]
    public async Task ReturnedPacketMappingRequiresActualOutputCapacityAndDescriptor(uint capacity, ushort featureBytes, bool bare)
    {
        var model = await RecordingAsync();
        var candidate = new LocalHidCandidate("owned-fixture", "fixture-interface", "Blade fixture", 0x1532, 0xAAAA, null,
            1, 2, new HidReportGeometry(0, 0, featureBytes), CollectionState.Collected, null, null, null, null, null, null, null);
        var buffer = new byte[90];
        buffer[0] = 2;
        model.SetOwnedTestObserver(new ReturnedOwnedObserver(new NativeObservationEvent(1, 100, "DeviceIoControl.HID_GET_FEATURE", "get-feature", true, 0,
            [2], buffer, null, 1, 90, 0, 101, capacity)), [candidate]);
        await Run(model.StopCommand);
        var response = Assert.Single(model.Evidence!.Observations, o => o.Source == ObservationSource.ReturnedBuffer);
        Assert.Equal(capacity, response.OutputBufferCapacity);
        Assert.Equal(bare ? null : (byte?)2, response.ReportId);
        Assert.Equal(90, response.BufferLength);
        Assert.Equal(101, response.CompletedRelativeMilliseconds);
        // The owned preview has no admitted interface or valid privacy envelope.
        Assert.Null(response.PayloadHex);
    }

    private sealed class ReturnedOwnedObserver(NativeObservationEvent observation) : INativeObservationSession
    {
        private bool delivered;
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public NativeSessionStatus GetStatus() => new(true, true, 0, 0, "Owned fixture");
        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<NativeObservationEvent> records = delivered ? [] : [observation];
            delivered = true;
            return Task.FromResult(records);
        }
        public Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default) => Task.FromResult(GetStatus());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
