using System.Collections;
using System.Text;
using System.Text.Json;

namespace SynapseObserver.Core;

// Only sanitized schema records enter these files. They live for the projection,
// not the capture session, and DeleteOnClose also covers abandoned projections.
internal sealed class ExportProjectionScope : IDisposable
{
    private static readonly AsyncLocal<ExportProjectionScope?> Active = new();
    private readonly ExportProjectionScope? previous = Active.Value;
    private readonly HashSet<IDisposable> owned = [];
    private bool disposed;
    internal CancellationToken CancellationToken { get; }
    internal string DirectoryPath { get; }
    internal static ExportProjectionScope? Current => Active.Value;

    internal ExportProjectionScope(CancellationToken cancellationToken = default, string? directory = null)
    {
        CancellationToken = cancellationToken.CanBeCanceled ? cancellationToken : previous?.CancellationToken ?? default;
        DirectoryPath = directory ?? previous?.DirectoryPath ?? Path.GetTempPath();
        Active.Value = this;
    }

    internal void Add(IDisposable item) => owned.Add(item);
    internal void Remove(IDisposable item) => owned.Remove(item);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Active.Value = previous;
        Exception? firstFailure = null;
        while (owned.Count > 0)
        {
            var item = owned.First();
            owned.Remove(item);
            try { item.Dispose(); }
            catch (Exception error) { firstFailure ??= error; }
        }
        if (firstFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstFailure).Throw();
    }
}

internal sealed class ExportDiskList<T> : IReadOnlyList<T>, IDisposable
{
    private readonly ExportProjectionScope? scope = ExportProjectionScope.Current;
    private FileStream? file;
    private StreamWriter? writer;
    private bool disposed;
    public int Count { get; private set; }

    internal ExportDiskList() => scope?.Add(this);

    internal void Add(T value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        scope?.CancellationToken.ThrowIfCancellationRequested();
        if (writer is null)
        {
            var path = Path.Combine(scope?.DirectoryPath ?? Path.GetTempPath(), $"synapse-observer-projection-{Guid.NewGuid():N}.jsonl");
            file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose);
            writer = new StreamWriter(file, new UTF8Encoding(false), 4 * 1024);
        }
        writer.WriteLine(JsonSerializer.Serialize(value));
        Count = checked(Count + 1);
    }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            return this.Skip(index).First();
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (file is null) yield break;
        writer!.Flush();
        using var input = new FileStream(file.Name, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
        using var reader = new StreamReader(input, Encoding.UTF8, false, 64 * 1024);
        for (var index = 0; index < Count; index++)
        {
            scope?.CancellationToken.ThrowIfCancellationRequested();
            var line = reader.ReadLine() ?? throw new EndOfStreamException("A derived evidence record is incomplete.");
            yield return JsonSerializer.Deserialize<T>(line)
                ?? throw new InvalidDataException("A derived evidence record is invalid.");
        }
        GC.KeepAlive(this);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        scope?.Remove(this);
        // Enumeration flushes records that are actually read. Disposal discards
        // any unwritten scratch tail rather than flushing a file being deleted.
        writer = null;
        file?.Dispose();
        file = null;
    }
}

internal static class ExportExternalSort
{
    // Fixed-size sorted runs and two-way merging bound both memory and open
    // files. The 32 levels cover every possible IReadOnlyList record count.
    internal const int RunSize = 1024;

    internal static ExportDiskList<T> Sort<T>(IEnumerable<T> source, IComparer<T> comparer)
    {
        var levels = new ExportDiskList<T>?[32];
        ExportDiskList<T>? result = null;
        try
        {
            foreach (var chunk in source.Chunk(RunSize))
            {
                Array.Sort(chunk, comparer);
                var run = new ExportDiskList<T>();
                try
                {
                    foreach (var value in chunk) run.Add(value);
                    var level = 0;
                    while (levels[level] is { } prior)
                    {
                        var merged = Merge(prior, run, comparer);
                        prior.Dispose();
                        levels[level] = null;
                        run.Dispose();
                        run = merged;
                        level++;
                    }
                    levels[level] = run;
                }
                catch { run.Dispose(); throw; }
            }
            foreach (var run in levels)
            {
                if (run is null) continue;
                if (result is null) result = run;
                else
                {
                    var merged = Merge(result, run, comparer);
                    result.Dispose();
                    result = merged;
                }
            }
            result ??= new ExportDiskList<T>();
            return result;
        }
        catch { result?.Dispose(); throw; }
        finally
        {
            foreach (var run in levels)
                if (!ReferenceEquals(run, result)) run?.Dispose();
        }
    }

    private static ExportDiskList<T> Merge<T>(IEnumerable<T> first, IEnumerable<T> second, IComparer<T> comparer)
    {
        var result = new ExportDiskList<T>();
        try
        {
            using var left = first.GetEnumerator();
            using var right = second.GetEnumerator();
            var hasLeft = left.MoveNext();
            var hasRight = right.MoveNext();
            while (hasLeft || hasRight)
            {
                if (hasLeft && (!hasRight || comparer.Compare(left.Current, right.Current) <= 0))
                {
                    result.Add(left.Current);
                    hasLeft = left.MoveNext();
                }
                else
                {
                    result.Add(right.Current);
                    hasRight = right.MoveNext();
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
