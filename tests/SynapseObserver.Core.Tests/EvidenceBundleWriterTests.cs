using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SynapseObserver.Core.Tests;

public sealed class EvidenceBundleWriterTests
{
    [Fact]
    public async Task WriteAsync_ExportsPartialCancellationAndPrivacyBoundedSchema()
    {
        var inventory = SyntheticPreview.CreateInventory() with
        {
            RazerHidInterfaces = SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Cancelled([]),
            SynapseProcesses = SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Unavailable([], "Module inventory unavailable."),
        };
        var session = new EvidenceSession(inventory, DateTimeOffset.UnixEpoch);
        var secret = Encoding.UTF8.GetBytes(@"C:\Users\alice\VID_1532&PID_9999\serial");
        session.AddObservation(new ControlObservation(
            "op-privacy",
            10,
            ObservationSource.UserModeCall,
            "DeviceIoControl",
            ObservationDirection.Input,
            ObservationStatus.Pending,
            null,
            null,
            0x1532,
            0x9999,
            "hid-opaque",
            1,
            2,
            0,
            secret,
            PayloadDisposition.RedactedUnproven,
            12.5));
        session.SetCoverage(new CoverageItem(
            "capture-performance",
            "Performance mode",
            CoverageState.Cancelled,
            CoverageReason.Cancelled));

        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "evidence.zip");
            await EvidenceBundleWriter.WriteAsync(session, destination, CancellationToken.None);

            using var archive = ZipFile.OpenRead(destination);
            Assert.Equal(
                new[] { "annotation-draft.json", "coverage-draft.json", "manifest.json", "quality-receipt.json", "README.txt" },
                archive.Entries.Select(entry => entry.FullName).Order().ToArray());
            var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json");
            using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8);
            var manifest = await reader.ReadToEndAsync();

            Assert.Contains("\"razerHidInterfaces\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"collectorBuild\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"informationalVersion\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"cancelled\"", manifest, StringComparison.Ordinal);
            Assert.Contains($"\"bufferLength\": {secret.Length}", manifest, StringComparison.Ordinal);
            Assert.Contains("\"completedRelativeMilliseconds\": 12.5", manifest, StringComparison.Ordinal);
            Assert.Contains("\"schemaVersion\": \"synapse-observer.bundle.v2\"", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("alice", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("interfacePath", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("commandLine", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payloadHex\": \"", manifest, StringComparison.Ordinal);

            using var coverage = await ReadJsonAsync(archive, "coverage-draft.json");
            Assert.Equal("synapse-observer.coverage-draft.v2",
                coverage.RootElement.GetProperty("schemaVersion").GetString());
            var coverageItems = coverage.RootElement.GetProperty("coverage");
            Assert.Contains(coverageItems.EnumerateArray(), item =>
                item.GetProperty("capability").GetString() == "performance.externalPower.presets");
            Assert.Contains(coverageItems.EnumerateArray(), item =>
                item.GetProperty("capability").GetString() == "specialKeys.fnP.inputEvidence");
            Assert.All(coverageItems.EnumerateArray(), item => Assert.Contains(
                item.GetProperty("status").GetString()!,
                new[] { "NotInvestigated", "Captured" }));
            Assert.DoesNotContain("queryValidated", coverage.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("productionAdmitted", coverage.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);

            using var annotations = await ReadJsonAsync(archive, "annotation-draft.json");
            Assert.Equal("synapse-observer.annotation-draft.v2",
                annotations.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Contains("UserModeFeatureApi", annotations.RootElement.GetRawText(), StringComparison.Ordinal);
            Assert.Contains("WindowsRawInput", annotations.RootElement.GetRawText(), StringComparison.Ordinal);
            Assert.Contains("WindowsInventory", annotations.RootElement.GetRawText(), StringComparison.Ordinal);

            using var receipt = await ReadJsonAsync(archive, "quality-receipt.json");
            Assert.Equal("synapse-observer.quality-receipt.v2",
                receipt.RootElement.GetProperty("schemaVersion").GetString());
            Assert.False(receipt.RootElement.GetProperty("claimsQueryValidation").GetBoolean());
            Assert.False(receipt.RootElement.GetProperty("claimsSetterValidation").GetBoolean());
            Assert.False(receipt.RootElement.GetProperty("claimsProductionAdmission").GetBoolean());
            Assert.Contains("PayloadRedacted", receipt.RootElement.GetRawText(), StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateCoverageDraft_RequiresReviewedReportInsideCompleteActionWindow()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory(), DateTimeOffset.UnixEpoch);
        session.AddMarker(new EvidenceMarker(1, EvidenceMarkerKind.UserReportedStartingValue,
            "performance", "Starting value", EvidenceProvenance.UserReported, "Balanced") { PowerSourceAtMarker = PowerSourceKind.Ac });
        session.AddObservation(new ControlObservation(
            "op-thermal",
            2,
            ObservationSource.UserModeCall,
            "HidD_SetFeature",
            ObservationDirection.Input,
            ObservationStatus.Succeeded,
            0,
            null,
            0x1532,
            0xFFFF,
            "hid-preview-01",
            1,
            2,
            0,
            FeatureReportTestData.Create(0x0D, 0x02, [0x01, 0x01, 0x05, 0x00]),
            PayloadDisposition.ProvenFeatureControl,
            2.1));
        session.AddMarker(new EvidenceMarker(3, EvidenceMarkerKind.UserReportedTargetValue,
            "performance", "Changed value", EvidenceProvenance.UserReported, "Silent") { PowerSourceAtMarker = PowerSourceKind.Ac });
        session.AddMarker(new EvidenceMarker(4, EvidenceMarkerKind.UserActionConfirmed,
            "performance", "Change marked", EvidenceProvenance.UserReported) { PowerSourceAtMarker = PowerSourceKind.Ac });
        session.AddMarker(new EvidenceMarker(5, EvidenceMarkerKind.UserReportedRestoration,
            "performance", "Restored", EvidenceProvenance.UserReported, "Balanced") { PowerSourceAtMarker = PowerSourceKind.Ac });

        var draft = ContributionDraftBuilder.CreateCoverageDraft(session.Snapshot());

        var item = Assert.Single(draft.Coverage, item => item.Capability == "performance.externalPower.presets");
        Assert.Equal(ContributionCoverageStatus.Captured, item.Status);
        Assert.Equal(ContributionObservationLayer.UserModeFeatureApi, Assert.Single(item.Evidence).Layer);
        Assert.Equal(
            ContributionCoverageStatus.NotInvestigated,
            Assert.Single(draft.Coverage, item => item.Capability == "performance.ac.presets").Status);
        Assert.All(draft.Coverage.Where(entry => entry.Capability.StartsWith("lifecycle.", StringComparison.Ordinal)),
            entry => Assert.Equal(ContributionCoverageStatus.NotInvestigated, entry.Status));
    }

    [Fact]
    public void CreateCoverageDraft_DoesNotPromoteReportWithoutCompleteMarkers()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory(), DateTimeOffset.UnixEpoch);
        session.AddMarker(new EvidenceMarker(1, EvidenceMarkerKind.UserReportedStartingValue,
            "performance", "Starting value", EvidenceProvenance.UserReported, "Balanced"));
        session.AddObservation(new ControlObservation(
            "op-thermal",
            2,
            ObservationSource.UserModeCall,
            "HidD_SetFeature",
            ObservationDirection.Input,
            ObservationStatus.Succeeded,
            0,
            null,
            0x1532,
            0xFFFF,
            "hid-preview-01",
            1,
            2,
            0,
            FeatureReportTestData.Create(0x0D, 0x02, [0x01, 0x01, 0x05, 0x00]),
            PayloadDisposition.ProvenFeatureControl,
            2.1));
        session.AddMarker(new EvidenceMarker(3, EvidenceMarkerKind.UserReportedTargetValue,
            "performance", "Changed value", EvidenceProvenance.UserReported, "Silent"));

        var draft = ContributionDraftBuilder.CreateCoverageDraft(session.Snapshot());

        Assert.Equal(
            ContributionCoverageStatus.NotInvestigated,
            Assert.Single(draft.Coverage, item => item.Capability == "performance.externalPower.presets").Status);
    }

    [Fact]
    public void CreateDrafts_HandleSharedOperationIdAndRepeatedActionWithoutBroadWindow()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory(), DateTimeOffset.UnixEpoch);
        AddCompleteMarkers(session, "performance", 1, 5);
        var request = FeatureReportTestData.Create(
            0x0D,
            0x82,
            [0x01, 0x01, 0x00, 0x00]);
        var response = FeatureReportTestData.Create(
            0x0D,
            0x82,
            [0x01, 0x01, 0x05, 0x00],
            ObservationDirection.Output);
        session.AddObservation(new ControlObservation(
            "op-shared", 2, ObservationSource.UserModeCall, "HidD_GetFeature",
            ObservationDirection.Input, ObservationStatus.Pending, null, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0, request,
            PayloadDisposition.ProvenFeatureControl));
        session.AddObservation(new ControlObservation(
            "op-shared", 3, ObservationSource.ReturnedBuffer, "HidD_GetFeature",
            ObservationDirection.Output, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0, response,
            PayloadDisposition.ProvenFeatureControl, 3, 91, 91, 91));
        AddCompleteMarkers(session, "performance", 10, 14);
        session.AddObservation(new ControlObservation(
            "op-second", 12, ObservationSource.UserModeCall, "HidD_SetFeature",
            ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(0x0D, 0x02, [0x01, 0x01, 0x02, 0x00]),
            PayloadDisposition.ProvenFeatureControl, 12.1));

        var annotations = ContributionDraftBuilder.CreateAnnotationDraft(session.Snapshot());
        var coverage = ContributionDraftBuilder.CreateCoverageDraft(session.Snapshot());

        Assert.Equal(2, annotations.ActionWindows.Count);
        Assert.Equal(2, annotations.ActionWindows[0].Observations.Count);
        Assert.Single(annotations.ActionWindows[1].Observations);
        Assert.Equal(3, annotations.ActionWindows.SelectMany(static item => item.Observations)
            .Select(static item => item.ReferenceId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ContributionCoverageStatus.Captured,
            Assert.Single(coverage.Coverage, item => item.Capability == "performance.externalPower.readback").Status);
    }

    [Fact]
    public void CreateCoverageDraft_QueryRequestIsAnnotationOnlyUntilReturnedBufferArrives()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory(), DateTimeOffset.UnixEpoch);
        AddCompleteMarkers(session, "performance", 1, 5);
        session.AddObservation(new ControlObservation(
            "op-query-request", 2.5, ObservationSource.UserModeCall, "HidD_GetFeature",
            ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(0x0D, 0x82, [0x01, 0x01, 0x05, 0x00]),
            PayloadDisposition.ProvenFeatureControl, 2.6));

        var annotations = ContributionDraftBuilder.CreateAnnotationDraft(session.Snapshot());
        var coverage = ContributionDraftBuilder.CreateCoverageDraft(session.Snapshot());

        Assert.Equal(
            ContributionObservationRole.FeatureQueryRequest,
            Assert.Single(Assert.Single(annotations.ActionWindows).Observations).Role);
        Assert.Equal(
            ContributionCoverageStatus.NotInvestigated,
            Assert.Single(coverage.Coverage,
                item => item.Capability == "performance.externalPower.readback").Status);
    }

    [Fact]
    public void CreateCoverageDraft_PromptedSpecialKeyUsesGenericLeafWithoutAssumingRoleOrFn()
    {
        var inventory = SyntheticPreview.CreateInventory();
        inventory = inventory with { RazerHidInterfaces = SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Collected(
            [inventory.RazerHidInterfaces.Value[0] with { UsagePage = 1, Usage = 6 }]) };
        using var session = new EvidenceSession(inventory, DateTimeOffset.UnixEpoch);
        var observation = new SpecialKeyObservation(
            1,
            SpecialKeyExpectedKey.FnP,
            SpecialKeyModeContext.Unknown,
            "hid-preview-01",
            SpecialKeyObservationOrigin.WindowsRawInput,
            SpecialKeyPhase.KeyDown,
            VirtualKey: 0x50);
        Assert.Equal(EvidenceAddResult.Added, session.AddSpecialKeyObservation(observation));
        Assert.Equal(EvidenceAddResult.Added, session.AddSpecialKeyProbe(new SpecialKeyProbeSummary(
            SpecialKeyExpectedKey.FnP,
            SpecialKeyModeContext.Unknown,
            SpecialKeyProbeEndReason.Timeout,
            1,
            0,
            0,
            "Prompt ended.")));

        var coverage = ContributionDraftBuilder.CreateCoverageDraft(session.Snapshot());

        Assert.Equal(
            ContributionCoverageStatus.Captured,
            Assert.Single(coverage.Coverage, item => item.Capability == "specialKeys.fnP.inputEvidence").Status);
        Assert.Equal(
            ContributionCoverageStatus.NotInvestigated,
            Assert.Single(coverage.Coverage, item => item.Capability == "specialKeys.performanceKey").Status);
        Assert.Equal(
            ContributionCoverageStatus.NotInvestigated,
            Assert.Single(coverage.Coverage, item => item.Capability == "specialKeys.functionLayerReports").Status);
    }

    [Fact]
    public void CreateCoverageDraft_DoesNotTreatMisorderedMarkersAsComplete()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory(), DateTimeOffset.UnixEpoch);
        session.AddMarker(new EvidenceMarker(1, EvidenceMarkerKind.UserReportedStartingValue,
            "performance", "Start", EvidenceProvenance.UserReported, "Balanced"));
        session.AddMarker(new EvidenceMarker(2, EvidenceMarkerKind.UserActionConfirmed,
            "performance", "Action", EvidenceProvenance.UserReported));
        session.AddObservation(new ControlObservation(
            "op-thermal", 2.5, ObservationSource.UserModeCall, "HidD_SetFeature",
            ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(0x0D, 0x02, [0x01, 0x01, 0x05, 0x00]),
            PayloadDisposition.ProvenFeatureControl, 2.6));
        session.AddMarker(new EvidenceMarker(3, EvidenceMarkerKind.UserReportedTargetValue,
            "performance", "Target", EvidenceProvenance.UserReported, "Silent"));
        session.AddMarker(new EvidenceMarker(4, EvidenceMarkerKind.UserReportedRestoration,
            "performance", "Restore", EvidenceProvenance.UserReported, "Balanced"));

        var coverage = ContributionDraftBuilder.CreateCoverageDraft(session.Snapshot());

        Assert.Equal(
            ContributionCoverageStatus.NotInvestigated,
            Assert.Single(coverage.Coverage, item => item.Capability == "performance.externalPower.presets").Status);
    }

    [Theory]
    [InlineData("Balanced", false)]
    [InlineData(" balanced ", false)]
    [InlineData("BALANCED", false)]
    [InlineData("Custom: CPU Boost, GPU Medium", true)]
    public void CreateDrafts_RequiresDistinctStartingAndTargetValues(string target, bool expectedComplete)
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory(), DateTimeOffset.UnixEpoch);
        session.AddMarker(new EvidenceMarker(1, EvidenceMarkerKind.UserReportedStartingValue,
            "performance", "Starting value", EvidenceProvenance.UserReported, "Balanced"));
        session.AddMarker(new EvidenceMarker(2, EvidenceMarkerKind.UserReportedTargetValue,
            "performance", "Changed value", EvidenceProvenance.UserReported, target));
        session.AddMarker(new EvidenceMarker(3, EvidenceMarkerKind.UserActionConfirmed,
            "performance", "Change marked", EvidenceProvenance.UserReported));
        session.AddMarker(new EvidenceMarker(4, EvidenceMarkerKind.UserReportedRestoration,
            "performance", "Restored", EvidenceProvenance.UserReported, "Balanced"));

        var drafts = ContributionDraftBuilder.CreateDrafts(session.Snapshot());

        var window = Assert.Single(drafts.Annotation.ActionWindows);
        Assert.Equal(expectedComplete, window.CompleteUserReportedSequence);
        Assert.Equal(!expectedComplete, window.UnchangedReportedValues);
        Assert.Equal(!expectedComplete, window.Gap?.Contains("unchanged", StringComparison.Ordinal));
        Assert.Single(drafts.Quality.Gaps, gap => gap.Area == "action.performance"
            && gap.Code == "NoMatchingObservation");
        Assert.Equal(!expectedComplete, drafts.Quality.Gaps.Any(gap => gap.Area == "action.performance"
            && gap.Code == "UnchangedReportedValues"));
        if (!expectedComplete)
            Assert.Single(drafts.Quality.Gaps, gap => gap.Area == "action.performance"
                && gap.Code == "UnchangedReportedValues");
        Assert.DoesNotContain(drafts.Quality.Gaps, gap => gap.Area == "action.performance"
            && gap.Code == "IncompleteActionMarkers");
    }

    [Fact]
    public void CreateDrafts_NullValuesRemainUnknownRatherThanUnchanged()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory(), DateTimeOffset.UnixEpoch);
        session.AddMarker(new EvidenceMarker(1, EvidenceMarkerKind.UserReportedStartingValue,
            "performance", "Starting value", EvidenceProvenance.UserReported));
        session.AddMarker(new EvidenceMarker(2, EvidenceMarkerKind.UserReportedTargetValue,
            "performance", "Changed value", EvidenceProvenance.UserReported));
        session.AddMarker(new EvidenceMarker(3, EvidenceMarkerKind.UserActionConfirmed,
            "performance", "Change marked", EvidenceProvenance.UserReported));
        session.AddMarker(new EvidenceMarker(4, EvidenceMarkerKind.UserReportedRestoration,
            "performance", "Restored", EvidenceProvenance.UserReported));

        var drafts = ContributionDraftBuilder.CreateDrafts(session.Snapshot());

        var window = Assert.Single(drafts.Annotation.ActionWindows);
        Assert.True(window.CompleteUserReportedSequence);
        Assert.False(window.UnchangedReportedValues);
        Assert.DoesNotContain(drafts.Quality.Gaps, gap => gap.Code == "UnchangedReportedValues");
    }

    [Fact]
    public async Task WriteAsync_PreCancelledTokenLeavesExistingDestinationUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "evidence.zip");
            await File.WriteAllTextAsync(destination, "existing");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                EvidenceBundleWriter.WriteAsync(SyntheticPreview.CreateSession(), destination, cancellation.Token));

            Assert.Equal("existing", await File.ReadAllTextAsync(destination));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task WriteSnapshotAsync_StreamsBeyondFormerEntryBoundAndRetainsEveryReference()
    {
        const int count = 18000;
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        AddCompleteMarkers(session, "brightness", 0, count + 2);
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(new ControlObservation(
            "seed", 1, ObservationSource.UserModeCall, "HidD_SetFeature",
            ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(3, 3, [1, 5, 197]), PayloadDisposition.ProvenFeatureControl, 1.5)));
        using var snapshot = session.Snapshot();
        var seed = Assert.Single(snapshot.Observations);
        var generated = new GeneratedObservations(count, index => seed with
        {
            OperationId = $"op-{index}", RelativeMilliseconds = index + 1,
            CompletedRelativeMilliseconds = index + 1.5,
        });
        var exportSnapshot = snapshot with { Observations = generated };
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "continuous.zip");
            await EvidenceBundleWriter.WriteSnapshotAsync(exportSnapshot, destination);
            Assert.Equal(5, generated.EnumerationCount); // Routing, semantic correlation, coverage, statistics, manifest.
            using var archive = ZipFile.OpenRead(destination);
            Assert.True(archive.GetEntry("manifest.json")!.Length > 16 * 1024 * 1024);
            using (var manifest = await ReadJsonAsync(archive, "manifest.json"))
            {
                var rows = manifest.RootElement.GetProperty("session").GetProperty("observations");
                Assert.Equal(count, rows.GetArrayLength());
                Assert.Equal("op-17999", rows[count - 1].GetProperty("operationId").GetString());
                Assert.Equal(count + 0.5, rows[count - 1].GetProperty("completedRelativeMilliseconds").GetDouble());
            }
            using (var annotation = await ReadJsonAsync(archive, "annotation-draft.json"))
                Assert.Equal(count, annotation.RootElement.GetProperty("actionWindows")[0]
                    .GetProperty("observations").GetArrayLength());
            using (var coverage = await ReadJsonAsync(archive, "coverage-draft.json"))
            {
                var leaf = Assert.Single(coverage.RootElement.GetProperty("coverage").EnumerateArray(),
                    item => item.GetProperty("capability").GetString() == "keyboardLighting.brightnessRange");
                Assert.Equal(count, leaf.GetProperty("evidence").GetArrayLength());
            }
            // The externally owned snapshot remains usable after export.
            Assert.Equal("seed", Assert.Single(snapshot.Observations).OperationId);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void IncompleteThermalAck_CorrelatesOutOfOrderAcrossExternalSortRuns()
    {
        const int pairs = 1100;
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        AddCompleteMarkers(session, "performance", 0, pairs * 4 + 2);
        // Reverse arrival order forces sorting, and reusing one key verifies
        // that each request is consumed once rather than matched to the nearest.
        for (var index = pairs - 1; index >= 0; index--)
        {
            var time = index * 4 + 1;
            session.AddObservation(CreateIncompleteThermalAck($"get-{index}", time + 1, 0x42));
            session.AddObservation(new ControlObservation(
                $"set-{index}", time, ObservationSource.UserModeCall, "HidD_SetFeature",
                ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
                0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
                FeatureReportTestData.Create(0x0D, 0x02, [1, 1, 5, 0], transactionId: 0x42),
                PayloadDisposition.ProvenFeatureControl, time + 0.5));
        }
        using var snapshot = session.Snapshot();
        using var projections = new ExportProjectionScope();
        var window = Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(snapshot).ActionWindows);
        Assert.Equal(pairs * 2, window.Observations.Count);
        foreach (var response in window.Observations.Where(item => item.OperationId.StartsWith("get-", StringComparison.Ordinal)))
        {
            var number = response.OperationId[4..];
            Assert.Contains($":set-{number}:", response.CorrelatedRequestReferenceId);
        }
    }

    [Fact]
    public void IncompleteThermalAck_DoesNotConsumeARequestBeforeItsCompletion()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        AddCompleteMarkers(session, "performance", 0, 20);
        session.AddObservation(new ControlObservation(
            "set-delayed", 1, ObservationSource.UserModeCall, "HidD_SetFeature",
            ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(0x0D, 0x02, [1, 1, 5, 0], transactionId: 0x42),
            PayloadDisposition.ProvenFeatureControl, 10));
        session.AddObservation(CreateIncompleteThermalAck("get-late", 11, 0x42));
        session.AddObservation(CreateIncompleteThermalAck("get-early", 5, 0x42));
        using var snapshot = session.Snapshot();
        using var projections = new ExportProjectionScope();
        var observations = Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(snapshot).ActionWindows).Observations;
        Assert.Null(Assert.Single(observations, item => item.OperationId == "get-early").CorrelatedRequestReferenceId);
        Assert.Contains(":set-delayed:", Assert.Single(observations, item => item.OperationId == "get-late").CorrelatedRequestReferenceId);
    }

    [Fact]
    public void Drafts_ReadCaptureOnceForWindowRoutingAndReviewReadsDoNotScaleWithWindowCount()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        for (var index = 0; index < 20; index++)
            AddCompleteMarkers(session, $"action-{index}", index * 10, index * 10 + 9);
        session.AddObservation(new ControlObservation(
            "seed", 1, ObservationSource.UserModeCall, "HidD_SetFeature",
            ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(3, 3, [1, 5, 197]), PayloadDisposition.ProvenFeatureControl));
        using var snapshot = session.Snapshot();
        var seed = Assert.Single(snapshot.Observations);
        var generated = new GeneratedObservations(200, index => seed with
        {
            OperationId = $"op-{index}", RelativeMilliseconds = index,
        });
        var source = snapshot with { Observations = generated };
        using var projections = new ExportProjectionScope();
        var annotation = ContributionDraftBuilder.CreateAnnotationDraft(source);
        Assert.Equal(1, generated.EnumerationCount);
        Assert.Equal(200, annotation.ActionWindows.Sum(window => window.Observations.Count));
        foreach (var window in annotation.ActionWindows)
            Assert.Equal(10, window.Observations.Count());
        Assert.Equal(1, generated.EnumerationCount);
        var readsBeforeReview = generated.EnumerationCount;
        var quality = ContributionDraftBuilder.CreateQualityReceipt(source);
        Assert.Equal(20, quality.Counts.CompleteActionWindows);
        Assert.Equal(4, generated.EnumerationCount - readsBeforeReview);
        var readsBeforeTextReview = generated.EnumerationCount;
        var review = EvidenceCollectionReview.Create(source);
        Assert.Contains("20 complete action sequences", review.Summary);
        Assert.Equal(4, generated.EnumerationCount - readsBeforeTextReview);
    }

    [Fact]
    public async Task WriteSnapshotAsync_ScratchCleanupFailureDoesNotPublishOrReplaceDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
            using var snapshot = session.Snapshot();
            var source = snapshot with { Markers = new CleanupFailureMarkers() };
            var destination = Path.Combine(directory, "evidence.zip");
            await File.WriteAllTextAsync(destination, "existing");
            await Assert.ThrowsAsync<IOException>(() => EvidenceBundleWriter.WriteSnapshotAsync(source, destination));
            Assert.Equal("existing", await File.ReadAllTextAsync(destination));
            Assert.Equal(new[] { destination }, Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class CleanupFailureMarkers : IReadOnlyList<EvidenceMarker>
    {
        private bool registered;
        public int Count => 0;
        public EvidenceMarker this[int index] => throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<EvidenceMarker> GetEnumerator()
        {
            if (!registered)
            {
                ExportProjectionScope.Current!.Add(new FailedProjectionCleanup());
                registered = true;
            }
            return Enumerable.Empty<EvidenceMarker>().GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FailedProjectionCleanup : IDisposable
    {
        public void Dispose() => throw new IOException("Synthetic scratch cleanup failure.");
    }

    private sealed class GeneratedObservations(int count, Func<int, SanitizedControlObservation> factory)
        : IReadOnlyList<SanitizedControlObservation>
    {
        public int Count => count;
        public int EnumerationCount { get; private set; }
        public SanitizedControlObservation this[int index] => index >= 0 && index < count
            ? factory(index) : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<SanitizedControlObservation> GetEnumerator()
        {
            EnumerationCount++;
            for (var index = 0; index < count; index++) yield return factory(index);
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void ContributionIdentityDoesNotBorrowUnrelatedPeripheralPids()
    {
        var inventory = SyntheticPreview.CreateInventory();
        inventory = inventory with
        {
            Support = SupportInventorySnapshot.Empty with
            {
                HidDescriptors = SectionResult<IReadOnlyList<HidDescriptorSupportEvidence>>.Collected(
                    [new("hid-blade", "device-blade", 0x1532, 0x02E0, 1, 2, 0, 0, 91, 0, 0, 0, 0, 0, 0)])
            }
        };
        using var session = new EvidenceSession(inventory);
        var draft = ContributionDraftBuilder.CreateCoverageDraft(session.Snapshot());
        Assert.Equal(new[] { "02E0" }, draft.Device.ProductIds);
        Assert.DoesNotContain("FFFF", draft.Device.ProductIds);
    }

    [Theory]
    [InlineData("HidD_GetFeature")]
    [InlineData("DeviceIoControl.HID_GET_FEATURE")]
    public void SetterAckReceivedThroughGetFeatureDoesNotBecomeQueryReadback(string api)
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        AddCompleteMarkers(session, "performance", 1, 5);
        session.AddObservation(new ControlObservation(
            "op-ack", 3, ObservationSource.ReturnedBuffer, api,
            ObservationDirection.Output, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(0x0D, 0x02, [0x01, 0x01, 0x05, 0x00], ObservationDirection.Output),
            PayloadDisposition.ProvenFeatureControl, 3.1, 91, 91, 91));

        var drafts = ContributionDraftBuilder.CreateDrafts(session.Snapshot());

        var observation = Assert.Single(Assert.Single(drafts.Annotation.ActionWindows).Observations);
        Assert.Equal(ContributionObservationRole.FeatureSetResponse, observation.Role);
        Assert.False(observation.FirmwareReadback);
        Assert.Equal(ContributionCoverageStatus.NotInvestigated,
            Assert.Single(drafts.Coverage.Coverage, item => item.Capability == "performance.externalPower.readback").Status);
    }

    [Fact]
    public void IncompleteThermalAck_CorrelatesUniqueReviewedSetterWithoutClaimingReadback()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        AddCompleteMarkers(session, "performance", 1, 10);
        session.AddObservation(new ControlObservation(
            "op-set", 2, ObservationSource.UserModeCall, "HidD_SetFeature",
            ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(0x0D, 0x02, [0x01, 0x01, 0x05, 0x00],
                transactionId: 0x42),
            PayloadDisposition.ProvenFeatureControl, 2.1));
        session.AddObservation(new ControlObservation(
            "op-interleaved", 2.2, ObservationSource.UserModeCall, "HidD_SetFeature",
            ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(0x07, 0x8C, [0x00, 0x00], transactionId: 0x43),
            PayloadDisposition.ProvenFeatureControl, 2.3));
        session.AddObservation(CreateIncompleteThermalAck("op-get", 3, 0x42));

        var drafts = ContributionDraftBuilder.CreateDrafts(session.Snapshot());

        var observations = Assert.Single(drafts.Annotation.ActionWindows).Observations;
        var request = Assert.Single(observations, item => item.OperationId == "op-set");
        var response = Assert.Single(observations, item => item.OperationId == "op-get");
        Assert.Equal(ContributionObservationRole.FeatureSetResponse, response.Role);
        Assert.Equal(request.ReferenceId, response.CorrelatedRequestReferenceId);
        Assert.NotNull(response.SemanticResponse);
        Assert.False(response.CompletePayloadRetained);
        Assert.False(response.FirmwareReadback);
        Assert.Equal(1, drafts.Quality.Counts.ReviewedSemanticOnlyResponses);
        Assert.Contains(EvidenceCollectionReview.Create(session.Snapshot()).Notes,
            note => note.StartsWith("1 returned response summary retained", StringComparison.Ordinal));
        Assert.Equal(ContributionCoverageStatus.NotInvestigated,
            Assert.Single(drafts.Coverage.Coverage,
                item => item.Capability == "performance.externalPower.readback").Status);
    }

    [Fact]
    public void IncompleteThermalAck_DoesNotInventCorrelationForDuplicateMatchingRequests()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        AddCompleteMarkers(session, "performance", 1, 10);
        foreach (var (operation, time) in new[] { ("op-set-1", 2.0), ("op-set-2", 2.2) })
        {
            session.AddObservation(new ControlObservation(
                operation, time, ObservationSource.UserModeCall, "HidD_SetFeature",
                ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
                0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
                FeatureReportTestData.Create(0x0D, 0x02, [0x01, 0x01, 0x05, 0x00],
                    transactionId: 0x42),
                PayloadDisposition.ProvenFeatureControl, time + 0.1));
        }
        session.AddObservation(CreateIncompleteThermalAck("op-get", 3, 0x42));

        var response = Assert.Single(Assert.Single(
                ContributionDraftBuilder.CreateAnnotationDraft(session.Snapshot()).ActionWindows)
            .Observations, item => item.OperationId == "op-get");

        Assert.Equal(ContributionObservationRole.FeatureSetResponse, response.Role);
        Assert.Null(response.CorrelatedRequestReferenceId);
        Assert.False(response.FirmwareReadback);
    }

    [Theory]
    [InlineData(0x00, 0x00, true)]
    [InlineData(0x04, 0x04, true)]
    [InlineData(0x00, 0x04, false)]
    [InlineData(0x04, 0x00, false)]
    public void IncompleteThermalAck_FixedMaximumCorrelationRequiresExactEncoding(
        byte requestMode,
        byte responseMode,
        bool expectedCorrelation)
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        AddCompleteMarkers(session, "performance", 1, 10);
        session.AddObservation(new ControlObservation(
            "op-set", 2, ObservationSource.UserModeCall, "HidD_SetFeature",
            ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(0x0D, 0x02, [0x01, 0x01, requestMode, 0x01],
                transactionId: 0x42),
            PayloadDisposition.ProvenFeatureControl, 2.1));
        session.AddObservation(CreateIncompleteThermalAck(
            "op-get", 3, 0x42, responseMode, 0x01));

        var response = Assert.Single(Assert.Single(
                ContributionDraftBuilder.CreateAnnotationDraft(session.Snapshot()).ActionWindows)
            .Observations, item => item.OperationId == "op-get");

        Assert.Equal(ContributionObservationRole.FeatureSetResponse, response.Role);
        Assert.Equal(expectedCorrelation, response.CorrelatedRequestReferenceId is not null);
        Assert.False(response.FirmwareReadback);
    }

    [Theory]
    [InlineData(7, 0x12, new byte[] { 80 }, "battery.limitQuery", FeaturePayloadProjectionKind.ReviewedBatterySetting)]
    [InlineData(3, 3, new byte[] { 1, 5, 197 }, "keyboardLighting.brightnessQuery", FeaturePayloadProjectionKind.ReviewedKeyboardBrightness)]
    public void BatteryAndBrightnessSetterAcksDoNotClaimGetterCoverage(byte commandClass, byte commandId,
        byte[] payload, string getter, FeaturePayloadProjectionKind projection)
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        AddCompleteMarkers(session, "setting", 1, 5);
        session.AddObservation(new ControlObservation("op-ack", 3, ObservationSource.ReturnedBuffer, "HidD_GetFeature",
            ObservationDirection.Output, ObservationStatus.Succeeded, 0, null, 0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(commandClass, commandId, payload, ObservationDirection.Output),
            PayloadDisposition.ProvenFeatureControl, 3.1, 91, 91, null));
        Assert.Equal(projection, Assert.Single(session.Snapshot().Observations).PayloadProjectionKind);
        var drafts = ContributionDraftBuilder.CreateDrafts(session.Snapshot());
        Assert.Equal(ContributionObservationRole.FeatureSetResponse,
            Assert.Single(Assert.Single(drafts.Annotation.ActionWindows).Observations).Role);
        Assert.Equal(ContributionCoverageStatus.NotInvestigated,
            Assert.Single(drafts.Coverage.Coverage, item => item.Capability == getter).Status);
    }

    [Theory]
    [InlineData(EvidenceMarkerKind.UserReportedCurrentState)]
    [InlineData(EvidenceMarkerKind.ItemSkipped)]
    [InlineData(EvidenceMarkerKind.UserReportedFeatureDefinition)]
    [InlineData(EvidenceMarkerKind.SpecialKeyProbeStarted)]
    public void ContextMarkersDoNotCreateIncompleteSettingWindows(EvidenceMarkerKind kind)
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.AddMarker(new EvidenceMarker(1, kind, "performance", "Reported", EvidenceProvenance.UserReported));

        var drafts = ContributionDraftBuilder.CreateDrafts(session.Snapshot());

        Assert.Empty(drafts.Annotation.ActionWindows);
        Assert.Contains(drafts.Annotation.ContextMarkers, item => item.Marker.Kind == kind);
        Assert.DoesNotContain(drafts.Quality.Gaps, item => item.Code == "IncompleteActionMarkers");
    }

    [Theory]
    [InlineData(EvidenceMarkerKind.UserReportedCurrentState, EvidenceMarkerKind.SpecialKeyProbeStarted)]
    [InlineData(EvidenceMarkerKind.UserReportedStartingValue, EvidenceMarkerKind.UserActionConfirmed)]
    public void KeyDeclarationAndLaterProbeDoNotEncloseUnrelatedFeatureTraffic(
        EvidenceMarkerKind declaration, EvidenceMarkerKind probe)
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.AddMarker(new EvidenceMarker(1, declaration, "special-key-m1", "M1 assignment", EvidenceProvenance.UserReported, "Page Up"));
        session.AddObservation(new ControlObservation(
            "op-unrelated", 50, ObservationSource.UserModeCall, "HidD_SetFeature",
            ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(0x0D, 0x02, [0x01, 0x01, 0x05, 0x00]),
            PayloadDisposition.ProvenFeatureControl, 50.1));
        session.AddMarker(new EvidenceMarker(100, probe, "special-key-m1", "M1 probe", EvidenceProvenance.UserReported));

        var drafts = ContributionDraftBuilder.CreateDrafts(session.Snapshot());

        Assert.Empty(drafts.Annotation.ActionWindows);
        Assert.Equal(2, drafts.Annotation.ContextMarkers.Count);
        Assert.Equal(1, drafts.Quality.Counts.UnassociatedControlObservations);
        Assert.DoesNotContain(drafts.Quality.Gaps, item => item.Code == "IncompleteActionMarkers");
    }

    [Fact]
    public void SettingStartWithoutTargetStillReportsIncompleteAction()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.AddMarker(new EvidenceMarker(1, EvidenceMarkerKind.UserReportedStartingValue,
            "performance", "Start", EvidenceProvenance.UserReported, "Balanced"));

        var drafts = ContributionDraftBuilder.CreateDrafts(session.Snapshot());

        Assert.False(Assert.Single(drafts.Annotation.ActionWindows).CompleteUserReportedSequence);
        Assert.Single(drafts.Quality.Gaps, item => item.Code == "IncompleteActionMarkers");
        Assert.Single(drafts.Quality.Gaps, item => item.Code == "NoMatchingObservation");
    }

    [Theory]
    [InlineData(EvidenceMarkerKind.UserReportedCurrentState)]
    [InlineData(EvidenceMarkerKind.SpecialKeyProbeStarted)]
    public void ReportAndProbeMarkersRequireUserProvenance(EvidenceMarkerKind kind)
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddMarker(new EvidenceMarker(
            1, kind, "report", "Report", EvidenceProvenance.Observer)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteSnapshotAsync_ReturnsWhileDraftWorkIsBlockedAndObservesCancellation(bool cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshot = session.Snapshot() with { Markers = new GatedMarkers(entered, release) };
        var destination = Path.Combine(directory, "evidence.zip");
        var caller = Task.Factory.StartNew(() =>
        {
            try { returned.SetResult(EvidenceBundleWriter.WriteSnapshotAsync(snapshot, destination, cancellation.Token)); }
            catch (Exception error) { returned.SetException(error); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var export = await returned.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(export.IsCompleted);
            if (cancel) cancellation.Cancel();
            release.Set();
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
                Assert.False(File.Exists(destination));
            }
            else
            {
                await export;
                Assert.True(File.Exists(destination));
            }
        }
        finally
        {
            release.Set();
            await caller;
            if (returned.Task.IsCompletedSuccessfully)
            {
                try { await (await returned.Task); } catch (OperationCanceledException) { }
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_FreezesSessionBeforeReturning()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
            var destination = Path.Combine(directory, "evidence.zip");
            var export = EvidenceBundleWriter.WriteAsync(session, destination);
            session.AddMarker(new EvidenceMarker(1, EvidenceMarkerKind.ItemSkipped,
                "after-export", "Added after export began", EvidenceProvenance.UserReported));
            await export;
            using var archive = ZipFile.OpenRead(destination);
            using var manifest = await ReadJsonAsync(archive, "manifest.json");
            Assert.DoesNotContain("after-export", manifest.RootElement.GetRawText(), StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class GatedMarkers(TaskCompletionSource entered, ManualResetEventSlim release)
        : IReadOnlyList<EvidenceMarker>
    {
        public int Count => 0;
        public EvidenceMarker this[int index] => throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<EvidenceMarker> GetEnumerator()
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Draft work was not released.");
            return Enumerable.Empty<EvidenceMarker>().GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static async Task<JsonDocument> ReadJsonAsync(ZipArchive archive, string name)
    {
        var entry = Assert.Single(archive.Entries, item => item.FullName == name);
        await using var stream = entry.Open();
        return await JsonDocument.ParseAsync(stream);
    }

    private static void AddCompleteMarkers(EvidenceSession session, string activity, double start, double end)
    {
        session.AddMarker(new EvidenceMarker(start, EvidenceMarkerKind.UserReportedStartingValue,
            activity, "Start", EvidenceProvenance.UserReported, "Balanced") { PowerSourceAtMarker = PowerSourceKind.Ac });
        session.AddMarker(new EvidenceMarker(start + 1, EvidenceMarkerKind.UserReportedTargetValue,
            activity, "Target", EvidenceProvenance.UserReported, "Silent") { PowerSourceAtMarker = PowerSourceKind.Ac });
        session.AddMarker(new EvidenceMarker(end - 1, EvidenceMarkerKind.UserActionConfirmed,
            activity, "Action", EvidenceProvenance.UserReported) { PowerSourceAtMarker = PowerSourceKind.Ac });
        session.AddMarker(new EvidenceMarker(end, EvidenceMarkerKind.UserReportedRestoration,
            activity, "Restore", EvidenceProvenance.UserReported, "Balanced") { PowerSourceAtMarker = PowerSourceKind.Ac });
    }

    private static ControlObservation CreateIncompleteThermalAck(
        string operationId,
        double time,
        byte transactionId,
        byte mode = 0x05,
        byte fanValue = 0x00)
    {
        var bytes = FeatureReportTestData.Create(
            0x0D,
            0x02,
            [0x01, 0x01, mode, fanValue],
            ObservationDirection.Output,
            transactionId: transactionId)[..90];
        bytes[0] = 0xA5;
        return new ControlObservation(
            operationId, time, ObservationSource.ReturnedBuffer, "DeviceIoControl.HID_GET_FEATURE",
            ObservationDirection.Output, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, null, bytes,
            PayloadDisposition.ProvenFeatureControl, time, 91, 91, 90);
    }
}
