using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace SynapseObserver.Core;

public sealed class WindowsInventorySource : IInventorySource
{
    private const ushort RazerVendorId = 0x1532;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint OpenExisting = 3;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const int ErrorNoMoreItems = 259;
    private const int CrSuccess = 0;
    private const uint CmDrpDeviceDescription = 0x00000001;
    private const uint CmDrpFriendlyName = 0x0000000D;
    private readonly byte[] _sessionLabelKey = RandomNumberGenerator.GetBytes(32);

    public ValueTask<SectionResult<IReadOnlyList<MachineIdentity>>> CollectMachineIdentityAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identities = new List<MachineIdentity>(2);
        var failures = 0;

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var bios = baseKey.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS", writable: false);
                if (bios is null)
                {
                    failures++;
                    continue;
                }

                identities.Add(new MachineIdentity(
                    view == RegistryView.Registry64 ? "Registry64" : "Registry32",
                    ReadRegistryText(bios, "SystemManufacturer"),
                    ReadRegistryText(bios, "SystemProductName"),
                    ReadRegistryText(bios, "SystemSKU"),
                    ReadRegistryText(bios, "BIOSVersion"),
                    ReadRegistryText(bios, "BIOSReleaseDate")));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                failures++;
            }
        }

        if (identities.Count == 0)
        {
            return ValueTask.FromResult(SectionResult<IReadOnlyList<MachineIdentity>>.Unavailable(
                identities,
                "Windows BIOS identity registry values were unavailable."));
        }

        if (failures > 0)
        {
            return ValueTask.FromResult(SectionResult<IReadOnlyList<MachineIdentity>>.Partial(
                identities,
                "One Windows registry view was unavailable."));
        }

        return ValueTask.FromResult(SectionResult<IReadOnlyList<MachineIdentity>>.Collected(identities));
    }

    public ValueTask<SectionResult<PowerSourceEvidence?>> CollectPowerSourceAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!GetSystemPowerStatus(out var status))
        {
            return ValueTask.FromResult(SectionResult<PowerSourceEvidence?>.Unavailable(
                null,
                $"Windows power status was unavailable (Win32 {Marshal.GetLastWin32Error()})."));
        }

        var source = status.AcLineStatus switch
        {
            0 => PowerSourceKind.Battery,
            1 => PowerSourceKind.Ac,
            _ => PowerSourceKind.Unknown,
        };
        var batteryPresent = (status.BatteryFlag & 0x80) == 0;
        int? percent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null;
        return ValueTask.FromResult(SectionResult<PowerSourceEvidence?>.Collected(
            new PowerSourceEvidence(source, percent, batteryPresent)));
    }

    public ValueTask<SectionResult<IReadOnlyList<HidInterfaceEvidence>>> CollectRazerHidInterfacesAsync(
        CancellationToken cancellationToken)
    {
        var local = DiscoverLocalControlCandidates(cancellationToken);
        IReadOnlyList<HidInterfaceEvidence> exported = local.Value
            .Select(static candidate => new HidInterfaceEvidence(
                candidate.InterfaceLabel,
                candidate.VendorId,
                candidate.ProductId,
                candidate.VersionNumber,
                candidate.UsagePage,
                candidate.Usage,
                candidate.ReportGeometry,
                candidate.DescriptorState,
                candidate.Issue))
            .ToArray();
        return ValueTask.FromResult(new SectionResult<IReadOnlyList<HidInterfaceEvidence>>(
            local.State,
            exported,
            local.Issue));
    }

    public SectionResult<IReadOnlyList<LocalHidCandidate>> DiscoverLocalControlCandidates(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var interfaces = new List<LocalHidCandidate>();
        var failures = 0;
        HidD_GetHidGuid(out var hidGuid);

        using var infoSet = SetupDiGetClassDevs(
            ref hidGuid,
            null,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);
        if (infoSet.IsInvalid)
        {
            return SectionResult<IReadOnlyList<LocalHidCandidate>>.Unavailable(
                interfaces,
                $"Windows HID enumeration was unavailable (Win32 {Marshal.GetLastWin32Error()}).");
        }

        for (uint index = 0; interfaces.Count < EvidenceLimits.MaxHidInterfaces; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var interfaceData = new SpDeviceInterfaceData { Size = Marshal.SizeOf<SpDeviceInterfaceData>() };
            if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorNoMoreItems)
                {
                    failures++;
                }

                break;
            }

            if (!TryGetDevicePath(infoSet, ref interfaceData, out var devicePath, out var deviceInstance, out _))
            {
                failures++;
                continue;
            }

            using var handle = CreateFile(
                devicePath,
                0,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
            var attributesRead = !handle.IsInvalid && HidD_GetAttributes(handle, ref attributes);
            if (!attributesRead && !TryParseRazerIdentity(devicePath, out attributes.VendorId, out attributes.ProductId))
            {
                continue;
            }

            if (attributes.VendorId != RazerVendorId)
            {
                continue;
            }

            ushort? usagePage = null;
            ushort? usage = null;
            HidReportGeometry? geometry = null;
            string? issue = null;
            var descriptorState = CollectionState.Collected;

            if (handle.IsInvalid)
            {
                descriptorState = CollectionState.Unavailable;
                issue = "The HID interface could not be opened for descriptor-only access.";
                failures++;
            }
            else if (!TryGetCaps(handle, out var caps))
            {
                descriptorState = CollectionState.Partial;
                issue = "HID report capabilities were unavailable.";
                failures++;
            }
            else
            {
                usagePage = caps.UsagePage;
                usage = caps.Usage;
                geometry = new HidReportGeometry(
                    caps.InputReportByteLength,
                    caps.OutputReportByteLength,
                    caps.FeatureReportByteLength);
            }

            var parent = GetParentIdentity(deviceInstance);
            interfaces.Add(new LocalHidCandidate(
                devicePath,
                MakeOpaqueLabel("hid", devicePath),
                handle.IsInvalid ? null : GetHidProductName(handle),
                attributes.VendorId,
                attributes.ProductId,
                attributesRead ? attributes.VersionNumber : null,
                usagePage,
                usage,
                geometry,
                descriptorState,
                issue,
                parent.InstanceId,
                parent.BaseHardwareIdentity,
                parent.FriendlyName,
                parent.UsbInstanceId,
                parent.UsbBaseHardwareIdentity,
                parent.UsbProductName));
        }

        if (interfaces.Count == EvidenceLimits.MaxHidInterfaces)
        {
            failures++;
        }

        IReadOnlyList<LocalHidCandidate> result = interfaces;
        if (interfaces.Count == 0 && failures == 0)
        {
            return SectionResult<IReadOnlyList<LocalHidCandidate>>.Absent(result);
        }

        if (interfaces.Count == 0)
        {
            return SectionResult<IReadOnlyList<LocalHidCandidate>>.Unavailable(
                result,
                "Razer HID inventory could not be completed.");
        }

        if (failures > 0)
        {
            return SectionResult<IReadOnlyList<LocalHidCandidate>>.Partial(
                result,
                $"HID inventory completed with {failures.ToString(CultureInfo.InvariantCulture)} unavailable or truncated item(s).");
        }

        return SectionResult<IReadOnlyList<LocalHidCandidate>>.Collected(result);
    }

    public ValueTask<SectionResult<IReadOnlyList<SynapseProcessEvidence>>> CollectSynapseProcessesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evidence = new List<SynapseProcessEvidence>();
        var failures = 0;
        var processLimitReached = false;

        var selectedCount = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string processName;
                try
                {
                    processName = process.ProcessName;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (!SynapseInventoryScope.IsProcessName(processName))
                {
                    continue;
                }

                if (selectedCount++ >= EvidenceLimits.MaxSynapseProcesses)
                {
                    processLimitReached = true;
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var modules = new List<SynapseModuleEvidence>();
                string? productVersion = null;
                string? processIssue = null;
                var moduleState = CollectionState.Collected;

                try
                {
                    var mainPath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(mainPath))
                    {
                        productVersion = CleanText(FileVersionInfo.GetVersionInfo(mainPath).ProductVersion);
                    }

                    var moduleLimitReached = false;
                    foreach (ProcessModule module in process.Modules)
                    {
                        using (module)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!SynapseInventoryScope.IsModule(module.FileName, mainPath))
                            {
                                continue;
                            }

                            if (modules.Count >= EvidenceLimits.MaxModulesPerProcess)
                            {
                                moduleLimitReached = true;
                                continue;
                            }

                            modules.Add(CollectModule(module));
                        }
                    }

                    if (moduleLimitReached)
                    {
                        moduleState = CollectionState.Partial;
                        processIssue = "Module inventory reached its documented limit.";
                        failures++;
                    }
                    else if (modules.Any(static module => module.State != CollectionState.Collected))
                    {
                        moduleState = CollectionState.Partial;
                        processIssue = "One or more module files were unavailable.";
                        failures++;
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    moduleState = CollectionState.Unavailable;
                    processIssue = "Windows denied module inventory for this process.";
                    failures++;
                }

                evidence.Add(new SynapseProcessEvidence(
                    CleanText(processName) ?? "Synapse process",
                    MakeOpaqueLabel("process", process.Id.ToString(CultureInfo.InvariantCulture)),
                    productVersion,
                    modules,
                    moduleState,
                    processIssue));
            }
        }

        IReadOnlyList<SynapseProcessEvidence> result = evidence;
        if (evidence.Count == 0)
        {
            return ValueTask.FromResult(SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Absent(result));
        }

        if (failures > 0 || processLimitReached)
        {
            return ValueTask.FromResult(SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Partial(
                result,
                "Synapse process inventory completed with unavailable module details."));
        }

        return ValueTask.FromResult(SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Collected(result));
    }

    private static SynapseModuleEvidence CollectModule(ProcessModule module)
    {
        var path = module.FileName;
        var fileName = CleanText(Path.GetFileName(path)) ?? "module";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var version = CleanText(module.FileVersionInfo.FileVersion);
            return new SynapseModuleEvidence(fileName, version, hash, CollectionState.Collected, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new SynapseModuleEvidence(
                fileName,
                null,
                null,
                CollectionState.Unavailable,
                "The module file could not be read.");
        }
    }

    private static string? ReadRegistryText(RegistryKey key, string valueName) =>
        CleanText(key.GetValue(valueName)?.ToString());

    private static string? CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value
            .Where(static character => !char.IsControl(character))
            .Take(EvidenceLimits.MaxTextLength)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned.Trim();
    }

    private string MakeOpaqueLabel(string prefix, string localIdentity)
    {
        using var hmac = new HMACSHA256(_sessionLabelKey);
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(localIdentity));
        return $"{prefix}-{Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant()}";
    }

    private static ParentIdentity GetParentIdentity(uint deviceInstance)
    {
        if (CM_Get_Parent(out var parent, deviceInstance, 0) != CrSuccess)
        {
            return default;
        }

        var direct = GetDeviceNodeIdentity(parent);
        if (direct.InstanceId is null)
        {
            return default;
        }

        var current = parent;
        DeviceNodeIdentity usb = default;
        string? usbProductName = null;
        for (var depth = 0; depth < 8; depth++)
        {
            var node = GetDeviceNodeIdentity(current);
            if (node.InstanceId is not null
                && node.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)
                && node.InstanceId.Contains("VID_", StringComparison.OrdinalIgnoreCase))
            {
                usb = node;
                usbProductName = GetBusReportedProductName(current);
            }

            if (CM_Get_Parent(out current, current, 0) != CrSuccess)
            {
                break;
            }
        }

        return new ParentIdentity(
            direct.InstanceId,
            direct.BaseHardwareIdentity,
            direct.FriendlyName,
            usb.InstanceId,
            usb.BaseHardwareIdentity,
            usbProductName);
    }

    private static DeviceNodeIdentity GetDeviceNodeIdentity(uint deviceInstance)
    {
        var instanceId = new StringBuilder(512);
        if (CM_Get_Device_ID(deviceInstance, instanceId, instanceId.Capacity, 0) != CrSuccess)
        {
            return default;
        }

        var rawInstanceId = instanceId.ToString();
        var friendlyName = GetDeviceRegistryText(deviceInstance, CmDrpFriendlyName)
            ?? GetDeviceRegistryText(deviceInstance, CmDrpDeviceDescription);
        return new DeviceNodeIdentity(
            rawInstanceId,
            GetBaseHardwareIdentity(rawInstanceId),
            CleanText(friendlyName));
    }

    private static string? GetHidProductName(SafeFileHandle handle)
    {
        var product = new StringBuilder(EvidenceLimits.MaxTextLength + 1);
        return HidD_GetProductString(handle, product, product.Capacity * sizeof(char))
            ? CleanText(product.ToString())
            : null;
    }

    private static string? GetBusReportedProductName(uint deviceInstance)
    {
        // FriendlyName commonly says USB Composite Device. Read the bus-provided
        // description of this exact USB ancestor instead of inferring its model.
        // DEVPKEY_Device_BusReportedDeviceDesc, defined by the Windows SDK.
        var key = new DevicePropertyKey { FormatId = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), PropertyId = 4 };
        var buffer = new byte[1024];
        uint length = (uint)buffer.Length;
        return CM_Get_DevNode_PropertyW(deviceInstance, ref key, out uint type, buffer, ref length, 0) == CrSuccess
            ? DecodeBusReportedProductName(buffer, type, length)
            : null;
    }

    internal static string? DecodeBusReportedProductName(ReadOnlySpan<byte> buffer, uint type, uint length)
    {
        const uint stringType = 0x12;
        if (type != stringType || length < 2 || length > 1024 || length > buffer.Length || (length & 1) != 0)
        {
            return null;
        }

        var value = buffer[..checked((int)length)];
        if (value[^2] != 0 || value[^1] != 0)
        {
            return null;
        }

        string text = Encoding.Unicode.GetString(value[..^2]);
        return text.Contains('\0') ? null : CleanText(text);
    }
    private static string? GetDeviceRegistryText(uint deviceInstance, uint property)
    {
        uint dataType = 0;
        uint byteLength = 1024;
        var buffer = new StringBuilder(512);
        return CM_Get_DevNode_Registry_Property(
            deviceInstance,
            property,
            out dataType,
            buffer,
            ref byteLength,
            0) == CrSuccess
            ? buffer.ToString()
            : null;
    }

    private static string GetBaseHardwareIdentity(string instanceId)
    {
        var firstSeparator = instanceId.IndexOf('\\');
        if (firstSeparator < 0)
        {
            return instanceId;
        }

        var secondSeparator = instanceId.IndexOf('\\', firstSeparator + 1);
        return secondSeparator < 0 ? instanceId : instanceId[..secondSeparator];
    }

    private static bool TryParseRazerIdentity(string devicePath, out ushort vendorId, out ushort productId)
    {
        vendorId = 0;
        productId = 0;
        var vendorIndex = devicePath.IndexOf("vid_1532", StringComparison.OrdinalIgnoreCase);
        if (vendorIndex < 0)
        {
            return false;
        }

        var productIndex = devicePath.IndexOf("pid_", vendorIndex, StringComparison.OrdinalIgnoreCase);
        if (productIndex < 0 || productIndex + 8 > devicePath.Length)
        {
            return false;
        }

        if (!ushort.TryParse(
                devicePath.AsSpan(productIndex + 4, 4),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out productId))
        {
            return false;
        }

        vendorId = RazerVendorId;
        return true;
    }

    private static bool TryGetCaps(SafeFileHandle handle, out HidpCaps caps)
    {
        caps = default;
        if (!HidD_GetPreparsedData(handle, out var preparsedData))
        {
            return false;
        }

        try
        {
            return HidP_GetCaps(preparsedData, out caps) >= 0;
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    private static bool TryGetDevicePath(
        SafeDeviceInfoSetHandle infoSet,
        ref SpDeviceInterfaceData interfaceData,
        out string devicePath,
        out uint deviceInstance,
        out int error)
    {
        devicePath = string.Empty;
        deviceInstance = 0;
        SetupDiGetDeviceInterfaceDetailNoData(
            infoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            out var requiredSize,
            IntPtr.Zero);
        error = Marshal.GetLastWin32Error();
        if (requiredSize == 0)
        {
            return false;
        }

        var detailBuffer = Marshal.AllocHGlobal(checked((int)requiredSize));
        try
        {
            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
            var deviceInfo = new SpDevinfoData { Size = Marshal.SizeOf<SpDevinfoData>() };
            if (!SetupDiGetDeviceInterfaceDetail(
                    infoSet,
                    ref interfaceData,
                    detailBuffer,
                    requiredSize,
                    out _,
                    ref deviceInfo))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            devicePath = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4)) ?? string.Empty;
            deviceInstance = deviceInfo.DeviceInstance;
            return devicePath.Length > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevicePropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public ushort Reserved01;
        public ushort Reserved02;
        public ushort Reserved03;
        public ushort Reserved04;
        public ushort Reserved05;
        public ushort Reserved06;
        public ushort Reserved07;
        public ushort Reserved08;
        public ushort Reserved09;
        public ushort Reserved10;
        public ushort Reserved11;
        public ushort Reserved12;
        public ushort Reserved13;
        public ushort Reserved14;
        public ushort Reserved15;
        public ushort Reserved16;
        public ushort Reserved17;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    private sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeDeviceInfoSetHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => SetupDiDestroyDeviceInfoList(handle);
    }

    private readonly record struct ParentIdentity(
        string? InstanceId,
        string? BaseHardwareIdentity,
        string? FriendlyName,
        string? UsbInstanceId,
        string? UsbBaseHardwareIdentity,
        string? UsbProductName);

    private readonly record struct DeviceNodeIdentity(
        string? InstanceId,
        string? BaseHardwareIdentity,
        string? FriendlyName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HiddAttributes attributes);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetProductString(
        SafeFileHandle hidDeviceObject,
        StringBuilder buffer,
        int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

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

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_DevNode_PropertyW(
        uint deviceInstance,
        ref DevicePropertyKey propertyKey,
        out uint propertyType,
        [Out] byte[] buffer,
        ref uint bufferLength,
        uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parentDeviceInstance, uint deviceInstance, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID(
        uint deviceInstance,
        StringBuilder buffer,
        int bufferLength,
        uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_Property(
        uint deviceInstance,
        uint property,
        out uint registryDataType,
        StringBuilder buffer,
        ref uint bufferLength,
        uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
