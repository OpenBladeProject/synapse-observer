namespace SynapseObserver.Core.Tests;

public sealed class SynapseInventoryScopeTests
{
    [Theory]
    [InlineData("RazerAppEngine")]
    [InlineData("razerappengine")]
    [InlineData("Razer Synapse")]
    [InlineData("Razer Synapse 3")]
    [InlineData("Razer Synapse Service")]
    [InlineData("RzSynapse")]
    public void IsProcessName_KnownSynapseFamily_ReturnsTrue(string processName)
    {
        Assert.True(SynapseInventoryScope.IsProcessName(processName));
    }

    [Theory]
    [InlineData("SynapseObserver")]
    [InlineData("SynapseObserver.Core.Tests")]
    [InlineData("RazerCentral")]
    [InlineData("Razer Chroma SDK Service")]
    [InlineData("UnrelatedSynapseTool")]
    public void IsProcessName_UnrelatedFamily_ReturnsFalse(string processName)
    {
        Assert.False(SynapseInventoryScope.IsProcessName(processName));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Razer\RazerAppEngine.exe", @"C:\Program Files\Razer\RazerAppEngine.exe")]
    [InlineData(@"C:\Program Files\Razer\node-rz-hid.node", @"C:\Program Files\Razer\RazerAppEngine.exe")]
    [InlineData(@"C:\Program Files\Razer\resources\node-rz-hid\build\Release\HID.node", @"C:\Program Files\Razer\RazerAppEngine.exe")]
    [InlineData(@"C:\Program Files\Razer\RazerDevice.dll", @"C:\Program Files\Razer\RazerAppEngine.exe")]
    [InlineData(@"C:\Program Files\Razer\RzControl.node", @"C:\Program Files\Razer\RazerAppEngine.exe")]
    public void IsModule_RelevantEngineOrRazerModule_ReturnsTrue(string modulePath, string mainModulePath)
    {
        Assert.True(SynapseInventoryScope.IsModule(modulePath, mainModulePath));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\kernel32.dll")]
    [InlineData(@"C:\Program Files\Razer\libEGL.dll")]
    [InlineData(@"C:\Program Files\Razer\d3dcompiler_47.dll")]
    [InlineData(@"C:\Users\alice\unrelated.node")]
    [InlineData(@"C:\Program Files\Unrelated\build\Release\HID.node")]
    public void IsModule_UnrelatedFrameworkOrSystemModule_ReturnsFalse(string modulePath)
    {
        Assert.False(SynapseInventoryScope.IsModule(
            modulePath,
            @"C:\Program Files\Razer\RazerAppEngine.exe"));
    }
}
