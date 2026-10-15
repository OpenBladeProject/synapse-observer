using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace SynapseObserver.Backend;
internal static class Native
{
    [DllImport("kernel32", SetLastError=true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32", SetLastError=true)] internal static extern bool IsWow64Process2(SafeProcessHandle process, out ushort processMachine, out ushort nativeMachine);
    [DllImport("advapi32", SetLastError=true)] internal static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeFileHandle token);
    [DllImport("advapi32", SetLastError=true)] internal static extern bool GetTokenInformation(SafeFileHandle token, int type, IntPtr information, int length, out int required);
    [DllImport("advapi32")] internal static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32")] internal static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr File; public IntPtr KnownSubject; }
    [StructLayout(LayoutKind.Sequential)] internal struct TrustData { public uint Size; public IntPtr Callback; public IntPtr SipClient; public uint UiChoice; public uint RevocationChecks; public uint UnionChoice; public IntPtr FileInfo; public uint StateAction; public IntPtr StateData; public IntPtr Url; public uint ProviderFlags; public uint UiContext; public IntPtr SignatureSettings; }
    [DllImport("wintrust", ExactSpelling=true)] internal static extern int WinVerifyTrust(IntPtr window, ref Guid policy, ref TrustData data);
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("ntdll")] internal static extern int NtQueryObject(SafeFileHandle handle, int informationClass, IntPtr data, int length, out int required);
    [StructLayout(LayoutKind.Sequential)] internal struct HidAttributes { public int Size; public ushort VendorId; public ushort ProductId; public ushort Version; }
    [StructLayout(LayoutKind.Sequential)] internal struct HidCaps { public ushort Usage; public ushort UsagePage; public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength; [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved; public ushort NumberLinkCollectionNodes; public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps; public ushort NumberInputDataIndices; public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps; public ushort NumberOutputDataIndices; public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps; public ushort NumberFeatureDataIndices; }
    [DllImport("hid", SetLastError=true)] internal static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
    [DllImport("hid", SetLastError=true)] internal static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid")] internal static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid")] internal static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);
    [DllImport("kernel32", SetLastError=true)] internal static extern IntPtr VirtualAllocEx(SafeProcessHandle process, IntPtr address, nuint size, uint allocation, uint protection);
    [DllImport("kernel32", SetLastError=true)] internal static extern bool VirtualFreeEx(SafeProcessHandle process, IntPtr address, nuint size, uint operation);
    [DllImport("kernel32", SetLastError=true)] internal static extern bool WriteProcessMemory(SafeProcessHandle process, IntPtr address, byte[] buffer, nuint length, out nuint written);
    [DllImport("kernel32", SetLastError=true)] internal static extern SafeFileHandle CreateRemoteThread(SafeProcessHandle process, IntPtr attributes, nuint stackSize, IntPtr start, IntPtr argument, uint flags, out uint threadId);
    [DllImport("kernel32", SetLastError=true)] internal static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);
    [DllImport("kernel32", SetLastError=true)] internal static extern bool GetExitCodeThread(SafeFileHandle thread, out uint code);
    [DllImport("kernel32", CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32", CharSet=CharSet.Ansi, ExactSpelling=true)] internal static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32")] internal static extern bool FreeLibrary(IntPtr module);
    [StructLayout(LayoutKind.Sequential)] internal struct ProviderCertificatePrefix { public uint Size; public IntPtr Certificate; }
    [DllImport("wintrust", ExactSpelling=true)] internal static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust", ExactSpelling=true)] internal static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterIndex);
    [DllImport("wintrust", ExactSpelling=true)] internal static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificate);
    [DllImport("crypt32", ExactSpelling=true, CharSet=CharSet.Unicode)] internal static extern uint CertGetNameStringW(IntPtr certificate, uint type, uint flags, IntPtr parameter, System.Text.StringBuilder name, uint size);
    [DllImport("kernel32", ExactSpelling=true, CharSet=CharSet.Unicode, SetLastError=true)] internal static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, System.Text.StringBuilder path, uint size, uint flags);
}
