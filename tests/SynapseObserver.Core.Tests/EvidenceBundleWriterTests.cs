using System.IO.Compression;
using System.Text;

namespace SynapseObserver.Core.Tests;

public sealed class EvidenceBundleWriterTests
{
    [Fact]
    public async Task WriteAsync_ExportsPartialCancellationAndPrivacyBoundedSchema()
    {
        var inventory = SyntheticPreview.CreateInventory() with
        {
            RazerHidInterfaces = SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Cancelled([]),
            SynapseProcesses = SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Unavailable([], "Module inventory unavailable."),
        };
        var session = new EvidenceSession(inventory, DateTimeOffset.UnixEpoch);
        var secret = Encoding.UTF8.GetBytes(@"C:\Users\alice\VID_1532&PID_9999\serial");
        session.AddObservation(new ControlObservation(
            "op-privacy",
            10,
            ObservationSource.UserModeCall,
            "DeviceIoControl",
            ObservationDirection.Input,
            ObservationStatus.Pending,
            null,
            null,
            0x1532,
            0x9999,
            "hid-opaque",
            1,
            2,
            0,
            secret,
            PayloadDisposition.RedactedUnproven,
            12.5));
        session.SetCoverage(new CoverageItem(
            "capture-performance",
            "Performance mode",
            CoverageState.Cancelled,
            CoverageReason.Cancelled));

        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "evidence.zip");
            await EvidenceBundleWriter.WriteAsync(session, destination, CancellationToken.None);

            using var archive = ZipFile.OpenRead(destination);
            Assert.Equal(new[] { "manifest.json", "README.txt" }, archive.Entries.Select(entry => entry.FullName).Order().ToArray());
            var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json");
            using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8);
            var manifest = await reader.ReadToEndAsync();

            Assert.Contains("\"razerHidInterfaces\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"collectorBuild\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"informationalVersion\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"cancelled\"", manifest, StringComparison.Ordinal);
            Assert.Contains($"\"bufferLength\": {secret.Length}", manifest, StringComparison.Ordinal);
            Assert.Contains("\"completedRelativeMilliseconds\": 12.5", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("alice", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("interfacePath", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("commandLine", manifest, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payloadHex\": \"", manifest, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_PreCancelledTokenLeavesExistingDestinationUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "evidence.zip");
            await File.WriteAllTextAsync(destination, "existing");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                EvidenceBundleWriter.WriteAsync(SyntheticPreview.CreateSession(), destination, cancellation.Token));

            Assert.Equal("existing", await File.ReadAllTextAsync(destination));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_MaximumAcceptedSessionFitsDocumentedManifestBound()
    {
        var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        var unknownPayload = new byte[EvidenceLimits.MaxPayloadBytesPerObservation];
        for (var index = 0; index < EvidenceLimits.MaxObservations; index++)
        {
            Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, session.AddObservation(new ControlObservation(
                $"op-{index}",
                index,
                ObservationSource.UserModeCall,
                "HidD_SetFeature",
                ObservationDirection.Input,
                ObservationStatus.Succeeded,
                null,
                null,
                0x1532,
                0xFFFF,
                "hid-preview-01",
                1,
                2,
                0,
                unknownPayload,
                PayloadDisposition.ProvenFeatureControl,
                index + 0.5)));
        }

        for (var index = session.Snapshot().Markers.Count; index < EvidenceLimits.MaxMarkers; index++)
        {
            Assert.Equal(EvidenceAddResult.Added, session.AddMarker(new EvidenceMarker(
                index,
                EvidenceMarkerKind.UserActionConfirmed,
                $"marker-{index}",
                new string('M', EvidenceLimits.MaxTextLength),
                EvidenceProvenance.UserReported,
                new string('V', EvidenceLimits.MaxTextLength))));
        }

        for (var index = 0; index < EvidenceLimits.MaxCoverageItems; index++)
        {
            Assert.Equal(EvidenceAddResult.Added, session.SetCoverage(new CoverageItem(
                $"coverage-{index}",
                new string('C', EvidenceLimits.MaxTextLength),
                CoverageState.CompletedWithGaps,
                CoverageReason.NoMatchingEvents)));
        }

        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "maximum.zip");
            await EvidenceBundleWriter.WriteAsync(session, destination, CancellationToken.None);

            using var archive = ZipFile.OpenRead(destination);
            var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json");
            Assert.InRange(manifestEntry.Length, 1, EvidenceLimits.MaxBundleJsonBytes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
