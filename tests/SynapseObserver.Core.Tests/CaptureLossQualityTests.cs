using System.IO.Compression;
using System.Text.Json;

namespace SynapseObserver.Core.Tests;

public sealed class CaptureLossQualityTests
{
    [Theory]
    [InlineData("unreadable-buffer")]
    [InlineData("buffer-exceeds-limit")]
    public void SuccessfulNativeCopyGap_ReportsSpecificAndGenericQualityGaps(string errorCategory)
    {
        using var session = SyntheticPreview.CreateSession();

        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(CreateObservation(
            ObservationStatus.Succeeded,
            errorCategory,
            nativeStatus: 299)));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(ObservationStatus.Succeeded, stored.Status);
        Assert.Equal(299, stored.NativeStatus);
        Assert.Equal(errorCategory, stored.ErrorCategory);
        Assert.Equal(PayloadDisposition.RedactedUnproven, stored.PayloadDisposition);
        Assert.Null(stored.PayloadHex);

        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;
        Assert.Contains(gaps, item => item.Code == "CapturePayloadLoss");
        Assert.Contains(gaps, item => item.Code == "PayloadRedacted");
        Assert.DoesNotContain(gaps, item => item.Code == "ObservedApiFailure");
    }

    [Fact]
    public void PrivacyOnlyRedaction_DoesNotReportCapturePayloadLoss()
    {
        using var session = SyntheticPreview.CreateSession();
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(CreateObservation(
            ObservationStatus.Succeeded,
            errorCategory: null,
            buffer: [0x53, 0x45, 0x43, 0x52, 0x45, 0x54])));

        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;
        Assert.Contains(gaps, item => item.Code == "PayloadRedacted");
        Assert.DoesNotContain(gaps, item => item.Code == "CapturePayloadLoss");
    }

    [Theory]
    [InlineData("access-denied")]
    [InlineData("unknown-token")]
    [InlineData("queue-full")]
    public void OtherNativeErrors_DoNotImplyCapturePayloadLoss(string errorCategory)
    {
        using var session = SyntheticPreview.CreateSession();
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(CreateObservation(
            ObservationStatus.Failed,
            errorCategory,
            nativeStatus: 5)));

        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;
        Assert.Contains(gaps, item => item.Code == "ObservedApiFailure");
        Assert.DoesNotContain(gaps, item => item.Code == "CapturePayloadLoss");
    }

    [Fact]
    public void FailedCallWithNativeCopyGap_ReportsBothFailures()
    {
        using var session = SyntheticPreview.CreateSession();
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(CreateObservation(
            ObservationStatus.Failed,
            "unreadable-buffer",
            nativeStatus: 299)));

        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;
        Assert.Contains(gaps, item => item.Code == "ObservedApiFailure");
        Assert.Contains(gaps, item => item.Code == "CapturePayloadLoss");
    }

    [Theory]
    [InlineData(ObservationStatus.Pending, "pending-completion-not-observed")]
    [InlineData(ObservationStatus.Cancelled, null)]
    public void IncompleteCompletion_DoesNotReportCapturePayloadLoss(
        ObservationStatus status,
        string? errorCategory)
    {
        using var session = SyntheticPreview.CreateSession();
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(CreateObservation(
            status,
            errorCategory,
            nativeStatus: null,
            completedRelativeMilliseconds: null)));

        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;
        Assert.Contains(gaps, item => item.Code == "CompletionNotObserved");
        Assert.DoesNotContain(gaps, item => item.Code == "CapturePayloadLoss");
    }

    [Fact]
    public void ReturnedLengthUnknown_DoesNotReportCapturePayloadLoss()
    {
        using var session = SyntheticPreview.CreateSession();
        var observation = CreateObservation(
            ObservationStatus.Succeeded,
            errorCategory: null,
            source: ObservationSource.ReturnedBuffer,
            api: "HidD_GetFeature",
            direction: ObservationDirection.Output) with
        {
            ReturnedLengthUnknown = true,
        };
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(observation));

        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;
        Assert.Contains(gaps, item => item.Code == "ReturnedLengthNotProvided");
        Assert.DoesNotContain(gaps, item => item.Code == "CapturePayloadLoss");
    }

    [Fact]
    public async Task Export_PreservesCapturePayloadLossQualityCode()
    {
        using var session = SyntheticPreview.CreateSession();
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(CreateObservation(
            ObservationStatus.Succeeded,
            "buffer-exceeds-limit")));
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-capture-loss-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "evidence.zip");
            await EvidenceBundleWriter.WriteAsync(session, destination);

            using var archive = ZipFile.OpenRead(destination);
            var entry = Assert.Single(archive.Entries, item => item.FullName == "quality-receipt.json");
            await using var stream = entry.Open();
            using var receipt = await JsonDocument.ParseAsync(stream);
            Assert.Contains(receipt.RootElement.GetProperty("gaps").EnumerateArray(), item =>
                item.GetProperty("code").GetString() == "CapturePayloadLoss");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ControlObservation CreateObservation(
        ObservationStatus status,
        string? errorCategory,
        int? nativeStatus = 0,
        byte[]? buffer = null,
        double? completedRelativeMilliseconds = 11,
        ObservationSource source = ObservationSource.UserModeCall,
        string api = "HidD_GetFeature",
        ObservationDirection direction = ObservationDirection.Input) => new(
            "op-capture-loss",
            10,
            source,
            api,
            direction,
            status,
            nativeStatus,
            errorCategory,
            0x1532,
            0xFFFF,
            "hid-preview-01",
            1,
            2,
            0,
            buffer ?? [],
            PayloadDisposition.RedactedUnproven,
            completedRelativeMilliseconds);
}
