using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
namespace SynapseObserver.Backend;
internal static class TargetPolicy
{
    internal const string EngineHash = "DD18CB63EC4277A80800F78BFA700C03D01FC13A0A199DE1E576BDA76E1D3455";
    internal const string HidModuleHash = "D60AA56D33781E7226A4F860F0EDE20F24E79BEB55B293D643326CB7B1BB6310";
    internal static string SafeDiagnostic(Exception exception) => exception is InvalidOperationException ? exception.Message : "Process identity could not be verified. Start Synapse normally under the same Windows account and refresh.";
    internal static void Validate(Process process)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new InvalidOperationException("The observer requires 64-bit Windows.");
        if (process.ProcessName != "RazerAppEngine") throw new InvalidOperationException("The selected process is not the Synapse engine.");
        if (process.SessionId != Process.GetCurrentProcess().SessionId) throw new InvalidOperationException("Synapse is in a different Windows session.");
        var executable = process.MainModule?.FileName ?? throw new InvalidOperationException("The Synapse engine path is unavailable.");
        if (!HashMatches(executable, EngineHash)) throw new InvalidOperationException("This Synapse engine version has not been reviewed. Inventory and partial export remain available.");
        VerifySignature(executable);
        using var handle = Native.OpenProcess(0x1000, false, process.Id);
        if (handle.IsInvalid) throw new Win32Exception();
        if (!Native.IsWow64Process2(handle, out var machine, out var nativeMachine) || machine != 0 || nativeMachine != 0x8664) throw new InvalidOperationException("Synapse must be a native x64 process.");
        using var current = Process.GetCurrentProcess();
        var identity = TokenIdentity(handle);
        var self = TokenIdentity(current.SafeHandle);
        if (identity != self) throw new InvalidOperationException("Synapse and the collector must use the same Windows account and integrity level. Start both normally.");
        var modules = process.Modules.Cast<ProcessModule>().Where(x => string.Equals(x.ModuleName, "HID.node", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (modules.Length != 1 || !modules[0].FileName.Replace('/', '\\').Contains("\\node-rz-hid\\build\\Release\\", StringComparison.OrdinalIgnoreCase) || !HashMatches(modules[0].FileName, HidModuleHash))
            throw new InvalidOperationException("The reviewed node-rz-hid module is not loaded. Open the laptop controls in Synapse, then refresh. An unfamiliar module requires maintainer review.");
    }
    internal static bool HashMatches(string path, string expected)
    { using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase); }
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
        var file = new Native.TrustFile { Size = (uint)Marshal.SizeOf<Native.TrustFile>(), Path = path };
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<Native.TrustFile>());
        try {
            Marshal.StructureToPtr(file, ptr, false);
            var data = new Native.TrustData { Size = (uint)Marshal.SizeOf<Native.TrustData>(), UiChoice = 2, UnionChoice = 1, FileInfo = ptr, ProviderFlags = 0x1010 };
            Guid policy = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            if (Native.WinVerifyTrust(new IntPtr(-1), ref policy, ref data) != 0) throw new InvalidOperationException("The engine's Authenticode signature could not be verified.");
            // The exact SHA above identifies the reviewed Razer-signed engine.
            // WinVerifyTrust independently validates that pinned file signature.
        } finally { Marshal.DestroyStructure<Native.TrustFile>(ptr); Marshal.FreeHGlobal(ptr); }
    }
}
