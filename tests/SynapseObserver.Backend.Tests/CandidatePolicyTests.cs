using SynapseObserver.Core;
using Xunit;
namespace SynapseObserver.Backend.Tests;
public sealed class CandidatePolicyTests
{
    private static LocalHidCandidate Candidate(string child = "MI_02", string root = "USB-parent-A", string product = "Razer Blade New Model") =>
        new("local-only", child, product, 0x1532, 0xFFFF, 1, 1, 2, new(0, 0, 37), CollectionState.Collected, null,
            child, "USB\\VID_1532&PID_FFFF&" + child, "USB Input Device", root, "USB\\VID_1532&PID_FFFF", product);
    [Fact] public void UnknownPidAndGenericUsageAndGeometryCanBeEligible() => Assert.True(BackendDiscovery.IsEligibleControlCandidate(Candidate()));
    [Fact] public void DifferentMiChildrenOfOnePhysicalLaptopAreAdmitted() => Assert.True(BackendDiscovery.IsSamePhysicalLaptop(new[] { Candidate("MI_01"), Candidate("MI_02") }));
    [Fact] public void SameModelOnDifferentUsbRootsIsNotOneDevice() => Assert.False(BackendDiscovery.IsSamePhysicalLaptop(new[] { Candidate(), Candidate(root: "USB-parent-B") }));
    [Fact] public void ExternalRazerPeripheralNameIsExcluded() => Assert.False(BackendDiscovery.IsEligibleControlCandidate(Candidate(product: "Razer Mouse")));
    [Fact] public void HidProductNameAloneDoesNotProveLaptopRoot() => Assert.False(BackendDiscovery.IsEligibleControlCandidate(Candidate() with { UsbParentProductName = null }));
    [Fact] public void MissingPhysicalRootFailsClosed() => Assert.False(BackendDiscovery.IsSamePhysicalLaptop(new[] { Candidate() with { UsbParentDeviceInstanceId = null } }));
    [Fact] public void NonRazerUsbRootFailsClosed() => Assert.False(BackendDiscovery.IsEligibleControlCandidate(Candidate() with { UsbParentBaseHardwareIdentity = "USB\\VID_9999&PID_FFFF" }));
    [Fact] public void FeaturelessCollectionIsExcluded() => Assert.False(BackendDiscovery.IsEligibleControlCandidate(Candidate() with { ReportGeometry = new(37, 37, 0) }));
    // These identities are synthetic: this checks discovery policy, not hardware support.
    [Theory]
    [InlineData("Razer Blade 14", 0xFFFF, 0x0001, 37)]
    [InlineData("Razer Blade 15", 0xFFFE, 0x000C, 375)]
    [InlineData("Razer Blade 16", 0xFFFD, 0x0001, 91)]
    [InlineData("Razer Blade 17", 0xFFFC, 0xFF00, 512)]
    [InlineData("Razer Blade 18", 0xFFFB, 0x000C, 4096)]
    [InlineData("Razer Blade Pro", 0xFFFA, 0xFF00, 128)]
    [InlineData("Razer Blade Stealth 13", 0xFFF9, 0x0001, 64)]
    [InlineData("Razer Blade Future Model", 0xFFF8, 0xFFAA, 257)]
    public void BladeFamiliesWithUnfamiliarIdentityAndGeometryRemainDiscoverable(string product, ushort pid, ushort usagePage, ushort featureBytes)
    {
        var candidate = Candidate(product: product) with { ProductId = pid, UsagePage = usagePage, ReportGeometry = new(0, 0, featureBytes) };
        Assert.True(BackendDiscovery.IsEligibleControlCandidate(candidate));
        Assert.True(BackendDiscovery.IsSamePhysicalLaptop(new[] { candidate }));
    }
    [Fact] public void GenericCompositeLabelDoesNotProveLaptopRoot() => Assert.False(BackendDiscovery.IsEligibleControlCandidate(Candidate() with { UsbParentProductName = "USB Composite Device" }));
    [Theory]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void PhysicalLaptopCollectionLimitIsExplicit(int count, bool expected) =>
        Assert.Equal(expected, BackendDiscovery.IsSamePhysicalLaptop(Enumerable.Range(0, count).Select(i => Candidate(child: $"MI_{i:D2}")).ToArray()));
}
