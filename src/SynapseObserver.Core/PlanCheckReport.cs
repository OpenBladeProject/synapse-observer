namespace SynapseObserver.Core;

public enum PlanCheckOutcome { Started, Worked, Problem, Unavailable, Skipped }
public enum PlanRestoration { Pending, Restored, NoChanges, Unconfirmed, NotRequired }

// An owner's report, never a device readback or automatic hardware verdict.
public sealed record PlanCheckReport(
    double RelativeMilliseconds,
    string CheckId,
    PlanCheckOutcome Outcome,
    string Baseline,
    string? Details,
    PlanRestoration Restoration)
{
    public EvidenceProvenance Provenance => EvidenceProvenance.UserReported;
    public bool IsFirmwareReadback => false;
}
