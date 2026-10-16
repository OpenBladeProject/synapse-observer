using System.Text;
using System.Text.Json;

namespace SynapseObserver.Core.Tests;

public sealed class ExpandedPayloadEvidenceTests
{
    [Fact]
    public void MatrixFrame_RetainsAll102RgbSlotsWithoutBorrowingModelAdmission()
    {
        var bytes = MatrixFrame();
        using var session = Session(bytes.Length);
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(Observation(bytes)));
        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(FeaturePayloadProjectionKind.ReviewedMatrixFrame, stored.PayloadProjectionKind);
        Assert.Equal(Convert.ToHexString(bytes), stored.PayloadHex);
        Assert.True(stored.CompletePayloadRetained);
        Assert.Equal((ushort)0xFFFF, stored.ProductId);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("reserved")]
    [InlineData("padding")]
    [InlineData("terminator")]
    [InlineData("selector")]
    [InlineData("size")]
    [InlineData("report-id")]
    [InlineData("truncated")]
    [InlineData("bare")]
    public void MatrixFrame_MalformedEnvelopeNeverExportsBytes(string defect)
    {
        var bytes = MatrixFrame();
        switch (defect)
        {
            case "checksum": bytes[373] ^= 1; break;
            case "reserved": bytes[3] = 1; RechecksumMatrix(bytes); break;
            case "padding": bytes[319] = 1; RechecksumMatrix(bytes); break;
            case "terminator": bytes[374] = 1; break;
            case "selector": bytes[12] = 0x64; RechecksumMatrix(bytes); break;
            case "size": bytes[5] = 0; RechecksumMatrix(bytes); break;
            case "report-id": bytes[0] = 1; break;
            case "truncated": bytes = bytes[..374]; break;
            case "bare": bytes = bytes[1..]; break;
        }

        Assert.Null(FeaturePayloadPrivacy.Classify(Observation(bytes)).PayloadHex);
    }

    [Theory]
    [InlineData("non-razer")]
    [InlineData("input-report")]
    [InlineData("wrong-interface")]
    [InlineData("wrong-geometry")]
    [InlineData("returned-frame")]
    [InlineData("error")]
    [InlineData("failed")]
    public void MatrixFrame_RequiresScopedFeatureContext(string defect)
    {
        var bytes = MatrixFrame();
        using var session = Session(defect == "wrong-geometry" ? 91 : 375);
        var observation = Observation(bytes);
        observation = defect switch
        {
            "non-razer" => observation with { VendorId = 0x1234 },
            "input-report" => observation with { Api = "ReadFile" },
            "wrong-interface" => observation with { InterfaceLabel = "hid-unrelated" },
            "returned-frame" => observation with { Source = ObservationSource.ReturnedBuffer,
                Direction = ObservationDirection.Output, Api = "HidD_GetFeature" },
            "error" => observation with { ErrorCategory = "capture-truncated" },
            "failed" => observation with { Status = ObservationStatus.Failed },
            _ => observation,
        };
        session.AddObservation(observation);
        Assert.Null(Assert.Single(session.Snapshot().Observations).PayloadHex);
    }

    [Theory]
    [InlineData(3, 0x0A, new byte[] { 5, 0 }, FeaturePayloadProjectionKind.ReviewedMatrixLatch)]
    [InlineData(3, 3, new byte[] { 1, 5, 197 }, FeaturePayloadProjectionKind.ReviewedKeyboardBrightness)]
    [InlineData(3, 0, new byte[] { 1, 4, 1 }, FeaturePayloadProjectionKind.ReviewedLogoState)]
    [InlineData(3, 2, new byte[] { 0, 4, 2 }, FeaturePayloadProjectionKind.ReviewedLogoEffect)]
    [InlineData(0, 4, new byte[] { 3, 0 }, FeaturePayloadProjectionKind.ReviewedDeviceMode)]
    [InlineData(0, 8, new byte[] { 1, 0, 0, 0 }, FeaturePayloadProjectionKind.ReviewedKeyboardBehavior)]
    [InlineData(2, 6, new byte[] { 0, 1 }, FeaturePayloadProjectionKind.ReviewedKeyboardBehavior)]
    [InlineData(15, 24, new byte[] { 0, 1 }, FeaturePayloadProjectionKind.ReviewedKeyboardBehavior)]
    [InlineData(13, 7, new byte[] { 1, 2, 2 }, FeaturePayloadProjectionKind.ReviewedCustomPerformance)]
    [InlineData(7, 15, new byte[] { 4 }, FeaturePayloadProjectionKind.ReviewedBatterySetting)]
    public void ReviewedNumericSettings_RetainOriginalEnvelope(byte commandClass, byte commandId,
        byte[] payload, FeaturePayloadProjectionKind kind)
    {
        var bytes = FeatureReportTestData.Create(commandClass, commandId, payload);
        var projected = FeaturePayloadPrivacy.Classify(Observation(bytes));
        Assert.Equal(kind, projected.Kind);
        Assert.Equal(Convert.ToHexString(bytes), projected.PayloadHex);
    }

    [Theory]
    [InlineData(3, 0x83, new byte[] { 1, 5, 197 }, FeaturePayloadProjectionKind.ReviewedKeyboardBrightness)]
    [InlineData(3, 0x80, new byte[] { 1, 4, 1 }, FeaturePayloadProjectionKind.ReviewedLogoState)]
    [InlineData(3, 0x82, new byte[] { 0, 4, 2 }, FeaturePayloadProjectionKind.ReviewedLogoEffect)]
    [InlineData(0, 0x84, new byte[] { 3, 0 }, FeaturePayloadProjectionKind.ReviewedDeviceMode)]
    [InlineData(0, 0x88, new byte[] { 1, 0, 0, 0 }, FeaturePayloadProjectionKind.ReviewedKeyboardBehavior)]
    [InlineData(2, 0x86, new byte[] { 1, 1 }, FeaturePayloadProjectionKind.ReviewedKeyboardBehavior)]
    [InlineData(15, 0x98, new byte[] { 1 }, FeaturePayloadProjectionKind.ReviewedKeyboardBehavior)]
    [InlineData(13, 0x87, new byte[] { 1, 2, 2 }, FeaturePayloadProjectionKind.ReviewedCustomPerformance)]
    [InlineData(7, 0x8C, new byte[] { 0x12, 0x11 }, FeaturePayloadProjectionKind.ReviewedAdapterPower)]
    public void ReviewedNumericReadbacks_RetainCompleteResponse(byte commandClass, byte commandId,
        byte[] payload, FeaturePayloadProjectionKind kind)
    {
        var bytes = FeatureReportTestData.Create(commandClass, commandId, payload, ObservationDirection.Output);
        var observation = Observation(bytes) with { Source = ObservationSource.ReturnedBuffer,
            Direction = ObservationDirection.Output, Api = "HidD_GetFeature", CompletedRelativeMilliseconds = 12 };
        Assert.Equal(kind, FeaturePayloadPrivacy.Classify(observation).Kind);
    }

    [Fact]
    public void MatrixRow_RequiresExactRgbGeometryAndZeroPadding()
    {
        var row = new byte[55];
        row[0] = 0xFF;
        row[1] = 5;
        row[3] = 16;
        row.AsSpan(4).Fill(0xA7);
        var bytes = FeatureReportTestData.Create(3, 0x0B, row);
        Assert.Equal(FeaturePayloadProjectionKind.ReviewedMatrixRow,
            FeaturePayloadPrivacy.Classify(Observation(bytes)).Kind);
        row[1] = 6;
        Assert.Null(FeaturePayloadPrivacy.Classify(Observation(FeatureReportTestData.Create(3, 0x0B, row))).PayloadHex);
        row[1] = 5;
        row[3] = 17;
        Assert.Null(FeaturePayloadPrivacy.Classify(Observation(FeatureReportTestData.Create(3, 0x0B, row))).PayloadHex);
        bytes[80] = 0x41;
        byte checksum = 0;
        foreach (var value in bytes.AsSpan(3, 86)) { checksum ^= value; }
        bytes[89] = checksum;
        Assert.Null(FeaturePayloadPrivacy.Classify(Observation(bytes)).PayloadHex);
    }

    [Fact]
    public void UnknownBodies_EqualityIsSessionOnlyAndDeltaContainsNoByteValues()
    {
        var secret = new byte[91];
        Encoding.UTF8.GetBytes("SERIAL-private-device").CopyTo(secret, 0);
        using var session = Session(91);
        session.AddObservation(Observation(secret));
        session.AddObservation(Observation(secret) with { OperationId = "op-2", RelativeMilliseconds = 20 });
        var changed = (byte[])secret.Clone();
        changed[40] = 3;
        changed[41] = 7;
        changed[60] = 9;
        session.AddObservation(Observation(changed) with { OperationId = "op-3", RelativeMilliseconds = 30 });
        var observations = session.Snapshot().Observations;
        var first = Assert.IsType<PayloadComparisonEvidence>(observations[0].PayloadComparison);
        var same = Assert.IsType<PayloadComparisonEvidence>(observations[1].PayloadComparison);
        var delta = Assert.IsType<PayloadComparisonEvidence>(observations[2].PayloadComparison);
        Assert.Equal(first.SessionEqualityToken, same.SessionEqualityToken);
        Assert.Equal(0, same.ChangedByteCount);
        Assert.Equal("op-2", delta.PreviousOperationId);
        Assert.Equal(20, delta.PreviousRelativeMilliseconds);
        Assert.Equal(3, delta.ChangedByteCount);
        Assert.Equal([new ChangedByteRange(40, 2), new ChangedByteRange(60, 1)], delta.ChangedRanges);
        Assert.NotEqual(first.SessionEqualityToken, delta.SessionEqualityToken);
        Assert.All(observations, item => Assert.Null(item.PayloadHex));
        Assert.DoesNotContain("SERIAL", JsonSerializer.Serialize(session.Snapshot()));
        using var anotherSession = Session(91);
        anotherSession.AddObservation(Observation(secret));
        Assert.NotEqual(first.SessionEqualityToken,
            Assert.Single(anotherSession.Snapshot().Observations).PayloadComparison!.SessionEqualityToken);
    }

    [Fact]
    public void Comparison_IsPartitionedByApiAndTruncatesRangesWithoutLosingChangedCount()
    {
        using var session = Session(91);
        session.AddObservation(Observation(new byte[91]));
        var changed = new byte[91];
        for (var i = 0; i < changed.Length; i += 2) { changed[i] = 1; }
        session.AddObservation(Observation(changed) with { OperationId = "op-2" });
        session.AddObservation(Observation(changed) with { OperationId = "op-3", Api = "DeviceIoControl.HID_SET_FEATURE" });
        var observations = session.Snapshot().Observations;
        Assert.Equal(46, observations[1].PayloadComparison!.ChangedByteCount);
        Assert.Equal(EvidenceLimits.MaxPayloadChangedRanges, observations[1].PayloadComparison!.ChangedRanges.Count);
        Assert.True(observations[1].PayloadComparison!.ChangedRangesTruncated);
        Assert.Null(observations[2].PayloadComparison!.PreviousOperationId);
    }

    [Fact]
    public void Comparison_DoesNotExportMetadataForUnrelatedInputReports()
    {
        using var session = Session(91);
        session.AddObservation(Observation(new byte[91]) with { Api = "ReadFile" });
        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Null(stored.PayloadComparison);
        Assert.Null(stored.HeaderHints);
        Assert.False(stored.CompletePayloadRetained);
    }

    [Fact]
    public void Comparison_EvictsOldStreamsAtDocumentedMemoryBound()
    {
        var inventory = SyntheticPreview.CreateInventory();
        var candidate = inventory.RazerHidInterfaces.Value[0];
        var interfaces = Enumerable.Range(0, EvidenceLimits.MaxPayloadComparisonStreams + 1)
            .Select(i => candidate with { InterfaceLabel = $"hid-{i}" }).ToArray();
        using var session = new EvidenceSession(inventory with
        { RazerHidInterfaces = SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Collected(interfaces) });
        foreach (var item in interfaces)
        {
            session.AddObservation(Observation(new byte[91]) with { InterfaceLabel = item.InterfaceLabel });
        }

        session.AddObservation(Observation(new byte[91]) with { InterfaceLabel = "hid-0" });
        var last = session.Snapshot().Observations.Last();
        Assert.NotNull(last.PayloadComparison);
        Assert.Null(last.PayloadComparison.PreviousOperationId);
    }

    [Fact]
    public void ReturnedByteCountMismatch_RedactsResponseAndPreservesCallMetadata()
    {
        var bytes = FeatureReportTestData.Create(3, 0x83, [1, 5, 100], ObservationDirection.Output);
        using var session = Session(91);
        session.AddObservation(Observation(bytes) with
        {
            Source = ObservationSource.ReturnedBuffer,
            Direction = ObservationDirection.Output,
            Api = "DeviceIoControl.HID_GET_FEATURE",
            CompletedRelativeMilliseconds = 15,
            OutputBufferCapacity = 128,
            RequestedBytes = 91,
            ReturnedBytes = 80,
        });
        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Null(stored.PayloadHex);
        Assert.Equal(128U, stored.OutputBufferCapacity);
        Assert.Equal(91U, stored.RequestedBytes);
        Assert.Equal(80U, stored.ReturnedBytes);
        Assert.Equal(15, stored.CompletedRelativeMilliseconds);
    }

    [Fact]
    public void Dispose_StopsNewPayloadRetentionButKeepsSanitizedSnapshot()
    {
        var session = Session(91);
        session.AddObservation(Observation(new byte[91]));
        using var snapshot = session.Snapshot();
        session.Dispose();
        session.Dispose();
        Assert.Single(snapshot.Observations);
        Assert.Throws<ObjectDisposedException>(() => session.AddObservation(Observation(new byte[91])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(6)]
    public void HardwareEffectGrammarRetainsOnlyReviewedParameters(byte effect)
    {
        var payload = new byte[80];
        payload[0] = effect;
        if (effect == 1) payload[1] = 2;
        if (effect == 6) { payload[1] = 123; payload[2] = 45; payload[3] = 67; }
        var bytes = FeatureReportTestData.Create(3, 0x0A, payload);
        Assert.Equal(FeaturePayloadProjectionKind.ReviewedHardwareLightingEffect, FeaturePayloadPrivacy.Classify(Observation(bytes)).Kind);
        payload[79] = 42;
        Assert.Null(FeaturePayloadPrivacy.Classify(Observation(FeatureReportTestData.Create(3, 0x0A, payload))).PayloadHex);
    }

    [Theory]
    [InlineData(13, 2, new byte[] { 1, 1, 0xF4, 0xFE })]
    [InlineData(13, 7, new byte[] { 1, 1, 3 })]
    [InlineData(13, 1, new byte[] { 1, 2, 100 })]
    [InlineData(7, 0x12, new byte[] { 0xE1 })]
    public void UnfamiliarNumericValuesAreRetainedWithoutInterpretingTheirMeaning(byte commandClass, byte commandId, byte[] payload)
    {
        var bytes = FeatureReportTestData.Create(commandClass, commandId, payload);
        using var session = Session(91);
        session.AddObservation(Observation(bytes));
        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(Convert.ToHexString(bytes), stored.PayloadHex);
        Assert.True(stored.NumericMeaningUninterpreted);
        // A changed selector or length is a different grammar, not a new value.
        var invalidPayload = payload.Length == 1 ? new byte[] { payload[0], 0 } : payload.ToArray();
        if (payload.Length > 1) invalidPayload[0] = 0xFF;
        Assert.Null(FeaturePayloadPrivacy.Classify(Observation(FeatureReportTestData.Create(commandClass, commandId, invalidPayload))).PayloadHex);
    }

    [Theory]
    [InlineData(13, 0x82, new byte[] { 1, 1, 2, 0 })]
    [InlineData(13, 0x87, new byte[] { 1, 1, 3 })]
    [InlineData(7, 0x92, new byte[] { 0xBC })]
    public void QueryInputRetainsOnlyReviewedZeroArguments(byte commandClass, byte commandId, byte[] payload)
    {
        Assert.Null(FeaturePayloadPrivacy.Classify(Observation(FeatureReportTestData.Create(commandClass, commandId, payload))).PayloadHex);
        for (int i = commandClass == 13 ? 2 : 0; i < payload.Length; i++) payload[i] = 0;
        Assert.NotNull(FeaturePayloadPrivacy.Classify(Observation(FeatureReportTestData.Create(commandClass, commandId, payload))).PayloadHex);
    }

    private static EvidenceSession Session(int featureBytes)
    {
        var inventory = SyntheticPreview.CreateInventory();
        var candidate = inventory.RazerHidInterfaces.Value[0] with
        { ReportGeometry = new HidReportGeometry(91, 91, (ushort)featureBytes) };
        return new EvidenceSession(inventory with
        { RazerHidInterfaces = SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Collected([candidate]) });
    }

    private static ControlObservation Observation(byte[] bytes) => new(
        "op-1", 10, ObservationSource.UserModeCall, "HidD_SetFeature", ObservationDirection.Input,
        ObservationStatus.Succeeded, 0, null, 0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
        bytes, PayloadDisposition.ProvenFeatureControl);

    private static byte[] MatrixFrame()
    {
        var bytes = new byte[375];
        bytes[2] = 0x1F;
        bytes[5] = 1;
        bytes[6] = 0x36;
        bytes[7] = 3;
        bytes[8] = 0x0B;
        bytes[12] = 0x65;
        for (var i = 13; i < 319; i++) { bytes[i] = (byte)(i % 256); }
        RechecksumMatrix(bytes);
        return bytes;
    }

    private static void RechecksumMatrix(byte[] bytes)
    {
        byte checksum = 0;
        foreach (var value in bytes.AsSpan(3, 370)) { checksum ^= value; }
        bytes[373] = checksum;
    }
}
