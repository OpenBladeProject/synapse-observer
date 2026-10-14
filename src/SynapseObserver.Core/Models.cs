using System.Collections.ObjectModel;

namespace SynapseObserver.Core;

public enum CollectionState
{
    Collected,
    Absent,
    Partial,
    Unavailable,
    Skipped,
    Cancelled,
}

public sealed record SectionResult<T>(CollectionState State, T Value, string? Issue = null)
{
    public static SectionResult<T> Collected(T value) => new(CollectionState.Collected, value);

    public static SectionResult<T> Absent(T value) => new(CollectionState.Absent, value);

    public static SectionResult<T> Partial(T value, string issue) => new(CollectionState.Partial, value, issue);

    public static SectionResult<T> Unavailable(T value, string issue) => new(CollectionState.Unavailable, value, issue);

    public static SectionResult<T> Cancelled(T value) => new(CollectionState.Cancelled, value, "Collection was cancelled before this section completed.");
}

public sealed record MachineIdentity(
    string RegistryView,
    string? Manufacturer,
    string? ProductName,
    string? SystemSku,
    string? BiosVersion,
    string? BiosReleaseDate);

public enum PowerSourceKind
{
    Ac,
    Battery,
    Unknown,
}

public sealed record PowerSourceEvidence(
    PowerSourceKind Source,
    int? BatteryPercent,
    bool? BatteryPresent);

public sealed record HidReportGeometry(
    ushort InputBytes,
    ushort OutputBytes,
    ushort FeatureBytes);

public sealed record HidInterfaceEvidence(
    string InterfaceLabel,
    ushort VendorId,
    ushort ProductId,
    ushort? VersionNumber,
    ushort? UsagePage,
    ushort? Usage,
    HidReportGeometry? ReportGeometry,
    CollectionState DescriptorState,
    string? Issue);

// InterfacePath and parent instance ID may contain machine-specific identifiers. Keep this
// object in memory only and use InterfaceLabel in every exported record. EvidenceBundleWriter
// never serializes this type.
public sealed record LocalHidCandidate(
    string InterfacePath,
    string InterfaceLabel,
    string? ProductName,
    ushort VendorId,
    ushort ProductId,
    ushort? VersionNumber,
    ushort? UsagePage,
    ushort? Usage,
    HidReportGeometry? ReportGeometry,
    CollectionState DescriptorState,
    string? Issue,
    string? ParentDeviceInstanceId,
    string? ParentBaseHardwareIdentity,
    string? ParentFriendlyName,
    string? UsbParentDeviceInstanceId,
    string? UsbParentBaseHardwareIdentity,
    string? UsbParentProductName);

public sealed record SynapseModuleEvidence(
    string FileName,
    string? FileVersion,
    string? Sha256,
    CollectionState State,
    string? Issue);

public sealed record SynapseProcessEvidence(
    string ProcessName,
    string ProcessLabel,
    string? ProductVersion,
    IReadOnlyList<SynapseModuleEvidence> Modules,
    CollectionState ModuleState,
    string? Issue);

public sealed record InventorySnapshot(
    string SchemaVersion,
    DateTimeOffset CollectedAtUtc,
    SectionResult<IReadOnlyList<MachineIdentity>> MachineIdentity,
    SectionResult<PowerSourceEvidence?> PowerSource,
    SectionResult<IReadOnlyList<HidInterfaceEvidence>> RazerHidInterfaces,
    SectionResult<IReadOnlyList<SynapseProcessEvidence>> SynapseProcesses)
{
    public const string CurrentSchemaVersion = "synapse-observer.inventory.v1";
}

public enum EvidenceMarkerKind
{
    SessionStarted,
    RecordingStarted,
    RecordingStopped,
    UserActionConfirmed,
    UserReportedStartingValue,
    UserReportedTargetValue,
    UserReportedRestoration,
    ItemSkipped,
    Failure,
    CancellationRequested,
}

public enum EvidenceProvenance
{
    Collector,
    Observer,
    UserReported,
}

public sealed record EvidenceMarker(
    double RelativeMilliseconds,
    EvidenceMarkerKind Kind,
    string ActivityId,
    string Label,
    EvidenceProvenance Provenance,
    string? Value = null);

public enum ObservationSource
{
    UserModeCall,
    ReturnedBuffer,
    UsbTransfer,
}

public enum ObservationDirection
{
    Input,
    Output,
}

public enum ObservationStatus
{
    Pending,
    Succeeded,
    Failed,
    Cancelled,
}

public enum PayloadDisposition
{
    ProvenFeatureControl,
    RedactedUnproven,
    ExcludedSensitive,
    RedactedOverLimit,
}

public sealed record ControlObservation(
    string OperationId,
    double RelativeMilliseconds,
    ObservationSource Source,
    string Api,
    ObservationDirection Direction,
    ObservationStatus Status,
    int? NativeStatus,
    string? ErrorCategory,
    ushort VendorId,
    ushort ProductId,
    string InterfaceLabel,
    ushort? UsagePage,
    ushort? Usage,
    byte? ReportId,
    ReadOnlyMemory<byte> Buffer,
    PayloadDisposition PayloadDisposition,
    double? CompletedRelativeMilliseconds = null);

public enum FeaturePayloadProjectionKind
{
    MetadataOnly,
    ReviewedThermalMode,
    ReviewedFanSpeed,
    ReviewedFanRpm,
    ReviewedBatterySetting,
}

public sealed record FeatureReportHeaderHints(
    byte? FeatureReportId,
    byte Status,
    byte TransactionId,
    byte DataSize,
    byte CommandClass,
    byte CommandId,
    bool ReservedBytesZero,
    bool ChecksumValid,
    bool PaddingZero);

public sealed record FeaturePayloadProjection(
    FeaturePayloadProjectionKind Kind,
    FeatureReportHeaderHints? HeaderHints,
    string? PayloadHex)
{
    public bool IncludesPayload => PayloadHex is not null;
}

public sealed record SanitizedControlObservation(
    string OperationId,
    double RelativeMilliseconds,
    ObservationSource Source,
    string Api,
    ObservationDirection Direction,
    ObservationStatus Status,
    int? NativeStatus,
    string? ErrorCategory,
    ushort VendorId,
    ushort ProductId,
    string InterfaceLabel,
    ushort? UsagePage,
    ushort? Usage,
    byte? ReportId,
    int BufferLength,
    PayloadDisposition PayloadDisposition,
    string? PayloadHex,
    double? CompletedRelativeMilliseconds = null)
{
    public FeaturePayloadProjectionKind PayloadProjectionKind { get; init; }

    public FeatureReportHeaderHints? HeaderHints { get; init; }
}

public enum CoverageState
{
    Pending,
    Completed,
    CompletedWithGaps,
    Skipped,
    Unavailable,
    Failed,
    Cancelled,
}

public enum CoverageReason
{
    None,
    UserSkipped,
    ObserverUnavailable,
    CaptureFailed,
    Cancelled,
    NoMatchingEvents,
    IncompleteRestoration,
}

public sealed record CoverageItem(
    string ActivityId,
    string Label,
    CoverageState State,
    CoverageReason Reason);

public enum EvidenceAddResult
{
    Added,
    AddedPayloadRedacted,
    RejectedInvalid,
    RejectedLimit,
    RejectedPayloadPolicy,
}

public sealed record EvidenceSessionSnapshot(
    string SchemaVersion,
    DateTimeOffset StartedAtUtc,
    InventorySnapshot Inventory,
    IReadOnlyList<EvidenceMarker> Markers,
    IReadOnlyList<SanitizedControlObservation> Observations,
    IReadOnlyList<CoverageItem> Coverage,
    int RejectedMarkerCount,
    int RejectedObservationCount,
    int RejectedCoverageCount)
{
    public const string CurrentSchemaVersion = "synapse-observer.session.v1";

    internal static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToArray());
}

public sealed record CollectorBuildEvidence(
    string AssemblyName,
    string InformationalVersion,
    string AssemblyVersion);
