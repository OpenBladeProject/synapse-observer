using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
namespace SynapseObserver.Backend;
public interface INativeObservationSession : IAsyncDisposable
{
    long OriginTimestampTicks { get; }
    NativeSessionStatus GetStatus();
    Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default);
    Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default);
    // Owned test fixtures without a resident registration need no invalidation state.
    void InvalidateReuse() { }
}

public sealed class NativeObservationSession : INativeObservationSession
{
    internal const int HeaderSize = 32;
    internal const int Slots = 32;
    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor view;
    private readonly SafeProcessHandle process;
    private readonly SafeFileHandle worker;
    private readonly long origin;
    private readonly ResidentObserverRegistration? resident;
    private readonly SemaphoreSlim stopGate = new(1, 1);
    public long OriginTimestampTicks => origin;
    private bool disposed;
    private bool stopRequested;
    private int cleanupResolution;
    private NativeObservationSession(MemoryMappedFile mapping, MemoryMappedViewAccessor view, SafeProcessHandle process, SafeFileHandle worker, long origin, ResidentObserverRegistration? resident)
    { this.mapping = mapping; this.view = view; this.process = process; this.worker = worker; this.origin = origin; this.resident = resident; }
    internal NativeObservationSession(MemoryMappedFile mapping, long origin)
        : this(mapping, mapping.CreateViewAccessor(), new SafeProcessHandle(IntPtr.Zero, false), new SafeFileHandle(IntPtr.Zero, false), origin, null) { }
    internal NativeObservationSession(
        MemoryMappedFile mapping,
        long origin,
        ResidentObserverRegistration resident,
        SafeFileHandle worker)
        : this(mapping, mapping.CreateViewAccessor(), new SafeProcessHandle(IntPtr.Zero, false), worker, origin, resident) { }
    private static readonly SemaphoreSlim StartGate = new(1, 1);
    private static readonly Dictionary<int, ResidentObserverRegistration> Residents = [];
    public static Task<NativeObservationSession> StartAsync(BackendCandidate candidate, string selectedInterfacePath, CancellationToken cancellationToken = default) => StartAsync(candidate, new[] { selectedInterfacePath }, cancellationToken);
    public static Task<NativeObservationSession> StartAsync(BackendCandidate candidate, IReadOnlyList<string> selectedInterfacePaths, CancellationToken cancellationToken = default) => StartAsync(candidate, selectedInterfacePaths, 300, cancellationToken);
    public static Task<NativeObservationSession> StartAsync(BackendCandidate candidate, IReadOnlyList<string> selectedInterfacePaths, uint durationSeconds, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (durationSeconds is < 1 or > 300) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        StartGate.Wait(cancellationToken);
        try { return Start(candidate, selectedInterfacePaths, durationSeconds, cancellationToken); }
        finally { StartGate.Release(); }
    }, cancellationToken);
    private static NativeObservationSession Start(BackendCandidate candidate, IReadOnlyList<string> interfacePaths, uint durationSeconds, CancellationToken token)
    {
        long origin = Stopwatch.GetTimestamp();
        token.ThrowIfCancellationRequested();
        using var target = Process.GetProcessById(candidate.ProcessId);
        TargetPolicy.Validate(target); // Do not trust a stale discovery result or caller-supplied Eligible.
        if (interfacePaths.Count is < 1 or > 8 || interfacePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != interfacePaths.Count) throw new InvalidOperationException("Select one to eight distinct laptop feature collections.");
        var localCandidates = new SynapseObserver.Core.WindowsInventorySource().DiscoverLocalControlCandidates(token);
        var selected = interfacePaths.Select(path => localCandidates.Value.SingleOrDefault(c => string.Equals(c.InterfacePath, path, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (selected.Any(c => c is null || !BackendDiscovery.IsEligibleControlCandidate(c))) throw new InvalidOperationException("The selected interfaces are not tied to a Razer Blade laptop parent with feature reports. Inventory remains available.");
        if (!BackendDiscovery.IsSamePhysicalLaptop(selected.Select(c => c!).ToArray())) throw new InvalidOperationException("All observed feature collections must belong to one verified physical laptop device.");
        var selectedNames = interfacePaths.Select(ResolveSelectedObject).ToArray();
        if (selectedNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedNames.Length) throw new InvalidOperationException("Windows mapped multiple selected interfaces to one object. Collection identity is ambiguous; inventory remains available.");
        string dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "SynapseObserver.Native.dll"));
        using var lockedDll = File.Open(dll, FileMode.Open, FileAccess.Read, FileShare.Read);
        string lockedDllPath = FinalFilePath(lockedDll.SafeFileHandle);
        string payloadHash = Convert.ToHexString(SHA256.HashData(lockedDll));
        if (!payloadHash.Equals(NativePayload.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The observer DLL does not match this build. Reinstall the matching package.");
        string mapName = "Local\\SynapseObserver-" + Guid.NewGuid().ToString("N");
        var mapping = MemoryMappedFile.CreateNew(mapName, HeaderSize + Slots * NativeEventParser.RecordSize, MemoryMappedFileAccess.ReadWrite);
        var view = mapping.CreateViewAccessor();
        var process = Native.OpenProcess(0x043A, false, candidate.ProcessId);
        if (process.IsInvalid) { view.Dispose(); mapping.Dispose(); process.Dispose(); throw new Win32Exception(); }
        SafeFileHandle? worker = null;
        ResidentObserverRegistration? resident = null;
        bool recordingClaimed = false;
        try
        {
            // Pin identity again after opening the actual operation handle. A PID
            // cannot be reused while target's Process handle remains open.
            TargetPolicy.Validate(target);
            token.ThrowIfCancellationRequested();
            long processStartTime = target.StartTime.ToUniversalTime().Ticks;
            ProcessModule[] existingModules = ObserverModules(target);
            bool knownResident = Residents.TryGetValue(candidate.ProcessId, out resident);
            if (knownResident && ShouldDiscardStaleResident(resident!, processStartTime, existingModules.Length))
            {
                Residents.Remove(candidate.ProcessId);
                resident = null;
                knownResident = false;
            }
            if (!knownResident)
            {
                if (existingModules.Length != 0)
                    throw new InvalidOperationException("This Synapse process hosts an observer that this collector did not verify. Restart Synapse before another recording.");
                LoadFixedDll(target, process, dll);
                target.Refresh();
                existingModules = ObserverModules(target);
            }

            ProcessModule remoteModule;
            try { remoteModule = RequireExactResidentModule(existingModules, lockedDllPath); }
            catch { if (knownResident) resident!.Fault(); throw; }
            var currentIdentity = new ResidentObserverIdentity(
                candidate.ProcessId,
                processStartTime,
                lockedDllPath,
                payloadHash,
                remoteModule.BaseAddress);
            if (!knownResident)
            {
                resident = new ResidentObserverRegistration(currentIdentity);
                Residents.Add(candidate.ProcessId, resident);
            }

            ResidentStartDecision decision = resident!.TryBegin(currentIdentity);
            if (decision != ResidentStartDecision.Started)
                throw new InvalidOperationException(decision switch
                {
                    ResidentStartDecision.Busy => "The resident observer is still recording or its previous worker has not completed.",
                    ResidentStartDecision.IdentityMismatch => "The resident observer identity changed. Restart Synapse before another recording.",
                    _ => "The resident observer had an uncertain cleanup and cannot be reused. Restart Synapse before another recording.",
                });
            recordingClaimed = true;

            IntPtr local = Native.LoadLibraryExW(dll, IntPtr.Zero, 1); // map only; do not execute DllMain locally
            if (local == IntPtr.Zero) throw new Win32Exception();
            IntPtr start;
            try { var export = Native.GetProcAddress(local, "ObserverStart"); if (export == IntPtr.Zero) throw new InvalidOperationException("Observer export missing."); start = remoteModule.BaseAddress + checked((int)(export - local)); }
            finally { Native.FreeLibrary(local); }
            byte[] config = new byte[8492];
            BitConverter.GetBytes((uint)config.Length).CopyTo(config, 0); BitConverter.GetBytes(durationSeconds).CopyTo(config, 4);
            Encoding.Unicode.GetBytes(mapName).CopyTo(config, 8);
            BitConverter.GetBytes((uint)selectedNames.Length).CopyTo(config, 264);
            for (int i = 0; i < selectedNames.Length; ++i) Encoding.Unicode.GetBytes(selectedNames[i]).CopyTo(config, 268 + i * 1024);
            for (int i = 0; i < selected.Length; ++i) BitConverter.GetBytes((uint)selected[i]!.ReportGeometry!.FeatureBytes).CopyTo(config, 8460 + 4 * i);
            IntPtr remoteConfig = WriteRemote(process, config);
            worker = Native.CreateRemoteThread(process, IntPtr.Zero, 0, start, remoteConfig, 0, out _);
            if (worker.IsInvalid) { Native.VirtualFreeEx(process, remoteConfig, 0, 0x8000); throw new Win32Exception(); }
            var session = new NativeObservationSession(mapping, view, process, worker, origin, resident);
            for (int i = 0; i < 500; ++i)
            {
                if (view.ReadInt32(0) != 0) { Native.VirtualFreeEx(process, remoteConfig, 0, 0x8000); return session; }
                if (view.ReadInt32(4) != 0 || Native.WaitForSingleObject(worker, 0) == 0) { Native.VirtualFreeEx(process, remoteConfig, 0, 0x8000); throw new InvalidOperationException("The native observer could not enable all hooks. Partial evidence remains available; restart Synapse before retrying."); }
                if (token.IsCancellationRequested) break;
                Thread.Sleep(10);
            }
            view.Write(12, 1); // bounded native session also expires automatically
            throw new InvalidOperationException("Observer startup was not confirmed. A stop was requested; the DLL remains resident until Synapse exits. Restart Synapse manually before retrying.");
        }
        catch
        {
            if (recordingClaimed) resident?.Fault();
            worker?.Dispose(); process.Dispose(); view.Dispose(); mapping.Dispose(); throw;
        }
    }
    public NativeSessionStatus GetStatus()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        bool started = view.ReadInt32(0) != 0, finished = view.ReadInt32(4) != 0;
        int failure = view.ReadInt32(8), dropped = view.ReadInt32(24);
        return new(started, finished, failure, dropped, failure != 0 ? "Observation failed or detachment was not confirmed. Restart Synapse manually before another session." : finished ? "Hooks stopped. The inert observer DLL remains resident until Synapse exits; pending asynchronous completions were not captured." : "Observing completed feature API calls. Pending asynchronous completions are reported as gaps.");
    }
    public Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this); cancellationToken.ThrowIfCancellationRequested();
        var events = new List<NativeObservationEvent>();
        int read = view.ReadInt32(20), write = view.ReadInt32(16);
        Thread.MemoryBarrier();
        if (read < 0 || write < read || write - read > Slots)
        {
            InvalidateReuse();
            throw new InvalidDataException("Observer ring counters are invalid.");
        }
        int rejected = 0;
        try
        {
            while (read < write)
            {
                var bytes = new byte[NativeEventParser.RecordSize];
                int offset = HeaderSize + (read % Slots) * NativeEventParser.RecordSize;
                try
                {
                    if (view.ReadArray(offset, bytes, 0, bytes.Length) != bytes.Length)
                        throw new IOException("Observer ring slot could not be read completely.");
                    try { events.Add(NativeEventParser.Parse(bytes, origin, Stopwatch.Frequency)); }
                    catch (InvalidDataException) { rejected++; }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                    // Clear every consumed or rejected slot before releasing it to the producer.
                    view.WriteArray(offset, bytes, 0, bytes.Length);
                }
                Thread.MemoryBarrier(); view.Write(20, read + 1); read++;
            }
        }
        catch
        {
            ClearEvents(events);
            InvalidateReuse();
            throw;
        }
        if (rejected != 0)
        {
            InvalidateReuse();
            throw new NativeObservationPollException(events, rejected);
        }
        return Task.FromResult<IReadOnlyList<NativeObservationEvent>>(events);
    }

    private static void ClearEvents(IEnumerable<NativeObservationEvent> events)
    {
        foreach (var item in events)
        {
            CryptographicOperations.ZeroMemory(item.RequestBytes);
            CryptographicOperations.ZeroMemory(item.ResponseBytes);
        }
    }
    public async Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await stopGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            view.Write(12, 1); stopRequested = true;
            for (int i = 0; i < 100 && view.ReadInt32(4) == 0; ++i)
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            var status = GetStatus();
            if (!status.Finished)
            {
                ResolveCleanup(false);
                return status with { Failure = 170, Diagnostic = "Stop was requested but hook cleanup was not confirmed. The resident observer is blocked until Synapse restarts." };
            }

            bool workerFinished = worker.IsInvalid;
            for (int i = 0; !workerFinished && i < 100; ++i)
            {
                workerFinished = Native.WaitForSingleObject(worker, 0) == 0;
                if (!workerFinished) await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
            if (!workerFinished)
            {
                ResolveCleanup(false);
                return status with { Failure = 170, Diagnostic = "Hooks stopped, but the observer worker did not complete. The resident observer is blocked until Synapse restarts." };
            }

            bool cleanupWasAlreadyUncertain = Volatile.Read(ref cleanupResolution) == 2;
            ResolveCleanup(status.Failure == 0);
            if (cleanupWasAlreadyUncertain)
                return status with { Failure = 170, Diagnostic = "Hook cleanup was previously uncertain. Late completion cannot make the resident observer reusable; restart Synapse before another recording." };
            return status.Failure == 0
                ? status with { Diagnostic = "Hooks stopped and the observer worker completed. The verified resident observer can serve another recording." }
                : status with { Diagnostic = "Observation failed or hook cleanup was uncertain. Restart Synapse before another recording." };
        }
        catch
        {
            ResolveCleanup(false);
            throw;
        }
        finally { stopGate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        try
        {
            try
            {
                // Explicit callers own bounded waits and final evidence collection.
                // Do not hide another wait after their last status/record read.
                if (!stopRequested) await StopAsync().ConfigureAwait(false);
                else if (Volatile.Read(ref cleanupResolution) == 0) ResolveCleanup(false);
            }
            finally
            {
                disposed = true;
                worker.Dispose(); process.Dispose(); view.Dispose(); mapping.Dispose(); stopGate.Dispose();
            }
        }
        catch
        {
            InvalidateReuse();
            throw;
        }
    }
    public void InvalidateReuse() => ResolveCleanup(false);
    private void ResolveCleanup(bool confirmed)
    {
        int resolution = confirmed ? 1 : 2;
        int prior = Interlocked.CompareExchange(ref cleanupResolution, resolution, 0);
        if (prior == 0) resident?.Complete(confirmed);
        else if (!confirmed && prior == 1)
        {
            Interlocked.Exchange(ref cleanupResolution, 2);
            resident?.Fault();
        }
    }
    private static IntPtr WriteRemote(SafeProcessHandle process, byte[] bytes)
    {
        IntPtr address = Native.VirtualAllocEx(process, IntPtr.Zero, (nuint)bytes.Length, 0x3000, 4);
        if (address == IntPtr.Zero) throw new Win32Exception();
        if (!Native.WriteProcessMemory(process, address, bytes, (nuint)bytes.Length, out var written) || written != (nuint)bytes.Length) { Native.VirtualFreeEx(process, address, 0, 0x8000); throw new Win32Exception(); }
        return address;
    }
    private static ProcessModule[] ObserverModules(Process target) => target.Modules
        .Cast<ProcessModule>()
        .Where(static module => string.Equals(
            module.ModuleName,
            "SynapseObserver.Native.dll",
            StringComparison.OrdinalIgnoreCase))
        .ToArray();
    internal static bool ShouldDiscardStaleResident(
        ResidentObserverRegistration registration,
        long currentProcessStartTimeUtcTicks,
        int observerModuleCount) =>
        registration.Identity.ProcessStartTimeUtcTicks != currentProcessStartTimeUtcTicks
        && observerModuleCount == 0;
    private static ProcessModule RequireExactResidentModule(
        IReadOnlyList<ProcessModule> modules,
        string expectedPath)
    {
        if (modules.Count != 1)
            throw new InvalidOperationException("The exact resident observer module is unavailable. Restart Synapse before another recording.");
        string actualPath = FinalFilePath(modules[0].FileName);
        if (!string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The resident observer path does not match this build. Restart Synapse before another recording.");
        return modules[0];
    }
    private static string FinalFilePath(SafeFileHandle file)
    {
        var buffer = new StringBuilder(4096);
        uint count = Native.GetFinalPathNameByHandleW(file, buffer, (uint)buffer.Capacity, 0);
        if (count == 0 || count >= buffer.Capacity)
            throw new InvalidOperationException("The observer DLL's final identity is unavailable.");
        string resolved = buffer.ToString();
        if (resolved.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) return "\\\\" + resolved[8..];
        return resolved.StartsWith("\\\\?\\", StringComparison.Ordinal) ? resolved[4..] : resolved;
    }
    private static string FinalFilePath(string path)
    {
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return FinalFilePath(file);
    }
    private static void LoadFixedDll(Process target, SafeProcessHandle process, string dll)
    {
        // Resolve the actual owning module: LoadLibraryW may be forwarded.
        IntPtr localKernel = Native.GetModuleHandleW("kernel32.dll");
        IntPtr loader = Native.GetProcAddress(localKernel, "LoadLibraryW");
        using var self = Process.GetCurrentProcess();
        var owner = self.Modules.Cast<ProcessModule>().Single(m => loader.ToInt64() >= m.BaseAddress.ToInt64() && loader.ToInt64() < m.BaseAddress.ToInt64() + m.ModuleMemorySize);
        var remoteOwner = target.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.ModuleName, owner.ModuleName, StringComparison.OrdinalIgnoreCase));
        IntPtr remoteLoader = remoteOwner.BaseAddress + checked((int)(loader - owner.BaseAddress));
        IntPtr path = WriteRemote(process, Encoding.Unicode.GetBytes(dll + '\0'));
        using var thread = Native.CreateRemoteThread(process, IntPtr.Zero, 0, remoteLoader, path, 0, out _);
        if (thread.IsInvalid) { Native.VirtualFreeEx(process, path, 0, 0x8000); throw new Win32Exception(); }
        if (Native.WaitForSingleObject(thread, 5000) != 0)
            throw new InvalidOperationException("Observer loading was not confirmed. Restart Synapse manually before retrying.");
        Native.VirtualFreeEx(process, path, 0, 0x8000);
        if (!Native.GetExitCodeThread(thread, out uint loadedModule) || loadedModule == 0)
            throw new InvalidOperationException("The matching observer DLL was not loaded. Restart Synapse manually before retrying.");
    }
    private static string ResolveSelectedObject(string interfacePath)
    {
        if (!interfacePath.StartsWith("\\\\?\\hid#", StringComparison.OrdinalIgnoreCase) || interfacePath.Length > 2048) throw new InvalidOperationException("Select a locally enumerated HID control interface.");
        using var handle = Native.CreateFileW(interfacePath, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new InvalidOperationException("The selected control interface cannot be inspected without device access.");
        var attributes = new Native.HidAttributes { Size = Marshal.SizeOf<Native.HidAttributes>() };
        if (!Native.HidD_GetAttributes(handle, ref attributes) || attributes.VendorId != 0x1532 || !Native.HidD_GetPreparsedData(handle, out var preparsed)) throw new InvalidOperationException("The selected interface is not a verified Razer HID device.");
        try { if (Native.HidP_GetCaps(preparsed, out var caps) != 0x110000 || caps.FeatureReportByteLength == 0) throw new InvalidOperationException("Select a laptop control interface with feature reports. Input reports are never observed."); }
        finally { Native.HidD_FreePreparsedData(preparsed); }
        IntPtr data = Marshal.AllocHGlobal(4096);
        try {
            if (Native.NtQueryObject(handle, 1, data, 4096, out _) < 0) throw new InvalidOperationException("Windows did not provide an exact control-handle object name. Inventory remains available.");
            int length = (ushort)Marshal.ReadInt16(data); IntPtr pointer = Marshal.ReadIntPtr(data, 8);
            if (length == 0 || length >= 1024 || pointer.ToInt64() < data.ToInt64() || pointer.ToInt64() + length > data.ToInt64() + 4096) throw new InvalidOperationException("The control-handle identity is unavailable.");
            string name = Marshal.PtrToStringUni(pointer, length / 2) ?? "";
            if (!name.StartsWith("\\Device\\", StringComparison.Ordinal)) throw new InvalidOperationException("The control-handle identity is not supported.");
            return name;
        } finally { Marshal.FreeHGlobal(data); }
    }
}
