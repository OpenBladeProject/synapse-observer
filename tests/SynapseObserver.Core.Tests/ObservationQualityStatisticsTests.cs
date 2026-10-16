namespace SynapseObserver.Core.Tests;

public sealed class ObservationQualityStatisticsTests
{
    [Fact]
    public void Collect_ObservesCancellationWhileReadingTheSpool()
    {
        using var cancellation = new CancellationTokenSource();
        using var scope = new ExportProjectionScope(cancellation.Token);
        var visited = 0;
        var observation = new SanitizedControlObservation("op-1", 0, ObservationSource.UserModeCall,
            "HidD_SetFeature", ObservationDirection.Input, ObservationStatus.Succeeded, 0, null,
            0x1532, 0xFFFF, "hid-preview-01", 1, 2, 0, 1, PayloadDisposition.ExcludedSensitive, null);
        IEnumerable<SanitizedControlObservation> Observations()
        {
            for (var index = 0; index < 100; index++)
            {
                if (++visited == 3) cancellation.Cancel();
                yield return observation;
            }
        }
        Assert.Throws<OperationCanceledException>(() => ObservationQualityStatistics.Collect(Observations(), _ => true));
        Assert.Equal(3, visited);
    }

    [Fact]
    public void NestedScope_InheritsDestinationAndCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var outer = new ExportProjectionScope(cancellation.Token, directory);
            using var inner = new ExportProjectionScope();
            Assert.Equal(directory, inner.DirectoryPath);
            Assert.Equal(cancellation.Token, inner.CancellationToken);
            cancellation.Cancel();
            var records = new ExportDiskList<string>();
            Assert.Throws<OperationCanceledException>(() => records.Add("sanitized"));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally { Directory.Delete(directory); }
    }
}
