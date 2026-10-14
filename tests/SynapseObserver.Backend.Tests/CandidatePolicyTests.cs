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
}
