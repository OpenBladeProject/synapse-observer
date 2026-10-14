param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskRoot 'artifacts/portable'
Push-Location $taskRoot
try {
    & (Join-Path $PSScriptRoot 'Build-NativeObserver.ps1') -RunTests
    if ($LASTEXITCODE -ne 0) { throw "Native checks failed ($LASTEXITCODE)." }
    dotnet restore SynapseObserver.slnx -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "Restore failed ($LASTEXITCODE)." }
    dotnet test SynapseObserver.slnx -c Release --no-restore -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "Solution tests failed ($LASTEXITCODE)." }
    dotnet publish src/SynapseObserver.App/SynapseObserver.App.csproj -c Release -r win-x64 --self-contained false --no-restore -p:NuGetAudit=false -p:DebugType=None -p:DebugSymbols=false -o $taskOutput
    if ($LASTEXITCODE -ne 0) { throw "GUI publish failed ($LASTEXITCODE)." }
    $taskExecutable = Join-Path $taskOutput 'SynapseObserver.exe'
    if (!(Test-Path -LiteralPath $taskExecutable) -or (Get-Item -LiteralPath $taskExecutable).Length -eq 0) { throw 'The portable executable is missing or empty.' }
    $taskUnexpected = @(Get-ChildItem -LiteralPath $taskOutput -File | Where-Object Name -notin @('SynapseObserver.exe', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'build-manifest.json'))
    if ($taskUnexpected.Count -ne 0) { throw 'Unexpected loose payload: use a fresh portable output directory and rebuild.' }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination (Join-Path $taskOutput 'LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.txt') -Destination $taskOutput
    $taskManifest = [ordered]@{
        schemaVersion = 1
        sourceRevision = ((git rev-parse HEAD) -join '').Trim()
        frameworkDependent = $true
        requiredRuntime = '.NET 10 Desktop Runtime (x64)'
        signingStatus = 'Unsigned development prototype'
        file = 'SynapseObserver.exe'
        sha256 = (Get-FileHash -LiteralPath $taskExecutable -Algorithm SHA256).Hash
        byteLength = (Get-Item -LiteralPath $taskExecutable).Length
    }
    [IO.File]::WriteAllText((Join-Path $taskOutput 'build-manifest.json'), ($taskManifest | ConvertTo-Json) + "`r`n", [Text.UTF8Encoding]::new($false))
    $taskArchive = Join-Path $taskRoot 'artifacts/SynapseObserver-win-x64-prototype.zip'
    Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $taskOutput -File).FullName -DestinationPath $taskArchive -Force
    Write-Output $taskArchive
} finally {
    Pop-Location
}
