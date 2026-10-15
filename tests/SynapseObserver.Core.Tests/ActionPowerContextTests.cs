namespace SynapseObserver.Core.Tests;

public sealed class ActionPowerContextTests
{
    [Fact]
    public void LaterBatteryActionDoesNotInheritStartupExternalPower()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        AddAction(session, 1, PowerSourceKind.Battery, PowerSourceKind.Battery);
        var draft = ContributionDraftBuilder.CreateCoverageDraft(session.Snapshot());
        Assert.Equal(ContributionCoverageStatus.Captured, Leaf(draft, "performance.battery.presets"));
        Assert.Equal(ContributionCoverageStatus.NotInvestigated, Leaf(draft, "performance.externalPower.presets"));
        Assert.Equal(ContributionCoverageStatus.Captured, Leaf(draft, "power.externalPower"));
        Assert.Equal(ContributionCoverageStatus.NotInvestigated, Leaf(draft, "power.fullAc"));
        Assert.Equal(ContributionCoverageStatus.NotInvestigated, Leaf(draft, "power.usbCPowerClasses"));
    }

    [Fact]
    public void ConsecutiveActionsKeepTheirOwnSampledPowerContexts()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        AddAction(session, 1, PowerSourceKind.Ac, PowerSourceKind.Ac);
        AddAction(session, 10, PowerSourceKind.Battery, PowerSourceKind.Battery);
        var snapshot = session.Snapshot();
        var windows = ContributionDraftBuilder.CreateAnnotationDraft(snapshot).ActionWindows;
        Assert.Equal(new[] { PowerSourceKind.Ac, PowerSourceKind.Battery }, windows.Select(window => window.PowerSourceAtAction));
        var draft = ContributionDraftBuilder.CreateCoverageDraft(snapshot);
        Assert.Equal(ContributionCoverageStatus.Captured, Leaf(draft, "performance.externalPower.presets"));
        Assert.Equal(ContributionCoverageStatus.Captured, Leaf(draft, "performance.battery.presets"));
    }

    [Theory]
    [InlineData(PowerSourceKind.Ac, PowerSourceKind.Battery)]
    [InlineData(null, null)]
    [InlineData(PowerSourceKind.Unknown, PowerSourceKind.Unknown)]
    public void ChangedMissingOrUnknownSamplesDoNotClaimPowerSpecificPerformance(PowerSourceKind? start, PowerSourceKind? end)
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        AddAction(session, 1, start, end);
        var snapshot = session.Snapshot();
        var window = Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(snapshot).ActionWindows);
        Assert.True(window.CompleteUserReportedSequence);
        Assert.Equal(PowerSourceKind.Unknown, window.PowerSourceAtAction);
        var draft = ContributionDraftBuilder.CreateCoverageDraft(snapshot);
        Assert.Equal(ContributionCoverageStatus.NotInvestigated, Leaf(draft, "performance.externalPower.presets"));
        Assert.Equal(ContributionCoverageStatus.NotInvestigated, Leaf(draft, "performance.battery.presets"));
        Assert.Contains(ContributionDraftBuilder.CreateQualityReceipt(snapshot).Gaps, gap => gap.Code == "ActionPowerContextUnknown");
    }

    [Fact]
    public void IndependentIdentityMismatchRemainsVisibleWithoutChoosingAProduct()
    {
        var inventory = SyntheticPreview.CreateInventory();
        var first = inventory.MachineIdentity.Value[0] with { Source = MachineIdentitySource.BiosRegistry, RegistryView = "Registry64", ProductName = "Blade A" };
        var second = first with { Source = MachineIdentitySource.SystemInformationRegistry, ProductName = "Blade B" };
        using var session = new EvidenceSession(inventory with { MachineIdentity = SectionResult<IReadOnlyList<MachineIdentity>>.Collected([first, second]) });
        Assert.Contains(ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps, gap => gap.Code == "IdentitySourcesDisagree");
        Assert.Equal(new[] { "Blade A", "Blade B" }, ContributionDraftBuilder.CreateCoverageDraft(session.Snapshot()).Device.ProductNames);
    }

    private static ContributionCoverageStatus Leaf(ContributionCoverageDraft draft, string path) =>
        Assert.Single(draft.Coverage, item => item.Capability == path).Status;

    private static void AddAction(EvidenceSession session, double start, PowerSourceKind? before, PowerSourceKind? after)
    {
        session.AddMarker(new(start, EvidenceMarkerKind.UserReportedStartingValue, "performance", "Starting", EvidenceProvenance.UserReported, "Balanced") { PowerSourceAtMarker = before });
        session.AddObservation(new("set-" + start, start + 0.5, ObservationSource.UserModeCall, "HidD_SetFeature", ObservationDirection.Input,
            ObservationStatus.Succeeded, 0, null, 0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0,
            FeatureReportTestData.Create(0x0D, 0x02, [1, 1, 5, 0]), PayloadDisposition.ProvenFeatureControl, start + 0.6));
        session.AddMarker(new(start + 1, EvidenceMarkerKind.UserReportedTargetValue, "performance", "Changed", EvidenceProvenance.UserReported, "Silent") { PowerSourceAtMarker = after });
        session.AddMarker(new(start + 2, EvidenceMarkerKind.UserActionConfirmed, "performance", "Marked", EvidenceProvenance.UserReported) { PowerSourceAtMarker = after });
        session.AddMarker(new(start + 3, EvidenceMarkerKind.UserReportedRestoration, "performance", "Restored", EvidenceProvenance.UserReported, "Balanced") { PowerSourceAtMarker = after });
    }
}
