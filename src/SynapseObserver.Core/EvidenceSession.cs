using System.Security.Cryptography;

namespace SynapseObserver.Core;

public sealed class EvidenceSession : IDisposable
{
    private readonly object _sync = new();
    private readonly List<EvidenceMarker> _markers = [];
    private readonly List<SpecialKeyObservation> _specialKeys = [];
    private readonly List<SpecialKeyProbeSummary> _specialKeyProbes = [];
    private readonly List<SanitizedControlObservation> _observations = [];
    private readonly Dictionary<string, CoverageItem> _coverage = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> _dropMarkerIndices = [];
    private int _payloadBytes;
    private int _rejectedMarkers;
    private int _rejectedObservations;
    private int _rejectedCoverage;
    private readonly byte[] _comparisonKey = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<ComparisonStream, PreviousBuffer> _previousBuffers = [];
    private bool _disposed;

    public EvidenceSession(InventorySnapshot inventory, DateTimeOffset? startedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        Inventory = InventorySanitizer.Sanitize(inventory);
        StartedAtUtc = startedAtUtc ?? DateTimeOffset.UtcNow;
    }

    public int SpecialKeyProbeCount { get { lock (_sync) return _specialKeyProbes.Count; } }
    public int SpecialKeyObservationCount { get { lock (_sync) return _specialKeys.Count; } }

    public int MarkerCount { get { lock (_sync) return _markers.Count; } }

    public int ObservationCount { get { lock (_sync) return _observations.Count; } }

    public InventorySnapshot Inventory { get; }

    public DateTimeOffset StartedAtUtc { get; }

    // A growing queue-loss counter must not consume one marker per poll.
    // Replacement retains the first-loss timestamp and recording boundary.
    public EvidenceAddResult RecordObserverDroppedEvents(double relativeMilliseconds, int recordingNumber, int droppedEvents)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!double.IsFinite(relativeMilliseconds) || relativeMilliseconds < 0 || recordingNumber < 1 || droppedEvents < 1)
            {
                _rejectedMarkers++;
                return EvidenceAddResult.RejectedInvalid;
            }
            var value = $"count-{droppedEvents}";
            if (_dropMarkerIndices.TryGetValue(recordingNumber, out var index))
            {
                _markers[index] = _markers[index] with { Value = value };
                return EvidenceAddResult.Added;
            }
            var result = AddMarker(new EvidenceMarker(relativeMilliseconds, EvidenceMarkerKind.Failure,
                "observer", "Observer queue dropped events", EvidenceProvenance.Observer, value));
            if (result == EvidenceAddResult.Added) _dropMarkerIndices.Add(recordingNumber, _markers.Count - 1);
            return result;
        }
    }

    public EvidenceAddResult AddMarker(EvidenceMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        lock (_sync)
        {
            if (_markers.Count >= EvidenceLimits.MaxMarkers)
            {
                _rejectedMarkers++;
                return EvidenceAddResult.RejectedLimit;
            }

            if (!double.IsFinite(marker.RelativeMilliseconds)
                || marker.RelativeMilliseconds < 0
                || !EvidenceText.IsToken(marker.ActivityId)
                || !EvidenceText.IsDisplayText(marker.Label)
                || (marker.Value is not null && !EvidenceText.IsDisplayText(marker.Value))
                || marker.PowerSourceAtMarker is { } powerSource && !Enum.IsDefined(powerSource))
            {
                _rejectedMarkers++;
                return EvidenceAddResult.RejectedInvalid;
            }

            if (marker.Kind is EvidenceMarkerKind.UserReportedStartingValue
                    or EvidenceMarkerKind.UserReportedTargetValue
                    or EvidenceMarkerKind.UserReportedRestoration
                    or EvidenceMarkerKind.UserReportedCurrentState
                    or EvidenceMarkerKind.SpecialKeyProbeStarted
                && marker.Provenance != EvidenceProvenance.UserReported)
            {
                _rejectedMarkers++;
                return EvidenceAddResult.RejectedInvalid;
            }

            _markers.Add(marker);
            return EvidenceAddResult.Added;
        }
    }

    public EvidenceAddResult AddObservation(ControlObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_observations.Count >= EvidenceLimits.MaxObservations)
            {
                _rejectedObservations++;
                return EvidenceAddResult.RejectedLimit;
            }

            if (!IsValidObservation(observation))
            {
                _rejectedObservations++;
                return EvidenceAddResult.RejectedInvalid;
            }

            var disposition = observation.PayloadDisposition;
            string? payloadHex = null;
            var scoped = IsProvenFeatureControlScope(observation);
            var projection = scoped
                ? FeaturePayloadPrivacy.Classify(observation)
                : new FeaturePayloadProjection(FeaturePayloadProjectionKind.MetadataOnly, null, null);
            if (disposition == PayloadDisposition.ProvenFeatureControl)
            {
                if (!scoped)
                {
                    disposition = PayloadDisposition.RedactedUnproven;
                }
                else if (observation.Buffer.Length > EvidenceLimits.MaxPayloadBytesPerObservation)
                {
                    disposition = PayloadDisposition.RedactedOverLimit;
                }
                else if (!projection.IncludesPayload)
                {
                    disposition = PayloadDisposition.ExcludedSensitive;
                }
                else if (_payloadBytes <= EvidenceLimits.MaxTotalPayloadBytes - observation.Buffer.Length)
                {
                    payloadHex = projection.PayloadHex;
                    _payloadBytes += observation.Buffer.Length;
                }
                else
                {
                    disposition = PayloadDisposition.RedactedOverLimit;
                }
            }

            _observations.Add(new SanitizedControlObservation(
                observation.OperationId,
                observation.RelativeMilliseconds,
                observation.Source,
                observation.Api,
                observation.Direction,
                observation.Status,
                observation.NativeStatus,
                observation.ErrorCategory,
                observation.VendorId,
                observation.ProductId,
                observation.InterfaceLabel,
                observation.UsagePage,
                observation.Usage,
                observation.ReportId,
                observation.Buffer.Length,
                disposition,
                payloadHex,
                observation.CompletedRelativeMilliseconds,
                observation.OutputBufferCapacity,
                observation.RequestedBytes,
                observation.ReturnedBytes)
            {
                PayloadProjectionKind = projection.Kind,
                NumericMeaningUninterpreted = projection.NumericMeaningUninterpreted,
                ReturnedLengthUnknown = observation.ReturnedLengthUnknown,
                HeaderHints = projection.HeaderHints,
                ResponseStructureDiagnostics = projection.ResponseStructureDiagnostics,
                SemanticResponse = projection.SemanticResponse,
                PayloadComparison = scoped && observation.Buffer.Length <= EvidenceLimits.MaxPayloadBytesPerObservation
                    ? ComparePayload(observation) : null,
            });
            return disposition == observation.PayloadDisposition
                ? EvidenceAddResult.Added
                : EvidenceAddResult.AddedPayloadRedacted;
        }
    }

    public EvidenceAddResult SetCoverage(CoverageItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_sync)
        {
            if (!EvidenceText.IsToken(item.ActivityId) || !EvidenceText.IsDisplayText(item.Label))
            {
                _rejectedCoverage++;
                return EvidenceAddResult.RejectedInvalid;
            }

            if (!_coverage.ContainsKey(item.ActivityId)
                && _coverage.Count >= EvidenceLimits.MaxCoverageItems)
            {
                _rejectedCoverage++;
                return EvidenceAddResult.RejectedLimit;
            }

            _coverage[item.ActivityId] = item;
            return EvidenceAddResult.Added;
        }
    }

    public EvidenceAddResult AddSpecialKeyObservation(SpecialKeyObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_specialKeys.Count >= EvidenceLimits.MaxSpecialKeyObservations) return EvidenceAddResult.RejectedLimit;
            if (!SpecialKeyEvidencePolicy.IsValid(observation, Inventory)) return EvidenceAddResult.RejectedInvalid;
            if (_specialKeys.Count(item => item.ProbeLabel == observation.ProbeLabel) >= EvidenceLimits.MaxSpecialKeyObservationsPerProbe)
                return EvidenceAddResult.RejectedLimit;
            _specialKeys.Add(observation with { ChangedRanges = observation.ChangedRanges?.ToArray() });
            return EvidenceAddResult.Added;
        }
    }

    public EvidenceAddResult AddSpecialKeyProbe(SpecialKeyProbeSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_specialKeyProbes.Count >= EvidenceLimits.MaxSpecialKeyProbes) return EvidenceAddResult.RejectedLimit;
            if (!EvidenceText.IsToken(summary.ProbeLabel) || !double.IsFinite(summary.RelativeMilliseconds)
                || summary.RelativeMilliseconds < 0 || !Enum.IsDefined(summary.ExpectedKey)
                || !Enum.IsDefined(summary.ModeContext) || !Enum.IsDefined(summary.EndReason)
                || (summary.ObservationCount < 0 || summary.ObservationCount > EvidenceLimits.MaxSpecialKeyObservationsPerProbe) || summary.IgnoredTypingCount is < 0 or > 1000000
                || summary.RejectedPacketCount is < 0 or > 1000000)
                return EvidenceAddResult.RejectedInvalid;
            // Fixed policy text prevents any input-supplied diagnostic from exporting private text.
            _specialKeyProbes.Add(summary with { Gap = SpecialKeyEvidencePolicy.Gap(summary.EndReason) });
            return EvidenceAddResult.Added;
        }
    }

    public EvidenceSessionSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new EvidenceSessionSnapshot(
                EvidenceSessionSnapshot.CurrentSchemaVersion,
                StartedAtUtc,
                Inventory,
                EvidenceSessionSnapshot.Freeze(_markers),
                EvidenceSessionSnapshot.Freeze(_observations),
                EvidenceSessionSnapshot.Freeze(_coverage.Values.OrderBy(static item => item.ActivityId, StringComparer.Ordinal)),
                _rejectedMarkers,
                _rejectedObservations,
                _rejectedCoverage)
            {
                SpecialKeyObservations = EvidenceSessionSnapshot.Freeze(_specialKeys),
                SpecialKeyProbes = EvidenceSessionSnapshot.Freeze(_specialKeyProbes),
            };
        }
    }

    private static bool IsValidObservation(ControlObservation observation) =>
        double.IsFinite(observation.RelativeMilliseconds)
        && observation.RelativeMilliseconds >= 0
        && (observation.CompletedRelativeMilliseconds is null
            || double.IsFinite(observation.CompletedRelativeMilliseconds.Value)
                && observation.CompletedRelativeMilliseconds.Value >= observation.RelativeMilliseconds)
        && EvidenceText.IsToken(observation.OperationId)
        && EvidenceText.IsToken(observation.Api)
        && EvidenceText.IsToken(observation.InterfaceLabel)
        && (observation.ErrorCategory is null || EvidenceText.IsToken(observation.ErrorCategory))
        && (!observation.ReturnedLengthUnknown
            || observation.Source == ObservationSource.ReturnedBuffer
                && observation.Api == "HidD_GetFeature"
                && observation.Direction == ObservationDirection.Output
                && observation.Status == ObservationStatus.Succeeded
                && observation.ErrorCategory is null
                && observation.ReturnedBytes is null)
        && observation.Buffer.Length <= int.MaxValue;

    private PayloadComparisonEvidence ComparePayload(ControlObservation observation)
    {
        // HMAC is keyed with fresh per-session randomness; neither this key nor
        // any stable digest is exported. The token does not reveal byte values.
        var token = Convert.ToHexString(HMACSHA256.HashData(_comparisonKey, observation.Buffer.Span));
        var stream = new ComparisonStream(observation.InterfaceLabel, observation.Api, observation.Source,
            observation.Direction, observation.Buffer.Length, observation.VendorId, observation.ProductId,
            observation.UsagePage, observation.Usage, observation.ReportId);
        _previousBuffers.TryGetValue(stream, out var previous);
        var ranges = new List<ChangedByteRange>();
        int? changedCount = previous is null ? null : 0;
        var truncated = false;
        if (previous is not null)
        {
            var current = observation.Buffer.Span;
            for (var offset = 0; offset < current.Length;)
            {
                if (current[offset] == previous.Bytes[offset])
                {
                    offset++;
                    continue;
                }

                var start = offset;
                while (offset < current.Length && current[offset] != previous.Bytes[offset])
                {
                    offset++;
                    changedCount++;
                }

                if (ranges.Count < EvidenceLimits.MaxPayloadChangedRanges)
                {
                    ranges.Add(new ChangedByteRange(start, offset - start));
                }
                else
                {
                    truncated = true;
                }
            }
        }

        var result = new PayloadComparisonEvidence(token, previous?.OperationId,
            previous?.RelativeMilliseconds, changedCount, ranges.AsReadOnly(), truncated);
        if (previous is not null)
        {
            CryptographicOperations.ZeroMemory(previous.Bytes);
        }
        else if (_previousBuffers.Count >= EvidenceLimits.MaxPayloadComparisonStreams)
        {
            // Evict one prior stream; never retain every opaque observation.
            var oldest = _previousBuffers.First();
            CryptographicOperations.ZeroMemory(oldest.Value.Bytes);
            _previousBuffers.Remove(oldest.Key);
        }

        _previousBuffers[stream] = new PreviousBuffer(observation.Buffer.ToArray(),
            observation.OperationId, observation.RelativeMilliseconds);
        return result;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CryptographicOperations.ZeroMemory(_comparisonKey);
            foreach (var previous in _previousBuffers.Values)
            {
                CryptographicOperations.ZeroMemory(previous.Bytes);
            }

            _previousBuffers.Clear();
        }
    }

    private sealed record ComparisonStream(string Interface, string Api, ObservationSource Source,
        ObservationDirection Direction, int Length, ushort Vendor, ushort Product,
        ushort? UsagePage, ushort? Usage, byte? ReportId);

    private sealed record PreviousBuffer(byte[] Bytes, string OperationId, double RelativeMilliseconds);

    private bool IsProvenFeatureControlScope(ControlObservation observation)
    {
        if (observation.VendorId != 0x1532
            || observation.Source is ObservationSource.UsbTransfer
            || observation.Api is not ("HidD_SetFeature" or "HidD_GetFeature"
                or "DeviceIoControl.HID_SET_FEATURE" or "DeviceIoControl.HID_GET_FEATURE")
            || observation.Source == ObservationSource.ReturnedBuffer
                && (observation.Status != ObservationStatus.Succeeded
                    || observation.Direction != ObservationDirection.Output))
        {
            return false;
        }

        var bareResponse = FeaturePayloadPrivacy.IsCompletedBareFeatureResponse(observation);
        if (observation.Buffer.Length == 90 && !bareResponse)
        {
            return false;
        }

        return Inventory.RazerHidInterfaces.Value.Any(candidate =>
            candidate.InterfaceLabel == observation.InterfaceLabel
            && candidate.VendorId == observation.VendorId
            && candidate.ProductId == observation.ProductId
            && candidate.UsagePage == observation.UsagePage
            && candidate.Usage == observation.Usage
            && candidate.ReportGeometry is not null
            && candidate.ReportGeometry.FeatureBytes == (bareResponse ? 91 : observation.Buffer.Length));
    }
}

public static class EvidenceText
{
    public static bool IsToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > EvidenceLimits.MaxTokenLength)
        {
            return false;
        }

        return value.All(static character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.' or ':');
    }

    public static bool IsDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > EvidenceLimits.MaxTextLength)
        {
            return false;
        }

        return value.All(static character =>
            !char.IsControl(character)
            && character is not '\\' and not '/');
    }
}
