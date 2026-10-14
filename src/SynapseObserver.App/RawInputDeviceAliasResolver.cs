using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SynapseObserver.Core;

namespace SynapseObserver.App;

// Raw Input names keyboard devices through GUID_DEVINTERFACE_KEYBOARD, while
// HID inventory uses GUID_DEVINTERFACE_HID. Associate those distinct paths only
// through the exact local PnP device instance reported by SetupAPI.
internal static class RawInputDeviceAliasResolver
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const int ErrorNoMoreItems = 259;
    private const int CrSuccess = 0;
    private const int MaxInterfaces = 256;
    private static readonly Guid KeyboardInterfaceClass = new("884b96c3-56ef-11d1-bc8c-00a0c91405dd");

    internal static IReadOnlyDictionary<string, LocalHidCandidate> ResolveKeyboardAliases(
        IEnumerable<LocalHidCandidate> candidates)
    {
        var selected = candidates
            .Where(static candidate => candidate.UsagePage == 1 && candidate.Usage == 6)
            .ToArray();
        if (selected.Length == 0)
        {
            return new Dictionary<string, LocalHidCandidate>(StringComparer.OrdinalIgnoreCase);
        }

        HidD_GetHidGuid(out var hidInterfaceClass);
        if (!TryEnumerateInterfaces(hidInterfaceClass, out var hidInterfaces)
            || !TryEnumerateInterfaces(KeyboardInterfaceClass, out var keyboardInterfaces))
        {
            return new Dictionary<string, LocalHidCandidate>(StringComparer.OrdinalIgnoreCase);
        }

        return MatchKeyboardAliases(selected, hidInterfaces, keyboardInterfaces);
    }

    internal static IReadOnlyDictionary<string, LocalHidCandidate> MatchKeyboardAliases(
        IEnumerable<LocalHidCandidate> candidates,
        IEnumerable<LocalDeviceInterfaceIdentity> hidInterfaces,
        IEnumerable<LocalDeviceInterfaceIdentity> keyboardInterfaces)
    {
        var selectedByPath = candidates
            .Where(static candidate => candidate.UsagePage == 1 && candidate.Usage == 6
                && !string.IsNullOrWhiteSpace(candidate.InterfacePath))
            .GroupBy(static candidate => candidate.InterfacePath, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.OrdinalIgnoreCase);

        var candidateIdentityPairs = hidInterfaces
            .Where(static item => !string.IsNullOrWhiteSpace(item.InterfacePath)
                && !string.IsNullOrWhiteSpace(item.DeviceInstanceId))
            .GroupBy(static item => item.InterfacePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                InterfacePath = group.Key,
                InstanceIds = group.Select(static item => item.DeviceInstanceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            })
            .Where(static item => item.InstanceIds.Length == 1)
            .Where(item => selectedByPath.ContainsKey(item.InterfacePath))
            .Select(item => (item.InstanceIds[0], Candidate: selectedByPath[item.InterfacePath]));

        var candidateByIdentity = candidateIdentityPairs
            .GroupBy(static pair => pair.Item1, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Select(pair => pair.Candidate.InterfacePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.First().Candidate,
                StringComparer.OrdinalIgnoreCase);

        var aliases = new Dictionary<string, LocalHidCandidate>(StringComparer.OrdinalIgnoreCase);
        var unambiguousKeyboardInterfaces = keyboardInterfaces
            .Where(static item => !string.IsNullOrWhiteSpace(item.InterfacePath)
                && !string.IsNullOrWhiteSpace(item.DeviceInstanceId))
            .GroupBy(static item => item.InterfacePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                InterfacePath = group.Key,
                InstanceIds = group.Select(static item => item.DeviceInstanceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            })
            .Where(static item => item.InstanceIds.Length == 1);
        foreach (var keyboardInterface in unambiguousKeyboardInterfaces)
        {
            if (!candidateByIdentity.TryGetValue(keyboardInterface.InstanceIds[0], out var candidate))
            {
                continue;
            }

            aliases[keyboardInterface.InterfacePath] = candidate;
        }

        return aliases;
    }

    private static bool TryEnumerateInterfaces(Guid interfaceClass, out IReadOnlyList<LocalDeviceInterfaceIdentity> interfaces)
    {
        var found = new List<LocalDeviceInterfaceIdentity>();
        using var infoSet = SetupDiGetClassDevs(ref interfaceClass, null, IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);
        if (infoSet.IsInvalid)
        {
            interfaces = [];
            return false;
        }

        for (uint index = 0; index < MaxInterfaces; index++)
        {
            var interfaceData = new SpDeviceInterfaceData { Size = Marshal.SizeOf<SpDeviceInterfaceData>() };
            if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref interfaceClass, index, ref interfaceData))
            {
                interfaces = found;
                return Marshal.GetLastWin32Error() == ErrorNoMoreItems;
            }

            if (!TryGetInterfaceIdentity(infoSet, ref interfaceData, out var identity))
            {
                interfaces = [];
                return false;
            }

            found.Add(identity);
        }

        interfaces = [];
        return false;
    }

    private static bool TryGetInterfaceIdentity(
        SafeDeviceInfoSetHandle infoSet,
        ref SpDeviceInterfaceData interfaceData,
        out LocalDeviceInterfaceIdentity identity)
    {
        identity = default;
        SetupDiGetDeviceInterfaceDetailNoData(infoSet, ref interfaceData, IntPtr.Zero, 0,
            out var requiredSize, IntPtr.Zero);
        if (requiredSize == 0)
        {
            return false;
        }

        var detailBuffer = Marshal.AllocHGlobal(checked((int)requiredSize));
        try
        {
            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
            var deviceInfo = new SpDevinfoData { Size = Marshal.SizeOf<SpDevinfoData>() };
            if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detailBuffer, requiredSize,
                    out _, ref deviceInfo))
            {
                return false;
            }

            var interfacePath = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4));
            var instanceId = new StringBuilder(512);
            if (string.IsNullOrWhiteSpace(interfacePath)
                || CM_Get_Device_ID(deviceInfo.DeviceInstance, instanceId, instanceId.Capacity, 0) != CrSuccess
                || instanceId.Length == 0)
            {
                return false;
            }

            identity = new LocalDeviceInterfaceIdentity(interfacePath, instanceId.ToString());
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int Size;
        public Guid ClassGuid;
        public uint DeviceInstance;
        public UIntPtr Reserved;
    }

    private sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeDeviceInfoSetHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => SetupDiDestroyDeviceInfoList(handle);
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeDeviceInfoSetHandle SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        SafeDeviceInfoSetHandle deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailNoData(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID(
        uint deviceInstance,
        StringBuilder buffer,
        int bufferLength,
        uint flags);
}

internal readonly record struct LocalDeviceInterfaceIdentity(string InterfacePath, string DeviceInstanceId);
