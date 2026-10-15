namespace SynapseObserver.Core;

public enum FeatureResponseLayout
{
    BareLogicalPacket,
    ReportIdPrefixedPacket,
}

public enum FeatureResponseCompleteness
{
    Complete,
    MissingFinalPadding,
}

public enum FeatureResponseInvariantFailure
{
    UnexpectedStatus,
    UnexpectedReportId,
    ReservedBytesNonzero,
    DataSizeOutOfRange,
    ChecksumMismatch,
    PaddingNonzero,
}

public sealed record FeatureResponseLayoutDiagnostic(
    FeatureResponseLayout Layout,
    FeatureResponseCompleteness Completeness,
    bool MatchesAvailableBytes,
    IReadOnlyList<FeatureResponseInvariantFailure> Failures);

public sealed record FeatureResponseStructureDiagnostics(
    FeatureResponseLayoutDiagnostic BareLogicalPacket,
    FeatureResponseLayoutDiagnostic ReportIdPrefixedPacket);
