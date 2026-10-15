using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
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

        var identity = new ResidentObserverIdentity(11, 12, "observer.dll", new string('A', 64), (nint)13);
        var resident = new ResidentObserverRegistration(identity);
        Assert.Equal(ResidentStartDecision.Started, resident.TryBegin(identity));
        await using var session = new NativeObservationSession(
            mapping,
            Origin,
            resident,
            new SafeFileHandle(IntPtr.Zero, ownsHandle: false));
        var failure = await Assert.ThrowsAsync<NativeObservationPollException>(() => session.PollAsync());
        IReadOnlyList<NativeObservationEvent> next = [];
        try
        {
            Assert.Equal(1, failure.RejectedRecords);
            Assert.Equal<ulong>([1UL, 3UL], failure.Events.Select(item => item.Sequence));
            Assert.Equal<byte>([1, 3], failure.Events.Select(item => item.RequestBytes[0]));
            Assert.Equal(3, writer.ReadInt32(20));
            AssertSlotsCleared(writer, 0, 1, 2);
            Assert.Equal(ResidentObserverState.Faulted, resident.State);
            NativeSessionStatus invalidated = session.GetStatus();
            Assert.Equal(0, invalidated.Failure);
            Assert.False(invalidated.ReuseAllowed);

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

        var identity = new ResidentObserverIdentity(21, 22, "observer.dll", new string('A', 64), (nint)23);
        var resident = new ResidentObserverRegistration(identity);
        Assert.Equal(ResidentStartDecision.Started, resident.TryBegin(identity));
        await using var session = new NativeObservationSession(
            mapping,
            Origin,
            resident,
            new SafeFileHandle(IntPtr.Zero, ownsHandle: false));
        await Assert.ThrowsAsync<InvalidDataException>(() => session.PollAsync());

        Assert.Equal(ResidentObserverState.Faulted, resident.State);
        Assert.Equal(0, session.GetStatus().Failure);
        Assert.False(session.GetStatus().ReuseAllowed);
        Assert.Equal(read, writer.ReadInt32(20));
        var retained = new byte[NativeEventParser.RecordSize];
        Assert.Equal(retained.Length, writer.ReadArray(NativeObservationSession.HeaderSize, retained, 0, retained.Length));
        Assert.Equal((byte)0x5A, retained[52]);
        CryptographicOperations.ZeroMemory(retained);
    }

    [Fact]
    public async Task CallerCancellationBeforePollingDoesNotInvalidateResidentReuse()
    {
        var mapping = MemoryMappedFile.CreateNew(null,
            NativeObservationSession.HeaderSize + NativeObservationSession.Slots * NativeEventParser.RecordSize,
            MemoryMappedFileAccess.ReadWrite);
        using var writer = mapping.CreateViewAccessor();
        writer.Write(4, 1);
        var identity = new ResidentObserverIdentity(31, 32, "observer.dll", new string('A', 64), (nint)33);
        var resident = new ResidentObserverRegistration(identity);
        Assert.Equal(ResidentStartDecision.Started, resident.TryBegin(identity));
        await using var session = new NativeObservationSession(
            mapping,
            Origin,
            resident,
            new SafeFileHandle(IntPtr.Zero, ownsHandle: false));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.PollAsync(cancellation.Token));

        Assert.Equal(ResidentObserverState.Recording, resident.State);
        Assert.True(session.GetStatus().ReuseAllowed);
    }

    [Fact]
    public async Task ExplicitInvalidationOverridesCleanCompletionAndRemainsSafeAfterDisposal()
    {
        var mapping = MemoryMappedFile.CreateNew(null,
            NativeObservationSession.HeaderSize + NativeObservationSession.Slots * NativeEventParser.RecordSize,
            MemoryMappedFileAccess.ReadWrite);
        using var writer = mapping.CreateViewAccessor();
        writer.Write(0, 1);
        writer.Write(4, 1);
        var identity = new ResidentObserverIdentity(41, 42, "observer.dll", new string('A', 64), (nint)43);
        var resident = new ResidentObserverRegistration(identity);
        Assert.Equal(ResidentStartDecision.Started, resident.TryBegin(identity));
        var session = new NativeObservationSession(
            mapping,
            Origin,
            resident,
            new SafeFileHandle(IntPtr.Zero, ownsHandle: false));

        NativeSessionStatus status = await session.StopAsync();
        Assert.Equal(0, status.Failure);
        Assert.Equal(ResidentObserverState.Idle, resident.State);

        session.InvalidateReuse();
        Assert.Equal(ResidentObserverState.Faulted, resident.State);
        NativeSessionStatus invalidated = await session.StopAsync();
        Assert.Equal(0, invalidated.Failure);
        Assert.False(invalidated.ReuseAllowed);
        Assert.Contains("cannot be reused", invalidated.Diagnostic, StringComparison.Ordinal);
        await session.DisposeAsync();

        session.InvalidateReuse();
        Assert.Equal(ResidentObserverState.Faulted, resident.State);
        Assert.Equal(ResidentStartDecision.Faulted, resident.TryBegin(identity));
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
        Assert.False(status.ReuseAllowed);
        var disposal = session.DisposeAsync();
        Assert.True(disposal.IsCompletedSuccessfully);
        await disposal;
    }

    [Fact]
    public async Task CleanResidentIsReusableOnlyAfterWorkerCompletionIsConfirmed()
    {
        var mapping = MemoryMappedFile.CreateNew(null,
            NativeObservationSession.HeaderSize + NativeObservationSession.Slots * NativeEventParser.RecordSize,
            MemoryMappedFileAccess.ReadWrite);
        using var writer = mapping.CreateViewAccessor();
        writer.Write(0, 1);
        writer.Write(4, 1);
        var identity = new ResidentObserverIdentity(1, 2, "observer.dll", new string('A', 64), (nint)3);
        var resident = new ResidentObserverRegistration(identity);
        Assert.Equal(ResidentStartDecision.Started, resident.TryBegin(identity));
        using var completed = new EventWaitHandle(false, EventResetMode.ManualReset);
        var borrowedWorker = new SafeFileHandle(completed.SafeWaitHandle.DangerousGetHandle(), ownsHandle: false);
        await using var session = new NativeObservationSession(mapping, Origin, resident, borrowedWorker);

        Task<NativeSessionStatus> stop = session.StopAsync();
        await Task.Delay(50);
        Assert.Equal(ResidentObserverState.Recording, resident.State);
        completed.Set();
        NativeSessionStatus status = await stop;

        Assert.Equal(0, status.Failure);
        Assert.True(status.ReuseAllowed);
        Assert.Equal(ResidentObserverState.Idle, resident.State);
    }

    [Fact]
    public async Task NativeFailurePermanentlyFaultsResidentAfterWorkerCompletion()
    {
        var mapping = MemoryMappedFile.CreateNew(null,
            NativeObservationSession.HeaderSize + NativeObservationSession.Slots * NativeEventParser.RecordSize,
            MemoryMappedFileAccess.ReadWrite);
        using var writer = mapping.CreateViewAccessor();
        writer.Write(0, 1);
        writer.Write(4, 1);
        writer.Write(8, 170);
        var identity = new ResidentObserverIdentity(1, 2, "observer.dll", new string('A', 64), (nint)3);
        var resident = new ResidentObserverRegistration(identity);
        Assert.Equal(ResidentStartDecision.Started, resident.TryBegin(identity));
        using var completed = new EventWaitHandle(true, EventResetMode.ManualReset);
        var borrowedWorker = new SafeFileHandle(completed.SafeWaitHandle.DangerousGetHandle(), ownsHandle: false);
        await using var session = new NativeObservationSession(mapping, Origin, resident, borrowedWorker);

        NativeSessionStatus status = await session.StopAsync();

        Assert.Equal(170, status.Failure);
        Assert.False(status.ReuseAllowed);
        Assert.Equal(ResidentObserverState.Faulted, resident.State);
        Assert.Equal(ResidentStartDecision.Faulted, resident.TryBegin(identity));
    }

    [Fact]
    public async Task LateWorkerCompletionCannotReverseAnUncertainStop()
    {
        var mapping = MemoryMappedFile.CreateNew(null,
            NativeObservationSession.HeaderSize + NativeObservationSession.Slots * NativeEventParser.RecordSize,
            MemoryMappedFileAccess.ReadWrite);
        using var writer = mapping.CreateViewAccessor();
        writer.Write(0, 1);
        writer.Write(4, 1);
        var identity = new ResidentObserverIdentity(1, 2, "observer.dll", new string('A', 64), (nint)3);
        var resident = new ResidentObserverRegistration(identity);
        Assert.Equal(ResidentStartDecision.Started, resident.TryBegin(identity));
        using var completed = new EventWaitHandle(false, EventResetMode.ManualReset);
        var borrowedWorker = new SafeFileHandle(completed.SafeWaitHandle.DangerousGetHandle(), ownsHandle: false);
        await using var session = new NativeObservationSession(mapping, Origin, resident, borrowedWorker);

        NativeSessionStatus first = await session.StopAsync();
        Assert.Equal(170, first.Failure);
        Assert.False(first.ReuseAllowed);
        Assert.Equal(ResidentObserverState.Faulted, resident.State);

        completed.Set();
        NativeSessionStatus late = await session.StopAsync();

        Assert.True(late.Finished);
        Assert.Equal(0, late.Failure);
        Assert.False(late.ReuseAllowed);
        Assert.Contains("cannot be reused", late.Diagnostic, StringComparison.Ordinal);
        Assert.Equal(ResidentObserverState.Faulted, resident.State);
        Assert.Equal(ResidentStartDecision.Faulted, resident.TryBegin(identity));
    }

    [Fact]
    public async Task NonzeroWorkerExitPermanentlyRefusesReuse()
    {
        var mapping = MemoryMappedFile.CreateNew(null,
            NativeObservationSession.HeaderSize + NativeObservationSession.Slots * NativeEventParser.RecordSize,
            MemoryMappedFileAccess.ReadWrite);
        using var writer = mapping.CreateViewAccessor();
        writer.Write(0, 1);
        writer.Write(4, 1);
        var identity = new ResidentObserverIdentity(51, 52, "observer.dll", new string('A', 64), (nint)53);
        var resident = new ResidentObserverRegistration(identity);
        Assert.Equal(ResidentStartDecision.Started, resident.TryBegin(identity));
        using var completed = new EventWaitHandle(true, EventResetMode.ManualReset);
        var borrowedWorker = new SafeFileHandle(completed.SafeWaitHandle.DangerousGetHandle(), ownsHandle: false);
        await using var session = new NativeObservationSession(
            mapping,
            Origin,
            resident,
            borrowedWorker,
            workerExitCode: 123);

        NativeSessionStatus status = await session.StopAsync();

        Assert.Equal(123, status.Failure);
        Assert.False(status.ReuseAllowed);
        Assert.Contains("unsuccessful exit", status.Diagnostic, StringComparison.Ordinal);
        Assert.Equal(ResidentObserverState.Faulted, resident.State);
        Assert.Equal(ResidentStartDecision.Faulted, resident.TryBegin(identity));
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
