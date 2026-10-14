using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using SynapseObserver.Backend;
using Xunit;

namespace SynapseObserver.Backend.Tests;

public sealed class NativeObservationSessionTests
{
    private const long Frequency = 10_000_000;
    private static readonly long Origin = 1_000;

    [Fact]
    public async Task MalformedSlotIsReleasedWhileValidPrefixAndSuffixArePreserved()
    {
        var mapping = MemoryMappedFile.CreateNew(null,
            NativeObservationSession.HeaderSize + NativeObservationSession.Slots * NativeEventParser.RecordSize,
            MemoryMappedFileAccess.ReadWrite);
        using var writer = mapping.CreateViewAccessor();
        writer.Write(4, 1); // Keep test disposal from waiting for a native worker.
        WriteSlot(writer, 0, Record(1, 1));
        WriteSlot(writer, 1, Record(2, 2, operation: 4));
        WriteSlot(writer, 2, Record(3, 3));
        writer.Write(16, 3);

        await using var session = new NativeObservationSession(mapping, Origin);
        var failure = await Assert.ThrowsAsync<NativeObservationPollException>(() => session.PollAsync());
        IReadOnlyList<NativeObservationEvent> next = [];
        try
        {
            Assert.Equal(1, failure.RejectedRecords);
            Assert.Equal<ulong>([1UL, 3UL], failure.Events.Select(item => item.Sequence));
            Assert.Equal<byte>([1, 3], failure.Events.Select(item => item.RequestBytes[0]));
            Assert.Equal(3, writer.ReadInt32(20));
            AssertSlotsCleared(writer, 0, 1, 2);

            WriteSlot(writer, 3, Record(4, 4));
            writer.Write(16, 4);
            next = await session.PollAsync();
            Assert.Collection(next, item => Assert.Equal(4UL, item.Sequence));
            Assert.Equal(4, writer.ReadInt32(20));
            AssertSlotsCleared(writer, 3);
        }
        finally
        {
            ClearEvents(failure.Events);
            ClearEvents(next);
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    [InlineData(0, NativeObservationSession.Slots + 1)]
    public async Task InvalidRingCountersFailClosedWithoutReleasingSlots(int read, int write)
    {
        var mapping = MemoryMappedFile.CreateNew(null,
            NativeObservationSession.HeaderSize + NativeObservationSession.Slots * NativeEventParser.RecordSize,
            MemoryMappedFileAccess.ReadWrite);
        using var writer = mapping.CreateViewAccessor();
        writer.Write(4, 1);
        WriteSlot(writer, 0, Record(1, 0x5A));
        writer.Write(20, read);
        writer.Write(16, write);

        await using var session = new NativeObservationSession(mapping, Origin);
        await Assert.ThrowsAsync<InvalidDataException>(() => session.PollAsync());

        Assert.Equal(read, writer.ReadInt32(20));
        var retained = new byte[NativeEventParser.RecordSize];
        Assert.Equal(retained.Length, writer.ReadArray(NativeObservationSession.HeaderSize, retained, 0, retained.Length));
        Assert.Equal((byte)0x5A, retained[52]);
        CryptographicOperations.ZeroMemory(retained);
    }

    [Fact]
    public async Task TimedOutExplicitStopDoesNotHideAnotherWaitInsideDisposal()
    {
        var mapping = MemoryMappedFile.CreateNew(null,
            NativeObservationSession.HeaderSize + NativeObservationSession.Slots * NativeEventParser.RecordSize,
            MemoryMappedFileAccess.ReadWrite);
        await using var session = new NativeObservationSession(mapping, Origin);
        var status = await session.StopAsync();
        Assert.False(status.Finished);
        Assert.Equal(170, status.Failure);
        var disposal = session.DisposeAsync();
        Assert.True(disposal.IsCompletedSuccessfully);
        await disposal;
    }

    private static byte[] Record(ulong sequence, byte marker, uint operation = 1)
    {
        var bytes = new byte[NativeEventParser.RecordSize];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, sequence);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), Origin + 100);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), operation);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(NativeEventParser.OutputBufferCapacityOffset), 1);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(NativeEventParser.CompletionTimestampOffset), Origin + 200);
        bytes[52] = marker;
        bytes[4148] = marker;
        return bytes;
    }

    private static void WriteSlot(MemoryMappedViewAccessor writer, int slot, byte[] record)
    {
        try
        {
            int offset = NativeObservationSession.HeaderSize + slot * NativeEventParser.RecordSize;
            writer.WriteArray(offset, record, 0, record.Length);
        }
        finally { CryptographicOperations.ZeroMemory(record); }
    }

    private static void AssertSlotsCleared(MemoryMappedViewAccessor writer, params int[] slots)
    {
        var bytes = new byte[NativeEventParser.RecordSize];
        try
        {
            foreach (int slot in slots)
            {
                int offset = NativeObservationSession.HeaderSize + slot * NativeEventParser.RecordSize;
                Assert.Equal(bytes.Length, writer.ReadArray(offset, bytes, 0, bytes.Length));
                Assert.All(bytes, value => Assert.Equal((byte)0, value));
            }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void ClearEvents(IEnumerable<NativeObservationEvent> events)
    {
        foreach (var item in events)
        {
            CryptographicOperations.ZeroMemory(item.RequestBytes);
            CryptographicOperations.ZeroMemory(item.ResponseBytes);
        }
    }
}
