namespace SynapseObserver.Core.Tests;

public sealed class RecordingObservationLossQualityTests
{
    [Theory]
    [InlineData("Observer queue dropped events")]
    [InlineData("Observer polling failed")]
    [InlineData("Native observer failed")]
    [InlineData("Recording cleanup has a gap")]
    public void ObserverRecordingFailure_ReportsGenericQualityGap(string diagnostic)
    {
        using var session = CreateSession();
        Assert.Equal(EvidenceAddResult.Added, session.AddMarker(new EvidenceMarker(
            10,
            EvidenceMarkerKind.Failure,
            "observer",
            diagnostic,
            EvidenceProvenance.Observer,
            "private-diagnostic-value")));

        var gap = Assert.Single(ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps,
            item => item.Code == "RecordingObservationLoss");

        Assert.Equal("recording.observer", gap.Area);
        Assert.Equal("The observer reported that recording evidence may be incomplete.", gap.Detail);
        Assert.DoesNotContain(diagnostic, gap.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("private-diagnostic-value", gap.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordingFailureDetail_IsIndependentOfArbitraryMarkerDiagnostics()
    {
        using var first = CreateSession();
        using var second = CreateSession();
        Assert.Equal(EvidenceAddResult.Added, first.AddMarker(new EvidenceMarker(
            10,
            EvidenceMarkerKind.Failure,
            "observer",
            "first-private-diagnostic",
            EvidenceProvenance.Observer,
            "first-private-value")));
        Assert.Equal(EvidenceAddResult.Added, second.AddMarker(new EvidenceMarker(
            20,
            EvidenceMarkerKind.Failure,
            "observer",
            "second-private-diagnostic",
            EvidenceProvenance.Observer,
            "second-private-value")));

        var firstGap = Assert.Single(ContributionDraftBuilder.CreateQualityReceipt(first.Snapshot()).Gaps,
            item => item.Code == "RecordingObservationLoss");
        var secondGap = Assert.Single(ContributionDraftBuilder.CreateQualityReceipt(second.Snapshot()).Gaps,
            item => item.Code == "RecordingObservationLoss");

        Assert.Equal(firstGap, secondGap);
    }

    [Fact]
    public void ObserverDiscoveryFailure_DoesNotReportRecordingObservationLoss()
    {
        using var session = CreateSession();
        Assert.Equal(EvidenceAddResult.Added, session.AddMarker(new EvidenceMarker(
            10,
            EvidenceMarkerKind.Failure,
            "observer",
            "Native observer unavailable",
            EvidenceProvenance.Collector)));

        Assert.DoesNotContain(ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps,
            item => item.Code == "RecordingObservationLoss");
    }

    [Fact]
    public void FailedApiCall_DoesNotReportRecordingObservationLoss()
    {
        using var session = CreateSession();
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(CreateObservation(
            ObservationStatus.Failed,
            errorCategory: "access-denied",
            nativeStatus: 5)));

        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;

        Assert.Contains(gaps, item => item.Code == "ObservedApiFailure");
        Assert.DoesNotContain(gaps, item => item.Code == "RecordingObservationLoss");
    }

    [Fact]
    public void PrivacyRedaction_DoesNotReportRecordingObservationLoss()
    {
        using var session = CreateSession();
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(CreateObservation(
            ObservationStatus.Succeeded,
            buffer: [0x53, 0x45, 0x43, 0x52, 0x45, 0x54])));

        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;

        Assert.Contains(gaps, item => item.Code == "PayloadRedacted");
        Assert.DoesNotContain(gaps, item => item.Code == "RecordingObservationLoss");
    }

    [Fact]
    public void UnknownReturnedLength_DoesNotReportRecordingObservationLoss()
    {
        using var session = CreateSession();
        var observation = CreateObservation(
            ObservationStatus.Succeeded,
            source: ObservationSource.ReturnedBuffer,
            direction: ObservationDirection.Output) with
        {
            ReturnedLengthUnknown = true,
        };
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(observation));

        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;

        Assert.Contains(gaps, item => item.Code == "ReturnedLengthNotProvided");
        Assert.DoesNotContain(gaps, item => item.Code == "RecordingObservationLoss");
    }

    private static EvidenceSession CreateSession() =>
        new(SyntheticPreview.CreateInventory(), DateTimeOffset.UnixEpoch);

    private static ControlObservation CreateObservation(
        ObservationStatus status,
        string? errorCategory = null,
        int? nativeStatus = 0,
        byte[]? buffer = null,
        ObservationSource source = ObservationSource.UserModeCall,
        ObservationDirection direction = ObservationDirection.Input) => new(
            "op-quality",
            10,
            source,
            "HidD_GetFeature",
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
            11);
}
