# Native observer prototype

This source implements a bounded x64 user-mode observer. It does not issue HID
reports, firmware commands, input reads, or arbitrary IOCTLs. Attachment changes
the reviewed Synapse process by loading the matching observer DLL and installing
MinHook detours. Live attachment requires the user's explicit recording action.

The managed backend verifies Windows Authenticode trust and the accepted signer's
Razer legal publisher identity for both the installed RazerAppEngine executable
and its loaded node-rz-hid module. Canonical installation paths and signed engine
role metadata prevent a renamed unrelated executable from becoming a target.
The module must belong to the same engine installation. Native x64 architecture,
Windows user, session, and integrity level must match the collector. Version and
file hashes are evidence provenance, with no per-build admission list.

The fixed observer DLL still matches the SHA embedded in its own managed build.
There is no arbitrary DLL, executable, or elevation override. Attachment is
revalidated immediately before loading the observer. See [validation status](../VALIDATION.md)
for the actual configurations and transports exercised.

Before attachment the backend re-enumerates the selected feature collections.
Every collection must belong to the same physical USB parent with Razer vendor
identity and a bus-reported Blade product name. Unfamiliar PID, interface number, usage page,
and positive feature-report sizes are allowed; missing caps are not inferred.
Zero-access descriptor inspection and
`NtQueryObject(ObjectNameInformation)` establish exact NT object names. Hooks
compare each original handle with those names and ignore every other handle.
No path, serial, or object name is included in event records.

Observed APIs are `HidD_SetFeature`, `HidD_GetFeature`, and `DeviceIoControl` only
for `IOCTL_HID_SET_FEATURE` and `IOCTL_HID_GET_FEATURE`. Nested calls are suppressed
so the HidD wrapper and its underlying IOCTL do not produce duplicates. Original
arguments, return values, and last-error state are preserved. The event's error
field is the raw last-error snapshot and may be stale after a successful call.
GET requests retain only the report ID, never the uninitialized caller tail.

The DLL queue holds 256 records and the shared ring holds 32. Each request or
response is bounded at 4096 bytes. Overflows increment a dropped-record counter.
Up to eight feature collections from one laptop may be observed for five minutes.
Event timestamps use QueryPerformanceCounter; managed relative time starts before
attachment. Each event keeps call entry and API-return completion separately;
a pending asynchronous operation's API-return timestamp does not claim its I/O
completed. GET events also retain the caller-provided output buffer capacity,
independently from request and returned byte counts. `InterfaceIndex` preserves
the selected collection order.

Known limits:

- Pending overlapped IOCTL completion is not followed. It produces an explicit
  gap with no completed response. Direct native syscalls and other entry points
  are outside this observer's coverage.
- HidD_GetFeature supplies no actual returned byte count. Its completed caller
  buffer is capped at descriptor geometry and marked `return-length-unknown`.
  This is a returned user-mode buffer, not a USB transfer or firmware readback.
- NT object-name mapping matched one live 375-byte laptop feature call. Other
  collections and command paths still need coverage. An unmapped handle is
  excluded; zero observations remain partial evidence.
- Startup, stop, timeout, and dropped-record failures remain explicit. Stop
  disables hooks, waits briefly for callbacks, and drains completed records.
  The DLL and trampoline allocations remain pinned until Synapse exits because
  callback counters alone cannot prove that no thread is about to enter a hook.
  Another recording requires a manual Synapse restart. No process is stopped
  automatically. A failure to confirm stop must remain visible to the user.
- The remote loader completed one authorized trial on the reviewed Synapse build.
  Other builds and security configurations remain unvalidated. Process protection
  or code-integrity policy may refuse attachment. There is no bypass.

Build from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/Build-NativeObserver.ps1 -RunTests
dotnet test tests/SynapseObserver.Backend.Tests/SynapseObserver.Backend.Tests.csproj -p:NuGetAudit=false
```

The first test executable installs real MinHook detours around owned synthetic
functions and exercises status/error preservation, nested suppression, bounds,
pending gaps, descriptor-tail exclusion, queue overflow, and final draining.
The second loads the actual DLL into its own process and exercises the startup
handshake and bounded stop with no device calls. Managed tests cover event
parsing, forged target rejection, cancellation, and physical-parent policy.
These tests do not prove live Synapse coverage or device compatibility.

MinHook source is pinned at `c3fcafdc10146beb5919319d0683e44e3c30d537`
(tag v1.3.4). Its license and source-file SHA-256 manifest are preserved under
`third_party/minhook`. The build verifies every upstream hash. Only source was
vendored; no third-party instrumentation executable was downloaded or installed.
The build emits the DLL and generated managed SHA pin into ignored `artifacts`.
