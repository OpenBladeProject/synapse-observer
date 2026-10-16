using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Interop;
using System.Windows.Threading;
using SynapseObserver.Core;

namespace SynapseObserver.App;

// Foreground-only Raw Input: no hooks, INPUTSINK, NOLEGACY, device writes, or
// synthetic input. Returning unhandled preserves normal Windows input cleanup.
public sealed class SpecialKeyProbe : IDisposable
{
    private const uint RidInput = 0x10000003, RidHeader = 0x10000005;
    private const int MaxPacketBytes = 4096;
    private readonly nint hwnd;
    private readonly HwndSource source;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch elapsed = new();
    private RawInputDevice[] registrations = [];
    private Dictionary<string, LocalHidCandidate> candidates = new(StringComparer.OrdinalIgnoreCase);
    private SpecialKeyProbeParser? parser;
    private Action<SpecialKeyObservation>? callback;
    private Func<bool>? foreground;
    private TimeSpan duration;
    private bool disposed;

    public SpecialKeyProbe(nint hwnd)
    {
        this.hwnd = hwnd;
        source = HwndSource.FromHwnd(hwnd) ?? throw new ArgumentException("An initialized collector window is required.", nameof(hwnd));
        if (GetWindowThreadProcessId(hwnd, out uint processId) == 0 || processId != Environment.ProcessId)
            throw new ArgumentException("The window must belong to this collector.", nameof(hwnd));
        source.Dispatcher.VerifyAccess();
        source.AddHook(WindowProcedure);
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Send, OnTick, source.Dispatcher);
        timer.Stop();
    }

    public bool IsArmed => parser is not null;
    public event Action<SpecialKeyProbeSummary>? Completed;

    public bool Arm(SpecialKeyExpectedKey expectedKey, IReadOnlyList<LocalHidCandidate> inputCandidates,
        Action<SpecialKeyObservation> onObservation, Func<bool> isForeground,
        SpecialKeyModeContext modeContext = SpecialKeyModeContext.Unknown, TimeSpan? timeout = null)
    {
        source.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(onObservation);
        ArgumentNullException.ThrowIfNull(isForeground);
        Disarm();
        if (registrations.Length != 0)
            throw new InvalidOperationException("The previous input registration could not be removed. Close the collector before another probe.");
        duration = timeout ?? TimeSpan.FromSeconds(5);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        parser = new SpecialKeyProbeParser(expectedKey, modeContext);
        callback = onObservation;
        foreground = isForeground;
        if (!SpecialKeyProbeParser.IsSingleBladeRoot(inputCandidates) || !HasFocus())
        {
            End(SpecialKeyProbeEndReason.Unavailable);
            return false;
        }
        candidates = inputCandidates.Where(SpecialKeyProbeParser.IsInputCandidate)
            .DistinctBy(c => c.InterfacePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(c => c.InterfacePath, StringComparer.OrdinalIgnoreCase);
        foreach (var (aliasPath, candidate) in RawInputDeviceAliasResolver.ResolveKeyboardAliases(candidates.Values))
        {
            candidates.TryAdd(aliasPath, candidate);
        }
        registrations = CreateRegistrations(candidates.Values, hwnd);
        if (registrations.Length == 0 || !RegisterRawInputDevices(registrations, (uint)registrations.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            End(SpecialKeyProbeEndReason.Unavailable);
            return false;
        }
        elapsed.Restart();
        timer.Start();
        return true;
    }

    public void Disarm() { source.Dispatcher.VerifyAccess(); End(SpecialKeyProbeEndReason.Cancelled); }
    internal void EndAtCapacity() { source.Dispatcher.VerifyAccess(); End(SpecialKeyProbeEndReason.LimitReached); }

    private bool HasFocus() => GetForegroundWindow() == hwnd && foreground?.Invoke() == true;
    private void OnTick(object? sender, EventArgs args) => CheckActive();
    private bool CheckActive()
    {
        if (parser is null) return false;
        if (!HasFocus()) { End(SpecialKeyProbeEndReason.FocusLost); return false; }
        if (elapsed.Elapsed >= duration) { End(SpecialKeyProbeEndReason.Timeout); return false; }
        return true;
    }

    private nint WindowProcedure(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (parser is null) return 0;
        if ((message == 0x0006 && ((long)wParam & 0xFFFF) == 0) || (message == 0x001C && wParam == 0) || message == 0x0082)
            End(SpecialKeyProbeEndReason.FocusLost);
        else if (message == 0x00FF && CheckActive())
        {
            try { ReadInput(lParam); }
            catch (Exception exception) when (exception is ExternalException or ArgumentException or OverflowException)
            { parser?.Reject(); }
        }
        return 0;
    }

    private void ReadInput(nint handle)
    {
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint size = headerSize;
        nint headerBuffer = Marshal.AllocHGlobal((int)headerSize);
        RawInputHeader header;
        try
        {
            if (GetRawInputData(handle, RidHeader, headerBuffer, ref size, headerSize) != headerSize) { parser?.Reject(); return; }
            header = Marshal.PtrToStructure<RawInputHeader>(headerBuffer);
        }
        finally { ClearAndFree(headerBuffer, (int)headerSize); }
        // Page-only registration can deliver mouse headers too. Never read a
        // mouse body or count unrelated mouse activity as a failed key packet.
        if (header.Type == 0) return;
        if (header.Type is not (1 or 2) || header.Size < headerSize || header.Size > MaxPacketBytes) { parser?.Reject(); return; }
        var candidate = ResolveCandidate(header.Device);
        if (candidate is null || !CheckActive()) return;
        int length = (int)header.Size;
        nint buffer = Marshal.AllocHGlobal(length);
        byte[] body = new byte[length - (int)headerSize];
        try
        {
            size = header.Size;
            uint copied = GetRawInputData(handle, RidInput, buffer, ref size, headerSize);
            if (copied != header.Size || size != header.Size) { parser?.Reject(); return; }
            var actual = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (actual.Size != header.Size || actual.Device != header.Device || actual.Type != header.Type) { parser?.Reject(); return; }
            Marshal.Copy(buffer + (int)headerSize, body, 0, body.Length);
            if (!CheckActive()) return;
            if (header.Type == 1)
            {
                if (body.Length != 16) { parser?.Reject(); return; }
                Emit(parser?.Keyboard(candidate, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(6)),
                    BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2)), elapsed.Elapsed.TotalMilliseconds,
                    BinaryPrimitives.ReadUInt16LittleEndian(body)));
            }
            else
            {
                if (!SpecialKeyProbeParser.TryGetHidShape(body, candidate.ReportGeometry?.InputBytes, out int reportSize, out int count))
                { parser?.Reject(); return; }
                for (int i = 0; i < count && CheckActive(); i++)
                    Emit(parser?.Hid(candidate, body.AsSpan(8 + i * reportSize, reportSize), elapsed.Elapsed.TotalMilliseconds));
            }
        }
        finally { CryptographicOperations.ZeroMemory(body); ClearAndFree(buffer, length); }
    }

    private LocalHidCandidate? ResolveCandidate(nint device)
    {
        uint characters = 0;
        if (GetRawInputDeviceInfo(device, 0x20000007, 0, ref characters) == uint.MaxValue || characters == 0 || characters > 4096) return null;
        int bytes = checked((int)(characters + 1) * 2);
        nint buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            Marshal.WriteInt16(buffer, bytes - 2, 0);
            uint returned = GetRawInputDeviceInfo(device, 0x20000007, buffer, ref characters);
            if (returned == uint.MaxValue || returned >= bytes / 2) return null;
            var path = Marshal.PtrToStringUni(buffer, (int)returned)?.TrimEnd('\0');
            return path is not null && candidates.TryGetValue(path, out var match) ? match : null;
        }
        finally { ClearAndFree(buffer, bytes); }
    }

    private void Emit(SpecialKeyObservation? observation)
    {
        if (observation is null || !CheckActive()) return;
        callback?.Invoke(observation);
        if (parser?.ObservationCount >= SpecialKeyProbeParser.MaxObservations) End(SpecialKeyProbeEndReason.LimitReached);
    }

    private void End(SpecialKeyProbeEndReason reason)
    {
        var current = parser;
        parser = null;
        timer.Stop();
        elapsed.Stop();
        bool removed = true;
        if (registrations.Length != 0)
        {
            var removals = CreateRemovalRegistrations(registrations);
            removed = RegisterRawInputDevices(removals, (uint)removals.Length, (uint)Marshal.SizeOf<RawInputDevice>());
        }
        if (removed) registrations = [];
        candidates.Clear();
        callback = null;
        foreground = null;
        if (current is null) return;
        var summary = current.Summary(removed ? reason : SpecialKeyProbeEndReason.Unavailable);
        if (!removed) summary = summary with { Gap = "Windows did not confirm removal of the input registration. Collection has stopped; close the collector before another probe. " + summary.Gap };
        current.Dispose();
        Completed?.Invoke(summary);
    }

    public void Dispose()
    {
        source.Dispatcher.VerifyAccess();
        if (disposed) return;
        End(SpecialKeyProbeEndReason.Disposed);
        source.RemoveHook(WindowProcedure);
        timer.Tick -= OnTick;
        disposed = true;
    }

    internal static RawInputDevice[] CreateRegistrations(IEnumerable<LocalHidCandidate> inputCandidates, nint target)
    {
        var selected = inputCandidates.Where(SpecialKeyProbeParser.IsInputCandidate).ToArray();
        // Undefined Generic Desktop TLCs carry additional input reports on
        // Blade keyboards. Usage zero requires PAGEONLY, which also covers
        // the selected keyboard TLC; do not add a redundant page-1 entry.
        // This affects delivery only: ResolveCandidate still checks the exact
        // selected device path before ReadInput copies any report body.
        bool desktopPage = selected.Any(c => c.UsagePage == 1 && c.Usage == 0);
        return selected.Select(c => new RawInputDevice
        {
            UsagePage = c.UsagePage!.Value,
            Usage = desktopPage && c.UsagePage == 1 ? (ushort)0 : c.Usage!.Value,
            Flags = desktopPage && c.UsagePage == 1 ? 0x20u : 0,
            Target = target,
        }).DistinctBy(r => (r.UsagePage, r.Usage)).ToArray();
    }

    internal static RawInputDevice[] CreateRemovalRegistrations(IEnumerable<RawInputDevice> active) =>
        active.Select(r => new RawInputDevice
        {
            UsagePage = r.UsagePage, Usage = r.Usage,
            Flags = 1u | (r.Flags & 0x20u), Target = 0,
        }).ToArray();

    private static void ClearAndFree(nint buffer, int length)
    {
        for (int i = 0; i < length; i++) Marshal.WriteByte(buffer, i, 0);
        Marshal.FreeHGlobal(buffer);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct RawInputHeader { public uint Type, Size; public nint Device, WParam; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern uint GetRawInputDeviceInfo(nint device, uint command, nint data, ref uint size);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}

// Pure, bounded parser exercised without registering input or opening hardware.
internal sealed class SpecialKeyProbeParser(SpecialKeyExpectedKey expected, SpecialKeyModeContext mode) : IDisposable
{
    internal const int MaxObservations = 32;
    private readonly byte[] equalityKey = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<string, byte[]> previous = [];
    private readonly Dictionary<string, string> labels = [];
    private readonly HashSet<(string, ushort)> pressed = [];
    private int ignoredTyping, rejected;
    private bool disposed;
    internal int ObservationCount { get; private set; }
    internal void Reject() => rejected = Math.Min(rejected + 1, 1000000);

    internal static bool IsInputCandidate(LocalHidCandidate candidate) =>
        SpecialKeyInputCollection.IsEligible(candidate.VendorId, candidate.DescriptorState,
            candidate.UsagePage, candidate.Usage, candidate.ReportGeometry);

    internal static bool IsSingleBladeRoot(IReadOnlyList<LocalHidCandidate> candidates)
    {
        if (candidates.Count is 0 or > 64) return false;
        string? root = candidates[0].UsbParentDeviceInstanceId;
        return !string.IsNullOrWhiteSpace(root) && candidates.All(c => c.VendorId == 0x1532 &&
            string.Equals(root, c.UsbParentDeviceInstanceId, StringComparison.OrdinalIgnoreCase) &&
            c.UsbParentProductName?.Contains("Blade", StringComparison.OrdinalIgnoreCase) == true);
    }

    internal SpecialKeyObservation? Keyboard(LocalHidCandidate candidate, ushort virtualKey, ushort flags, double milliseconds, ushort? scanCode = null)
    {
        if (disposed || ObservationCount >= MaxObservations) return null;
        if (candidate.UsagePage != 1 || candidate.Usage != 6 || (flags & ~7) != 0) { Reject(); return null; }
        bool admitted = virtualKey is >= 0x70 and <= 0x87 or 0x21 or 0x22 or >= 0xAD and <= 0xB3 or 0x2C or 0x5F ||
            (expected == SpecialKeyExpectedKey.FnP && virtualKey == 0x50);
        if (!admitted) { ignoredTyping = Math.Min(ignoredTyping + 1, 1000000); return null; }
        var key = (candidate.InterfaceLabel, virtualKey);
        bool up = (flags & 1) != 0;
        var phase = up ? SpecialKeyPhase.KeyUp : pressed.Add(key) ? SpecialKeyPhase.KeyDown : SpecialKeyPhase.Repeat;
        if (up) pressed.Remove(key);
        ObservationCount++;
        return new(milliseconds, expected, mode, candidate.InterfaceLabel, SpecialKeyObservationOrigin.WindowsRawInput, phase, virtualKey) { ScanCode = scanCode, IsExtendedKey = (flags & 6) != 0 };
    }

    internal static bool TryGetHidShape(ReadOnlySpan<byte> body, ushort? declaredSize, out int size, out int count)
    {
        size = count = 0;
        if (body.Length < 8) return false;
        uint reportSize = BinaryPrimitives.ReadUInt32LittleEndian(body), reportCount = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
        if (reportSize is 0 or > 1024 || reportCount is 0 or > MaxObservations || reportSize != declaredSize ||
            (ulong)reportSize * reportCount != (ulong)(body.Length - 8)) return false;
        size = (int)reportSize; count = (int)reportCount;
        return true;
    }

    internal SpecialKeyObservation? Hid(LocalHidCandidate candidate, ReadOnlySpan<byte> report, double milliseconds)
    {
        if (disposed || ObservationCount >= MaxObservations) return null;
        if (report.Length is 0 or > 1024 || report.Length != candidate.ReportGeometry?.InputBytes) { Reject(); return null; }
        ObservationCount++;
        if (TryReviewedSignal(candidate, report, out string? signal))
        {
            // These are whole modifier/key states, not independent make/break
            // packets. Preserve the state sequence without inventing key edges.
            return new(milliseconds, expected, mode, candidate.InterfaceLabel, SpecialKeyObservationOrigin.ReviewedHidSpecialKey, SpecialKeyPhase.Unknown,
                ReviewedSignal: signal, ReportLength: report.Length);
        }
        byte[] digest = HMACSHA256.HashData(equalityKey, report);
        string token = Convert.ToHexString(digest);
        CryptographicOperations.ZeroMemory(digest);
        if (!labels.TryGetValue(token, out string? label)) labels[token] = label = $"input-{labels.Count + 1:D2}";
        int? changed = null;
        var ranges = new List<ChangedByteRange>();
        if (previous.Remove(candidate.InterfaceLabel, out byte[]? before))
        {
            changed = 0;
            int start = -1;
            for (int i = 0; i < Math.Max(before.Length, report.Length); i++)
            {
                bool different = i >= before.Length || i >= report.Length || before[i] != report[i];
                if (different) { changed++; if (start < 0) start = i; }
                if (start >= 0 && (!different || i == Math.Max(before.Length, report.Length) - 1))
                { ranges.Add(new(start, (different ? i + 1 : i) - start)); start = -1; }
            }
            CryptographicOperations.ZeroMemory(before);
        }
        previous[candidate.InterfaceLabel] = report.ToArray();
        return new(milliseconds, expected, mode, candidate.InterfaceLabel, SpecialKeyObservationOrigin.UnknownHidMetadata,
            SpecialKeyPhase.Unknown, ReportLength: report.Length, ProbeEqualityLabel: label, ChangedByteCount: changed, ChangedRanges: ranges.Take(16).ToArray()) { ChangedRangesTruncated = ranges.Count > 16 };
    }

    // Privacy projection derived from OpenBlade.Tray.Core/SpecialKeyRawInput.cs.
    // Intentionally narrower than its decoder: exact known model, 04 envelope,
    // approved signal bytes, zero padding. No arbitrary keyboard report bytes.
    private static bool TryReviewedSignal(LocalHidCandidate candidate, ReadOnlySpan<byte> report, out string? signal)
    {
        signal = null;
        string? model = candidate.UsbParentProductName;
        bool exactModel = candidate.ProductId == 0x02E0 && model?.Contains("RZ09-0581", StringComparison.OrdinalIgnoreCase) == true ||
            candidate.ProductId == 0x02C6 && model?.Contains("RZ09-0528", StringComparison.OrdinalIgnoreCase) == true;
        if (!exactModel || candidate.VendorId != 0x1532 || report.Length is < 3 or > 32 || report[0] != 4) return false;
        for (int i = 3; i < report.Length; i++) if (report[i] != 0) return false;
        signal = (report[1], report[2]) switch
        {
            (0, 0) => "Released", (0x0A, 0) => "Fn", (0x0A, 3) => "Fn+M3",
            (0x0A, 0xD3) => "Fn+M4", (0x0A, 0xD4) => "Fn+M5",
            (3, 0) => "M3", (0xD4, 0) => "M5", _ => null,
        };
        return signal is not null;
    }

    internal SpecialKeyProbeSummary Summary(SpecialKeyProbeEndReason reason) => new(expected, mode, reason, ObservationCount,
        ignoredTyping, rejected, "Input is not firmware readback. Expected key and mode are user-reported. Ordinary typing and macros are omitted; P is admitted only for the Fn+P prompt and does not prove Fn. Unknown HID bodies are redacted. No event does not prove that a key or capability is absent.");

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(equalityKey);
        foreach (var value in previous.Values) CryptographicOperations.ZeroMemory(value);
        previous.Clear(); labels.Clear(); pressed.Clear(); disposed = true;
    }
}
