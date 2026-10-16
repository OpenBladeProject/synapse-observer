using System.IO.Compression;
using System.Text.Json;

namespace SynapseObserver.Core.Tests;

public sealed class IncompleteReviewedResponseTests
{
    [Theory]
    [InlineData(0x88, 0x01, 0x26, ReviewedFeatureResponseKind.FanRpmQueryResponse, 3800)]
    [InlineData(0x81, 0x02, 0x1E, ReviewedFeatureResponseKind.StoredFanTargetQueryResponse, 3000)]
    public void NumericFanGetterResponse_ProjectsTypedValueWithoutRetainingPacket(
        byte commandId,
        byte channel,
        byte rawValue,
        ReviewedFeatureResponseKind expectedKind,
        int expectedRpm)
    {
        var prefix = commandId == 0x81 ? (byte)0x00 : (byte)0xA5;
        var payloadClass = commandId == 0x81 ? (byte)0x00 : (byte)0x01;
        using var session = SyntheticPreview.CreateSession();

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted,
            session.AddObservation(IncompleteResponse(
                0x0D, commandId, [payloadClass, channel, rawValue], prefix: prefix)));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Null(stored.PayloadHex);
        Assert.Null(stored.HeaderHints);
        Assert.False(stored.CompletePayloadRetained);
        var semantic = Assert.IsType<ReviewedFeatureResponseProjection>(stored.SemanticResponse);
        Assert.Equal(expectedKind, semantic.Kind);
        Assert.Equal(channel == 1 ? ReviewedFanChannel.First : ReviewedFanChannel.Second,
            semantic.FanChannel);
        Assert.Equal(commandId == 0x88 ? expectedRpm : null, semantic.FanRpm);
        Assert.Equal(commandId == 0x81 ? expectedRpm : null, semantic.StoredFanTargetRpm);
        Assert.Equal(FeatureResponseCompleteness.MissingFinalPadding, semantic.Completeness);
        Assert.Equal(OpaquePrefixDisposition.Excluded, semantic.OpaquePrefixDisposition);
    }

    [Theory]
    [InlineData(0x01, 0x03, ReviewedCustomPerformanceLevel.Boost, false)]
    [InlineData(0x02, 0x03, null, true)]
    [InlineData(0x01, 0x04, null, true)]
    [InlineData(0x02, 0xA5, null, true)]
    public void CustomGetterResponse_RetainsBoundedRawLevelAndOnlyKnownMeaning(
        byte domain,
        byte rawLevel,
        ReviewedCustomPerformanceLevel? expectedLevel,
        bool uninterpreted)
    {
        using var session = SyntheticPreview.CreateSession();

        session.AddObservation(IncompleteResponse(0x0D, 0x87, [0x01, domain, rawLevel]));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(uninterpreted, stored.NumericMeaningUninterpreted);
        var semantic = Assert.IsType<ReviewedFeatureResponseProjection>(stored.SemanticResponse);
        Assert.Equal(ReviewedFeatureResponseKind.CustomPerformanceQueryResponse, semantic.Kind);
        Assert.Equal(domain == 1 ? ReviewedCustomPerformanceDomain.Cpu : ReviewedCustomPerformanceDomain.Gpu,
            semantic.CustomPerformanceDomain);
        Assert.Equal(rawLevel, semantic.CustomPerformanceRawLevel);
        Assert.Equal(expectedLevel, semantic.CustomPerformanceLevel);
    }

    [Fact]
    public void ThermalGetterResponse_IsDistinctFromSetterAcknowledgement()
    {
        using var session = SyntheticPreview.CreateSession();

        session.AddObservation(IncompleteResponse(0x0D, 0x82, [0x01, 0x02, 0x05, 0x00]));

        var semantic = Assert.IsType<ReviewedFeatureResponseProjection>(
            Assert.Single(session.Snapshot().Observations).SemanticResponse);
        Assert.Equal(ReviewedFeatureResponseKind.ThermalModeQueryResponse, semantic.Kind);
        Assert.Equal(ReviewedThermalChannel.Second, semantic.ThermalChannel);
        Assert.Equal(ReviewedThermalMode.SilentAutomatic, semantic.ThermalMode);
    }

    [Theory]
    [InlineData(0x80, 0x00, 0x04, 0x01, ReviewedFeatureResponseKind.LogoStateQueryResponse)]
    [InlineData(0x82, 0x01, 0x04, 0x02, ReviewedFeatureResponseKind.LogoEffectQueryResponse)]
    [InlineData(0x83, 0x01, 0x05, 0x40, ReviewedFeatureResponseKind.KeyboardBrightnessQueryResponse)]
    public void LightingGetterResponse_ProjectsClosedFields(
        byte commandId,
        byte lightingClass,
        byte region,
        byte value,
        ReviewedFeatureResponseKind expectedKind)
    {
        using var session = SyntheticPreview.CreateSession();

        session.AddObservation(IncompleteResponse(0x03, commandId, [lightingClass, region, value]));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Null(stored.PayloadHex);
        var semantic = Assert.IsType<ReviewedFeatureResponseProjection>(stored.SemanticResponse);
        Assert.Equal(expectedKind, semantic.Kind);
        Assert.Equal(lightingClass == 0 ? ReviewedLightingClass.Zero : ReviewedLightingClass.One,
            semantic.LightingClass);
        if (commandId == 0x80) Assert.Equal(ReviewedLogoState.On, semantic.LogoState);
        if (commandId == 0x82) Assert.Equal(ReviewedLogoEffect.Breathing, semantic.LogoEffect);
        if (commandId == 0x83) Assert.Equal((byte)0x40, semantic.KeyboardBrightness);
    }

    [Theory]
    [InlineData("returned-count")]
    [InlineData("requested-count")]
    [InlineData("usage")]
    [InlineData("checksum")]
    [InlineData("padding")]
    [InlineData("data-size")]
    [InlineData("unknown-command")]
    [InlineData("valid-bare-packet")]
    public void IncompleteGetterOutsideStrictEnvelope_RemainsMetadataOnly(string defect)
    {
        var observation = IncompleteResponse(0x0D, 0x88, [0x01, 0x01, 0x26]);
        var bytes = observation.Buffer.ToArray();
        observation = defect switch
        {
            "returned-count" => observation with { ReturnedBytes = 89 },
            "requested-count" => observation with { RequestedBytes = 90 },
            "usage" => observation with { Usage = 6 },
            "checksum" => observation with { Buffer = Mutate(bytes, static value => value[89] ^= 1) },
            "padding" => observation with { Buffer = Mutate(bytes, static value => value[40] = 1, true) },
            "data-size" => observation with { Buffer = Mutate(bytes, static value => value[6] = 81, true) },
            "unknown-command" => observation with { Buffer = Mutate(bytes, static value => value[8] = 0x99, true) },
            "valid-bare-packet" => observation with
            {
                Buffer = FeatureReportTestData.Create(
                    0x0D, 0x88, [0x01, 0x01, 0x26], ObservationDirection.Output)[1..],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };

        var projection = FeaturePayloadPrivacy.Classify(observation);

        Assert.Null(projection.SemanticResponse);
    }

    [Theory]
    [InlineData(0x88, 0x01, "fans.rpmQuery", "sensors.fanRpm")]
    [InlineData(0x81, 0x00, "fans.fixedSpeedQuery", null)]
    public void CorrelatedFanGetterResponse_AddsOnlyItsOwnCoverage(
        byte commandId,
        byte payloadClass,
        string expectedPath,
        string? secondExpectedPath)
    {
        using var session = SyntheticPreview.CreateSession();
        AddCompleteMarkers(session);
        var requestPayload = new byte[] { payloadClass, 0x01, 0x00 };
        session.AddObservation(Request(0x0D, commandId, requestPayload, transactionId: 0x42));
        session.AddObservation(GetFeatureCall(time: 2.5));
        session.AddObservation(IncompleteResponse(
            0x0D, commandId, [payloadClass, 0x01, 0x26], transactionId: 0x42, time: 3));

        var drafts = ContributionDraftBuilder.CreateDrafts(session.Snapshot());

        var observations = Assert.Single(drafts.Annotation.ActionWindows,
            item => item.ActivityId == "fans").Observations;
        var request = Assert.Single(observations, item => item.OperationId == "op-request");
        Assert.Single(observations, item => item.OperationId == "op-get-call");
        var response = Assert.Single(observations, item => item.OperationId == "op-response");
        Assert.Equal(ContributionObservationRole.FeatureQueryResponse, response.Role);
        Assert.Equal(request.ReferenceId, response.CorrelatedRequestReferenceId);
        Assert.False(response.FirmwareReadback);
        Assert.Equal(ContributionCoverageStatus.Captured,
            Assert.Single(drafts.Coverage.Coverage, item => item.Capability == expectedPath).Status);
        if (secondExpectedPath is not null)
        {
            Assert.Equal(ContributionCoverageStatus.Captured,
                Assert.Single(drafts.Coverage.Coverage, item => item.Capability == secondExpectedPath).Status);
        }
        if (commandId == 0x81)
        {
            Assert.Equal(ContributionCoverageStatus.NotInvestigated,
                Assert.Single(drafts.Coverage.Coverage, item => item.Capability == "fans.rpmQuery").Status);
            Assert.Equal(ContributionCoverageStatus.NotInvestigated,
                Assert.Single(drafts.Coverage.Coverage, item => item.Capability == "sensors.fanRpm").Status);
        }
    }

    [Fact]
    public void UnmatchedSemanticGetterResponse_DoesNotCreateCoverage()
    {
        using var session = SyntheticPreview.CreateSession();
        AddCompleteMarkers(session);
        session.AddObservation(IncompleteResponse(0x0D, 0x88, [0x01, 0x01, 0x26], time: 3));

        var coverage = ContributionDraftBuilder.CreateCoverageDraft(session.Snapshot());

        Assert.Equal(ContributionCoverageStatus.NotInvestigated,
            Assert.Single(coverage.Coverage, item => item.Capability == "fans.rpmQuery").Status);
    }

    [Fact]
    public void SemanticGetterResponse_DoesNotCorrelateFromOneByteGetCallAlone()
    {
        using var session = SyntheticPreview.CreateSession();
        AddCompleteMarkers(session);
        session.AddObservation(GetFeatureCall(time: 2.5));
        session.AddObservation(IncompleteResponse(
            0x0D, 0x88, [0x01, 0x01, 0x20], transactionId: 0x42, time: 3));

        var drafts = ContributionDraftBuilder.CreateDrafts(session.Snapshot());

        var response = Assert.Single(Assert.Single(drafts.Annotation.ActionWindows,
            item => item.ActivityId == "fans").Observations,
            item => item.OperationId == "op-response");
        Assert.Null(response.CorrelatedRequestReferenceId);
        Assert.Equal(ContributionCoverageStatus.NotInvestigated,
            Assert.Single(drafts.Coverage.Coverage,
                item => item.Capability == "fans.rpmQuery").Status);
    }

    [Fact]
    public async Task FanRpmSemanticResponse_RoundTripsThroughZipWithoutOpaqueBytes()
    {
        using var session = SyntheticPreview.CreateSession();
        session.AddObservation(IncompleteResponse(
            0x0D, 0x88, [0x01, 0x01, 0x20], prefix: 0xA5));
        var directory = Path.Combine(Path.GetTempPath(),
            $"synapse-observer-semantic-response-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "evidence.zip");
            await EvidenceBundleWriter.WriteAsync(session, destination);
            using var archive = ZipFile.OpenRead(destination);
            var entry = Assert.Single(archive.Entries,
                item => item.FullName == "manifest.json");
            await using var stream = entry.Open();
            using var manifest = await JsonDocument.ParseAsync(stream);
            var observation = Assert.Single(manifest.RootElement
                .GetProperty("session")
                .GetProperty("observations")
                .EnumerateArray());
            Assert.Equal(90, observation.GetProperty("bufferLength").GetInt32());
            Assert.Equal(JsonValueKind.Null, observation.GetProperty("payloadHex").ValueKind);
            Assert.Equal(JsonValueKind.Null, observation.GetProperty("headerHints").ValueKind);
            Assert.False(observation.GetProperty("completePayloadRetained").GetBoolean());
            var semantic = observation.GetProperty("semanticResponse");
            Assert.Equal("fanRpmQueryResponse", semantic.GetProperty("kind").GetString());
            Assert.Equal(3200, semantic.GetProperty("fanRpm").GetInt32());
            Assert.Equal("missingFinalPadding", semantic.GetProperty("completeness").GetString());
            Assert.Equal("excluded", semantic.GetProperty("opaquePrefixDisposition").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0x88, 0x01)]
    [InlineData(0x81, 0x00)]
    public void CompleteReplyConsumesQueryBeforeLaterClippedReplyWithSameTransaction(byte command, byte selector)
    {
        using var session = SyntheticPreview.CreateSession();
        AddCompleteMarkers(session);
        session.AddObservation(Request(0x0D, command, [selector, 1, 0], 0x42));
        session.AddObservation(CompleteResponse(command, [selector, 1, 0x20], 2.3));
        session.AddObservation(Request(0x0D, command, [selector, 1, 0], 0x42) with
        { OperationId = "op-second-query", RelativeMilliseconds = 2.6, CompletedRelativeMilliseconds = 2.7 });
        session.AddObservation(IncompleteResponse(0x0D, command, [selector, 1, 0x26], transactionId: 0x42, time: 3));

        using var snapshot = session.Snapshot();
        var window = Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(snapshot).ActionWindows,
            item => item.ActivityId == "fans");
        var second = Assert.Single(window.Observations, item => item.OperationId == "op-second-query");
        var clipped = Assert.Single(window.Observations, item => item.OperationId == "op-response");
        Assert.Equal(second.ReferenceId, clipped.CorrelatedRequestReferenceId);
    }

    [Fact]
    public void MissingReplyDoesNotPermitGuessingBetweenTwoPendingQueries()
    {
        using var session = SyntheticPreview.CreateSession();
        AddCompleteMarkers(session);
        session.AddObservation(Request(0x0D, 0x88, [1, 1, 0], 0x42));
        session.AddObservation(Request(0x0D, 0x88, [1, 1, 0], 0x42) with
        { OperationId = "op-second-query", RelativeMilliseconds = 2.6, CompletedRelativeMilliseconds = 2.7 });
        session.AddObservation(IncompleteResponse(0x0D, 0x88, [1, 1, 0x26], transactionId: 0x42, time: 3));
        using var snapshot = session.Snapshot();
        var drafts = ContributionDraftBuilder.CreateDrafts(snapshot);
        var clipped = Assert.Single(Assert.Single(drafts.Annotation.ActionWindows, window => window.ActivityId == "fans").Observations,
            item => item.OperationId == "op-response");
        Assert.Null(clipped.CorrelatedRequestReferenceId);
        Assert.NotNull(clipped.SemanticResponse);
        Assert.Equal(ContributionCoverageStatus.NotInvestigated,
            drafts.Coverage.Coverage.Single(item => item.Capability == "fans.rpmQuery").Status);
    }

    [Fact]
    public void StoredTargetQueryArgumentsAreReviewedRatherThanAnUninterpretedTarget()
    {
        var projection = FeaturePayloadPrivacy.Classify(Request(0x0D, 0x81, [0, 1, 0], 0x42));
        Assert.NotNull(projection.PayloadHex);
        Assert.False(projection.NumericMeaningUninterpreted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(255)]
    public void StoredTargetNumericMeaningFlagAgreesAcrossCompleteAndClippedReplies(byte target)
    {
        var complete = FeaturePayloadPrivacy.Classify(CompleteResponse(0x81, [0, 1, target], 3));
        var clipped = FeaturePayloadPrivacy.Classify(IncompleteResponse(0x0D, 0x81, [0, 1, target], time: 3));
        Assert.NotNull(complete.PayloadHex);
        Assert.NotNull(clipped.SemanticResponse);
        Assert.Equal(complete.NumericMeaningUninterpreted, clipped.NumericMeaningUninterpreted);
    }

    private static ControlObservation CompleteResponse(byte command, byte[] payload, double time) =>
        new("op-complete-response", time, ObservationSource.ReturnedBuffer,
            "DeviceIoControl.HID_GET_FEATURE", ObservationDirection.Output, ObservationStatus.Succeeded,
            0, null, 0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(0x0D, command, payload, ObservationDirection.Output, transactionId: 0x42),
            PayloadDisposition.ProvenFeatureControl, time + 0.1, 91, 91, 91);

    private static ControlObservation IncompleteResponse(
        byte commandClass,
        byte commandId,
        byte[] payload,
        byte prefix = 0xA5,
        byte transactionId = 0x1F,
        double time = 10)
    {
        var bytes = FeatureReportTestData.Create(
            commandClass, commandId, payload, ObservationDirection.Output,
            transactionId: transactionId)[..90];
        bytes[0] = prefix;
        return new ControlObservation(
            "op-response", time, ObservationSource.ReturnedBuffer,
            "DeviceIoControl.HID_GET_FEATURE", ObservationDirection.Output,
            ObservationStatus.Succeeded, 1234, null, 0x1532, 0xFFFF,
            "hid-preview-01", 1, 2, null, bytes,
            PayloadDisposition.ProvenFeatureControl, time + 0.1, 91, 91, 90);
    }

    private static ControlObservation Request(
        byte commandClass,
        byte commandId,
        byte[] payload,
        byte transactionId) => new(
        "op-request", 2, ObservationSource.UserModeCall,
        "HidD_SetFeature", ObservationDirection.Input,
        ObservationStatus.Succeeded, 0, null, 0x1532, 0xFFFF,
        "hid-preview-01", 1, 2, 0,
        FeatureReportTestData.Create(commandClass, commandId, payload, transactionId: transactionId),
        PayloadDisposition.ProvenFeatureControl, 2.1, 0, 91, null);

    private static ControlObservation GetFeatureCall(double time) => new(
        "op-get-call", time, ObservationSource.UserModeCall,
        "DeviceIoControl.HID_GET_FEATURE", ObservationDirection.Input,
        ObservationStatus.Succeeded, 0, null, 0x1532, 0xFFFF,
        "hid-preview-01", 1, 2, 0, new byte[] { 0x00 },
        PayloadDisposition.ProvenFeatureControl, time + 0.1, 91, 91, null);

    private static byte[] Mutate(byte[] source, Action<byte[]> mutation, bool checksum = false)
    {
        mutation(source);
        if (checksum)
        {
            byte value = 0;
            for (var index = 3; index <= 88; index++) value ^= source[index];
            source[89] = value;
        }
        return source;
    }

    private static void AddCompleteMarkers(EvidenceSession session)
    {
        session.AddMarker(new EvidenceMarker(1, EvidenceMarkerKind.UserReportedStartingValue,
            "fans", "Start", EvidenceProvenance.UserReported, "Automatic")
            { PowerSourceAtMarker = PowerSourceKind.Ac });
        session.AddMarker(new EvidenceMarker(1.5, EvidenceMarkerKind.UserReportedTargetValue,
            "fans", "Target", EvidenceProvenance.UserReported, "Fixed")
            { PowerSourceAtMarker = PowerSourceKind.Ac });
        session.AddMarker(new EvidenceMarker(4, EvidenceMarkerKind.UserActionConfirmed,
            "fans", "Action", EvidenceProvenance.UserReported)
            { PowerSourceAtMarker = PowerSourceKind.Ac });
        session.AddMarker(new EvidenceMarker(5, EvidenceMarkerKind.UserReportedRestoration,
            "fans", "Restore", EvidenceProvenance.UserReported, "Automatic")
            { PowerSourceAtMarker = PowerSourceKind.Ac });
    }
}
