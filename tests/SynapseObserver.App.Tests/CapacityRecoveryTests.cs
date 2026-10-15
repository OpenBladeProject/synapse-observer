using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using SynapseObserver.App;
using SynapseObserver.Backend;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class CapacityRecoveryTests
{
    private const int LegacyObservationLimit = 4_096;

    private static readonly LocalHidCandidate Candidate = new(
        "owned-device-path", "owned-interface", "Razer Blade owned fixture", 0x1532, 0xFFFF, null,
        1, 2, new HidReportGeometry(0, 0, 1), CollectionState.Collected, null,
        null, null, null, "owned-root", "owned-hardware", "Razer Blade owned fixture");

    [Fact]
    public async Task ContinuousCaptureExceedsLegacyLimitAndExportsLaterActionInOneZip()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-continuous-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "continuous-evidence.zip");
            var model = await RecordingAsync();
            model.EvidenceDestinationForTest = () => destination;
            var observer = CapacityObserver.HighVolume(LegacyObservationLimit + 128);
            model.SetOwnedTestObserver(observer, [Candidate]);

            await Poll(model);

            using (var sustained = model.Evidence!)
            {
                Assert.Equal(LegacyObservationLimit + 128, sustained.Observations.Count);
                Assert.Equal(0, sustained.RejectedObservationCount);
                Assert.Null(sustained.StorageFailure);
            }
            Assert.True(model.IsRecording);
            Assert.False(model.ShowCapacityRecovery);
            Assert.Contains($"{LegacyObservationLimit + 128} control reports saved locally", model.CapacitySummary);

            model.StartingSetting = "Balanced";
            await Run(model.MarkCommand);
            observer.EnqueueFinalResponse(
                (ulong)LegacyObservationLimit + 129,
                CurrentRelativeMilliseconds(model));
            await Poll(model);
            model.ChangedSetting = "Silent";
            await Run(model.MarkCommand);
            await Run(model.MarkCommand);
            await Run(model.StopCommand);

            using (var retained = model.Evidence!)
            {
                Assert.Equal(LegacyObservationLimit + 130, retained.Observations.Count);
                Assert.Equal(0, retained.RejectedObservationCount);
                Assert.Null(retained.StorageFailure);
                var window = Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(retained).ActionWindows);
                Assert.True(window.CompleteUserReportedSequence);
                Assert.Contains(window.Markers, marker => marker.Kind == EvidenceMarkerKind.UserReportedRestoration
                    && marker.Value == "Balanced");
                Assert.Contains(window.Observations, observation =>
                    observation.OperationId == $"recording-1-op-{LegacyObservationLimit + 129}"
                    && observation.Direction == ObservationDirection.Output);
                Assert.Equal(CoverageState.Completed, Assert.Single(retained.Coverage,
                    item => item.ActivityId == "performance").State);
            }

            await Run(model.SaveCommand);

            Assert.True(File.Exists(destination));
            Assert.Single(Directory.EnumerateFiles(directory, "*.zip"));
            using var manifest = ReadZipJson(destination, "manifest.json");
            using var annotation = ReadZipJson(destination, "annotation-draft.json");
            var observations = manifest.RootElement.GetProperty("session").GetProperty("observations")
                .EnumerateArray().ToArray();
            Assert.Equal(LegacyObservationLimit + 130, observations.Length);
            Assert.Equal(observations[^2].GetProperty("operationId").GetString(),
                observations[^1].GetProperty("operationId").GetString());
            Assert.Equal("returnedBuffer", observations[^1].GetProperty("source").GetString());
            Assert.True(Assert.Single(annotation.RootElement.GetProperty("actionWindows").EnumerateArray())
                .GetProperty("completeUserReportedSequence").GetBoolean());
            Assert.All(observer.RawBuffers, buffer => Assert.All(buffer, value => Assert.Equal((byte)0, value)));
            await model.CloseAsync();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PendingStorageWriteDoesNotBlockMarkersOrCapacitySummary()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-gated-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var model = await RecordingAsync();
            var oldSession = CurrentSession(model);
            GatedWriteStream? gated = null;
            var store = SanitizedObservationStore.Create(
                Path.Combine(directory, "gated.spool"), path =>
                {
                    gated = new GatedWriteStream(new FileStream(
                        path,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete,
                        bufferSize: 1,
                        FileOptions.SequentialScan));
                    return gated;
                });
            var session = new EvidenceSession(oldSession.Inventory, startedAtUtc: null, store);
            Assert.Contains(session.AddObservation(AnchorObservation()),
                new[] { EvidenceAddResult.Added, EvidenceAddResult.AddedPayloadRedacted });
            SetCurrentSession(model, session);
            oldSession.Dispose();
            var observer = CapacityObserver.SingleCall();
            model.SetOwnedTestObserver(observer, [Candidate]);
            gated!.GateNextWrite();

            var returned = System.Diagnostics.Stopwatch.StartNew();
            var poll = Poll(model);
            returned.Stop();
            try
            {
                Assert.True(returned.Elapsed < TimeSpan.FromSeconds(1));
                await gated.WriteBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(poll.IsCompleted);

                var marker = await Task.Run(() => session.AddMarker(new EvidenceMarker(
                        0, EvidenceMarkerKind.Failure, "owned-marker", "Metadata remains responsive", EvidenceProvenance.Collector)))
                    .WaitAsync(TimeSpan.FromSeconds(1));
                Assert.Equal(EvidenceAddResult.Added, marker);
                var summary = await Task.Run(() => model.CapacitySummary).WaitAsync(TimeSpan.FromSeconds(1));
                Assert.Contains("1 control reports saved locally", summary);
            }
            finally
            {
                gated.ReleaseWrite();
            }

            await poll.WaitAsync(TimeSpan.FromSeconds(5));
            using (var evidence = model.Evidence!)
            {
                Assert.Equal(2, evidence.Observations.Count);
                Assert.Contains(evidence.Markers, marker => marker.Label == "Metadata remains responsive");
                Assert.Null(evidence.StorageFailure);
            }
            Assert.All(observer.RawBuffers, buffer => Assert.All(buffer, value => Assert.Equal((byte)0, value)));
            await Run(model.StopCommand);
            await model.CloseAsync();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptedCommittedSpoolStillStopsAndPublishesFallbackReview()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-corrupt-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var model = await RecordingAsync();
            var healthyReview = model.ReviewSummary;
            model.EvidenceDestinationForTest = () => Path.Combine(directory, "partial.zip");
            var oldSession = CurrentSession(model);
            CorruptibleWriteStream? corruptible = null;
            var store = SanitizedObservationStore.Create(
                Path.Combine(directory, "corrupt.spool"), path =>
                {
                    corruptible = new CorruptibleWriteStream(new FileStream(
                        path,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete,
                        bufferSize: 1,
                        FileOptions.SequentialScan));
                    return corruptible;
                });
            var session = new EvidenceSession(oldSession.Inventory, startedAtUtc: null, store);
            Assert.Contains(session.AddObservation(AnchorObservation()),
                new[] { EvidenceAddResult.Added, EvidenceAddResult.AddedPayloadRedacted });
            SetCurrentSession(model, session);
            oldSession.Dispose();
            corruptible!.CorruptFirstCommittedRecord();
            model.SetOwnedTestObserver(CapacityObserver.Empty(), [Candidate]);

            await Run(model.StopCommand);

            Assert.False(model.IsRecording);
            Assert.False(model.IsBusy);
            Assert.True(model.ShowExport);
            Assert.True(model.SaveCommand.CanExecute(null));
            Assert.NotEqual(healthyReview, model.ReviewSummary);
            Assert.Contains("could not be prepared", model.ReviewSummary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("free disk space", model.ReviewNotes, StringComparison.OrdinalIgnoreCase);
            await model.CloseAsync();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MarkerCapacityDuringPendingPowerReadCannotAdvanceMarkerPhase()
    {
        var model = await RecordingAsync();
        model.EvidenceDestinationForTest = () => Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.zip");
        var enteredPower = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePower = new TaskCompletionSource<PowerSourceKind>(TaskCreationOptions.RunContinuationsAsynchronously);
        model.ActionPowerSourceForTest = async () =>
        {
            enteredPower.TrySetResult();
            return await releasePower.Task;
        };
        model.StartingSetting = "Balanced";
        model.MarkCommand.Execute(null);
        await enteredPower.Task.WaitAsync(TimeSpan.FromSeconds(5));

        FillMarkersTo(model, EvidenceLimits.MaxMarkers - CollectorViewModel.CleanupMarkerReserve);
        CheckMarkerCapacity(model);
        await WaitForPublishedCapacityStopAsync(model);

        Assert.False(model.SaveCommand.CanExecute(null));
        Assert.False(model.MarkCommand.CurrentExecution.IsCompleted);
        releasePower.SetResult(PowerSourceKind.Ac);
        await model.MarkCommand.CurrentExecution;

        Assert.True(model.ShowStartingValue);
        Assert.False(model.CapacityRestorationRequired);
        Assert.True(model.SaveCommand.CanExecute(null));
        using var evidence = model.Evidence!;
        Assert.DoesNotContain(evidence.Markers,
            marker => marker.Kind == EvidenceMarkerKind.UserReportedStartingValue);
        await model.CloseAsync();
    }

    [Fact]
    public async Task ReadOnlyReportAtMarkerReserveCleansUpAlreadyEndedReader()
    {
        var model = await RecordingAsync();
        model.EvidenceDestinationForTest = () => Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.zip");
        model.SelectedActivityIndex = ActivityIndex(model, "power-source");
        var observer = CapacityObserver.Empty();
        observer.FinishNaturally();
        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);
        await CurrentPump(model).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsRecording);

        var capacityThreshold = EvidenceLimits.MaxMarkers - CollectorViewModel.CleanupMarkerReserve;
        FillMarkersTo(model, capacityThreshold - 1);
        model.StartingSetting = "Battery";
        await Run(model.MarkCommand);
        await WaitForCapacityStopAsync(model, observer);

        using var evidence = model.Evidence!;
        Assert.Equal(capacityThreshold + 1, evidence.Markers.Count);
        Assert.Equal(0, evidence.RejectedMarkerCount);
        Assert.Contains("checklist", model.CapacitySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(evidence.Markers, marker => marker.Kind == EvidenceMarkerKind.UserReportedCurrentState);
        Assert.Contains(evidence.Markers, marker => marker.Kind == EvidenceMarkerKind.RecordingStopped);
        Assert.Contains(evidence.Coverage, item =>
            item.ActivityId == "power-source" && item.State == CoverageState.Completed);
        Assert.False(model.CapacityRestorationRequired);
        Assert.True(model.SaveCommand.CanExecute(null));
        Assert.Equal(1, observer.Stops);
        Assert.Equal(0, observer.Invalidations);
        await model.CloseAsync();
    }

    [Fact]
    public async Task RestorationAtMarkerReserveRemainsCompleteAndLeavesCleanupRoom()
    {
        var model = await RecordingAsync();
        model.EvidenceDestinationForTest = () => Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.zip");
        var observer = CapacityObserver.Empty();
        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);
        model.StartingSetting = "Balanced";
        await Run(model.MarkCommand);
        model.ChangedSetting = "Silent";
        await Run(model.MarkCommand);
        var capacityThreshold = EvidenceLimits.MaxMarkers - CollectorViewModel.CleanupMarkerReserve;
        FillMarkersTo(model, capacityThreshold - 1);

        await Run(model.MarkCommand);
        await WaitForCapacityStopAsync(model, observer);

        using var evidence = model.Evidence!;
        Assert.Equal(capacityThreshold + 1, evidence.Markers.Count);
        Assert.Equal(0, evidence.RejectedMarkerCount);
        Assert.Contains(evidence.Markers, marker => marker.Kind == EvidenceMarkerKind.UserReportedRestoration
            && marker.Value == "Balanced");
        Assert.Contains(evidence.Markers, marker => marker.Kind == EvidenceMarkerKind.RecordingStopped);
        Assert.DoesNotContain(evidence.Coverage, item =>
            item.ActivityId == "performance" && item.Reason == CoverageReason.IncompleteRestoration);
        Assert.True(Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(evidence).ActionWindows)
            .CompleteUserReportedSequence);
        Assert.False(model.CapacityRestorationRequired);
        Assert.True(model.SaveCommand.CanExecute(null));
        await model.CloseAsync();
    }

    [Fact]
    public async Task IncreasingObserverDropsCoalescePerActionSegmentWithoutMarkerFlood()
    {
        var model = await RecordingAsync();
        var observer = CapacityObserver.Empty();
        model.SetOwnedTestObserver(observer, [Candidate]);
        for (var dropped = 1; dropped <= 20; dropped++)
        {
            observer.DroppedEvents = dropped;
            await Poll(model);
        }

        using var beforeAction = model.Evidence!;
        var first = Assert.Single(beforeAction.Markers,
            marker => marker.Label == "Observer queue dropped events");
        model.StartingSetting = "Balanced";
        await Run(model.MarkCommand);
        using var afterBaseline = model.Evidence!;
        var baseline = Assert.Single(afterBaseline.Markers,
            marker => marker.Kind == EvidenceMarkerKind.UserReportedStartingValue);

        for (var dropped = 21; dropped <= 600; dropped++)
        {
            observer.DroppedEvents = dropped;
            await Poll(model);
        }
        model.ChangedSetting = "Silent";
        await Run(model.MarkCommand);
        await Run(model.MarkCommand);
        await Run(model.StopCommand);

        using var evidence = model.Evidence!;
        var retained = evidence.Markers
            .Where(marker => marker.Label == "Observer queue dropped events")
            .OrderBy(marker => marker.RelativeMilliseconds)
            .ToArray();
        Assert.Equal(2, retained.Length);
        Assert.Equal(first.RelativeMilliseconds, retained[0].RelativeMilliseconds);
        Assert.Equal("count-20", retained[0].Value);
        Assert.True(retained[1].RelativeMilliseconds >= baseline.RelativeMilliseconds);
        Assert.Equal("count-600", retained[1].Value);
        var window = Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(evidence).ActionWindows);
        Assert.InRange(retained[1].RelativeMilliseconds,
            window.StartRelativeMilliseconds, window.EndRelativeMilliseconds);
        Assert.Equal(CoverageReason.CaptureFailed, Assert.Single(evidence.Coverage,
            item => item.ActivityId == "performance").Reason);
        Assert.Equal(0, evidence.RejectedMarkerCount);
        Assert.True(evidence.Markers.Count < EvidenceLimits.MaxMarkers - CollectorViewModel.CleanupMarkerReserve);
        Assert.Contains("600 recording gaps", model.ObservationSummary);
        Assert.False(model.ShowCapacityRecovery);
        await model.CloseAsync();
    }

    [Fact]
    public async Task SpecialKeyLimitDoesNotStopControlRecording()
    {
        var model = await RecordingAsync();
        var observer = CapacityObserver.Empty();
        model.SetOwnedTestObserver(observer, [Candidate]);

        for (var index = 1; index <= EvidenceLimits.MaxSpecialKeyProbes; index++)
        {
            SetActiveProbeLabel(model, $"probe-{index:D3}");
            model.CompleteSpecialKeyProbe(ProbeSummary());
        }
        SetActiveProbeLabel(model, "probe-065");
        model.CompleteSpecialKeyProbe(ProbeSummary());

        Assert.True(model.IsRecording);
        Assert.False(model.ShowCapacityRecovery);
        Assert.Contains("could not be retained", model.SpecialKeyStatus, StringComparison.OrdinalIgnoreCase);
        observer.EnqueueSingleCall();
        await Poll(model);
        using (var evidence = model.Evidence!)
        {
            Assert.Equal(EvidenceLimits.MaxSpecialKeyProbes, evidence.SpecialKeyProbes.Count);
            Assert.Single(evidence.Observations);
            Assert.Null(evidence.StorageFailure);
        }
        Assert.True(model.IsRecording);
        await Run(model.StopCommand);
        await model.CloseAsync();
    }

    [Fact]
    public async Task FailedSaveRetryKeepsOneLateRestorationReportAndIncompleteWindow()
    {
        var model = await RecordingAsync();
        model.StartingSetting = "Balanced";
        await Run(model.MarkCommand);
        FillMarkersTo(model, EvidenceLimits.MaxMarkers - CollectorViewModel.CleanupMarkerReserve);
        CheckMarkerCapacity(model);
        await WaitForPublishedCapacityStopAsync(model);
        Assert.True(model.CapacityRestorationRequired);
        model.CapacityRestorationConfirmed = true;
        model.EvidenceDestinationForTest = () => Path.Combine(Path.GetTempPath(), $"retry-{Guid.NewGuid():N}.zip");
        using var beforeSave = model.Evidence!;

        EvidenceSessionSnapshot? failedExport = null;
        model.EvidenceWriterForTest = (session, _) =>
        {
            failedExport = session.Snapshot();
            throw new IOException("Owned writer failure");
        };
        await Run(model.SaveCommand);

        using (var afterFailure = model.Evidence!)
        {
            Assert.Equal(beforeSave.StartedAtUtc, afterFailure.StartedAtUtc);
            Assert.Equal(beforeSave.Observations.Count, afterFailure.Observations.Count);
            Assert.True(model.ShowCapacityRecovery);
            Assert.Contains(nameof(IOException), model.Status);
        }
        Assert.NotNull(failedExport);
        AssertLateRestorationIsSeparate(failedExport!);

        EvidenceSessionSnapshot? retriedExport = null;
        model.EvidenceWriterForTest = (session, _) =>
        {
            retriedExport = session.Snapshot();
            return Task.CompletedTask;
        };
        await Run(model.SaveCommand);

        Assert.NotNull(retriedExport);
        AssertLateRestorationIsSeparate(retriedExport!);
        using (var afterSuccess = model.Evidence!)
        {
            Assert.Equal(beforeSave.StartedAtUtc, afterSuccess.StartedAtUtc);
            Assert.Equal(beforeSave.Observations.Count, afterSuccess.Observations.Count);
            AssertLateRestorationIsSeparate(afterSuccess);
        }
        failedExport!.Dispose();
        retriedExport!.Dispose();
        await model.CloseAsync();
    }

    [Fact]
    public async Task StorageFailureStopsAndKeepsOnlyCommittedSanitizedPrefix()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-storage-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var model = await RecordingAsync();
            var oldSession = CurrentSession(model);
            var store = SanitizedObservationStore.Create(
                Path.Combine(directory, "failed.spool"), path => new ThrowingWriteStream(new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 1,
                    FileOptions.SequentialScan)));
            var failedSession = new EvidenceSession(oldSession.Inventory, startedAtUtc: null, store);
            SetCurrentSession(model, failedSession);
            oldSession.Dispose();
            var observer = CapacityObserver.SingleCall();
            model.SetOwnedTestObserver(observer, [Candidate], startPump: true);

            await WaitForCapacityStopAsync(model, observer);

            using var evidence = model.Evidence!;
            Assert.Equal(EvidenceStorageFailure.IoFailure, evidence.StorageFailure);
            Assert.Empty(evidence.Observations);
            Assert.Equal(1, evidence.RejectedObservationCount);
            Assert.True(model.ShowCapacityRecovery);
            Assert.Contains("storage", model.CapacitySummary, StringComparison.OrdinalIgnoreCase);
            Assert.All(observer.RawBuffers, buffer => Assert.All(buffer, value => Assert.Equal((byte)0, value)));
            await model.CloseAsync();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CloseWaitsForInFlightExportBeforeDisposingSession()
    {
        var model = await RecordingAsync();
        await Run(model.StopCommand);
        using var original = model.Evidence!;
        var entered = new TaskCompletionSource<EvidenceSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.EvidenceDestinationForTest = () => Path.Combine(Path.GetTempPath(), $"gated-{Guid.NewGuid():N}.zip");
        model.EvidenceWriterForTest = async (session, _) =>
        {
            entered.TrySetResult(session);
            await release.Task;
        };

        model.SaveCommand.Execute(null);
        var exported = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using (var duringExport = exported.Snapshot())
        {
            Assert.Equal(original.StartedAtUtc, duringExport.StartedAtUtc);
        }
        var close = model.CloseAsync();
        Assert.False(close.IsCompleted);

        release.SetResult();
        await Task.WhenAll(close, model.SaveCommand.CurrentExecution);
    }

    [Fact]
    public async Task FailedCapacityCleanupInvalidatesReuseAndRevokesConsent()
    {
        var model = await RecordingAsync();
        var observer = CapacityObserver.Empty(cleanupFailure: true);
        model.SetOwnedTestObserver(observer, [Candidate], startPump: true);
        FillMarkersTo(model, EvidenceLimits.MaxMarkers - CollectorViewModel.CleanupMarkerReserve - 1);
        model.StartingSetting = "Balanced";

        await Run(model.MarkCommand);
        await WaitForCapacityStopAsync(model, observer);

        Assert.True(model.ShowCapacityRecovery);
        Assert.True(observer.Invalidations > 0);
        Assert.False(model.AttachConsent);
        Assert.False(model.StartCommand.CanExecute(null));
        using var evidence = model.Evidence!;
        Assert.Contains(evidence.Markers, marker => marker.Label == "Recording cleanup has a gap");
        await model.CloseAsync();
    }

    private static void AssertLateRestorationIsSeparate(EvidenceSessionSnapshot evidence)
    {
        Assert.Single(evidence.Markers,
            marker => marker.Label == "User reported restoration after recording stopped"
                && marker.Kind == EvidenceMarkerKind.UserReportedCurrentState
                && marker.ActivityId == "performance" && marker.Value == "Balanced");
        Assert.DoesNotContain(evidence.Markers,
            marker => marker.Kind == EvidenceMarkerKind.UserReportedRestoration
                && marker.ActivityId == "performance");
        var coverage = Assert.Single(evidence.Coverage, item => item.ActivityId == "performance");
        Assert.Equal(CoverageState.CompletedWithGaps, coverage.State);
        Assert.Equal(CoverageReason.IncompleteRestoration, coverage.Reason);
        Assert.False(Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(evidence).ActionWindows)
            .CompleteUserReportedSequence);
    }

    private static async Task<CollectorViewModel> RecordingAsync()
    {
        var model = new CollectorViewModel(preview: true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        return model;
    }

    private static int ActivityIndex(CollectorViewModel model, string id) =>
        model.Activities.Select((activity, index) => (activity, index))
            .Single(item => item.activity.Id == id).index;

    private static EvidenceSession CurrentSession(CollectorViewModel model) =>
        (EvidenceSession)typeof(CollectorViewModel)
            .GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(model)!;

    private static void SetCurrentSession(CollectorViewModel model, EvidenceSession session) =>
        typeof(CollectorViewModel)
            .GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, session);

    private static void SetActiveProbeLabel(CollectorViewModel model, string label) =>
        typeof(CollectorViewModel)
            .GetField("activeProbeLabel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, label);

    private static SpecialKeyProbeSummary ProbeSummary() => new(
        SpecialKeyExpectedKey.M1, SpecialKeyModeContext.Unknown,
        SpecialKeyProbeEndReason.Timeout, 0, 0, 0, string.Empty);

    private static ControlObservation AnchorObservation() => new(
        "anchor-op",
        0,
        ObservationSource.UserModeCall,
        "HidD_SetFeature",
        ObservationDirection.Input,
        ObservationStatus.Succeeded,
        0,
        null,
        Candidate.VendorId,
        Candidate.ProductId,
        Candidate.InterfaceLabel,
        Candidate.UsagePage,
        Candidate.Usage,
        2,
        new byte[] { 2 },
        PayloadDisposition.ProvenFeatureControl,
        0);

    private static Task Poll(CollectorViewModel model) =>
        (Task)typeof(CollectorViewModel)
            .GetMethod("PollOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, null)!;

    private static Task CurrentPump(CollectorViewModel model) =>
        (Task)typeof(CollectorViewModel)
            .GetField("pump", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(model)!;

    private static double CurrentRelativeMilliseconds(CollectorViewModel model) =>
        (double)typeof(CollectorViewModel)
            .GetProperty("RelativeMilliseconds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(model)!;

    private static void CheckMarkerCapacity(CollectorViewModel model) =>
        typeof(CollectorViewModel)
            .GetMethod("CheckMarkerCapacity", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, null);

    private static void FillMarkersTo(CollectorViewModel model, int count)
    {
        var session = CurrentSession(model);
        while (session.MarkerCount < count)
        {
            Assert.Equal(EvidenceAddResult.Added, session.AddMarker(new EvidenceMarker(
                0, EvidenceMarkerKind.Failure, "owned-marker", "Owned marker reserve fixture", EvidenceProvenance.Collector)));
        }
    }

    private static JsonDocument ReadZipJson(string path, string entryName)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry(entryName)!;
        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitForCapacityStopAsync(CollectorViewModel model, CapacityObserver observer)
    {
        await observer.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForPublishedCapacityStopAsync(model);
    }

    private static async Task WaitForPublishedCapacityStopAsync(CollectorViewModel model)
    {
        var stopField = typeof(CollectorViewModel).GetField("stopTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await WaitUntilAsync(() => stopField.GetValue(model) is Task);
        var stop = (Task?)stopField.GetValue(model);
        Assert.NotNull(stop);
        await stop.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => !model.IsRecording && !model.IsBusy && model.ShowCapacityRecovery);
    }

    private static async Task Run(AsyncCommand command)
    {
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        await command.CurrentExecution;
    }

    private sealed class CapacityObserver : INativeObservationSession
    {
        private readonly object sync = new();
        private readonly Queue<IReadOnlyList<NativeObservationEvent>> batches = [];
        private readonly bool cleanupFailure;
        private bool finished;

        private CapacityObserver(bool cleanupFailure)
        {
            this.cleanupFailure = cleanupFailure;
        }

        public List<byte[]> RawBuffers { get; } = [];
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Stops { get; private set; }
        public int Invalidations { get; private set; }
        public int DroppedEvents { get; set; }
        public long OriginTimestampTicks { get; } = System.Diagnostics.Stopwatch.GetTimestamp();

        public static CapacityObserver Empty(bool cleanupFailure = false) => new(cleanupFailure);

        public static CapacityObserver SingleCall()
        {
            var observer = Empty();
            observer.EnqueueSingleCall();
            return observer;
        }

        public static CapacityObserver HighVolume(int calls)
        {
            var observer = Empty();
            observer.Enqueue(Enumerable.Range(1, calls)
                .Select(index => observer.Event((ulong)index, includeResponse: false, relativeMilliseconds: 0))
                .ToArray());
            return observer;
        }

        public void EnqueueSingleCall()
        {
            var at = ElapsedMilliseconds;
            Enqueue([Event(1, includeResponse: false, at)]);
        }

        public void EnqueueFinalResponse(ulong sequence, double relativeMilliseconds)
        {
            Enqueue([Event(sequence, includeResponse: true, relativeMilliseconds)]);
        }

        public void FinishNaturally() => finished = true;

        public NativeSessionStatus GetStatus() => new(
            Started: true,
            Finished: finished,
            Failure: cleanupFailure && finished ? 170 : 0,
            DroppedEvents: DroppedEvents,
            Diagnostic: "Owned capacity observer",
            ReuseAllowed: !cleanupFailure);

        public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                return Task.FromResult(batches.Count == 0 ? (IReadOnlyList<NativeObservationEvent>)[] : batches.Dequeue());
            }
        }

        public Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stops++;
            finished = true;
            return Task.FromResult(GetStatus());
        }

        public void InvalidateReuse() => Invalidations++;

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        private double ElapsedMilliseconds =>
            (System.Diagnostics.Stopwatch.GetTimestamp() - OriginTimestampTicks) * 1000.0
            / System.Diagnostics.Stopwatch.Frequency;

        private void Enqueue(IReadOnlyList<NativeObservationEvent> batch)
        {
            lock (sync) batches.Enqueue(batch);
        }

        private NativeObservationEvent Event(ulong sequence, bool includeResponse, double relativeMilliseconds)
        {
            var request = new byte[] { 2 };
            var response = includeResponse ? new byte[] { 3 } : [];
            RawBuffers.Add(request);
            RawBuffers.Add(response);
            return new NativeObservationEvent(
                sequence,
                relativeMilliseconds,
                includeResponse ? "DeviceIoControl.HID_GET_FEATURE" : "HidD_SetFeature",
                includeResponse ? "get-feature" : "set-feature",
                true,
                0,
                request,
                response,
                null,
                1,
                includeResponse ? 1u : 0,
                0,
                relativeMilliseconds,
                includeResponse ? 1u : 0);
        }
    }

    private sealed class GatedWriteStream(FileStream inner) : Stream
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool gateNextWrite;

        internal TaskCompletionSource WriteBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void GateNextWrite() => gateNextWrite = true;

        internal void ReleaseWrite() => release.TrySetResult();

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (gateNextWrite)
            {
                gateNextWrite = false;
                WriteBlocked.TrySetResult();
                if (!release.Task.Wait(TimeSpan.FromSeconds(5)))
                    throw new IOException("The owned write gate timed out.");
            }

            inner.Write(buffer);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class CorruptibleWriteStream(FileStream inner) : Stream
    {
        internal void CorruptFirstCommittedRecord()
        {
            inner.Flush(flushToDisk: true);
            using var corruptor = new FileStream(
                inner.Name,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.WriteThrough);
            corruptor.Position = sizeof(int);
            corruptor.WriteByte(0);
            corruptor.Flush(flushToDisk: true);
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingWriteStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Owned storage failure");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("Owned storage failure");

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
