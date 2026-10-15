using System.Text.Json.Serialization;

namespace SynapseObserver.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContributionObservationLayer
{
    UserModeFeatureApi,
    WindowsRawInput,
    WindowsInventory,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContributionCoverageStatus
{
    NotInvestigated,
    Captured,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContributionObservationRole
{
    FeatureSetRequest,
    FeatureQueryRequest,
    FeatureQueryResponse,
    UnclassifiedFeatureCall,
    FeatureSetResponse,
}

public sealed record ContributionEvidenceReference(
    ContributionObservationLayer Layer,
    string ReferenceId,
    string Summary);

public sealed record ContributionCoverageEntry(
    string Capability,
    ContributionCoverageStatus Status,
    IReadOnlyList<ContributionEvidenceReference> Evidence);

public sealed record ContributionDeviceIdentity(
    IReadOnlyList<string> ProductNames,
    IReadOnlyList<string> SystemSkus,
    IReadOnlyList<string> BiosVersions,
    IReadOnlyList<string> VendorIds,
    IReadOnlyList<string> ProductIds);

public sealed record ContributionCoverageDraft(
    string SchemaVersion,
    string SourceSessionSchemaVersion,
    ContributionDeviceIdentity Device,
    IReadOnlyList<ContributionCoverageStatus> StatusValues,
    IReadOnlyList<ContributionCoverageEntry> Coverage,
    string EvidenceBoundary)
{
    public const string CurrentSchemaVersion = "synapse-observer.coverage-draft.v2";
}

public sealed record ContributionMarkerDraft(
    double RelativeMilliseconds,
    EvidenceMarkerKind Kind,
    EvidenceProvenance Provenance,
    string Label,
    string? Value)
{
    public PowerSourceKind? PowerSourceAtMarker { get; init; }
}

public sealed record ContributionObservationDraft(
    ContributionObservationLayer Layer,
    string ReferenceId,
    string OperationId,
    ContributionObservationRole Role,
    double RelativeMilliseconds,
    string Api,
    ObservationDirection Direction,
    ObservationStatus Status,
    FeaturePayloadProjectionKind ProjectionKind,
    PayloadDisposition PayloadDisposition,
    bool CompletePayloadRetained,
    bool FirmwareReadback)
{
    public ReviewedFeatureResponseProjection? SemanticResponse { get; init; }

    public string? CorrelatedRequestReferenceId { get; init; }
}

public sealed record ContributionActionWindowDraft(
    string ActivityId,
    string Label,
    double StartRelativeMilliseconds,
    double EndRelativeMilliseconds,
    bool CompleteUserReportedSequence,
    IReadOnlyList<ContributionMarkerDraft> Markers,
    IReadOnlyList<ContributionObservationDraft> Observations,
    string? Gap)
{
    public PowerSourceKind PowerSourceAtAction { get; init; } = PowerSourceKind.Unknown;

    public bool UnchangedReportedValues { get; init; }
}

public sealed record ContributionSpecialKeyDraft(
    ContributionObservationLayer Layer,
    SpecialKeyObservation Observation);

public sealed record ContributionContextMarkerDraft(
    string ActivityId,
    ContributionMarkerDraft Marker);

public sealed record ContributionAnnotationDraft(
    string SchemaVersion,
    string SourceSessionSchemaVersion,
    IReadOnlyList<ContributionObservationLayer> ObservationLayers,
    IReadOnlyList<ContributionActionWindowDraft> ActionWindows,
    IReadOnlyList<ContributionSpecialKeyDraft> SpecialKeyObservations,
    IReadOnlyList<SpecialKeyProbeSummary> SpecialKeyProbes,
    string EvidenceBoundary)
{
    public const string CurrentSchemaVersion = "synapse-observer.annotation-draft.v2";

    public IReadOnlyList<ContributionContextMarkerDraft> ContextMarkers { get; init; } = [];
}

public sealed record ContributionQualityCounts(
    int InventorySectionsWithGaps,
    int CoverageLeaves,
    int CapturedCoverageLeaves,
    int NotInvestigatedCoverageLeaves,
    int ActionWindows,
    int CompleteActionWindows,
    int ControlObservations,
    int ReviewedControlReports,
    int RedactedControlBodies,
    int UnassociatedControlObservations,
    int SpecialKeyObservations,
    int RejectedRecords)
{
    public int ReviewedSemanticOnlyResponses { get; init; }
}

public sealed record ContributionQualityGap(
    string Area,
    string Code,
    string Detail);

public sealed record ContributionQualityReceipt(
    string SchemaVersion,
    string SourceSessionSchemaVersion,
    ContributionQualityCounts Counts,
    IReadOnlyList<ContributionQualityGap> Gaps,
    IReadOnlyList<string> RequiredMaintainerStages,
    bool ClaimsQueryValidation,
    bool ClaimsSetterValidation,
    bool ClaimsProductionAdmission)
{
    public const string CurrentSchemaVersion = "synapse-observer.quality-receipt.v2";
}
