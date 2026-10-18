namespace SynapseObserver.Core;

/// <summary>Contributor-facing facts about the evidence actually retained.</summary>
public sealed record EvidenceCollectionReview(string Summary, IReadOnlyList<string> Notes)
{
    private static string Count(int count, string singular, string? plural = null) =>
        $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";

    public static EvidenceCollectionReview Create(EvidenceSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var analysis = ContributionDraftBuilder.CreateQualityAnalysis(snapshot);
        return Create(snapshot, analysis.Quality, analysis.Statistics);
    }

    internal static EvidenceCollectionReview Create(
        EvidenceSessionSnapshot snapshot, ContributionQualityReceipt quality, ObservationQualityStatistics statistics)
    {
        var counts = quality.Counts;
        var manualResults = snapshot.PlanCheckReports.GroupBy(report => report.CheckId).Select(group => group.Last()).ToArray();
        var retained = counts.ControlObservations - counts.RedactedControlBodies;
        var summary = $"{Count(counts.CompleteActionWindows, "complete action sequence")} · "
            + $"{Count(snapshot.SpecialKeyProbes.Count, "key check")} · "
            + $"{Count(retained, "complete report body", "complete report bodies")} retained.";
        if (snapshot.Observations.Count == 0 && counts.ActionWindows == 0 && snapshot.SpecialKeyProbes.Count == 0)
            summary = "Device information collected. No Synapse recording or key check yet.";
        if (manualResults.Length > 0) summary += $" {manualResults.Count(report => report.Outcome != PlanCheckOutcome.Started)} owner-reported OpenBlade results.";
        var notes = new List<string>();
        if (snapshot.RequestedTestPlan is { } manualPlan)
        {
            int pendingManual = manualPlan.Checks.Count(check => check.Kind == PlanCheckKind.OpenBlade
                && !manualResults.Any(report => report.CheckId == check.Id && report.Outcome != PlanCheckOutcome.Started));
            if (pendingManual > 0) notes.Add($"{pendingManual} OpenBlade checks have no finished owner report. Maintainer research is not owner testing.");
        }
        if (manualResults.Any(report => report.Outcome == PlanCheckOutcome.Started))
            notes.Add("An OpenBlade check is unfinished. Restore its saved starting settings and save the partial report.");
        if (manualResults.Any(report => report.Restoration == PlanRestoration.Unconfirmed))
            notes.Add("OpenBlade restoration was reported unconfirmed. Stop testing and report the unresolved setting.");
        if (counts.CompleteActionWindows == 0 && (snapshot.RequestedTestPlan is null || counts.ActionWindows > 0))
            notes.Add("No complete setting-change sequence was recorded. Device information is still useful; add a Synapse recording if it is available.");
        var pending = snapshot.Coverage.Count(item => item.State == CoverageState.Pending
            && (snapshot.RequestedTestPlan is null || snapshot.RequestedTestPlan.ActivityIds.Contains(item.ActivityId, StringComparer.Ordinal)));
        if (pending > 0)
            notes.Add($"{Count(pending, "evidence item")} {(pending == 1 ? "has" : "have")} not been recorded. You can save this partial collection.");
        if (snapshot.RequestedTestPlan is { } requested)
        {
            var missingKeys = requested.SpecialKeys.Count(key => !snapshot.SpecialKeyProbes.Any(probe => probe.ExpectedKey == key));
            if (missingKeys > 0) notes.Add($"{Count(missingKeys, "requested key check")} {(missingKeys == 1 ? "has" : "have")} not been attempted. You can save this partial collection.");
        }
        var unchanged = quality.Gaps.Count(item => item.Code == "UnchangedReportedValues");
        if (unchanged > 0)
            notes.Add($"{Count(unchanged, "action sequence")} reported the same starting and changed value. "
                + (unchanged == 1 ? "It is retained but does not establish a setting change."
                    : "They are retained but do not establish setting changes."));
        var incomplete = counts.ActionWindows - counts.CompleteActionWindows - unchanged;
        if (incomplete > 0)
            notes.Add($"{Count(incomplete, "setting-change sequence")} {(incomplete == 1 ? "is" : "are")} unfinished. Restore any changed setting to its exact starting value before leaving Synapse.");
        var omitted = statistics.RedactedReturnedResponses;
        var semantic = statistics.SemanticReturnedResponses;
        if (semantic > 0)
            notes.Add($"{Count(semantic, "returned response summary", "returned response summaries")} retained with opaque and unavailable bytes omitted.");
        if (omitted > 0)
            notes.Add($"{omitted} of {Count(statistics.ReturnedResponses, "returned report body", "returned report bodies")} {(omitted == 1 ? "was" : "were")} omitted. Save this ZIP; the maintainer may need a focused follow-up to understand the responses.");
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
