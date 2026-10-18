using System.Text;

namespace SynapseObserver.Core.Tests;

public sealed class RequestedTestPlanEvidenceTests
{
    private static CaptureTestPlan Plan(string json) => CaptureTestPlan.Parse(
        Encoding.UTF8.GetBytes(json), new HashSet<string> { "fans" });

    [Fact]
    public void FocusedReviewCountsOnlyRequestedItemsWhileKeepingGlobalCoverage()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.SetRequestedTestPlan(Plan("{\"schemaVersion\":1,\"activityIds\":[\"fans\"]}"));
        session.SetCoverage(new("fans", "Fans", CoverageState.Pending, CoverageReason.None));
        session.SetCoverage(new("lighting", "Lighting", CoverageState.Pending, CoverageReason.None));
        using var snapshot = session.Snapshot();
        var review = EvidenceCollectionReview.Create(snapshot);
        Assert.Contains(review.Notes, note => note.StartsWith("1 evidence item has not been recorded", StringComparison.Ordinal));
        Assert.DoesNotContain(review.Notes, note => note.StartsWith("No complete setting-change sequence", StringComparison.Ordinal));
        Assert.Equal(2, snapshot.Coverage.Count);
        var quality = ContributionDraftBuilder.CreateQualityReceipt(snapshot);
        Assert.Contains(quality.Gaps, gap => gap.Area == "coverage.fans");
        Assert.DoesNotContain(quality.Gaps, gap => gap.Area == "coverage.lighting");
    }

    [Fact]
    public void KeyOnlyReviewDoesNotAskForUnrequestedRecording()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.SetRequestedTestPlan(Plan("{\"schemaVersion\":1,\"specialKeys\":[\"FnP\"]}"));
        session.SetCoverage(new("fans", "Fans", CoverageState.Pending, CoverageReason.None));
        using var snapshot = session.Snapshot();
        var review = EvidenceCollectionReview.Create(snapshot);
        Assert.Contains(review.Notes, note => note.StartsWith("1 requested key check has not been attempted", StringComparison.Ordinal));
        Assert.DoesNotContain(review.Notes, note => note.Contains("evidence item", StringComparison.Ordinal));
        Assert.DoesNotContain(review.Notes, note => note.StartsWith("No complete setting-change sequence", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(EvidenceMarkerKind.UserActionConfirmed)]
    [InlineData(EvidenceMarkerKind.UserReportedFeatureDefinition)]
    public void PlanLocksAfterAnyUserReportedMarker(EvidenceMarkerKind kind)
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.SetRequestedTestPlan(Plan("{\"schemaVersion\":1,\"activityIds\":[\"fans\"]}"));
        Assert.Equal(EvidenceAddResult.Added, session.AddMarker(new(1, kind, "fans", "User report", EvidenceProvenance.UserReported)));
        Assert.Throws<InvalidOperationException>(() => session.SetRequestedTestPlan(null));
        using var snapshot = session.Snapshot();
        Assert.NotNull(snapshot.RequestedTestPlan);
    }
}
