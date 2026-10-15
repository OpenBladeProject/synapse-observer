namespace SynapseObserver.Core.Tests;

public sealed class ExportProjectionStorageTests
{
    [Fact]
    public void Scope_UsesSelectedDirectoryAndRemovesFilesAfterCancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var cancellation = new CancellationTokenSource();
            using (var scope = new ExportProjectionScope(cancellation.Token, directory))
            {
                var records = new ExportDiskList<string>();
                records.Add("sanitized-record");
                Assert.Equal("sanitized-record", Assert.Single(records));
                Assert.Single(Directory.EnumerateFiles(directory, "synapse-observer-projection-*.jsonl"));
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => records.Add("after-cancellation"));
            }
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally { Directory.Delete(directory); }
    }

    [Fact]
    public void Scope_ReleasesEveryFileEvenWhenAnotherOwnedResourceFailsToDispose()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var scope = new ExportProjectionScope(directory: directory);
            scope.Add(new FailedCleanup());
            var records = new ExportDiskList<string>();
            records.Add("sanitized-record");
            Assert.Throws<IOException>(() => scope.Dispose());
            Assert.Empty(Directory.EnumerateFiles(directory));
            Assert.Throws<ObjectDisposedException>(() => records.Add("after-disposal"));
        }
        finally { Directory.Delete(directory); }
    }

    [Fact]
    public void Dispose_DiscardsUnwrittenScratchTailWithoutFlushingIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using (var scope = new ExportProjectionScope(directory: directory))
            {
                var records = new ExportDiskList<string>();
                records.Add("buffered-sanitized-record");
                var path = Assert.Single(Directory.GetFiles(directory));
                using var observer = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                Assert.Equal(0, observer.Length);
                records.Dispose();
                Assert.Equal(0, observer.Length);
            }
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally { Directory.Delete(directory); }
    }

    private sealed class FailedCleanup : IDisposable
    {
        public void Dispose() => throw new IOException("Synthetic cleanup failure.");
    }
}
