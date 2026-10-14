using System.Buffers.Binary;
using System.Diagnostics;
namespace SynapseObserver.Backend;
public sealed record BackendCandidate(int ProcessId, string Name, bool Eligible, string Diagnostic);
public sealed record NativeObservationEvent(ulong Sequence, double RelativeMilliseconds, string Api, string Operation, bool Result, uint NativeError, byte[] RequestBytes, byte[] ResponseBytes, string? CompletionGap, uint RequestedBytes, uint ReturnedBytes, uint InterfaceIndex, double CompletionRelativeMilliseconds, uint OutputBufferCapacity = 0);
public sealed record NativeSessionStatus(bool Started, bool Finished, int Failure, int DroppedEvents, string Diagnostic);
public static class BackendDiscovery
{
    public static bool IsEligibleControlCandidate(SynapseObserver.Core.LocalHidCandidate candidate) =>
        candidate.VendorId == 0x1532 && candidate.ReportGeometry?.FeatureBytes > 0 &&
        candidate.UsbParentProductName?.Contains("Blade", StringComparison.OrdinalIgnoreCase) == true &&
        candidate.UsbParentBaseHardwareIdentity?.StartsWith("USB\\VID_1532&", StringComparison.OrdinalIgnoreCase) == true;
    public static bool IsSamePhysicalLaptop(IReadOnlyList<SynapseObserver.Core.LocalHidCandidate> candidates) =>
        candidates.Count is >= 1 and <= 8 && candidates.All(IsEligibleControlCandidate) &&
        candidates.All(c => !string.IsNullOrEmpty(c.UsbParentDeviceInstanceId)) &&
        candidates.Select(c => c.UsbParentDeviceInstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
    public static Task<IReadOnlyList<BackendCandidate>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<BackendCandidate>>(() =>
    {
        var result = new List<BackendCandidate>();
        foreach (var process in Process.GetProcessesByName("RazerAppEngine"))
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { TargetPolicy.Validate(process); result.Add(new(process.Id, "Razer Synapse engine", true, "Razer engine and HID module signatures and roles verified. Feature-call coverage may be partial.")); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or IOException or System.Security.Cryptography.CryptographicException)
                { result.Add(new(process.Id, "Razer Synapse engine", false, TargetPolicy.SafeDiagnostic(ex))); }
            }
        }
        return result;
    }, cancellationToken);
}
public static class NativeEventParser
{
    public const int RecordSize = 8256;
    public const int OutputBufferCapacityOffset = 8244;
    public const int CompletionTimestampOffset = 8248;
    public static NativeObservationEvent Parse(ReadOnlySpan<byte> record, long originTicks, long frequency)
    {
        if (record.Length != RecordSize || frequency <= 0) throw new InvalidDataException("Invalid observer record geometry.");
        static uint U(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
        var operation = U(record, 16); var gap = U(record, 20); var inputLength = U(record, 40); var outputLength = U(record, 44);
        var returnedBytes = U(record, 36); var outputBufferCapacity = U(record, OutputBufferCapacityOffset);
        if (U(record, 48) >= 8 || operation > 3 || gap > 5 || inputLength > 4096 || outputLength > 4096) throw new InvalidDataException("Invalid bounded observer record.");
        if (outputBufferCapacity != 0 && (outputLength > outputBufferCapacity || returnedBytes > outputBufferCapacity)) throw new InvalidDataException("Observer response exceeds its output buffer capacity.");
        long entryTicks = BinaryPrimitives.ReadInt64LittleEndian(record.Slice(8, 8));
        long completionTicks = BinaryPrimitives.ReadInt64LittleEndian(record.Slice(CompletionTimestampOffset, 8));
        if (completionTicks < entryTicks) throw new InvalidDataException("Observer completion precedes call entry.");
        var error = U(record, 28); var success = U(record, 24) != 0;
        if ((!success || gap == 1) && outputLength != 0) throw new InvalidDataException("Uncompleted call contains response bytes.");
        var names = new[] { "HidD_SetFeature", "HidD_GetFeature", "DeviceIoControl.HID_SET_FEATURE", "DeviceIoControl.HID_GET_FEATURE" };
        var gaps = new string?[] { null, "pending-completion-not-observed", "buffer-exceeds-limit", "unreadable-buffer", "queue-full", "return-length-unknown" };
        return new(BinaryPrimitives.ReadUInt64LittleEndian(record), (entryTicks - originTicks) * 1000.0 / frequency,
            names[operation], operation is 0 or 2 ? "set-feature" : "get-feature", success, error,
            record.Slice(52, checked((int)inputLength)).ToArray(), record.Slice(4148, checked((int)outputLength)).ToArray(), gaps[gap], U(record, 32), returnedBytes, U(record, 48), (completionTicks - originTicks) * 1000.0 / frequency, outputBufferCapacity);
    }
}
