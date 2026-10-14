using SynapseObserver.Backend;
using Xunit;
namespace SynapseObserver.Backend.Tests;
public sealed class SynapseIdentityTests
{
    [Theory]
    [InlineData("Razer USA Ltd.", "Razer USA Ltd.", true)]
    [InlineData("razer usa ltd.", "RAZER USA LTD.", true)]
    [InlineData("Razer USA Ltd. Unrelated", "Razer USA Ltd.", false)]
    [InlineData("Razer USA Ltd.", "Unrelated Company", false)]
    [InlineData("Unrelated Company", "Razer USA Ltd.", false)]
    [InlineData(null, "Razer USA Ltd.", false)]
    public void PublisherRequiresExactVerifiedLegalIdentity(string? commonName, string? organization, bool expected) =>
        Assert.Equal(expected, TargetPolicy.IsRazerPublisher(commonName, organization));
    [Theory]
    [InlineData("RazerAppEngine.exe", "RazerAppEngine", null, true)]
    [InlineData("RazerAppEngine.exe", null, "RazerAppEngine", true)]
    [InlineData("RazerAppEngine.exe", "Other Tool", "Other Tool", false)]
    [InlineData("Other.exe", "RazerAppEngine", "RazerAppEngine", false)]
    public void RenamedUnrelatedSignedProgramIsNotTheEngine(string fileName, string? product, string? internalName, bool expected) =>
        Assert.Equal(expected, TargetPolicy.IsEngineRole(fileName, product, internalName));
    [Theory]
    [InlineData("app-4.0.823")]
    [InlineData("app-4.0.827")]
    [InlineData("app-9999.1-future")]
    [InlineData("future-installation")]
    public void InstallationDiscoveryDoesNotGateVersionFolder(string folder)
    {
        string engine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Razer", "RazerAppEngine", folder, "RazerAppEngine.exe");
        Assert.True(TargetPolicy.IsInstalledEnginePath(engine));
        string module = Path.Combine(Path.GetDirectoryName(engine)!, "resources", "app.asar.unpacked", "node_modules", "node-rz-hid", "build", "Release", "HID.node");
        Assert.True(TargetPolicy.IsHidModuleOfEngine(module, engine));
    }
    [Fact]
    public void CopiedEngineOutsideRazerInstallationIsRejected() =>
        Assert.False(TargetPolicy.IsInstalledEnginePath(Path.Combine(Path.GetTempPath(), "RazerAppEngine.exe")));
    [Fact]
    public void ModuleFromAnotherInstallationIsRejected()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Razer", "RazerAppEngine");
        string engine = Path.Combine(root, "app-current", "RazerAppEngine.exe");
        string module = Path.Combine(root, "app-other", "resources", "app.asar.unpacked", "node_modules", "node-rz-hid", "build", "Release", "HID.node");
        Assert.False(TargetPolicy.IsHidModuleOfEngine(module, engine));
    }
    [Fact]
    public void PublisherTrustChecksChainRevocationWithoutDisablingIt()
    {
        Assert.Equal(1u, TargetPolicy.TrustRevocationChecks);
        Assert.Equal(0u, TargetPolicy.TrustProviderFlags & 0x10u);
        Assert.NotEqual(0u, TargetPolicy.TrustProviderFlags & 0x80u);
    }
}
