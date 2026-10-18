using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SynapseObserver.Core.Tests;

public sealed class PlanCheckReportTests
{
    private static CaptureTestPlan Plan() => CaptureTestPlan.Parse(Encoding.UTF8.GetBytes("""
        {"schemaVersion":2,"checks":[
        {"id":"owner","kind":"openblade","title":"Owner check","steps":["Use the available control."],"expected":"It changes.","requiresRestoration":true,"restore":"Restore the original value."},
        {"id":"research","kind":"research","title":"Research","steps":["Review the interface."],"expected":"A reviewed contract.","requiresRestoration":false}],
        "covered":[{"id":"prior","summary":"Already captured."}]}
        """), new HashSet<string>());
    private static PlanCheckReport Start() => new(1, "owner", PlanCheckOutcome.Started, "Original settings", null, PlanRestoration.Pending);
    private static PlanCheckReport Finish() => new(2, "owner", PlanCheckOutcome.Worked, "Original settings", "Visible change", PlanRestoration.Restored);

    [Fact]
    public void ReportsRequireAnOrderedStartAndUnchangedBaseline()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.SetRequestedTestPlan(Plan());
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddPlanCheckReport(Finish()));
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddPlanCheckReport(Start() with { CheckId = "research" }));
        Assert.Equal(EvidenceAddResult.Added, session.AddPlanCheckReport(Start()));
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddPlanCheckReport(Start()));
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddPlanCheckReport(Finish() with { Baseline = "Changed baseline" }));
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddPlanCheckReport(Finish() with { Details = @"C:/Users/private" }));
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddPlanCheckReport(Finish() with { RelativeMilliseconds = 0 }));
        Assert.Equal(EvidenceAddResult.Added, session.AddPlanCheckReport(Finish()));
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddPlanCheckReport(Finish()));
    }

    [Fact]
    public void UnconfirmedRestorationCannotBeReportedAsWorked()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.SetRequestedTestPlan(Plan());
        session.AddPlanCheckReport(Start());
        Assert.Contains(EvidenceCollectionReview.Create(session.Snapshot()).Notes, note => note.Contains("unfinished"));
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddPlanCheckReport(Finish() with { Restoration = PlanRestoration.Unconfirmed }));
        Assert.Equal(EvidenceAddResult.Added, session.AddPlanCheckReport(Finish() with { Outcome = PlanCheckOutcome.Problem, Restoration = PlanRestoration.Unconfirmed }));
        var review = EvidenceCollectionReview.Create(session.Snapshot());
        Assert.Contains(review.Notes, note => note.Contains("Stop testing"));
        Assert.DoesNotContain(review.Notes, note => note.Contains("no finished owner report"));
    }

    [Fact]
    public async Task ExportRoundTripsV2AndKeepsReportsSeparateFromFirmwareEvidence()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.SetRequestedTestPlan(Plan());
        session.AddPlanCheckReport(Start());
        session.AddPlanCheckReport(Finish());
        var destination = Path.Combine(Path.GetTempPath(), $"observer-plan-{Guid.NewGuid():N}.zip");
        try
        {
            await EvidenceBundleWriter.WriteAsync(session, destination);
            using var zip = ZipFile.OpenRead(destination);
            using var planStream = zip.GetEntry("test-plan.json")!.Open();
            using var buffer = new MemoryStream();
            await planStream.CopyToAsync(buffer);
            var plan = CaptureTestPlan.Parse(buffer.ToArray(), new HashSet<string>());
            Assert.Equal(2, plan.FormatVersion);
            Assert.Equal(2, plan.Checks.Count);
            Assert.Single(plan.Covered);
            using var reportStream = zip.GetEntry("openblade-checks.json")!.Open();
            using var reports = await JsonDocument.ParseAsync(reportStream);
            Assert.Equal("userReported", reports.RootElement.GetProperty("provenance").GetString());
            Assert.False(reports.RootElement.GetProperty("isFirmwareReadback").GetBoolean());
            Assert.Equal(2, reports.RootElement.GetProperty("reports").GetArrayLength());
            Assert.All(reports.RootElement.GetProperty("reports").EnumerateArray(), report => Assert.False(report.GetProperty("isFirmwareReadback").GetBoolean()));
        }
        finally { File.Delete(destination); }
    }
}
