using System.IO.Compression;
using System.Text;

namespace SynapseObserver.Core.Tests;

public sealed class EvidenceCollectionReviewTests
{
    [Fact]
    public void InventoryOnlyReviewKeepsUnrecordedItemsAndMissingSettingsVisible()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.SetCoverage(new("performance", "Performance mode", CoverageState.Pending, CoverageReason.None));
        var review = EvidenceCollectionReview.Create(session.Snapshot());
        Assert.Contains("No Synapse recording", review.Summary);
        Assert.Contains(review.Notes, note => note.Contains("1 evidence item has not been recorded", StringComparison.Ordinal));
        Assert.Contains(review.Notes, note => note.StartsWith("No complete setting-change sequence", StringComparison.Ordinal));
        Assert.DoesNotContain(review.Notes, note => note.Contains("recording data could not", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpaqueResponsesAreVisibleInReviewAndSavedGuideWithoutBecomingCaptureLoss()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.SetCoverage(new("performance", "Performance mode", CoverageState.Completed, CoverageReason.None));
        session.AddMarker(new(1, EvidenceMarkerKind.UserReportedStartingValue, "performance", "Starting", EvidenceProvenance.UserReported, "Balanced"));
        session.AddObservation(Observation("set", 2, ObservationSource.UserModeCall, ObservationDirection.Input,
            FeatureReportTestData.Create(0x0D, 0x02, [1, 1, 5, 0])));
        session.AddObservation(Observation("get", 3, ObservationSource.ReturnedBuffer, ObservationDirection.Output,
            Enumerable.Repeat((byte)0xFF, 90).ToArray()) with { ReportId = null, ReturnedBytes = 90, OutputBufferCapacity = 91 });
        session.AddMarker(new(4, EvidenceMarkerKind.UserReportedTargetValue, "performance", "Changed", EvidenceProvenance.UserReported, "Silent"));
        session.AddMarker(new(5, EvidenceMarkerKind.UserActionConfirmed, "performance", "Change marked", EvidenceProvenance.UserReported));
        session.AddMarker(new(6, EvidenceMarkerKind.UserReportedRestoration, "performance", "Restored", EvidenceProvenance.UserReported, "Balanced"));
        var snapshot = session.Snapshot();
        var review = EvidenceCollectionReview.Create(snapshot);
        Assert.Contains("1 complete action sequence", review.Summary);
        Assert.Contains("1 complete report body retained", review.Summary);
        Assert.Contains(review.Notes, note => note.StartsWith("1 of 1 returned report body was omitted", StringComparison.Ordinal));
        Assert.DoesNotContain(review.Notes, note => note.Contains("recording data could not", StringComparison.Ordinal));
        var directory = Path.Combine(Path.GetTempPath(), "observer-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "evidence.zip");
            await EvidenceBundleWriter.WriteAsync(session, path);
            using var archive = ZipFile.OpenRead(path);
            using var reader = new StreamReader(archive.GetEntry("README.txt")!.Open(), Encoding.UTF8);
            var guide = await reader.ReadToEndAsync();
            Assert.Contains(review.Summary, guide);
            Assert.All(review.Notes, note => Assert.Contains(note, guide));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void MissingRestorationAndCaptureLossGetSeparateActionableNotes()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.AddMarker(new(1, EvidenceMarkerKind.UserReportedStartingValue, "performance", "Starting", EvidenceProvenance.UserReported, "Balanced"));
        session.AddMarker(new(2, EvidenceMarkerKind.Failure, "observer", "Observer queue dropped events", EvidenceProvenance.Observer, "count-2"));
        var review = EvidenceCollectionReview.Create(session.Snapshot());
        Assert.Contains(review.Notes, note => note.Contains("Restore any changed setting to its exact starting value", StringComparison.Ordinal));
        Assert.Contains(review.Notes, note => note.StartsWith("Some recording data could not be retained", StringComparison.Ordinal));
    }

    [Fact]
    public void RestoredNoOpIsReportedWithoutAnUnfinishedRestorationWarning()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.AddMarker(new(1, EvidenceMarkerKind.UserReportedStartingValue, "performance", "Starting", EvidenceProvenance.UserReported, "Balanced"));
        session.AddMarker(new(2, EvidenceMarkerKind.UserReportedTargetValue, "performance", "Changed", EvidenceProvenance.UserReported, " balanced "));
        session.AddMarker(new(3, EvidenceMarkerKind.UserActionConfirmed, "performance", "Change marked", EvidenceProvenance.UserReported));
        session.AddMarker(new(4, EvidenceMarkerKind.UserReportedRestoration, "performance", "Restored", EvidenceProvenance.UserReported, "Balanced"));

        var review = EvidenceCollectionReview.Create(session.Snapshot());

        Assert.Contains(review.Notes, note => note.Contains("same starting and changed value", StringComparison.Ordinal));
        Assert.DoesNotContain(review.Notes, note => note.Contains("unfinished", StringComparison.Ordinal));
        Assert.DoesNotContain(review.Notes, note => note.Contains("Restore any changed setting", StringComparison.Ordinal));
    }

    [Fact]
    public void CompleteMissingValueSequenceIsNotCalledASettingChange()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.AddMarker(new(1, EvidenceMarkerKind.UserReportedStartingValue, "performance", "Starting", EvidenceProvenance.UserReported));
        session.AddMarker(new(2, EvidenceMarkerKind.UserReportedTargetValue, "performance", "Changed", EvidenceProvenance.UserReported));
        session.AddMarker(new(3, EvidenceMarkerKind.UserActionConfirmed, "performance", "Change marked", EvidenceProvenance.UserReported));
        session.AddMarker(new(4, EvidenceMarkerKind.UserReportedRestoration, "performance", "Restored", EvidenceProvenance.UserReported));

        var review = EvidenceCollectionReview.Create(session.Snapshot());

        Assert.Contains("1 complete action sequence", review.Summary);
        Assert.DoesNotContain("setting change", review.Summary, StringComparison.Ordinal);
    }

    private static ControlObservation Observation(string operation, double time, ObservationSource source,
        ObservationDirection direction, byte[] bytes) => new(operation, time, source,
        direction == ObservationDirection.Output ? "DeviceIoControl.HID_GET_FEATURE" : "HidD_SetFeature",
        direction, ObservationStatus.Succeeded, 0, null, 0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
        bytes, PayloadDisposition.ProvenFeatureControl, time + 0.1);
}
