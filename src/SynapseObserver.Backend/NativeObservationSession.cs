using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace SynapseObserver.Backend;
public interface INativeObservationSession : IAsyncDisposable
{
    NativeSessionStatus GetStatus();
    Task<IReadOnlyList<NativeObservationEvent>> PollAsync(CancellationToken cancellationToken = default);
    Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default);
}

public sealed class NativeObservationSession : INativeObservationSession
{
    private const int HeaderSize = 32;
    private const int Slots = 32;
    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor view;
    private readonly SafeProcessHandle process;
    private readonly SafeFileHandle worker;
    private readonly long origin;
    private bool disposed;
    private NativeObservationSession(MemoryMappedFile mapping, MemoryMappedViewAccessor view, SafeProcessHandle process, SafeFileHandle worker, long origin)
    { this.mapping = mapping; this.view = view; this.process = process; this.worker = worker; this.origin = origin; }
    private static readonly SemaphoreSlim StartGate = new(1, 1);
    public static Task<NativeObservationSession> StartAsync(BackendCandidate candidate, string selectedInterfacePath, CancellationToken cancellationToken = default) => StartAsync(candidate, new[] { selectedInterfacePath }, cancellationToken);
    public static Task<NativeObservationSession> StartAsync(BackendCandidate candidate, IReadOnlyList<string> selectedInterfacePaths, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        StartGate.Wait(cancellationToken);
        try { return Start(candidate, selectedInterfacePaths, cancellationToken); }
        finally { StartGate.Release(); }
    }, cancellationToken);
    private static NativeObservationSession Start(BackendCandidate candidate, IReadOnlyList<string> interfacePaths, CancellationToken token)
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
        string dll = Path.Combine(AppContext.BaseDirectory, "SynapseObserver.Native.dll");
        using var lockedDll = File.Open(dll, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(lockedDll)).Equals(NativePayload.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The observer DLL does not match this build. Reinstall the matching package.");
        if (target.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("SynapseObserver.Native.dll", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("This Synapse process already hosted an observer. Restart Synapse manually before another session.");
        string mapName = "Local\\SynapseObserver-" + Guid.NewGuid().ToString("N");
        var mapping = MemoryMappedFile.CreateNew(mapName, HeaderSize + Slots * NativeEventParser.RecordSize, MemoryMappedFileAccess.ReadWrite);
        var view = mapping.CreateViewAccessor();
        var process = Native.OpenProcess(0x043A, false, candidate.ProcessId);
        if (process.IsInvalid) { view.Dispose(); mapping.Dispose(); process.Dispose(); throw new Win32Exception(); }
        SafeFileHandle? worker = null;
        try
        {
            // Pin identity again after opening the actual operation handle. A PID
            // cannot be reused while target's Process handle remains open.
            TargetPolicy.Validate(target);
            token.ThrowIfCancellationRequested();
            LoadFixedDll(target, process, dll);
            target.Refresh();
            var remoteModule = target.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.FileName, dll, StringComparison.OrdinalIgnoreCase));
            IntPtr local = Native.LoadLibraryExW(dll, IntPtr.Zero, 1); // map only; do not execute DllMain locally
            if (local == IntPtr.Zero) throw new Win32Exception();
            IntPtr start;
            try { var export = Native.GetProcAddress(local, "ObserverStart"); if (export == IntPtr.Zero) throw new InvalidOperationException("Observer export missing."); start = remoteModule.BaseAddress + checked((int)(export - local)); }
            finally { Native.FreeLibrary(local); }
            byte[] config = new byte[8492];
            BitConverter.GetBytes((uint)config.Length).CopyTo(config, 0); BitConverter.GetBytes(300u).CopyTo(config, 4);
            Encoding.Unicode.GetBytes(mapName).CopyTo(config, 8);
            BitConverter.GetBytes((uint)selectedNames.Length).CopyTo(config, 264);
            for (int i = 0; i < selectedNames.Length; ++i) Encoding.Unicode.GetBytes(selectedNames[i]).CopyTo(config, 268 + i * 1024);
            for (int i = 0; i < selected.Length; ++i) BitConverter.GetBytes((uint)selected[i]!.ReportGeometry!.FeatureBytes).CopyTo(config, 8460 + 4 * i);
            IntPtr remoteConfig = WriteRemote(process, config);
            worker = Native.CreateRemoteThread(process, IntPtr.Zero, 0, start, remoteConfig, 0, out _);
            if (worker.IsInvalid) { Native.VirtualFreeEx(process, remoteConfig, 0, 0x8000); throw new Win32Exception(); }
            var session = new NativeObservationSession(mapping, view, process, worker, origin);
            for (int i = 0; i < 500; ++i)
            {
                if (view.ReadInt32(0) != 0) { Native.VirtualFreeEx(process, remoteConfig, 0, 0x8000); return session; }
                if (view.ReadInt32(4) != 0 || Native.WaitForSingleObject(worker, 0) == 0) { Native.VirtualFreeEx(process, remoteConfig, 0, 0x8000); throw new InvalidOperationException("The native observer could not install all hooks. Partial evidence remains available; restart Synapse before retrying."); }
                if (token.IsCancellationRequested) break;
                Thread.Sleep(10);
            }
            view.Write(12, 1); // bounded native session also expires automatically
            throw new InvalidOperationException("Observer startup was not confirmed. A stop was requested; the DLL remains resident until Synapse exits. Restart Synapse manually before retrying.");
        }
        catch { worker?.Dispose(); process.Dispose(); view.Dispose(); mapping.Dispose(); throw; }
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
        if (read < 0 || write < read || write - read > Slots) throw new InvalidDataException("Observer ring counters are invalid.");
        while (read < write)
        {
            var bytes = new byte[NativeEventParser.RecordSize]; view.ReadArray(HeaderSize + (read % Slots) * NativeEventParser.RecordSize, bytes, 0, bytes.Length);
            events.Add(NativeEventParser.Parse(bytes, origin, Stopwatch.Frequency));
            Thread.MemoryBarrier(); view.Write(20, ++read);
        }
        return Task.FromResult<IReadOnlyList<NativeObservationEvent>>(events);
    }
    public async Task<NativeSessionStatus> StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this); view.Write(12, 1);
        for (int i = 0; i < 100 && view.ReadInt32(4) == 0; ++i) await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        var status = GetStatus();
        return status.Finished ? status : status with { Failure = 170, Diagnostic = "Stop was requested but detachment was not confirmed. The module stays resident. Restart Synapse manually before another recording." };
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        try { await StopAsync().ConfigureAwait(false); }
        finally { disposed = true; worker.Dispose(); process.Dispose(); view.Dispose(); mapping.Dispose(); }
    }
    private static IntPtr WriteRemote(SafeProcessHandle process, byte[] bytes)
    {
        IntPtr address = Native.VirtualAllocEx(process, IntPtr.Zero, (nuint)bytes.Length, 0x3000, 4);
        if (address == IntPtr.Zero) throw new Win32Exception();
        if (!Native.WriteProcessMemory(process, address, bytes, (nuint)bytes.Length, out var written) || written != (nuint)bytes.Length) { Native.VirtualFreeEx(process, address, 0, 0x8000); throw new Win32Exception(); }
        return address;
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
        if (Native.WaitForSingleObject(thread, 5000) != 0) throw new InvalidOperationException("Observer loading was not confirmed. Restart Synapse manually before retrying.");
        Native.VirtualFreeEx(process, path, 0, 0x8000);
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
