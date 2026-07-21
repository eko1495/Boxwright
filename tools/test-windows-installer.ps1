#requires -Version 7
<#
.SYNOPSIS
  Smoke-tests a Boxwright setup EXE: silent per-user install, assert the expected layout,
  silent uninstall, assert removal. Used locally and by package-windows.yml.

  WARNING: really installs into (and then removes) %LOCALAPPDATA%\Programs\Boxwright on
  THIS machine - do not run on a machine with a Boxwright install you care about.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $SetupExe
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Boxwright'
$silentArgs = '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'

Write-Host "Installing $SetupExe ..."
$p = Start-Process -FilePath (Resolve-Path $SetupExe) -ArgumentList $silentArgs -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Setup exited with $($p.ExitCode)" }

foreach ($f in 'Boxwright.App.exe', 'qemu\qemu-system-x86_64.exe', 'THIRD-PARTY-NOTICES.txt', 'unins000.exe') {
    $path = Join-Path $installDir $f
    if (-not (Test-Path $path)) { throw "Missing after install: $path" }
}
Write-Host "Install layout OK: $installDir"

Write-Host 'Uninstalling ...'
$p = Start-Process -FilePath (Join-Path $installDir 'unins000.exe') -ArgumentList $silentArgs -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Uninstaller exited with $($p.ExitCode)" }

# unins000.exe hands off to a temp copy of itself; the folder disappears shortly after it returns.
$deadline = (Get-Date).AddSeconds(30)
while ((Test-Path $installDir) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
if (Test-Path $installDir) { throw "Install dir still present after uninstall: $installDir" }
Write-Host 'Uninstall OK.'
