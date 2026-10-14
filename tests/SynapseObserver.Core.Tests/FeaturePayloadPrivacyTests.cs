using System.Text;

namespace SynapseObserver.Core.Tests;

public sealed class FeaturePayloadPrivacyTests
{
    [Theory]
    [InlineData(0x0D, 0x82, new byte[] { 0x01, 0x01, 0x00, 0x00 }, FeaturePayloadProjectionKind.ReviewedThermalMode)]
    [InlineData(0x0D, 0x01, new byte[] { 0x01, 0x02, 0x36 }, FeaturePayloadProjectionKind.ReviewedFanSpeed)]
    [InlineData(0x07, 0x92, new byte[] { 0x00 }, FeaturePayloadProjectionKind.ReviewedBatterySetting)]
    public void Classify_ReviewedTypedReport_RetainsCompleteEnvelope(
        byte commandClass,
        byte commandId,
        byte[] payload,
        FeaturePayloadProjectionKind expectedKind)
    {
        var bytes = FeatureReportTestData.Create(commandClass, commandId, payload);

        var projection = FeaturePayloadPrivacy.Classify(CreateObservation(bytes));

        Assert.Equal(expectedKind, projection.Kind);
        Assert.Equal(Convert.ToHexString(bytes), projection.PayloadHex);
        Assert.NotNull(projection.HeaderHints);
        Assert.True(projection.HeaderHints.ChecksumValid);
        Assert.True(projection.HeaderHints.PaddingZero);
    }

    [Fact]
    public void Classify_UnfamiliarCommandWithPrivateBytes_ExportsHeaderHintsOnly()
    {
        var privateBytes = Encoding.ASCII.GetBytes("SERIAL-ABC-123");
        var bytes = FeatureReportTestData.Create(0x0E, 0x99, privateBytes);

        var projection = FeaturePayloadPrivacy.Classify(CreateObservation(bytes));

        Assert.Equal(FeaturePayloadProjectionKind.MetadataOnly, projection.Kind);
        Assert.Null(projection.PayloadHex);
        var hints = Assert.IsType<FeatureReportHeaderHints>(projection.HeaderHints);
        Assert.Equal((byte)0x0E, hints.CommandClass);
        Assert.Equal((byte)0x99, hints.CommandId);
        Assert.Equal((byte)privateBytes.Length, hints.DataSize);
    }

    [Fact]
    public void Classify_KnownGetterWithUntouchedTail_ExportsMetadataOnly()
    {
        var bytes = FeatureReportTestData.Create(
            0x07,
            0x92,
            [0xB2],
            ObservationDirection.Output);
        bytes[40] = 0xA5;
        RecalculateChecksum(bytes);
        var observation = CreateObservation(bytes) with
        {
            Api = "HidD_GetFeature",
            Direction = ObservationDirection.Output,
            Source = ObservationSource.ReturnedBuffer,
            Status = ObservationStatus.Succeeded,
            ErrorCategory = "return-length-unknown",
        };

        var projection = FeaturePayloadPrivacy.Classify(observation);

        Assert.Equal(FeaturePayloadProjectionKind.MetadataOnly, projection.Kind);
        Assert.Null(projection.PayloadHex);
        Assert.False(projection.HeaderHints?.PaddingZero);
    }

    [Theory]
    [InlineData(0x49)]
    [InlineData(0xD1)]
    public void Classify_UnfamiliarBatteryNumericValue_RetainsFamilyWithUnknownMeaning(byte value)
    {
        var projection = FeaturePayloadPrivacy.Classify(CreateObservation(
            FeatureReportTestData.Create(0x07, 0x12, [value])));

        Assert.Equal(FeaturePayloadProjectionKind.ReviewedBatterySetting, projection.Kind);
        Assert.NotNull(projection.PayloadHex);
        Assert.True(projection.NumericMeaningUninterpreted);
    }

    [Fact]
    public void Classify_KnownCommandWithBadChecksum_ExportsMetadataOnly()
    {
        var bytes = FeatureReportTestData.Create(0x07, 0x12, [0xB2]);
        bytes[89] ^= 0x01;

        var projection = FeaturePayloadPrivacy.Classify(CreateObservation(bytes));

        Assert.Equal(FeaturePayloadProjectionKind.MetadataOnly, projection.Kind);
        Assert.Null(projection.PayloadHex);
        Assert.Null(projection.HeaderHints);
    }

    [Theory]
    [InlineData(91)]
    [InlineData(375)]
    public void Classify_OpaqueNonEnvelopeDoesNotExposePrivateBytesAsHeaderHints(int length)
    {
        var bytes = new byte[length];
        Encoding.ASCII.GetBytes("SERIAL-ABC-123").CopyTo(bytes, 0);
        var projection = FeaturePayloadPrivacy.Classify(CreateObservation(bytes));
        Assert.Null(projection.PayloadHex);
        Assert.Null(projection.HeaderHints);
    }

    [Fact]
    public void Classify_KnownCommandWithUnexpectedPayloadLength_ExportsMetadataOnly()
    {
        var bytes = FeatureReportTestData.Create(0x07, 0x12, [0xB2, 0x00]);

        var projection = FeaturePayloadPrivacy.Classify(CreateObservation(bytes));

        Assert.Equal(FeaturePayloadProjectionKind.MetadataOnly, projection.Kind);
        Assert.Null(projection.PayloadHex);
        Assert.Equal((byte)2, Assert.IsType<FeatureReportHeaderHints>(projection.HeaderHints).DataSize);
    }

    private static ControlObservation CreateObservation(byte[] buffer) =>
        new(
            "op-classify",
            10,
            ObservationSource.UserModeCall,
            "HidD_SetFeature",
            ObservationDirection.Input,
            ObservationStatus.Succeeded,
            null,
            null,
            0x1532,
            0xFFFF,
            "hid-preview-01",
            1,
            2,
            buffer[0],
            buffer,
            PayloadDisposition.ProvenFeatureControl);

    private static void RecalculateChecksum(Span<byte> feature)
    {
        var report = feature[1..];
        byte checksum = 0;
        for (var index = 2; index <= 87; index++)
        {
            checksum ^= report[index];
        }

        report[88] = checksum;
    }
}
