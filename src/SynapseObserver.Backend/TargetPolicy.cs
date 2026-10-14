using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
namespace SynapseObserver.Backend;
internal static class TargetPolicy
{
    internal static string SafeDiagnostic(Exception exception) => exception is InvalidOperationException ? exception.Message : "Process identity could not be verified. Start Synapse normally under the same Windows account and refresh.";
    internal static void Validate(Process process)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new InvalidOperationException("The observer requires 64-bit Windows.");
        if (process.ProcessName != "RazerAppEngine") throw new InvalidOperationException("The selected process is not the Synapse engine.");
        if (process.SessionId != Process.GetCurrentProcess().SessionId) throw new InvalidOperationException("Synapse is in a different Windows session.");
        var executable = process.MainModule?.FileName ?? throw new InvalidOperationException("The Synapse engine path is unavailable.");
        executable = FinalFilePath(executable);
        var engineInfo = FileVersionInfo.GetVersionInfo(executable);
        if (!IsInstalledEnginePath(executable) || !IsEngineRole(Path.GetFileName(executable), engineInfo.ProductName, engineInfo.InternalName))
            throw new InvalidOperationException("The running executable is not the installed Razer Synapse engine. Start Synapse normally.");
        VerifySignature(executable);
        using var handle = Native.OpenProcess(0x1000, false, process.Id);
        if (handle.IsInvalid) throw new Win32Exception();
        if (!Native.IsWow64Process2(handle, out var machine, out var nativeMachine) || machine != 0 || nativeMachine != 0x8664) throw new InvalidOperationException("Synapse must be a native x64 process.");
        using var current = Process.GetCurrentProcess();
        var identity = TokenIdentity(handle);
        var self = TokenIdentity(current.SafeHandle);
        if (identity != self) throw new InvalidOperationException("Synapse and the collector must use the same Windows account and integrity level. Start both normally.");
        var modules = process.Modules.Cast<ProcessModule>().Where(x => string.Equals(x.ModuleName, "HID.node", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (modules.Length != 1)
            throw new InvalidOperationException("Open your laptop's controls in Synapse so its HID module is loaded, then refresh.");
        string modulePath = FinalFilePath(modules[0].FileName);
        if (!IsHidModuleOfEngine(modulePath, executable))
            throw new InvalidOperationException("The loaded HID module does not belong to this Synapse installation. Device information remains available.");
        VerifySignature(modulePath);
    }
    internal static bool IsRazerPublisher(string? commonName, string? organization) =>
        string.Equals(commonName, "Razer USA Ltd.", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(organization, "Razer USA Ltd.", StringComparison.OrdinalIgnoreCase);
    internal static bool IsEngineRole(string fileName, string? productName, string? internalName) =>
        fileName.Equals("RazerAppEngine.exe", StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(productName, "RazerAppEngine", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(internalName, "RazerAppEngine", StringComparison.OrdinalIgnoreCase));
    internal static bool IsInstalledEnginePath(string executable)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Razer", "RazerAppEngine");
        string? directory = Path.GetDirectoryName(executable);
        return directory is not null && Path.GetFileName(executable).Equals("RazerAppEngine.exe", StringComparison.OrdinalIgnoreCase) &&
            (directory.Equals(root, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase));
    }
    internal static bool IsHidModuleOfEngine(string modulePath, string executable) =>
        string.Equals(modulePath, Path.Combine(Path.GetDirectoryName(executable)!, "resources", "app.asar.unpacked", "node_modules", "node-rz-hid", "build", "Release", "HID.node"), StringComparison.OrdinalIgnoreCase);
    private static string FinalFilePath(string path)
    {
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new System.Text.StringBuilder(4096);
        uint count = Native.GetFinalPathNameByHandleW(file, buffer, (uint)buffer.Capacity, 0);
        if (count == 0 || count >= buffer.Capacity) throw new InvalidOperationException("The installed file's final identity is unavailable.");
        string resolved = buffer.ToString();
        if (resolved.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) return "\\\\" + resolved[8..];
        return resolved.StartsWith("\\\\?\\", StringComparison.Ordinal) ? resolved[4..] : resolved;
    }
    private static (string Sid, int Integrity) TokenIdentity(Microsoft.Win32.SafeHandles.SafeProcessHandle process)
    {
        if (!Native.OpenProcessToken(process, 8, out var token)) throw new Win32Exception();
        using (token)
        {
            using var identity = new WindowsIdentity(token.DangerousGetHandle());
            Native.GetTokenInformation(token, 25, IntPtr.Zero, 0, out int size);
            if (size <= 0 || size > 4096) throw new InvalidOperationException("Integrity identity is unavailable.");
            IntPtr data = Marshal.AllocHGlobal(size);
            try {
                if (!Native.GetTokenInformation(token, 25, data, size, out _)) throw new Win32Exception();
                var sid = Marshal.ReadIntPtr(data); byte count = Marshal.ReadByte(Native.GetSidSubAuthorityCount(sid));
                if (count == 0) throw new InvalidOperationException("Integrity identity is unavailable.");
                int level = Marshal.ReadInt32(Native.GetSidSubAuthority(sid, (uint)(count - 1)));
                return (identity.User?.Value ?? throw new InvalidOperationException("Windows user identity is unavailable."), level);
            } finally { Marshal.FreeHGlobal(data); }
        }
    }
    private static void VerifySignature(string path)
    {
        using var lockedFile = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var file = new Native.TrustFile { Size = (uint)Marshal.SizeOf<Native.TrustFile>(), Path = path };
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<Native.TrustFile>());
        var data = new Native.TrustData { Size = (uint)Marshal.SizeOf<Native.TrustData>(), UiChoice = 2, UnionChoice = 1, FileInfo = ptr, StateAction = 1, ProviderFlags = 0x1010 };
        Guid policy = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            Marshal.StructureToPtr(file, ptr, false);
            if (Native.WinVerifyTrust(new IntPtr(-1), ref policy, ref data) != 0)
                throw new InvalidOperationException("The Synapse engine or HID module signature could not be verified. Device information remains available.");
            // Read the signer accepted by this trust operation, not an unrelated
            // certificate from a multi-signed file. Versions and hashes are provenance.
            IntPtr provider = Native.WTHelperProvDataFromStateData(data.StateData);
            IntPtr signer = provider == IntPtr.Zero ? IntPtr.Zero : Native.WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            IntPtr cert = signer == IntPtr.Zero ? IntPtr.Zero : Native.WTHelperGetProvCertFromChain(signer, 0);
            if (cert == IntPtr.Zero) throw new InvalidOperationException("The verified Synapse publisher is unavailable.");
            var certInfo = Marshal.PtrToStructure<Native.ProviderCertificatePrefix>(cert);
            if (certInfo.Size < Marshal.SizeOf<Native.ProviderCertificatePrefix>() || certInfo.Certificate == IntPtr.Zero)
                throw new InvalidOperationException("The verified Synapse signer is unavailable.");
            using var certificate = new X509Certificate2(certInfo.Certificate);
            IntPtr organizationOid = Marshal.StringToHGlobalAnsi("2.5.4.10");
            try
            {
                var organization = new System.Text.StringBuilder(512);
                uint length = Native.CertGetNameStringW(certificate.Handle, 3, 0, organizationOid, organization, (uint)organization.Capacity);
                if (length <= 1 || length >= organization.Capacity || !IsRazerPublisher(certificate.GetNameInfo(X509NameType.SimpleName, false), organization.ToString()))
                    throw new InvalidOperationException("The Synapse engine or HID module is not signed by Razer. Device information remains available.");
            }
            finally { Marshal.FreeHGlobal(organizationOid); }
        }
        finally
        {
            if (data.StateData != IntPtr.Zero) { data.StateAction = 2; Native.WinVerifyTrust(new IntPtr(-1), ref policy, ref data); }
            Marshal.DestroyStructure<Native.TrustFile>(ptr); Marshal.FreeHGlobal(ptr);
        }
    }
}
