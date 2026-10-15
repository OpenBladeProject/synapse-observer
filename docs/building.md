# Building and verification

Build from a dedicated task worktree. Development requires the .NET 10 SDK and
Visual Studio C++ x64 build tools. Neither is required by contributors using
the packaged app.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build/Publish.ps1
```

The script verifies pinned MinHook source hashes, builds the native DLL and its
owned synthetic fixtures, runs the managed solution tests, and publishes a
framework-dependent single executable. The native DLL is embedded with the
app and extracted by .NET when needed. It must match the hash compiled into
the managed backend; there is no user-supplied DLL path.

Output: `artifacts/SynapseObserver-win-x64-<version>.zip`. It includes the
executable, licenses, and a source-commit/SHA-256 manifest. The .NET runtime is
not included. Build output is ignored by Git.

To iterate after the native build:

```powershell
dotnet build SynapseObserver.slnx -c Release -p:NuGetAudit=false --no-restore
dotnet test SynapseObserver.slnx -c Release -p:NuGetAudit=false --no-build --no-restore
```

Rebuild the native source before managed compilation when its source changes:
the native payload hash is generated in `artifacts/native`, not a tracked file.

## Disconnected GUI checks

The GUI has a synthetic preview that performs no inventory, process attachment,
or device access. Preview exports are disabled.

```powershell
SynapseObserver.exe --render-preview C:\tmp\observer-preview
```

This writes PNGs of the guide stages, compact layout, and maximized window.
Inspect them for clipping,
readability, and visible controls. These renders do not validate live capture.

`--export-inventory <zip-path>` is a developer check of the read-only inventory
and export path. It never starts the observer or applies a setting.

## Live validation

Source tests do not establish successful attachment to Synapse. Obtain explicit
approval immediately before a live prototype test. Run Synapse and the collector
normally under the same account; do not use administrator launch or weaken
security protections. The collector records engine/module hashes and requires
a valid Razer signature, matching account/session/integrity/x64 identity, and
fresh device-interface discovery before attachment.

For an initial live check, observe existing activity without changing settings.
Confirm that handles match only the selected Blade and that stop is confirmed.
A later user-operated change/restore can supply action correlation. Keep the
request, response, completed timing, observed layer, and restoration source
distinct in the saved evidence. Stop on ambiguity and preserve a partial bundle.
