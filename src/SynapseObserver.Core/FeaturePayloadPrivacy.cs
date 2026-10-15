namespace SynapseObserver.Core;

/// <summary>
/// Projects reviewed complete reports and strictly bounded incomplete response semantics. This is a
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
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/Pid02E0MatrixReport.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/Rz090528MatrixLighting.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/LightingBrightnessProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/LogoLightingProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/Rz090528BrightnessLightingFactory.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/DeviceModeProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/CustomPerformanceProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/GamingModeProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/FunctionKeyPrimaryProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/StartupAnimationProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/TemporaryFullChargeProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/AdapterPowerProtocol.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/BatteryValidationFactory.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/806b0fdceaa2aa8645bcad6213bf73954939e8b2/src/OpenBlade.Protocol/LightingCaptureReportDecoder.cs",
    ];

    public static FeaturePayloadProjection Classify(ControlObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var buffer = observation.Buffer.Span;
        var structureDiagnostics = DiagnoseAmbiguousResponse(observation);
        if (buffer.IsEmpty || !IsFeatureContext(observation))
        {
            return MetadataOnly(null, structureDiagnostics);
        }

        if (buffer.Length < 9)
        {
            return MetadataOnly(null, structureDiagnostics);
        }

        if (buffer.Length == 375)
        {
            return ClassifyMatrixFrame(observation);
        }

        if (TryProjectIncompleteThermalAcknowledgement(
                observation, structureDiagnostics, out var semanticResponse))
        {
            return new FeaturePayloadProjection(
                FeaturePayloadProjectionKind.ReviewedThermalMode,
                null,
                null)
            {
                ResponseStructureDiagnostics = structureDiagnostics,
                SemanticResponse = semanticResponse,
            };
        }

        // A completed HID_GET_FEATURE IOCTL may return the logical packet
        // without the HID report-ID envelope. Preserve its actual bytes and
        // absent report ID; never manufacture a prefix to fit the grammar.
        var bareResponse = IsCompletedBareFeatureResponse(observation);
        if (!bareResponse && (buffer.Length != 91 || buffer[0] != 0))
        {
            return MetadataOnly(null, structureDiagnostics);
        }

        byte? reportId = bareResponse ? null : buffer[0];
        var report = bareResponse ? buffer : buffer[1..];
        var dataSize = report[5];
        var reservedBytesZero = report[2] == 0 && report[3] == 0 && report[4] == 0;
        var checksumValid = report[88] == CalculateChecksum(report);
        var paddingZero = dataSize <= 80
            && report[(8 + dataSize)..88].IndexOfAnyExcept((byte)0) < 0
            && report[89] == 0;
        // Header positions are meaningful only for a structurally identified
        // envelope. Do not reinterpret arbitrary opaque buffer bytes as hints.
        if (!reservedBytesZero || !checksumValid || dataSize > 80)
        {
            return MetadataOnly(null, structureDiagnostics);
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

        if (observation.ReportId != reportId
            || !reservedBytesZero
            || !checksumValid
            || !paddingZero
            || observation.ErrorCategory is not null
            || observation.Status is ObservationStatus.Failed or ObservationStatus.Cancelled
            || observation.Direction == ObservationDirection.Output
                && observation.ReturnedBytes is uint returned && returned != buffer.Length
            || !HasExpectedStatus(observation, report[0]))
        {
            return MetadataOnly(hints, structureDiagnostics);
        }

        var payload = report.Slice(8, dataSize);
        var kind = ClassifyReviewedPayload(report[6], report[7], payload, observation.Direction);
        return kind == FeaturePayloadProjectionKind.MetadataOnly
            ? MetadataOnly(hints, structureDiagnostics)
            : new FeaturePayloadProjection(kind, hints, Convert.ToHexString(buffer))
            {
                NumericMeaningUninterpreted = IsUninterpretedNumericValue(kind, payload, report[7], observation.Direction),
                ResponseStructureDiagnostics = structureDiagnostics,
            };
    }

    internal static FeatureResponseStructureDiagnostics? DiagnoseAmbiguousResponse(ControlObservation observation)
    {
        if (!IsCompletedBareFeatureResponse(observation))
        {
            return null;
        }

        var buffer = observation.Buffer.Span;
        return new FeatureResponseStructureDiagnostics(
            DiagnoseLayout(
                FeatureResponseLayout.BareLogicalPacket,
                FeatureResponseCompleteness.Complete,
                buffer,
                reportOffset: 0,
                checksumIndex: 88,
                paddingEndExclusive: 88,
                requireZeroReportId: false),
            DiagnoseLayout(
                FeatureResponseLayout.ReportIdPrefixedPacket,
                FeatureResponseCompleteness.MissingFinalPadding,
                buffer,
                reportOffset: 1,
                checksumIndex: 89,
                paddingEndExclusive: 89,
                requireZeroReportId: true));
    }

    private static FeatureResponseLayoutDiagnostic DiagnoseLayout(
        FeatureResponseLayout layout,
        FeatureResponseCompleteness completeness,
        ReadOnlySpan<byte> buffer,
        int reportOffset,
        int checksumIndex,
        int paddingEndExclusive,
        bool requireZeroReportId)
    {
        var failures = new List<FeatureResponseInvariantFailure>();
        if (requireZeroReportId && buffer[0] != 0)
        {
            failures.Add(FeatureResponseInvariantFailure.UnexpectedReportId);
        }

        if (buffer[reportOffset] != 0x02)
        {
            failures.Add(FeatureResponseInvariantFailure.UnexpectedStatus);
        }

        if (buffer.Slice(reportOffset + 2, 3).IndexOfAnyExcept((byte)0) >= 0)
        {
            failures.Add(FeatureResponseInvariantFailure.ReservedBytesNonzero);
        }

        var dataSize = buffer[reportOffset + 5];
        if (dataSize > 80)
        {
            failures.Add(FeatureResponseInvariantFailure.DataSizeOutOfRange);
        }

        byte checksum = 0;
        for (var index = reportOffset + 2; index <= reportOffset + 87; index++)
        {
            checksum ^= buffer[index];
        }

        if (checksum != buffer[checksumIndex])
        {
            failures.Add(FeatureResponseInvariantFailure.ChecksumMismatch);
        }

        if (dataSize <= 80)
        {
            var paddingStart = reportOffset + 8 + dataSize;
            if (buffer.Slice(paddingStart, paddingEndExclusive - paddingStart).IndexOfAnyExcept((byte)0) >= 0
                || completeness == FeatureResponseCompleteness.Complete && buffer[89] != 0)
            {
                failures.Add(FeatureResponseInvariantFailure.PaddingNonzero);
            }
        }

        return new FeatureResponseLayoutDiagnostic(layout, completeness, failures.Count == 0, failures.AsReadOnly());
    }

    internal static bool IsCompletedBareFeatureResponse(ControlObservation observation) =>
        observation.Buffer.Length == 90
        && observation.VendorId == 0x1532
        && observation.Api == "DeviceIoControl.HID_GET_FEATURE"
        && observation.Source == ObservationSource.ReturnedBuffer
        && observation.Direction == ObservationDirection.Output
        && observation.Status == ObservationStatus.Succeeded
        && observation.ErrorCategory is null
        && observation.ReportId is null
        // A 90-byte output capacity cannot distinguish a bare packet from
        // a clipped envelope. Require room for the full feature report.
        && observation.OutputBufferCapacity is >= 91
        && observation.CompletedRelativeMilliseconds is double completed
        && double.IsFinite(completed)
        && completed >= observation.RelativeMilliseconds;

    private static bool TryProjectIncompleteThermalAcknowledgement(
        ControlObservation observation,
        FeatureResponseStructureDiagnostics? diagnostics,
        out ReviewedFeatureResponseProjection? projection)
    {
        projection = null;
        if (observation.ReturnedLengthUnknown
            || observation.ReturnedBytes != 90
            || diagnostics?.ReportIdPrefixedPacket is not
            {
                Completeness: FeatureResponseCompleteness.MissingFinalPadding,
                MatchesAvailableBytes: false,
                Failures.Count: 1,
            } shifted
            || shifted.Failures[0] != FeatureResponseInvariantFailure.UnexpectedReportId)
        {
            return false;
        }

        // The leading byte is neither interpreted nor retained. Bytes 1..89
        // independently contain the logical report through its checksum, but
        // the final logical padding byte was not returned by the API.
        var report = observation.Buffer.Span[1..];
        if (report[0] != 0x02
            || report[2] != 0 || report[3] != 0 || report[4] != 0
            || report[5] != 4
            || report[6] != 0x0D || report[7] != 0x02
            || !ReviewedThermalEncoding.TryDecode(report.Slice(8, 4), out var channel, out var mode)
            || report[12..88].IndexOfAnyExcept((byte)0) >= 0
            || report[88] != CalculateChecksum(report))
        {
            return false;
        }

        projection = new ReviewedFeatureResponseProjection(
            ReviewedFeatureResponseKind.ThermalSetterAcknowledgement,
            ReviewedFeatureResponseStatus.Successful,
            report[1],
            channel,
            mode,
            FeatureResponseCompleteness.MissingFinalPadding,
            OpaquePrefixDisposition.Excluded);
        return true;
    }

    private static FeaturePayloadProjectionKind ClassifyReviewedPayload(
        byte commandClass,
        byte commandId,
        ReadOnlySpan<byte> payload,
        ObservationDirection direction)
    {
        // RGB values occupy the entire reviewed payload; no layout mask is
        // imposed on another model. Recognition is privacy-only, not support.
        if (commandClass == 0x03 && commandId == 0x0B && payload.Length == 55
            && payload[0] == 0xFF && payload[1] <= 5 && payload[2] == 0 && payload[3] == 16)
        {
            return FeaturePayloadProjectionKind.ReviewedMatrixRow;
        }

        // Full 80-byte reference hardware-effect envelope: only the reviewed
        // Off, Wave, Spectrum and Static selectors, numeric direction/RGB and
        // zero padding. This is a privacy grammar, never a model support claim.
        if (commandClass == 0x03 && commandId == 0x0A && payload.Length == 80
            && ((payload[0] is 0 or 4 && payload[1..].IndexOfAnyExcept((byte)0) < 0)
                || (payload[0] == 1 && payload[1] is 1 or 2 && payload[2..].IndexOfAnyExcept((byte)0) < 0)
                || (payload[0] == 6 && payload[4..].IndexOfAnyExcept((byte)0) < 0)))
        {
            return FeaturePayloadProjectionKind.ReviewedHardwareLightingEffect;
        }

        if (commandClass == 0x03 && commandId == 0x0A && payload.SequenceEqual<byte>([0x05, 0x00]))
        {
            return FeaturePayloadProjectionKind.ReviewedMatrixLatch;
        }

        if (commandClass == 0x03 && commandId is 0x03 or 0x83 && payload.Length == 3
            && payload[0] == 1 && payload[1] == 5
            && (commandId == 0x03 || direction == ObservationDirection.Output || payload[2] == 0))
        {
            return FeaturePayloadProjectionKind.ReviewedKeyboardBrightness;
        }

        if (commandClass == 0x03 && payload.Length == 3 && payload[0] is 0 or 1 && payload[1] == 4)
        {
            if (commandId is 0x00 or 0x80 && payload[2] <= 1
                && (commandId == 0 || direction == ObservationDirection.Output || payload[2] == 0))
            {
                return FeaturePayloadProjectionKind.ReviewedLogoState;
            }

            if (commandId is 0x02 or 0x82 && payload[2] is 0 or 2
                && (commandId == 2 || direction == ObservationDirection.Output || payload[2] == 0))
            {
                return FeaturePayloadProjectionKind.ReviewedLogoEffect;
            }
        }

        if (commandClass == 0 && commandId is 0x04 or 0x84 && payload.Length == 2
            && payload[0] is 0 or 3 && payload[1] == 0
            && (commandId == 4 || direction == ObservationDirection.Output || payload[0] == 0))
        {
            return FeaturePayloadProjectionKind.ReviewedDeviceMode;
        }

        if ((commandClass == 0 && commandId is 0x08 or 0x88 && payload.Length == 4
                && payload[0] <= 1 && payload[1..].IndexOfAnyExcept((byte)0) < 0
                && (commandId == 0x08 || direction == ObservationDirection.Output || payload[0] == 0))
            || (commandClass == 0 && commandId == 0x08 && direction == ObservationDirection.Output
                && payload.SequenceEqual<byte>([0x32, 0x0A, 0x00, 0x00]))
            || (commandClass == 2 && commandId == 0x06 && payload.Length == 2
                && payload[0] == 0 && payload[1] <= 1)
            || (commandClass == 2 && commandId == 0x86 && payload.Length == 2
                && payload[0] == 1 && payload[1] <= 1
                && (direction == ObservationDirection.Output || payload[1] == 0))
            || (commandClass == 0x0F && commandId == 0x98 && payload.Length == 1
                && payload[0] <= 1 && (direction == ObservationDirection.Output || payload[0] == 0))
            || (commandClass == 0x0F && commandId == 0x18 && payload.Length == 2
                && payload[0] == 0 && payload[1] <= 1))
        {
            return FeaturePayloadProjectionKind.ReviewedKeyboardBehavior;
        }

        if (commandClass == 0x0D && commandId is 0x07 or 0x87 && payload.Length == 3
            && payload[0] == 1 && payload[1] is 1 or 2
            && (commandId == 7 || direction == ObservationDirection.Output || payload[2] == 0))
        {
            return FeaturePayloadProjectionKind.ReviewedCustomPerformance;
        }

        if (commandClass == 7 && commandId is 0x0F or 0x8F && payload.Length == 1
            && payload[0] is 0 or 4
            && (commandId == 0x0F || direction == ObservationDirection.Output || payload[0] == 0))
        {
            return FeaturePayloadProjectionKind.ReviewedBatterySetting;
        }

        if (commandClass == 7 && commandId == 0x8C && payload.Length == 2
            && (direction == ObservationDirection.Output || payload.SequenceEqual<byte>([0x00, 0x00])))
        {
            return FeaturePayloadProjectionKind.ReviewedAdapterPower;
        }

        if (commandClass == 0x0D && commandId is 0x02 or 0x82 && IsThermalFamily(payload)
            && (commandId == 0x02 || direction == ObservationDirection.Output
                || payload[2..].IndexOfAnyExcept((byte)0) < 0))
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
            && (commandId == 0x12 || direction == ObservationDirection.Output || payload[0] == 0))
        {
            return FeaturePayloadProjectionKind.ReviewedBatterySetting;
        }

        return FeaturePayloadProjectionKind.MetadataOnly;
    }

    private static bool IsThermalFamily(ReadOnlySpan<byte> payload) =>
        payload.Length == 4 && payload[0] == 0x01 && payload[1] is 0x01 or 0x02;

    private static bool IsKnownThermalValue(ReadOnlySpan<byte> payload) =>
        ReviewedThermalEncoding.TryDecode(payload, out _, out _);

    private static bool IsFanSpeed(ReadOnlySpan<byte> payload) =>
        payload.Length == 3
        && payload[0] == 0x01
        && payload[1] is 0x01 or 0x02;

    private static bool IsFanRpm(ReadOnlySpan<byte> payload, ObservationDirection direction) =>
        payload.Length == 3
        && payload[0] == 0x01
        && payload[1] is 0x01 or 0x02
        && (direction == ObservationDirection.Output || payload[2] == 0x00);

    private static bool IsBatteryValue(byte value) =>
        value is 0x50 or 0xB2 or 0xB7 or 0xBC or 0xC1 or 0xC6 or 0xCB or 0xD0;

    private static bool IsUninterpretedNumericValue(FeaturePayloadProjectionKind kind,
        ReadOnlySpan<byte> payload, byte commandId, ObservationDirection direction) => kind switch
    {
        FeaturePayloadProjectionKind.ReviewedThermalMode => !IsKnownThermalValue(payload),
        FeaturePayloadProjectionKind.ReviewedCustomPerformance => payload[2] > 2,
        FeaturePayloadProjectionKind.ReviewedFanSpeed => payload[2] is < 0x14 or > 0x3C,
        FeaturePayloadProjectionKind.ReviewedBatterySetting when commandId is 0x12 or 0x92 =>
            !(commandId == 0x92 && direction == ObservationDirection.Input && payload[0] == 0)
            && !IsBatteryValue(payload[0]),
        _ => false,
    };

    private static bool HasExpectedStatus(ControlObservation observation, byte reportStatus) =>
        observation.Direction switch
        {
            ObservationDirection.Input => reportStatus == 0x00,
            ObservationDirection.Output => observation.Status == ObservationStatus.Succeeded
                && reportStatus == 0x02,
            _ => false,
        };

    private static bool IsFeatureContext(ControlObservation observation) =>
        observation.VendorId == 0x1532
        && observation.Api is "HidD_SetFeature" or "HidD_GetFeature"
            or "DeviceIoControl.HID_SET_FEATURE" or "DeviceIoControl.HID_GET_FEATURE"
        && ((observation.Source == ObservationSource.UserModeCall && observation.Direction == ObservationDirection.Input)
            || (observation.Source == ObservationSource.ReturnedBuffer && observation.Direction == ObservationDirection.Output
                && observation.Status == ObservationStatus.Succeeded));

    private static FeaturePayloadProjection ClassifyMatrixFrame(ControlObservation observation)
    {
        var buffer = observation.Buffer.Span;
        var report = buffer[1..];
        // The reviewed codec describes a complete outgoing 374-byte envelope,
        // not a returned matrix response or a bare/clipped large report.
        if (buffer[0] != 0 || observation.ReportId != 0
            || observation.Direction != ObservationDirection.Input || observation.ErrorCategory is not null
            || observation.Status is ObservationStatus.Failed or ObservationStatus.Cancelled
            || report[0] != 0 || report[2] != 0 || report[3] != 0
            || report[4] != 1 || report[5] != 0x36 || report[6] != 3 || report[7] != 0x0B
            || report[8] != 0 || report[9] != 0 || report[10] != 0 || report[11] != 0x65
            || report.Slice(318, 54).IndexOfAnyExcept((byte)0) >= 0 || report[373] != 0)
        {
            return MetadataOnly(null);
        }

        byte checksum = 0;
        foreach (var value in report[2..372])
        {
            checksum ^= value;
        }

        return checksum == report[372]
            ? new(FeaturePayloadProjectionKind.ReviewedMatrixFrame, null, Convert.ToHexString(buffer))
            : MetadataOnly(null);
    }

    private static byte CalculateChecksum(ReadOnlySpan<byte> report)
    {
        byte checksum = 0;
        for (var index = 2; index <= 87; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }

    private static FeaturePayloadProjection MetadataOnly(
        FeatureReportHeaderHints? hints,
        FeatureResponseStructureDiagnostics? structureDiagnostics = null) =>
        new(FeaturePayloadProjectionKind.MetadataOnly, hints, null)
        {
            ResponseStructureDiagnostics = structureDiagnostics,
        };
}
