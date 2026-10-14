namespace SynapseObserver.Core;

/// <summary>
/// Projects only reviewed, structurally complete Razer feature reports. This is a
/// privacy decision for evidence export and does not authorize a device or command.
/// </summary>
public static class FeaturePayloadPrivacy
{
    public static IReadOnlyList<string> ReviewedSourceUrls { get; } =
    [
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/RazerReport.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/ThermalModeProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/FanRpmProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/BatteryValidationProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/PerformanceFanSetterValidationRunner.cs",
    ];

    public static FeaturePayloadProjection Classify(ControlObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var buffer = observation.Buffer.Span;
        if (buffer.IsEmpty)
        {
            return MetadataOnly(null);
        }

        var reportId = buffer[0];
        if (buffer.Length < 9)
        {
            return MetadataOnly(new FeatureReportHeaderHints(
                reportId,
                0,
                0,
                0,
                0,
                0,
                false,
                false,
                false));
        }

        var report = buffer[1..];
        var dataSize = report[5];
        var reservedBytesZero = report[2] == 0 && report[3] == 0 && report[4] == 0;
        var checksumValid = buffer.Length == 91 && report[88] == CalculateChecksum(report);
        var paddingZero = buffer.Length == 91
            && dataSize <= 80
            && report[(8 + dataSize)..88].IndexOfAnyExcept((byte)0) < 0
            && report[89] == 0;
        // Header positions are meaningful only for a structurally identified
        // envelope. Do not reinterpret arbitrary opaque buffer bytes as hints.
        if (buffer.Length != 91 || reportId != 0 || !reservedBytesZero || !checksumValid || dataSize > 80)
        {
            return MetadataOnly(null);
        }

        var hints = new FeatureReportHeaderHints(
            reportId,
            report[0],
            report[1],
            dataSize,
            report[6],
            report[7],
            reservedBytesZero,
            checksumValid,
            paddingZero);

        if (buffer.Length != 91
            || observation.ReportId != reportId
            || !reservedBytesZero
            || !checksumValid
            || !paddingZero
            || !HasExpectedStatus(observation, report[0]))
        {
            return MetadataOnly(hints);
        }

        var payload = report.Slice(8, dataSize);
        var kind = ClassifyReviewedPayload(report[6], report[7], payload, observation.Direction);
        return kind == FeaturePayloadProjectionKind.MetadataOnly
            ? MetadataOnly(hints)
            : new FeaturePayloadProjection(kind, hints, Convert.ToHexString(buffer));
    }

    private static FeaturePayloadProjectionKind ClassifyReviewedPayload(
        byte commandClass,
        byte commandId,
        ReadOnlySpan<byte> payload,
        ObservationDirection direction)
    {
        if (commandClass == 0x0D && commandId is 0x02 or 0x82 && IsThermalMode(payload))
        {
            return FeaturePayloadProjectionKind.ReviewedThermalMode;
        }

        if (commandClass == 0x0D && commandId == 0x01 && IsFanSpeed(payload))
        {
            return FeaturePayloadProjectionKind.ReviewedFanSpeed;
        }

        if (commandClass == 0x0D && commandId == 0x88 && IsFanRpm(payload, direction))
        {
            return FeaturePayloadProjectionKind.ReviewedFanRpm;
        }

        if (commandClass == 0x07
            && commandId is 0x12 or 0x92
            && payload.Length == 1
            && IsBatteryValue(payload[0]))
        {
            return FeaturePayloadProjectionKind.ReviewedBatterySetting;
        }

        return FeaturePayloadProjectionKind.MetadataOnly;
    }

    private static bool IsThermalMode(ReadOnlySpan<byte> payload) =>
        payload.Length == 4
        && payload[0] == 0x01
        && payload[1] is 0x01 or 0x02
        && (payload[2], payload[3]) is
            (0x00, 0x00) or
            (0x05, 0x00) or
            (0x02, 0x00) or
            (0x04, 0x00) or
            (0x03, 0x00) or
            (0x06, 0x00) or
            (0x07, 0x00) or
            (0x00, 0x01) or
            (0x04, 0x01);

    private static bool IsFanSpeed(ReadOnlySpan<byte> payload) =>
        payload.Length == 3
        && payload[0] == 0x01
        && payload[1] is 0x01 or 0x02
        && payload[2] is >= 0x14 and <= 0x3C;

    private static bool IsFanRpm(ReadOnlySpan<byte> payload, ObservationDirection direction) =>
        payload.Length == 3
        && payload[0] == 0x01
        && payload[1] is 0x01 or 0x02
        && (direction == ObservationDirection.Output || payload[2] == 0x00);

    private static bool IsBatteryValue(byte value) =>
        value is 0x50 or 0xB2 or 0xB7 or 0xBC or 0xC1 or 0xC6 or 0xCB or 0xD0;

    private static bool HasExpectedStatus(ControlObservation observation, byte reportStatus) =>
        observation.Direction switch
        {
            ObservationDirection.Input => reportStatus == 0x00,
            ObservationDirection.Output => observation.Status == ObservationStatus.Succeeded
                && reportStatus == 0x02,
            _ => false,
        };

    private static byte CalculateChecksum(ReadOnlySpan<byte> report)
    {
        byte checksum = 0;
        for (var index = 2; index <= 87; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }

    private static FeaturePayloadProjection MetadataOnly(FeatureReportHeaderHints? hints) =>
        new(FeaturePayloadProjectionKind.MetadataOnly, hints, null);
}
