using System.Buffers.Binary;
using System.Collections;
using System.Text.Json;

namespace SynapseObserver.Core;

internal sealed class SanitizedObservationStore : IDisposable
{
    private readonly SpoolState? _state;
    private Stream? _writer;
    private long _committedLength;
    private int _count;
    private EvidenceStorageFailure? _failure;
    private bool _disposed;

    private SanitizedObservationStore(SpoolState? state, Stream? writer, EvidenceStorageFailure? failure)
    {
        _state = state;
        _writer = writer;
        _failure = failure;
    }

    internal int Count => _count;

    internal EvidenceStorageFailure? Failure => _failure;

    internal static SanitizedObservationStore CreateDefault()
    {
        try
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "SynapseObserver",
                $"session-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "sanitized-observations.spool");
            return Create(path);
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(EvidenceStorageFailure.AccessDenied);
        }
        catch (IOException)
        {
            return Failed(EvidenceStorageFailure.IoFailure);
        }
    }

    internal static SanitizedObservationStore Create(
        string path,
        Func<string, Stream>? writerFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The spool path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var state = new SpoolState(fullPath, directory);
        try
        {
            var writer = writerFactory?.Invoke(fullPath) ?? new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.SequentialScan);
            if (!writer.CanWrite || !writer.CanSeek)
            {
                writer.Dispose();
                throw new ArgumentException("The observation spool writer must be writable and seekable.", nameof(writerFactory));
            }

            return new SanitizedObservationStore(state, writer, null);
        }
        catch
        {
            state.ReleaseOwner();
            throw;
        }
    }

    internal bool TryAppend(SanitizedControlObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failure is not null || _writer is null)
        {
            return false;
        }

        // Only the privacy-reviewed projection reaches this serializer. The raw
        // report and the per-session comparison key never enter the spool.
        var json = JsonSerializer.SerializeToUtf8Bytes(observation);
        if (json.Length is <= 0 or > EvidenceLimits.MaxSanitizedObservationBytes)
        {
            Fail(EvidenceStorageFailure.RecordTooLarge);
            return false;
        }

        var previousLength = _committedLength;
        try
        {
            Span<byte> lengthPrefix = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, json.Length);
            _writer.Write(lengthPrefix);
            _writer.Write(json);
            _committedLength = checked(previousLength + sizeof(int) + json.Length);
            _count++;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            RollBackPartialRecord(previousLength);
            Fail(EvidenceStorageFailure.AccessDenied);
            return false;
        }
        catch (IOException)
        {
            RollBackPartialRecord(previousLength);
            Fail(EvidenceStorageFailure.IoFailure);
            return false;
        }
    }

    internal IReadOnlyList<SanitizedControlObservation> Snapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state is null)
        {
            return Array.Empty<SanitizedControlObservation>();
        }

        return new DiskBackedObservationList(_state.Acquire(), _count, _committedLength);
    }

    private static SanitizedObservationStore Failed(EvidenceStorageFailure failure) =>
        new(null, null, failure);

    private void RollBackPartialRecord(long previousLength)
    {
        try
        {
            if (_writer is not null)
            {
                _writer.SetLength(previousLength);
                _writer.Position = previousLength;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Readers are bounded by the last committed byte length, so an
            // untruncated partial tail remains logically excluded.
        }
    }

    private void Fail(EvidenceStorageFailure failure)
    {
        _failure ??= failure;
        try
        {
            _writer?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The original storage failure remains the actionable result.
        }
        finally
        {
            _writer = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _writer?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Disposal still releases the owner lease so snapshots control the
            // remaining lifetime of the committed sanitized prefix.
        }
        finally
        {
            _writer = null;
            _state?.ReleaseOwner();
            GC.SuppressFinalize(this);
        }
    }

    ~SanitizedObservationStore() => Dispose();

    private sealed class DiskBackedObservationList : IReadOnlyList<SanitizedControlObservation>, IDisposable
    {
        private SpoolLease? _lease;
        private readonly int _count;
        private readonly long _committedLength;

        internal DiskBackedObservationList(SpoolLease lease, int count, long committedLength)
        {
            _lease = lease;
            _count = count;
            _committedLength = committedLength;
        }

        public int Count => _count;

        public SanitizedControlObservation this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                using var enumerator = GetEnumerator();
                for (var current = 0; enumerator.MoveNext(); current++)
                {
                    if (current == index)
                    {
                        return enumerator.Current;
                    }
                }

                throw new InvalidDataException("The sanitized observation spool ended before its committed count.");
            }
        }

        public IEnumerator<SanitizedControlObservation> GetEnumerator()
        {
            var lease = _lease ?? throw new ObjectDisposedException(nameof(EvidenceSessionSnapshot));
            return new ObservationEnumerator(lease.State.Acquire(), _count, _committedLength);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            Interlocked.Exchange(ref _lease, null)?.Dispose();
            GC.SuppressFinalize(this);
        }

        ~DiskBackedObservationList() => Dispose();
    }

    private sealed class ObservationEnumerator : IEnumerator<SanitizedControlObservation>
    {
        private SpoolLease? _lease;
        private FileStream? _reader;
        private readonly int _count;
        private readonly long _committedLength;
        private int _index = -1;

        internal ObservationEnumerator(SpoolLease lease, int count, long committedLength)
        {
            _lease = lease;
            _count = count;
            _committedLength = committedLength;
            try
            {
                _reader = new FileStream(
                    lease.State.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64 * 1024,
                    FileOptions.SequentialScan);
            }
            catch
            {
                _lease = null;
                lease.Dispose();
                throw;
            }
        }

        public SanitizedControlObservation Current { get; private set; } = null!;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            ObjectDisposedException.ThrowIf(_reader is null, this);
            if (_index + 1 >= _count)
            {
                Current = null!;
                return false;
            }

            Span<byte> prefix = stackalloc byte[sizeof(int)];
            _reader.ReadExactly(prefix);
            var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
            if (length is <= 0 or > EvidenceLimits.MaxSanitizedObservationBytes
                || _reader.Position + length > _committedLength)
            {
                throw new InvalidDataException("The sanitized observation spool contains an invalid committed record.");
            }

            var json = new byte[length];
            _reader.ReadExactly(json);
            Current = JsonSerializer.Deserialize<SanitizedControlObservation>(json)
                ?? throw new InvalidDataException("The sanitized observation spool contains an empty record.");
            _index++;
            if (_index == _count - 1 && _reader.Position != _committedLength)
            {
                throw new InvalidDataException("The sanitized observation spool length does not match its committed prefix.");
            }

            return true;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose()
        {
            _reader?.Dispose();
            _reader = null;
            Interlocked.Exchange(ref _lease, null)?.Dispose();
            GC.SuppressFinalize(this);
        }

        ~ObservationEnumerator() => Dispose();
    }

    private sealed class SpoolState
    {
        private int _references = 1;
        private int _ownerReleased;

        internal SpoolState(string path, string directory)
        {
            Path = path;
            Directory = directory;
        }

        internal string Path { get; }

        private string Directory { get; }

        internal SpoolLease Acquire()
        {
            while (true)
            {
                var current = Volatile.Read(ref _references);
                ObjectDisposedException.ThrowIf(current == 0, this);
                if (Interlocked.CompareExchange(ref _references, current + 1, current) == current)
                {
                    return new SpoolLease(this);
                }
            }
        }

        internal void ReleaseOwner()
        {
            if (Interlocked.Exchange(ref _ownerReleased, 1) == 0)
            {
                Release();
            }
        }

        internal void Release()
        {
            if (Interlocked.Decrement(ref _references) != 0)
            {
                return;
            }

            try
            {
                File.Delete(Path);
                if (System.IO.Directory.Exists(Directory)
                    && !System.IO.Directory.EnumerateFileSystemEntries(Directory).Any())
                {
                    System.IO.Directory.Delete(Directory);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best-effort final cleanup must not turn a completed export or
                // session disposal into an application failure.
            }
        }
    }

    private sealed class SpoolLease : IDisposable
    {
        private SpoolState? _state;

        internal SpoolLease(SpoolState state) => _state = state;

        internal SpoolState State => _state ?? throw new ObjectDisposedException(nameof(SpoolLease));

        public void Dispose()
        {
            Interlocked.Exchange(ref _state, null)?.Release();
            GC.SuppressFinalize(this);
        }

        ~SpoolLease() => Dispose();
    }
}
