namespace SynapseObserver.Core.Tests;

public sealed class PowerModeCaptureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(128)]
    public void SharedPowerModeByteIsRetainedWithoutClaimingFullCharge(byte value)
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.AddMarker(new(1, EvidenceMarkerKind.UserReportedStartingValue, "fans", "Start", EvidenceProvenance.UserReported, "Automatic"));
        var bytes = FeatureReportTestData.Create(7, 0x0F, [value]);
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(new("power-op", 2,
            ObservationSource.UserModeCall, "HidD_SetFeature", ObservationDirection.Input,
            ObservationStatus.Succeeded, 0, null, 0x1532, 0xFFFF, "hid-preview-01", 1, 2,
            0, bytes, PayloadDisposition.ProvenFeatureControl, 2.1)));
        session.AddMarker(new(3, EvidenceMarkerKind.UserReportedTargetValue, "fans", "Changed", EvidenceProvenance.UserReported, "Maximum"));
        session.AddMarker(new(4, EvidenceMarkerKind.UserActionConfirmed, "fans", "Marked", EvidenceProvenance.UserReported));
        session.AddMarker(new(5, EvidenceMarkerKind.UserReportedRestoration, "fans", "Restored", EvidenceProvenance.UserReported, "Automatic"));
        using var snapshot = session.Snapshot();
        var observation = Assert.Single(snapshot.Observations);
        Assert.Equal(FeaturePayloadProjectionKind.ReviewedPowerModeControl, observation.PayloadProjectionKind);
        Assert.Equal(Convert.ToHexString(bytes), observation.PayloadHex);
        var coverage = ContributionDraftBuilder.CreateCoverageDraft(snapshot).Coverage;
        Assert.Equal(ContributionCoverageStatus.Captured, coverage.Single(item => item.Capability == "power.modeControlReports").Status);
        Assert.Equal(ContributionCoverageStatus.NotInvestigated, coverage.Single(item => item.Capability == "battery.temporaryFullCharge").Status);
        Assert.Equal(ContributionCoverageStatus.NotInvestigated, coverage.Single(item => item.Capability == "fans.maximum").Status);
    }
}
