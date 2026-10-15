using System.IO.Compression;

namespace SynapseObserver.Core.Tests;

public sealed class CapacityGuardTests
{
    [Fact]
    public void RecordObserverDroppedEvents_CoalescesPerRecordingAndPreservesFirstTimestamp()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());

        Assert.Equal(EvidenceAddResult.Added, session.RecordObserverDroppedEvents(125.5, 1, 2));
        Assert.Equal(EvidenceAddResult.Added, session.RecordObserverDroppedEvents(900, 1, 5));
        Assert.Equal(EvidenceAddResult.Added, session.RecordObserverDroppedEvents(950, 2, 7));

        Assert.Collection(
            session.Snapshot().Markers,
            first => AssertDroppedEventsMarker(first, 125.5, "count-5"),
            second => AssertDroppedEventsMarker(second, 950, "count-7"));
    }

    [Fact]
    public void RecordObserverDroppedEvents_InvalidValuesAreRejectedWithoutMutation()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());

        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.RecordObserverDroppedEvents(-1, 1, 1));
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.RecordObserverDroppedEvents(double.NaN, 1, 1));
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.RecordObserverDroppedEvents(1, 0, 1));
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.RecordObserverDroppedEvents(1, 1, 0));
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.RecordObserverDroppedEvents(1, 1, -1));

        var snapshot = session.Snapshot();
        Assert.Empty(snapshot.Markers);
        Assert.Equal(5, snapshot.RejectedMarkerCount);
    }

    [Fact]
    public void RecordObserverDroppedEvents_AtMarkerLimitUpdatesKnownRecordingButRejectsNewOne()
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        Assert.Equal(EvidenceAddResult.Added, session.RecordObserverDroppedEvents(10, 1, 2));
        for (var index = 1; index < EvidenceLimits.MaxMarkers; index++)
        {
            Assert.Equal(EvidenceAddResult.Added, session.AddMarker(new EvidenceMarker(
                index,
                EvidenceMarkerKind.UserActionConfirmed,
                $"action-{index}",
                "User action",
                EvidenceProvenance.UserReported)));
        }

        Assert.Equal(EvidenceAddResult.Added, session.RecordObserverDroppedEvents(20, 1, 5));
        Assert.Equal(EvidenceAddResult.RejectedLimit, session.RecordObserverDroppedEvents(30, 2, 1));

        var snapshot = session.Snapshot();
        Assert.Equal(EvidenceLimits.MaxMarkers, snapshot.Markers.Count);
        Assert.Equal(1, snapshot.RejectedMarkerCount);
        AssertDroppedEventsMarker(snapshot.Markers[0], 10, "count-5");
    }

    [Fact]
    public async Task WriteAsync_NoReplacePreservesExistingBundleAndCleansTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var session = SyntheticPreview.CreateSession();
            var existingDestination = Path.Combine(directory, "existing.zip");
            await EvidenceBundleWriter.WriteAsync(session, existingDestination);
            var originalBytes = await File.ReadAllBytesAsync(existingDestination);

            Assert.Equal(EvidenceAddResult.Added, session.RecordObserverDroppedEvents(500, 1, 4));
            await Assert.ThrowsAsync<IOException>(() => EvidenceBundleWriter.WriteAsync(
                session,
                existingDestination,
                CancellationToken.None,
                overwrite: false));

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(existingDestination));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));

            var newDestination = Path.Combine(directory, "new.zip");
            await EvidenceBundleWriter.WriteAsync(
                session,
                newDestination,
                CancellationToken.None,
                overwrite: false);
            using var archive = ZipFile.OpenRead(newDestination);
            Assert.NotEmpty(archive.Entries);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertDroppedEventsMarker(
        EvidenceMarker marker,
        double expectedRelativeMilliseconds,
        string expectedValue)
    {
        Assert.Equal(expectedRelativeMilliseconds, marker.RelativeMilliseconds);
        Assert.Equal(EvidenceMarkerKind.Failure, marker.Kind);
        Assert.Equal("observer", marker.ActivityId);
        Assert.Equal("Observer queue dropped events", marker.Label);
        Assert.Equal(EvidenceProvenance.Observer, marker.Provenance);
        Assert.Equal(expectedValue, marker.Value);
    }
}
