using System.Buffers.Binary;
using SynapseObserver.Backend;
using Xunit;
namespace SynapseObserver.Backend.Tests;
public sealed class NativeEventParserTests
{
    private static byte[] Record(uint operation = 1, uint gap = 0, bool success = true, uint input = 2, uint output = 2, uint? capacity = null, uint returned = 0)
    {
        var b = new byte[NativeEventParser.RecordSize];
        BinaryPrimitives.WriteUInt64LittleEndian(b, 1);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(8), 1500);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(NativeEventParser.CompletionTimestampOffset), 1750);
        U(b, 16, operation); U(b, 20, gap); U(b, 24, success ? 1u : 0); U(b, 28, 123); U(b, 36, returned); U(b, 40, input); U(b, 44, output);
        U(b, NativeEventParser.OutputBufferCapacityOffset, capacity ?? output);
        b[52] = 7; b[4148] = 9; return b;
    }
    private static void U(byte[] b, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset), value);
    [Fact] public void CompletedResponseKeepsSeparateRequestAndResponse()
    {
        var e = NativeEventParser.Parse(Record(), 1000, 1000);
        Assert.Equal("HidD_GetFeature", e.Api); Assert.Equal(500, e.RelativeMilliseconds); Assert.Equal(750, e.CompletionRelativeMilliseconds);
        Assert.Equal(7, e.RequestBytes[0]); Assert.Equal(9, e.ResponseBytes[0]); Assert.Equal(123u, e.NativeError); Assert.Equal(2u, e.OutputBufferCapacity);
    }
    [Fact] public void PendingRetainsRequestAndGapWithoutResponse()
    {
        var e = NativeEventParser.Parse(Record(3, 1, false, 2, 0), 0, 1000);
        Assert.Equal("pending-completion-not-observed", e.CompletionGap); Assert.Empty(e.ResponseBytes); Assert.False(e.Result);
    }
    [Theory]
    [InlineData(1, 1, false)] [InlineData(1, 0, false)]
    public void UncompletedResponseIsRejected(uint operation, uint gap, bool success) => Assert.Throws<InvalidDataException>(() => NativeEventParser.Parse(Record(operation, gap, success), 0, 1000));
    [Theory]
    [InlineData(4, 0, 2, 2)] [InlineData(0, 9, 2, 2)] [InlineData(0, 0, 4097, 0)] [InlineData(1, 0, 2, 4097)]
    public void InvalidEnumsAndLengthsRejected(uint operation, uint gap, uint input, uint output) => Assert.Throws<InvalidDataException>(() => NativeEventParser.Parse(Record(operation, gap, true, input, output), 0, 1000));
    [Fact] public void CallSpanningAnActionKeepsResponseAfterThatAction()
    {
        var e = NativeEventParser.Parse(Record(), 1000, 1000);
        const double actionMilliseconds = 600;
        Assert.True(e.RelativeMilliseconds < actionMilliseconds);
        Assert.True(e.CompletionRelativeMilliseconds > actionMilliseconds);
    }
    [Fact] public void EqualReturnedBytesKeepDistinctOutputCapacities()
    {
        var exact = NativeEventParser.Parse(Record(3, output: 90, capacity: 90, returned: 90), 0, 1000);
        var spare = NativeEventParser.Parse(Record(3, output: 90, capacity: 91, returned: 90), 0, 1000);
        Assert.Equal(90u, exact.ReturnedBytes); Assert.Equal(90u, spare.ReturnedBytes);
        Assert.Equal(90u, exact.OutputBufferCapacity); Assert.Equal(91u, spare.OutputBufferCapacity);
        Assert.Null(exact.CompletionGap); Assert.Null(spare.CompletionGap);
    }
    [Theory]
    [InlineData(91, 90, 90)]
    [InlineData(90, 91, 90)]
    public void CompletedBytesCannotExceedKnownCapacity(uint output, uint returned, uint capacity) =>
        Assert.Throws<InvalidDataException>(() => NativeEventParser.Parse(Record(3, output: output, capacity: capacity, returned: returned), 0, 1000));
    [Fact] public void LegacyZeroCapacityRecordRemainsReadable()
    {
        var e = NativeEventParser.Parse(Record(capacity: 0), 0, 1000);
        Assert.Equal(0u, e.OutputBufferCapacity); Assert.Equal(2, e.ResponseBytes.Length);
    }
    [Fact] public void CompletionBeforeEntryRejected()
    {
        var bytes = Record();
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(NativeEventParser.CompletionTimestampOffset), 1499);
        Assert.Throws<InvalidDataException>(() => NativeEventParser.Parse(bytes, 0, 1000));
    }
    [Fact] public void EqualCounterTicksAreAllowed()
    {
        var bytes = Record();
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(NativeEventParser.CompletionTimestampOffset), 1500);
        var e = NativeEventParser.Parse(bytes, 1000, 1000);
        Assert.Equal(e.RelativeMilliseconds, e.CompletionRelativeMilliseconds);
    }
    [Fact] public void TruncatedRecordRejected() => Assert.Throws<InvalidDataException>(() => NativeEventParser.Parse(new byte[8], 0, 1000));
    [Fact] public void InvalidFrequencyRejected() => Assert.Throws<InvalidDataException>(() => NativeEventParser.Parse(Record(), 0, 0));
    [Fact] public async Task ForgedEligibilityCannotAttachToOwnedTestProcess()
    {
        var candidate = new BackendCandidate(Environment.ProcessId, "Razer Synapse engine", true, "forged");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => NativeObservationSession.StartAsync(candidate, "unused"));
        Assert.Contains("not the Synapse engine", failure.Message);
    }
    [Fact] public async Task CancellationStopsBeforeAnyProcessOrDeviceAccess()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NativeObservationSession.StartAsync(new(-1, "unused", true, "unused"), "unused", cancellation.Token));
    }
}
