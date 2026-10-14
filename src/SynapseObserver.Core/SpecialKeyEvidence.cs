namespace SynapseObserver.Core;

public enum SpecialKeyExpectedKey { M1, M2, M3, M4, M5, Fn, FnP }
public enum SpecialKeyModeContext { Unknown, Normal, Driver }
public enum SpecialKeyObservationOrigin { WindowsRawInput, ReviewedHidSpecialKey, UnknownHidMetadata }
public enum SpecialKeyPhase { Unknown, KeyDown, KeyUp, Repeat }
public enum SpecialKeyProbeEndReason { Timeout, Cancelled, FocusLost, LimitReached, Unavailable, Disposed }

// ExpectedKey and ModeContext are user reports. Observed input does not prove a
// firmware setting, physical key identity, Fn modifier, or support for a write.
public sealed record SpecialKeyObservation(
    double RelativeMilliseconds,
    SpecialKeyExpectedKey ExpectedKey,
    SpecialKeyModeContext ModeContext,
    string InterfaceLabel,
    SpecialKeyObservationOrigin Origin,
    SpecialKeyPhase Phase,
    ushort? VirtualKey = null,
    string? ReviewedSignal = null,
    int? ReportLength = null,
    string? ProbeEqualityLabel = null,
    int? ChangedByteCount = null,
    IReadOnlyList<ChangedByteRange>? ChangedRanges = null)
{
    public string ProbeLabel { get; init; } = "probe-001";
    public ushort? ScanCode { get; init; }
    public bool? IsExtendedKey { get; init; }
    public bool ChangedRangesTruncated { get; init; }
    public EvidenceProvenance ContextProvenance => EvidenceProvenance.UserReported;
    public bool IsFirmwareReadback => false;
}

public sealed record SpecialKeyProbeSummary(
    SpecialKeyExpectedKey ExpectedKey,
    SpecialKeyModeContext ModeContext,
    SpecialKeyProbeEndReason EndReason,
    int ObservationCount,
    int IgnoredTypingCount,
    int RejectedPacketCount,
    string Gap)
{
    public string ProbeLabel { get; init; } = "probe-001";
    public double RelativeMilliseconds { get; init; }
}
