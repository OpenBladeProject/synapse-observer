using System.Text;
namespace SynapseObserver.Core.Tests;
public sealed class BusReportedProductNameTests
{
    [Fact]
    public void AcceptsTerminatedBusProductDescription()
    {
        byte[] data = Encoding.Unicode.GetBytes("Razer Blade 16\0");
        Assert.Equal("Razer Blade 16", WindowsInventorySource.DecodeBusReportedProductName(data, 0x12, (uint)data.Length));
    }
    [Theory]
    [InlineData(0x13, 4)]
    [InlineData(0x12, 1)]
    [InlineData(0x12, 3)]
    [InlineData(0x12, 1026)]
    [InlineData(0x12, 6)]
    public void RejectsInvalidTypeOrReturnedLength(uint type, uint length) =>
        Assert.Null(WindowsInventorySource.DecodeBusReportedProductName(new byte[4], type, length));
    [Theory]
    [InlineData("Razer Blade 16")]
    [InlineData("Razer\0Blade\0")]
    [InlineData("\0")]
    public void RejectsUnterminatedEmbeddedNullOrEmptyDescription(string text)
    {
        byte[] data = Encoding.Unicode.GetBytes(text);
        Assert.Null(WindowsInventorySource.DecodeBusReportedProductName(data, 0x12, (uint)data.Length));
    }
}
