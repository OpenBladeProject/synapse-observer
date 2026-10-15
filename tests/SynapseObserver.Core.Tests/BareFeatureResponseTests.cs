using System.Text;

namespace SynapseObserver.Core.Tests;

public sealed class BareFeatureResponseTests
{
    [Theory]
    [InlineData(0x01, 0x05)]
    [InlineData(0x02, 0x00)]
    public void AddObservation_CompletedThermalAcknowledgement_PreservesOriginalPacket(byte processor, byte mode)
    {
        var bytes = FeatureReportTestData.Create(
            0x0D, 0x02, [0x01, processor, mode, 0x00], ObservationDirection.Output)[1..];
        var session = SyntheticPreview.CreateSession();

        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(CreateObservation(bytes)));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(90, stored.BufferLength);
        Assert.Equal((uint)91, stored.OutputBufferCapacity);
        Assert.Equal(Convert.ToHexString(bytes), stored.PayloadHex);
        Assert.Equal(180, stored.PayloadHex!.Length);
        Assert.StartsWith("02", stored.PayloadHex, StringComparison.Ordinal);
        Assert.Null(stored.ReportId);
        var hints = Assert.IsType<FeatureReportHeaderHints>(stored.HeaderHints);
        Assert.Null(hints.FeatureReportId);
        Assert.Equal((byte)0x02, hints.Status);
        Assert.Equal((byte)0x1F, hints.TransactionId);
        Assert.True(hints.ChecksumValid);
        Assert.True(hints.PaddingZero);
        Assert.Equal(FeaturePayloadProjectionKind.ReviewedThermalMode, stored.PayloadProjectionKind);
    }

    [Theory]
    [InlineData("other-api")]
    [InlineData("hid-get-feature")]
    [InlineData("ioctl-set-feature")]
    [InlineData("call-source")]
    [InlineData("usb-source")]
    [InlineData("input")]
    [InlineData("failed")]
    [InlineData("pending")]
    [InlineData("cancelled")]
    [InlineData("report-id")]
    [InlineData("no-completion")]
    [InlineData("non-razer")]
    [InlineData("truncated-native-gap")]
    [InlineData("capacity-90")]
    [InlineData("capacity-unknown")]
    public void AddObservation_BarePacketOutsideExactCompletedContext_IsRedacted(string context)
    {
        var observation = CreateObservation(CreateThermalResponse());
        observation = context switch
        {
            "other-api" => observation with { Api = "DeviceIoControl" },
            "hid-get-feature" => observation with { Api = "HidD_GetFeature" },
            "ioctl-set-feature" => observation with { Api = "DeviceIoControl.HID_SET_FEATURE" },
            "call-source" => observation with { Source = ObservationSource.UserModeCall },
            "usb-source" => observation with { Source = ObservationSource.UsbTransfer },
            "input" => observation with { Direction = ObservationDirection.Input },
            "failed" => observation with { Status = ObservationStatus.Failed },
            "pending" => observation with { Status = ObservationStatus.Pending },
            "cancelled" => observation with { Status = ObservationStatus.Cancelled },
            "report-id" => observation with { ReportId = 0 },
            "no-completion" => observation with { CompletedRelativeMilliseconds = null },
            "non-razer" => observation with { VendorId = 0x1234 },
            "truncated-native-gap" => observation with { ErrorCategory = "buffer-truncated" },
            "capacity-90" => observation with { OutputBufferCapacity = 90 },
            "capacity-unknown" => observation with { OutputBufferCapacity = null },
            _ => throw new ArgumentOutOfRangeException(nameof(context)),
        };

        var projection = FeaturePayloadPrivacy.Classify(observation);
        Assert.Null(projection.PayloadHex);
        Assert.Null(projection.HeaderHints);
        var session = SyntheticPreview.CreateSession();
        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, session.AddObservation(observation));
        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Null(stored.PayloadHex);
        Assert.Equal(PayloadDisposition.RedactedUnproven, stored.PayloadDisposition);
    }

    [Theory]
    [InlineData("feature-90")]
    [InlineData("feature-375")]
    [InlineData("missing-geometry")]
    [InlineData("different-interface")]
    public void AddObservation_BarePacketRequiresMatchingFeature91Descriptor(string descriptor)
    {
        var inventory = SyntheticPreview.CreateInventory();
        var candidate = Assert.Single(inventory.RazerHidInterfaces.Value);
        candidate = descriptor switch
        {
            "feature-90" => candidate with { ReportGeometry = new HidReportGeometry(91, 91, 90) },
            "feature-375" => candidate with { ReportGeometry = new HidReportGeometry(91, 91, 375) },
            "missing-geometry" => candidate with { ReportGeometry = null },
            "different-interface" => candidate with { InterfaceLabel = "hid-other" },
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor)),
        };
        var session = new EvidenceSession(inventory with
        {
            RazerHidInterfaces = SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Collected([candidate]),
        });

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted,
            session.AddObservation(CreateObservation(CreateThermalResponse())));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(PayloadDisposition.RedactedUnproven, stored.PayloadDisposition);
        Assert.Null(stored.PayloadHex);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("checksum")]
    [InlineData("reserved")]
    [InlineData("padding")]
    [InlineData("final-padding")]
    [InlineData("status")]
    [InlineData("data-size")]
    public void AddObservation_BarePacketStillRequiresFullReviewedGrammar(string defect)
    {
        var bytes = CreateThermalResponse();
        switch (defect)
        {
            case "truncated": bytes = bytes[..89]; break;
            case "checksum": bytes[88] ^= 1; break;
            case "reserved": bytes[2] = 1; RecalculateChecksum(bytes); break;
            case "padding": bytes[40] = 0xA5; RecalculateChecksum(bytes); break;
            case "final-padding": bytes[89] = 1; break;
            case "status": bytes[0] = 0; break;
            case "data-size": bytes[5] = 81; RecalculateChecksum(bytes); break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        var session = SyntheticPreview.CreateSession();

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, session.AddObservation(CreateObservation(bytes)));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(bytes.Length, stored.BufferLength);
        Assert.Null(stored.PayloadHex);
        Assert.Equal(FeaturePayloadProjectionKind.MetadataOnly, stored.PayloadProjectionKind);
    }

    [Theory]
    [InlineData(0x0E, 0x99)]
    [InlineData(0x0D, 0x02)]
    public void AddObservation_BareResponseWithIdentifyingBody_ExportsOnlyHeaderHints(byte commandClass, byte commandId)
    {
        var bytes = FeatureReportTestData.Create(commandClass, commandId,
            Encoding.ASCII.GetBytes("SERIAL-PRIVATE-123"), ObservationDirection.Output)[1..];
        var session = SyntheticPreview.CreateSession();

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, session.AddObservation(CreateObservation(bytes)));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Null(stored.PayloadHex);
        Assert.Equal(PayloadDisposition.ExcludedSensitive, stored.PayloadDisposition);
        Assert.Equal(FeaturePayloadProjectionKind.MetadataOnly, stored.PayloadProjectionKind);
        Assert.Equal(commandClass, Assert.IsType<FeatureReportHeaderHints>(stored.HeaderHints).CommandClass);
        Assert.Null(stored.HeaderHints.FeatureReportId);
    }

    [Fact]
    public void AddObservation_AmbiguousNinetyByteResponse_DiagnosesBothLayoutsWithoutExposingValues()
    {
        var prefixed = FeatureReportTestData.Create(
            0x0D, 0x02, [0x01, 0x01, 0x05, 0x00], ObservationDirection.Output)[..90];
        var session = SyntheticPreview.CreateSession();

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted,
            session.AddObservation(CreateObservation(prefixed)));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Null(stored.PayloadHex);
        Assert.Null(stored.HeaderHints);
        var diagnostics = Assert.IsType<FeatureResponseStructureDiagnostics>(
            stored.ResponseStructureDiagnostics);
        Assert.False(diagnostics.BareLogicalPacket.MatchesAvailableBytes);
        Assert.NotEmpty(diagnostics.BareLogicalPacket.Failures);
        Assert.Equal(FeatureResponseCompleteness.Complete,
            diagnostics.BareLogicalPacket.Completeness);
        Assert.True(diagnostics.ReportIdPrefixedPacket.MatchesAvailableBytes);
        Assert.Empty(diagnostics.ReportIdPrefixedPacket.Failures);
        Assert.Equal(FeatureResponseCompleteness.MissingFinalPadding,
            diagnostics.ReportIdPrefixedPacket.Completeness);
    }

    [Fact]
    public void AddObservation_BareNinetyByteResponse_DiagnosesBareLayout()
    {
        var session = SyntheticPreview.CreateSession();

        Assert.Equal(EvidenceAddResult.Added,
            session.AddObservation(CreateObservation(CreateThermalResponse())));

        var stored = Assert.Single(session.Snapshot().Observations);
        var diagnostics = Assert.IsType<FeatureResponseStructureDiagnostics>(
            stored.ResponseStructureDiagnostics);
        Assert.True(diagnostics.BareLogicalPacket.MatchesAvailableBytes);
        Assert.Empty(diagnostics.BareLogicalPacket.Failures);
        Assert.False(diagnostics.ReportIdPrefixedPacket.MatchesAvailableBytes);
        Assert.NotEmpty(diagnostics.ReportIdPrefixedPacket.Failures);
    }

    [Fact]
    public void AddObservation_AmbiguousResponse_ReportsOnlyFixedInvariantFailures()
    {
        var prefixed = FeatureReportTestData.Create(
            0x0D, 0x02, [0x01, 0x01, 0x05, 0x00], ObservationDirection.Output)[..90];
        prefixed[3] = 1;
        var session = SyntheticPreview.CreateSession();

        session.AddObservation(CreateObservation(prefixed));

        var diagnostics = Assert.IsType<FeatureResponseStructureDiagnostics>(
            Assert.Single(session.Snapshot().Observations).ResponseStructureDiagnostics);
        Assert.Contains(FeatureResponseInvariantFailure.ReservedBytesNonzero,
            diagnostics.ReportIdPrefixedPacket.Failures);
        Assert.Contains(FeatureResponseInvariantFailure.ChecksumMismatch,
            diagnostics.ReportIdPrefixedPacket.Failures);
    }

    [Theory]
    [InlineData(0x01, 0x00, 0x00, ReviewedThermalChannel.First, ReviewedThermalMode.BalancedAutomatic)]
    [InlineData(0x02, 0x05, 0x00, ReviewedThermalChannel.Second, ReviewedThermalMode.SilentAutomatic)]
    [InlineData(0x01, 0x04, 0x01, ReviewedThermalChannel.First, ReviewedThermalMode.FixedMaximum)]
    [InlineData(0x02, 0x07, 0x00, ReviewedThermalChannel.Second, ReviewedThermalMode.HyperBoostObserved)]
    public void AddObservation_OpaquePrefixThermalAck_ExportsClosedSemanticsOnly(
        byte channel,
        byte mode,
        byte fanValue,
        ReviewedThermalChannel expectedChannel,
        ReviewedThermalMode expectedMode)
    {
        var bytes = FeatureReportTestData.Create(
            0x0D, 0x02, [0x01, channel, mode, fanValue], ObservationDirection.Output)[..90];
        bytes[0] = 0xA5;
        var session = SyntheticPreview.CreateSession();

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted,
            session.AddObservation(CreateObservation(bytes) with { ReturnedBytes = 90 }));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Null(stored.PayloadHex);
        Assert.Null(stored.HeaderHints);
        Assert.False(stored.CompletePayloadRetained);
        Assert.Equal(PayloadDisposition.ExcludedSensitive, stored.PayloadDisposition);
        Assert.Equal(FeaturePayloadProjectionKind.ReviewedThermalMode, stored.PayloadProjectionKind);
        var semantic = Assert.IsType<ReviewedFeatureResponseProjection>(stored.SemanticResponse);
        Assert.Equal(ReviewedFeatureResponseKind.ThermalSetterAcknowledgement, semantic.Kind);
        Assert.Equal(ReviewedFeatureResponseStatus.Successful, semantic.Status);
        Assert.Equal((byte)0x1F, semantic.TransactionId);
        Assert.Equal(expectedChannel, semantic.ThermalChannel);
        Assert.Equal(expectedMode, semantic.ThermalMode);
        Assert.Equal(FeatureResponseCompleteness.MissingFinalPadding, semantic.Completeness);
        Assert.Equal(OpaquePrefixDisposition.Excluded, semantic.OpaquePrefixDisposition);
        var diagnostics = Assert.IsType<FeatureResponseStructureDiagnostics>(stored.ResponseStructureDiagnostics);
        Assert.Equal([FeatureResponseInvariantFailure.UnexpectedReportId],
            diagnostics.ReportIdPrefixedPacket.Failures);
    }

    [Theory]
    [InlineData("returned-count-missing")]
    [InlineData("returned-count-mismatch")]
    [InlineData("query-command")]
    [InlineData("unknown-mode")]
    [InlineData("reserved")]
    [InlineData("checksum")]
    [InlineData("padding")]
    [InlineData("status")]
    public void AddObservation_IncompleteResponseOutsideExactThermalAckGrammar_RemainsMetadataOnly(
        string defect)
    {
        var bytes = FeatureReportTestData.Create(
            0x0D, 0x02, [0x01, 0x01, 0x05, 0x00], ObservationDirection.Output)[..90];
        bytes[0] = 0xA5;
        uint? returnedBytes = 90;
        switch (defect)
        {
            case "returned-count-missing": returnedBytes = null; break;
            case "returned-count-mismatch": returnedBytes = 89; break;
            case "query-command": bytes[8] = 0x82; RecalculateShiftedChecksum(bytes); break;
            case "unknown-mode": bytes[11] = 0x66; RecalculateShiftedChecksum(bytes); break;
            case "reserved": bytes[3] = 1; RecalculateShiftedChecksum(bytes); break;
            case "checksum": bytes[89] ^= 1; break;
            case "padding": bytes[40] = 1; RecalculateShiftedChecksum(bytes); break;
            case "status": bytes[1] = 0; break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        var session = SyntheticPreview.CreateSession();

        session.AddObservation(CreateObservation(bytes) with { ReturnedBytes = returnedBytes });

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Null(stored.SemanticResponse);
        Assert.Null(stored.PayloadHex);
        Assert.Equal(FeaturePayloadProjectionKind.MetadataOnly, stored.PayloadProjectionKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData((byte)0x07)]
    public void Classify_ShortBufferDoesNotFabricateHeaderHints(byte? reportId)
    {
        var observation = CreateObservation([0xA5]) with { ReportId = reportId };

        var projection = FeaturePayloadPrivacy.Classify(observation);

        Assert.Null(projection.PayloadHex);
        Assert.Null(projection.HeaderHints);
    }

    private static byte[] CreateThermalResponse() => FeatureReportTestData.Create(
        0x0D, 0x02, [0x01, 0x01, 0x05, 0x00], ObservationDirection.Output)[1..];

    private static ControlObservation CreateObservation(byte[] bytes) => new(
        "op-response", 10, ObservationSource.ReturnedBuffer, "DeviceIoControl.HID_GET_FEATURE",
        ObservationDirection.Output, ObservationStatus.Succeeded, 0, null,
        0x1532, 0xFFFF, "hid-preview-01", 1, 2, null, bytes,
        PayloadDisposition.ProvenFeatureControl, 11, 91);

    private static void RecalculateChecksum(Span<byte> report)
    {
        byte checksum = 0;
        for (var index = 2; index <= 87; index++)
        {
            checksum ^= report[index];
        }

        report[88] = checksum;
    }

    private static void RecalculateShiftedChecksum(Span<byte> buffer)
    {
        byte checksum = 0;
        for (var index = 3; index <= 88; index++)
        {
            checksum ^= buffer[index];
        }

        buffer[89] = checksum;
    }
}
