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
        for (var i = 0; i < model.Activities.Count; i++)
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

    [Fact]
    public async Task ActivityPickerJumpsDirectlyAndLocksUntilRestored()
    {
        var model = await RecordingAsync();
        var display = model.Activities.ToList().FindIndex(activity => activity.Id == "display-hdr");
        model.SelectedActivityIndex = display;
        Assert.Contains("Display HDR", model.StepTitle);
        Assert.Equal(CoverageState.Pending, model.Activities[0].State);
        model.StartingSetting = "Off";
        await Run(model.MarkCommand);
        Assert.False(model.CanSelectActivity);
        model.SelectedActivityIndex = 0;
        Assert.Equal(display, model.SelectedActivityIndex);
        model.ChangedSetting = "On";
        await Run(model.MarkCommand);
        model.SelectedActivityIndex = 0;
        Assert.Equal(display, model.SelectedActivityIndex);
        await Run(model.MarkCommand);
        Assert.True(model.CanSelectActivity);
        Assert.Equal(CoverageState.CompletedWithGaps, model.Activities[display].State);
    }

    [Fact]
    public async Task ReadOnlyDeclarationNeedsNoChangeOrRestoration()
    {
        var model = await RecordingAsync();
        var power = model.Activities.ToList().FindIndex(activity => activity.Id == "power-source");
        model.SelectedActivityIndex = power;
        model.StartingSetting = "Battery";
        Assert.Equal("Save current state", model.MarkButtonText);
        await Run(model.MarkCommand);
        Assert.Equal(CoverageState.Completed, model.Activities[power].State);
        Assert.Contains("Reported", model.Activities[power].DisplayLabel);
        Assert.True(model.CanSelectActivity);
        await Run(model.StopCommand);
        Assert.DoesNotContain(model.Evidence!.Markers, marker => marker.Kind is EvidenceMarkerKind.UserReportedTargetValue or EvidenceMarkerKind.UserReportedRestoration);
        Assert.DoesNotContain(model.Evidence.Coverage, item => item.Reason == CoverageReason.IncompleteRestoration);
        Assert.Equal("Battery", Assert.Single(model.Evidence.Markers, marker => marker.ActivityId == "power-source").Value);
    }

    [Fact]
    public async Task StoppingDoesNotRequireSkippingUnselectedItems()
    {
        var model = await RecordingAsync();
        model.SelectedActivityIndex = model.Activities.Count - 1;
        await Run(model.UnavailableCommand);
        await Run(model.StopCommand);
        Assert.Equal(model.Activities.Count - 1, model.Evidence!.Coverage.Count(item => item.State == CoverageState.Pending));
        Assert.Contains(model.Evidence.Coverage, item => item.ActivityId == "other-feature" && item.State == CoverageState.Unavailable);
    }

    [Fact]
    public void CatalogHasDistinctFeatureLeavesAndPreservesOriginalOrder()
    {
        var catalog = CaptureActivityCatalog.Create();
        Assert.Equal(new[] { "performance", "fans", "charge-limit", "lighting" }, catalog.Take(4).Select(activity => activity.Id));
        Assert.Equal(catalog.Length, catalog.Select(activity => activity.Id).Distinct().Count());
        Assert.All(catalog, activity => Assert.Single(activity.Choices, choice => choice.IsOther));
        foreach (var id in new[] { "custom-cpu-level", "custom-gpu-level", "hyperboost", "cooling-pad", "battery-protection", "full-charge",
                     "keyboard-brightness", "lighting-frames", "lighting-key-map", "keyboard-layout", "logo-lighting", "mute-indicator", "keyboard-mode", "function-row",
                     "gaming-mode", "startup-lighting", "special-key-m1", "special-key-m2", "special-key-m3", "special-key-m4", "special-key-m5",
                     "special-key-fn", "special-key-fn-p", "power-source", "adapter-rating", "gpu-core-offset", "gpu-memory-offset", "cpu-power-limits",
                     "cpu-optimizer", "display-refresh", "display-hdr", "display-vrr", "display-drr", "display-color", "display-brightness", "sensor-telemetry", "ec-mcu", "other-feature" })
            Assert.Contains(catalog, activity => activity.Id == id);
    }

    [Fact]
    public async Task InventoryOnlyReportsNeedNoAttachmentAndCannotAuthorizeSettingChange()
    {
        var model = new CollectorViewModel(true);
        await Run(model.CollectCommand);
        Assert.False(model.IsRecording);
        Assert.True(model.CanSelectActivity);
        model.StartingSetting = "Balanced";
        Assert.False(model.MarkCommand.CanExecute(null));
        model.SelectedActivityIndex = model.Activities.ToList().FindIndex(activity => activity.Id == "keyboard-layout");
        model.StartingSetting = "JIS Japanese";
        await Run(model.MarkCommand);
        Assert.Contains(model.Evidence!.Coverage, item => item.ActivityId == "keyboard-layout" && item.State == CoverageState.Completed);
        Assert.DoesNotContain(model.Evidence.Markers, marker => marker.Kind is EvidenceMarkerKind.RecordingStarted or EvidenceMarkerKind.UserReportedTargetValue or EvidenceMarkerKind.UserReportedRestoration);
        model.SelectedActivityIndex = model.Activities.ToList().FindIndex(activity => activity.Id == "lighting-key-map");
        model.StartingEntry.SelectedChoice = model.StartingEntry.Choices.Single(choice => choice.Label == "Single key color");
        model.StartingEntry.Details = "Q, row 3 column 2, RGB 0 255 0";
        Assert.False(model.MarkCommand.CanExecute(null));
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
        Assert.Equal(1u, response.RequestedBytes);
        Assert.Equal(90u, response.ReturnedBytes);
        Assert.All(buffer, value => Assert.Equal((byte)0, value));
        // The owned preview has no admitted interface or valid privacy envelope.
        Assert.Null(response.PayloadHex);
    }

    [Fact]
    public async Task HidDResponseDoesNotInventActualReturnedLength()
    {
        var model = await RecordingAsync();
        var candidate = new LocalHidCandidate("owned-fixture", "fixture-interface", "Blade fixture", 0x1532, 0xAAAA, null,
            1, 2, new HidReportGeometry(0, 0, 91), CollectionState.Collected, null, null, null, null, null, null, null);
        var request = new byte[] { 2 };
        var response = new byte[91];
        response[0] = 2;
        model.SetOwnedTestObserver(new ReturnedOwnedObserver(new NativeObservationEvent(1, 100, "HidD_GetFeature", "get-feature", true, 0,
            request, response, null, 91, 91, 0, 101, 91)), [candidate]);
        await Run(model.StopCommand);
        Assert.All(model.Evidence!.Observations, observation =>
        {
            Assert.Equal(91u, observation.RequestedBytes);
            Assert.Null(observation.ReturnedBytes);
        });
        Assert.All(request.Concat(response), value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public async Task FailedScopeValidationStillClearsTemporaryRawBuffers()
    {
        var model = await RecordingAsync();
        var request = new byte[] { 2, 3, 4 };
        var response = new byte[] { 5, 6, 7 };
        model.SetOwnedTestObserver(new ReturnedOwnedObserver(new NativeObservationEvent(1, 100, "HidD_GetFeature", "get-feature", true, 0,
            request, response, null, 3, 3, 0, 101, 3)));
        await Run(model.StopCommand);
        Assert.Empty(model.Evidence!.Observations);
        Assert.All(request.Concat(response), value => Assert.Equal((byte)0, value));
        Assert.Contains(model.Evidence.Markers, marker => marker.Label == "Final observer drain failed");
    }

    [Theory]
    [InlineData(1, "HidD_SetFeature", 0)]
    [InlineData(1, "invalid api", 1)]
    [InlineData(EvidenceLimits.MaxObservations + 1, "HidD_SetFeature", 1)]
    public async Task RetentionRejectionsAreGapsButPrivacyRedactionIsNot(int count, string api, int expectedGaps)
    {
        var model = await RecordingAsync();
        var candidate = new LocalHidCandidate("owned-fixture", "fixture-interface", "Blade fixture", 0x1532, 0xAAAA, null,
            1, 2, new HidReportGeometry(0, 0, 91), CollectionState.Collected, null, null, null, null, null, null, null);
        var events = Enumerable.Range(1, count).Select(index => new NativeObservationEvent((ulong)index, 100, api, "set-feature", true, 0,
            [2], [], null, 1, 0, 0, 101)).ToArray();
        model.SetOwnedTestObserver(new BurstOwnedObserver(events), [candidate]);
        await Run(model.StopCommand);
        Assert.Equal(expectedGaps, model.Evidence!.RejectedObservationCount);
        Assert.Contains($"{expectedGaps} recording gaps", model.ObservationSummary);
        if (count > EvidenceLimits.MaxObservations)
            Assert.Contains($"{EvidenceLimits.MaxObservations} observation limit", model.CollectionDetails);
        Assert.All(events, observation => Assert.All(observation.RequestBytes, value => Assert.Equal((byte)0, value)));
    }

    private sealed class BurstOwnedObserver(IReadOnlyList<NativeObservationEvent> events) : INativeObservationSession
    {
        private bool delivered;
        public long OriginTimestampTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public NativeSessionStatus GetStatus() => new(true, true, 0, 0, "Owned fixture");
        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default)
        {
            var result = delivered ? Array.Empty<NativeObservationEvent>() : events;
            delivered = true;
            return Task.FromResult(result);
        }
        public Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default) => Task.FromResult(GetStatus());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
