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
        "https://github.com/OpenBladeProject/openblade-core/blob/4912d467898462441f67dc2d42c57453df4b4c7f/src/OpenBlade.Protocol/ExperimentalBladeLightingFrame.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/4912d467898462441f67dc2d42c57453df4b4c7f/src/OpenBlade.Protocol/ExperimentalBladePort.Fans.cs",
        "https://github.com/OpenBladeProject/openblade-core/blob/4912d467898462441f67dc2d42c57453df4b4c7f/src/OpenBlade.Protocol/ExperimentalBladePort.Custom.cs",
        "https://apps.razer.com/synapse/products/672/mw/rzDevice25.85ce8530211c29f3c304.js",
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

        if (TryProjectIncompleteReviewedResponse(
                observation, structureDiagnostics, out var semanticKind, out var semanticResponse))
        {
            return new FeaturePayloadProjection(
                semanticKind,
                null,
                null)
            {
                NumericMeaningUninterpreted = semanticResponse is
                    { Kind: ReviewedFeatureResponseKind.CustomPerformanceQueryResponse,
                      CustomPerformanceLevel: null }
                    || semanticResponse is { StoredFanTargetRpm: < 2000 or > 6000 },
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

    private static bool TryProjectIncompleteReviewedResponse(
        ControlObservation observation,
        FeatureResponseStructureDiagnostics? diagnostics,
        out FeaturePayloadProjectionKind projectionKind,
        out ReviewedFeatureResponseProjection? projection)
    {
        projectionKind = FeaturePayloadProjectionKind.MetadataOnly;
        projection = null;
        if (!TryGetIncompleteReviewedResponseReport(observation, diagnostics, out var report))
        {
            return false;
        }

        var payload = report.Slice(8, report[5]);
        if (report[6] == 0x0D && report[7] == 0x02
            && ReviewedThermalEncoding.TryDecode(payload, out var setterChannel, out var setterMode))
        {
            projectionKind = FeaturePayloadProjectionKind.ReviewedThermalMode;
            projection = CreateIncompleteSemantic(
                ReviewedFeatureResponseKind.ThermalSetterAcknowledgement, report[1]) with
            {
                ThermalChannel = setterChannel,
                ThermalMode = setterMode,
            };
            return true;
        }

        if (report[6] == 0x0D && report[7] == 0x82
            && ReviewedThermalEncoding.TryDecode(payload, out var queryChannel, out var queryMode))
        {
            projectionKind = FeaturePayloadProjectionKind.ReviewedThermalMode;
            projection = CreateIncompleteSemantic(
                ReviewedFeatureResponseKind.ThermalModeQueryResponse, report[1]) with
            {
                ThermalChannel = queryChannel,
                ThermalMode = queryMode,
            };
            return true;
        }

        if (report[6] == 0x0D && report[7] == 0x88
            && payload is [0x01, 0x01 or 0x02, _])
        {
            projectionKind = FeaturePayloadProjectionKind.ReviewedFanRpm;
            projection = CreateIncompleteSemantic(
                ReviewedFeatureResponseKind.FanRpmQueryResponse, report[1]) with
            {
                FanChannel = payload[1] == 1 ? ReviewedFanChannel.First : ReviewedFanChannel.Second,
                FanRpm = payload[2] * 100,
            };
            return true;
        }

        if (report[6] == 0x0D && report[7] == 0x81
            && payload is [0x00, 0x01 or 0x02, _])
        {
            projectionKind = FeaturePayloadProjectionKind.ReviewedFanSpeed;
            projection = CreateIncompleteSemantic(
                ReviewedFeatureResponseKind.StoredFanTargetQueryResponse, report[1]) with
            {
                FanChannel = payload[1] == 1 ? ReviewedFanChannel.First : ReviewedFanChannel.Second,
                StoredFanTargetRpm = payload[2] * 100,
            };
            return true;
        }

        if (report[6] == 0x0D && report[7] == 0x87
            && TryDecodeCustomPerformance(payload, out var domain, out var rawLevel, out var level))
        {
            projectionKind = FeaturePayloadProjectionKind.ReviewedCustomPerformance;
            projection = CreateIncompleteSemantic(
                ReviewedFeatureResponseKind.CustomPerformanceQueryResponse, report[1]) with
            {
                CustomPerformanceDomain = domain,
                CustomPerformanceLevel = level,
                CustomPerformanceRawLevel = rawLevel,
            };
            return true;
        }

        if (report[6] == 0x03 && report[7] == 0x80
            && payload is [0x00 or 0x01, 0x04, 0x00 or 0x01])
        {
            projectionKind = FeaturePayloadProjectionKind.ReviewedLogoState;
            projection = CreateIncompleteSemantic(
                ReviewedFeatureResponseKind.LogoStateQueryResponse, report[1]) with
            {
                LightingClass = payload[0] == 0 ? ReviewedLightingClass.Zero : ReviewedLightingClass.One,
                LogoState = payload[2] == 0 ? ReviewedLogoState.Off : ReviewedLogoState.On,
            };
            return true;
        }

        if (report[6] == 0x03 && report[7] == 0x82
            && payload is [0x00 or 0x01, 0x04, 0x00 or 0x02])
        {
            projectionKind = FeaturePayloadProjectionKind.ReviewedLogoEffect;
            projection = CreateIncompleteSemantic(
                ReviewedFeatureResponseKind.LogoEffectQueryResponse, report[1]) with
            {
                LightingClass = payload[0] == 0 ? ReviewedLightingClass.Zero : ReviewedLightingClass.One,
                LogoEffect = payload[2] == 0 ? ReviewedLogoEffect.Static : ReviewedLogoEffect.Breathing,
            };
            return true;
        }

        if (report[6] == 0x03 && report[7] == 0x83
            && payload is [0x01, 0x05, _])
        {
            projectionKind = FeaturePayloadProjectionKind.ReviewedKeyboardBrightness;
            projection = CreateIncompleteSemantic(
                ReviewedFeatureResponseKind.KeyboardBrightnessQueryResponse, report[1]) with
            {
                LightingClass = ReviewedLightingClass.One,
                KeyboardBrightness = payload[2],
            };
            return true;
        }

        return false;
    }

    private static bool TryGetIncompleteReviewedResponseReport(
        ControlObservation observation,
        FeatureResponseStructureDiagnostics? diagnostics,
        out ReadOnlySpan<byte> report)
    {
        report = default;
        if (observation.ReturnedLengthUnknown
            || observation.ReturnedBytes != 90
            || observation.RequestedBytes is uint requested && requested != 91
            || observation.ProductId == 0
            || observation.UsagePage != 1
            || observation.Usage != 2
            || string.IsNullOrWhiteSpace(observation.InterfaceLabel)
            || diagnostics?.BareLogicalPacket.MatchesAvailableBytes != false
            || diagnostics.ReportIdPrefixedPacket is not
                { Completeness: FeatureResponseCompleteness.MissingFinalPadding } shifted
            || !shifted.MatchesAvailableBytes
                && (shifted.Failures.Count != 1
                    || shifted.Failures[0] != FeatureResponseInvariantFailure.UnexpectedReportId))
        {
            return false;
        }

        // The leading byte is excluded whether it is zero or opaque. Bytes
        // 1..89 independently contain the logical report through its checksum,
        // while the final logical padding byte was not returned by the API.
        report = observation.Buffer.Span[1..];
        var dataSize = report[5];
        if (report[0] != 0x02
            || report[2] != 0 || report[3] != 0 || report[4] != 0
            || dataSize > 80
            || report[(8 + dataSize)..88].IndexOfAnyExcept((byte)0) >= 0
            || report[88] != CalculateChecksum(report))
        {
            report = default;
            return false;
        }

        return true;
    }

    private static ReviewedFeatureResponseProjection CreateIncompleteSemantic(
        ReviewedFeatureResponseKind kind,
        byte transactionId) =>
        new(
            kind,
            ReviewedFeatureResponseStatus.Successful,
            transactionId,
            FeatureResponseCompleteness.MissingFinalPadding,
            OpaquePrefixDisposition.Excluded);

    private static bool TryDecodeCustomPerformance(
        ReadOnlySpan<byte> payload,
        out ReviewedCustomPerformanceDomain domain,
        out byte rawLevel,
        out ReviewedCustomPerformanceLevel? level)
    {
        domain = default;
        rawLevel = default;
        level = default;
        if (payload is not [0x01, 0x01 or 0x02, _])
        {
            return false;
        }

        rawLevel = payload[2];
        domain = payload[1] == 1
            ? ReviewedCustomPerformanceDomain.Cpu
            : ReviewedCustomPerformanceDomain.Gpu;
        level = payload[2] switch
        {
            0 => ReviewedCustomPerformanceLevel.Low,
            1 => ReviewedCustomPerformanceLevel.Medium,
            2 => ReviewedCustomPerformanceLevel.High,
            3 when domain == ReviewedCustomPerformanceDomain.Cpu => ReviewedCustomPerformanceLevel.Boost,
            _ => null,
        };
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
        if (commandClass == 0x03 && commandId == 0x0B && payload.Length is 52 or 55
            && payload[0] == 0xFF && payload[1] <= 5 && payload[2] == 0
            && payload[3] == (payload.Length - 4) / 3 - 1)
        {
            return FeaturePayloadProjectionKind.ReviewedMatrixRow;
        }

        // Vendor setLedMatrixRGBEffect sends the used length, while older
        // callers send all 80 bytes. Accept only reviewed numeric/RGB shapes
        // with zero unused bytes; no opaque effect data or model admission.
        if (commandClass == 0x03 && commandId == 0x0A && IsReviewedLightingEffect(payload))
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
            && (commandId == 0x0F || direction == ObservationDirection.Output || payload[0] == 0))
        {
            // Shared numeric bitmask: not a full-charge-only enum. Retain the
            // byte without inferring a feature from zero or unfamiliar bits.
            return FeaturePayloadProjectionKind.ReviewedPowerModeControl;
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

        if (commandClass == 0x0D && commandId == 0x81
            && payload.Length == 3
            && payload[0] == 0x00
            && payload[1] is 0x01 or 0x02
            && (direction == ObservationDirection.Output || payload[2] == 0x00))
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

    private static bool IsReviewedLightingEffect(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty) return false;
        var used = payload[0] switch
        {
            0 or 4 => 1,
            1 when payload.Length >= 2 && payload[1] is 1 or 2 => 2,
            2 when payload.Length >= 5 => 5, // duration and RGB
            3 when payload.Length >= 2 && payload[1] == 1 => 5,
            3 when payload.Length >= 2 && payload[1] == 2 => 8,
            3 when payload.Length >= 2 && payload[1] == 3 => 2,
            6 => 4,
            _ => 0,
        };
        return used > 0 && (payload.Length == used || payload.Length == 80)
            && payload.Length >= used && payload[used..].IndexOfAnyExcept((byte)0) < 0;
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
        FeaturePayloadProjectionKind.ReviewedFanSpeed =>
            !(commandId == 0x81 && direction == ObservationDirection.Input && payload[2] == 0)
            && payload[2] is < 0x14 or > 0x3C,
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
