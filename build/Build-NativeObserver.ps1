param([switch]$RunTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts/native'
New-Item -ItemType Directory -Force $out | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Visual Studio C++ x64 tools are required to build the offline native source.' }
$dev = Join-Path $vs 'Common7/Tools/VsDevCmd.bat'
$pin = Get-Content (Join-Path $root 'native/third_party/minhook/provenance.json') -Raw | ConvertFrom-Json
foreach ($entry in $pin.files) {
    $actual = (Get-FileHash (Join-Path $root ('native/third_party/minhook/' + $entry.path)) -Algorithm SHA256).Hash
    if ($actual -ne $entry.sha256) { throw "Pinned source hash mismatch: $($entry.path)" }
}
$script = @"
@echo off
call "$dev" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 exit /b 1
cd /d "$out"
cl /nologo /c /O2 /W4 /I"$root/native/third_party/minhook/include" "$root/native/third_party/minhook/src/buffer.c" "$root/native/third_party/minhook/src/hook.c" "$root/native/third_party/minhook/src/trampoline.c" "$root/native/third_party/minhook/src/hde/hde64.c"
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /W4 /Od /Zi /Fe:ObserverTests.exe "$root/native/tests/ObserverTests.cpp" "$root/native/src/ObserverEngine.cpp" buffer.obj hook.obj trampoline.obj hde64.obj /link /INCREMENTAL:NO
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /W4 /O2 /LD /Fe:SynapseObserver.Native.dll "$root/native/src/ObserverDll.cpp" "$root/native/src/ObserverEngine.cpp" buffer.obj hook.obj trampoline.obj hde64.obj /link /INCREMENTAL:NO
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /W4 /Od /Fe:ObserverDllTests.exe "$root/native/tests/ObserverDllTests.cpp" /link /INCREMENTAL:NO
if errorlevel 1 exit /b 1
"@
$command = Join-Path $out 'build-native.cmd'
[IO.File]::WriteAllText($command, $script.Replace("`r`n", "`n").Replace("`n", "`r`n"))
& $env:ComSpec /d /c $command
if ($LASTEXITCODE -ne 0) { throw "Native build failed ($LASTEXITCODE)." }
if ($RunTests) {
    & (Join-Path $out 'ObserverTests.exe')
    if ($LASTEXITCODE -ne 0) { throw "Native tests failed ($LASTEXITCODE)." }
    Push-Location $out
    try { & (Join-Path $out 'ObserverDllTests.exe'); if ($LASTEXITCODE -ne 0) { throw "Owned DLL tests failed ($LASTEXITCODE)." } } finally { Pop-Location }
}

$hash = (Get-FileHash (Join-Path $out 'SynapseObserver.Native.dll') -Algorithm SHA256).Hash
$generated = 'namespace SynapseObserver.Backend;' + "`r`n" + 'internal static class NativePayload { internal const string Sha256 = "' + $hash + '"; }' + "`r`n"
[IO.File]::WriteAllText((Join-Path $out 'NativePayload.g.cs'), $generated)
