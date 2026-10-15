namespace SynapseObserver.Core;

/// <summary>Contributor-facing facts about the evidence actually retained.</summary>
public sealed record EvidenceCollectionReview(string Summary, IReadOnlyList<string> Notes)
{
    private static string Count(int count, string singular, string? plural = null) =>
        $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";

    public static EvidenceCollectionReview Create(EvidenceSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Create(snapshot, ContributionDraftBuilder.CreateQualityReceipt(snapshot));
    }

    internal static EvidenceCollectionReview Create(
        EvidenceSessionSnapshot snapshot, ContributionQualityReceipt quality)
    {
        var counts = quality.Counts;
        var retained = snapshot.Observations.Count(item => item.CompletePayloadRetained);
        var summary = $"{Count(counts.CompleteActionWindows, "complete action sequence")} · "
            + $"{Count(snapshot.SpecialKeyProbes.Count, "key check")} · "
            + $"{Count(retained, "complete report body", "complete report bodies")} retained.";
        if (snapshot.Observations.Count == 0 && counts.ActionWindows == 0 && snapshot.SpecialKeyProbes.Count == 0)
            summary = "Device information collected. No Synapse recording or key check yet.";
        var notes = new List<string>();
        if (counts.CompleteActionWindows == 0)
            notes.Add("No complete setting-change sequence was recorded. Device information is still useful; add a Synapse recording if it is available.");
        var pending = snapshot.Coverage.Count(item => item.State == CoverageState.Pending);
        if (pending > 0)
            notes.Add($"{Count(pending, "evidence item")} {(pending == 1 ? "has" : "have")} not been recorded. You can save this partial collection.");
        var unchanged = quality.Gaps.Count(item => item.Code == "UnchangedReportedValues");
        if (unchanged > 0)
            notes.Add($"{Count(unchanged, "action sequence")} reported the same starting and changed value. "
                + (unchanged == 1 ? "It is retained but does not establish a setting change."
                    : "They are retained but do not establish setting changes."));
        var incomplete = counts.ActionWindows - counts.CompleteActionWindows - unchanged;
        if (incomplete > 0)
            notes.Add($"{Count(incomplete, "setting-change sequence")} {(incomplete == 1 ? "is" : "are")} unfinished. Restore any changed setting to its exact starting value before leaving Synapse.");
        var responses = snapshot.Observations.Where(item => item.Source == ObservationSource.ReturnedBuffer);
        var omitted = responses.Count(item => !item.CompletePayloadRetained);
        var semantic = responses.Count(item => item.SemanticResponse is not null);
        if (semantic > 0)
            notes.Add($"{Count(semantic, "returned response summary", "returned response summaries")} retained with opaque and unavailable bytes omitted.");
        if (omitted > 0)
            notes.Add($"{omitted} of {Count(responses.Count(), "returned report body", "returned report bodies")} {(omitted == 1 ? "was" : "were")} omitted. Save this ZIP; the maintainer may need a focused follow-up to understand the responses.");
        if (quality.Gaps.Any(item => item.Code is "RecordingObservationLoss" or "CapturePayloadLoss" or "EvidenceStorageFailure") || counts.RejectedRecords > 0)
            notes.Add("Some recording data could not be retained. Save the partial evidence so the maintainer can see the gap.");
        if (quality.Gaps.Any(item => item.Code == "ObservedApiFailure"))
            notes.Add("Synapse reported a failed device call. Its status is recorded separately from collection loss.");
        if (counts.InventorySectionsWithGaps > 0)
            notes.Add("Some device information was partial or unavailable. Those gaps are included for the maintainer.");
        notes.Add("Recorded selections and successful calls do not confirm firmware readback or complete laptop support.");
        return new(summary, notes.AsReadOnly());
    }
}
