using System.Text;

namespace SynapseObserver.Core.Tests;

public sealed class ContinuousObservationStorageTests
{
    [Fact]
    public void Snapshot_RetainsMoreThanLegacyCountAndPayloadBudgets()
    {
        var directory = TemporaryDirectory();
        try
        {
            using var session = Session(directory, featureBytes: 375);
            var payload = MatrixFrame();
            const int observationCount = 12000;
            Assert.True((long)observationCount * payload.Length > 4L * 1024 * 1024);

            for (var index = 0; index < observationCount; index++)
            {
                Assert.Equal(EvidenceAddResult.Added, session.AddObservation(
                    Observation(payload, $"op-{index}", index)));
            }

            using var snapshot = session.Snapshot();
            Assert.Equal(observationCount, snapshot.Observations.Count);
            Assert.Equal("op-0", snapshot.Observations[0].OperationId);
            Assert.Equal($"op-{observationCount - 1}", snapshot.Observations[^1].OperationId);
            Assert.All(snapshot.Observations.Take(3), item => Assert.True(item.CompletePayloadRetained));
            Assert.Null(snapshot.StorageFailure);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Snapshot_IsImmutableAndEnumeratorsUseIndependentReaders()
    {
        var directory = TemporaryDirectory();
        try
        {
            using var session = Session(directory);
            Assert.Equal(EvidenceAddResult.Added, session.AddObservation(
                Observation(FeatureReportTestData.Create(3, 3, [1, 5, 25]), "op-1", 1)));
            using var first = session.Snapshot();

            Assert.Equal(EvidenceAddResult.Added, session.AddObservation(
                Observation(FeatureReportTestData.Create(3, 3, [1, 5, 50]), "op-2", 2)));
            using var second = session.Snapshot();
            Assert.Single(first.Observations);
            Assert.Equal(2, second.Observations.Count);

            using var left = second.Observations.GetEnumerator();
            using var right = second.Observations.GetEnumerator();
            Assert.True(left.MoveNext());
            Assert.True(right.MoveNext());
            Assert.Equal("op-1", left.Current.OperationId);
            Assert.Equal("op-1", right.Current.OperationId);
            Assert.True(left.MoveNext());
            Assert.Equal("op-2", left.Current.OperationId);
            Assert.True(right.MoveNext());
            Assert.Equal("op-2", right.Current.OperationId);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SessionDisposal_KeepsInflightSnapshotReadableUntilLeaseIsDisposed()
    {
        var directory = TemporaryDirectory();
        var spoolPath = Path.Combine(directory, "observations.spool");
        var session = Session(directory);
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(
            Observation(FeatureReportTestData.Create(3, 3, [1, 5, 25]), "op-1", 1)));
        var snapshot = session.Snapshot();

        session.Dispose();
        Assert.True(File.Exists(spoolPath));
        Assert.Equal("op-1", Assert.Single(snapshot.Observations).OperationId);

        snapshot.Dispose();
        Assert.False(File.Exists(spoolPath));
        DeleteDirectory(directory);
    }

    [Fact]
    public void PartialWriteFailure_FreezesCommittedSanitizedPrefixAndNeverSpoolsRawPayload()
    {
        var directory = TemporaryDirectory();
        var spoolPath = Path.Combine(directory, "observations.spool");
        PartialWriteFailureStream? failing = null;
        var store = SanitizedObservationStore.Create(spoolPath, path =>
        {
            failing = new PartialWriteFailureStream(new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.SequentialScan));
            return failing;
        });
        using var session = new EvidenceSession(Inventory(91), null, store);
        var secret = new byte[91];
        Encoding.UTF8.GetBytes("SERIAL-private-device").CopyTo(secret, 0);

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, session.AddObservation(
            Observation(secret, "op-1", 1)));
        failing!.FailNextPayloadWrite = true;
        Assert.Equal(EvidenceAddResult.RejectedStorage, session.AddObservation(
            Observation(secret, "op-2", 2)));
        Assert.Equal(EvidenceAddResult.RejectedStorage, session.AddObservation(
            Observation(secret, "op-3", 3)));

        using var snapshot = session.Snapshot();
        Assert.Equal("op-1", Assert.Single(snapshot.Observations).OperationId);
        Assert.Equal(2, snapshot.RejectedObservationCount);
        Assert.Equal(EvidenceStorageFailureKind.IoFailure, snapshot.StorageFailure?.Kind);
        Assert.DoesNotContain(directory, snapshot.StorageFailure?.Summary ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Convert.ToHexString(Encoding.UTF8.GetBytes("SERIAL-private-device")),
            Convert.ToHexString(File.ReadAllBytes(spoolPath)),
            StringComparison.Ordinal);
    }

    private static EvidenceSession Session(string directory, int featureBytes = 91)
    {
        var store = SanitizedObservationStore.Create(Path.Combine(directory, "observations.spool"));
        return new EvidenceSession(Inventory(featureBytes), null, store);
    }

    private static InventorySnapshot Inventory(int featureBytes)
    {
        var inventory = SyntheticPreview.CreateInventory();
        var candidate = inventory.RazerHidInterfaces.Value[0] with
        {
            ReportGeometry = new HidReportGeometry(91, 91, (ushort)featureBytes),
        };
        return inventory with
        {
            RazerHidInterfaces = SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Collected([candidate]),
        };
    }

    private static ControlObservation Observation(byte[] bytes, string operationId, double milliseconds) => new(
        operationId,
        milliseconds,
        ObservationSource.UserModeCall,
        "HidD_SetFeature",
        ObservationDirection.Input,
        ObservationStatus.Succeeded,
        0,
        null,
        0x1532,
        0xFFFF,
        "hid-preview-01",
        1,
        2,
        0,
        bytes,
        PayloadDisposition.ProvenFeatureControl);

    private static byte[] MatrixFrame()
    {
        var bytes = new byte[375];
        bytes[2] = 0x1F;
        bytes[5] = 1;
        bytes[6] = 0x36;
        bytes[7] = 3;
        bytes[8] = 0x0B;
        bytes[12] = 0x65;
        for (var index = 13; index < 319; index++)
        {
            bytes[index] = (byte)(index % 256);
        }

        byte checksum = 0;
        foreach (var value in bytes.AsSpan(3, 370))
        {
            checksum ^= value;
        }

        bytes[373] = checksum;
        return bytes;
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-spool-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class PartialWriteFailureStream(Stream inner) : Stream
    {
        private bool prefixWrittenAfterArming;

        internal bool FailNextPayloadWrite { get; set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (!FailNextPayloadWrite)
            {
                inner.Write(buffer);
                return;
            }

            if (!prefixWrittenAfterArming)
            {
                prefixWrittenAfterArming = true;
                inner.Write(buffer);
                return;
            }

            inner.Write(buffer[..Math.Max(1, buffer.Length / 2)]);
            throw new IOException("Synthetic partial spool write failure.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
