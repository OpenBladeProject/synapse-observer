namespace SynapseObserver.Core.Tests;

public sealed class ColorProfileCoverageTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData(DisplayColorProfileSubtype.StandardSdr, true, false)]
    [InlineData(DisplayColorProfileSubtype.ExtendedHdr, false, true)]
    public void OnlyObservedDefaultSubtypeAdvancesProfileCoverage(
        DisplayColorProfileSubtype? subtype, bool expectedSdr, bool expectedHdr)
    {
        var profiles = new DisplayColorProfileInventoryEvidence(
            DisplayColorProfileScope.CurrentUser, CollectionState.Collected,
            [new(DisplayColorProfileScope.CurrentUser, CollectionState.Collected,
                [new("profile-safe", null, new string('A', 64), true,
                    subtype is { } known ? [known] : [])], false, null)], null);
        var inventory = SyntheticPreview.CreateInventory();
        using var session = new EvidenceSession(inventory with
        {
            Support = inventory.Support with
            {
                Displays = SectionResult<IReadOnlyList<DisplayEvidence>>.Collected(
                    [new("display-safe", "Panel", true, null, []) { ColorProfiles = profiles }]),
            },
        });
        var coverage = ContributionDraftBuilder.CreateCoverageDraft(session.Snapshot()).Coverage;
        Assert.Equal(expectedSdr, coverage.Single(item => item.Capability == "display.colorProfilesSdr").Status == ContributionCoverageStatus.Captured);
        Assert.Equal(expectedHdr, coverage.Single(item => item.Capability == "display.colorProfilesHdr").Status == ContributionCoverageStatus.Captured);
    }
}
