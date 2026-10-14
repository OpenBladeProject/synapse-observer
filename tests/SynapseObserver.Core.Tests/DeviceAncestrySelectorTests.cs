using SynapseObserver.Core;

namespace SynapseObserver.Core.Tests;

public sealed class DeviceAncestrySelectorTests
{
    [Fact]
    public void SelectsNearestPhysicalUsbVidWithoutReplacingItWithHub()
    {
        string?[] ancestry =
        [
            "HID\\VID_1532&PID_02E0&MI_00&COL01\\7&collection",
            "USB\\VID_1532&PID_02E0&MI_00\\7&interface",
            "USB\\VID_1532&PID_02E0\\physical-razer-root",
            "USB\\VID_05E3&PID_0626\\upstream-hub",
        ];

        Assert.Equal(2, DeviceAncestrySelector.FindNearestPhysicalUsbVid(ancestry, 0x1532, 0x02E0));
    }

    [Fact]
    public void KeepsDistinctPhysicalRootsDistinct()
    {
        string?[] first =
        [
            "USB\\VID_1532&PID_02E0&MI_03\\first-interface",
            "USB\\VID_1532&PID_02E0\\first-root",
            "USB\\VID_05E3&PID_0626\\shared-hub",
        ];
        string?[] second =
        [
            "USB\\VID_1532&PID_02C6&MI_01\\second-interface",
            "USB\\VID_1532&PID_02C6\\second-root",
            "USB\\VID_05E3&PID_0626\\shared-hub",
        ];

        Assert.Equal(1, DeviceAncestrySelector.FindNearestPhysicalUsbVid(first, 0x1532, 0x02E0));
        Assert.Equal(1, DeviceAncestrySelector.FindNearestPhysicalUsbVid(second, 0x1532, 0x02C6));
        Assert.NotEqual(first[1], second[1]);
    }

    [Theory]
    [InlineData("USB\\VID_1532&PID_02E0&MI_00\\interface")]
    [InlineData("USB\\ROOT_HUB30\\root")]
    [InlineData("USB\\VID_05E3&PID_0626\\unrelated-hub")]
    [InlineData("PCI\\VEN_1022&DEV_14DD\\controller")]
    [InlineData("USB\\VID_ZZZZ&PID_02E0\\invalid")]
    public void RejectsNonPhysicalOrMalformedAncestors(string instanceId)
    {
        Assert.Equal(-1, DeviceAncestrySelector.FindNearestPhysicalUsbVid([instanceId], 0x1532, 0x02E0));
    }
}
