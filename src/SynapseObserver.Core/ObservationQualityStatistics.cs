namespace SynapseObserver.Core;

// In-process aggregate shared by quality JSON and contributor review text.
// Keeping it separate leaves the public evidence schemas unchanged.
internal sealed class ObservationQualityStatistics
{
    internal int Reviewed { get; private set; }
    internal int Redacted { get; private set; }
    internal int Unassociated { get; private set; }
    internal int SemanticOnly { get; private set; }
    internal int ReturnedResponses { get; private set; }
    internal int RedactedReturnedResponses { get; private set; }
    internal int SemanticReturnedResponses { get; private set; }
    internal bool CapturePayloadLoss { get; private set; }
    internal bool ReturnedLengthUnknown { get; private set; }
    internal bool ApiFailure { get; private set; }
    internal bool IncompleteCompletion { get; private set; }

    internal static ObservationQualityStatistics Collect(
        IEnumerable<SanitizedControlObservation> observations,
        Func<SanitizedControlObservation, bool> associated)
    {
        var result = new ObservationQualityStatistics();
        foreach (var item in observations)
        {
            if (item.PayloadProjectionKind != FeaturePayloadProjectionKind.MetadataOnly) result.Reviewed++;
            if (!item.CompletePayloadRetained) result.Redacted++;
            if (!associated(item)) result.Unassociated++;
            if (item.SemanticResponse is not null && !item.CompletePayloadRetained) result.SemanticOnly++;
            if (item.Source == ObservationSource.ReturnedBuffer)
            {
                result.ReturnedResponses++;
                if (!item.CompletePayloadRetained) result.RedactedReturnedResponses++;
                if (item.SemanticResponse is not null) result.SemanticReturnedResponses++;
            }
            result.CapturePayloadLoss |= item.ErrorCategory is "unreadable-buffer" or "buffer-exceeds-limit";
            result.ReturnedLengthUnknown |= item.ReturnedLengthUnknown;
            result.ApiFailure |= item.Status == ObservationStatus.Failed;
            result.IncompleteCompletion |= item.Status is ObservationStatus.Pending or ObservationStatus.Cancelled;
        }
        return result;
    }
}
