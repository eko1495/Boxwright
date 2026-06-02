#requires -Version 7
<#
.SYNOPSIS
  Builds the self-contained Windows ZIP for Boxwright (ADR-0009): publishes the app, bundles
  QEMU (weilnetz, unmodified) under qemu/, drops the license + notices, and zips it.

.DESCRIPTION
  By default it downloads the pinned weilnetz QEMU installer and silently extracts it. Pass
  -QemuDir to copy a local QEMU install instead (faster for local testing; CI uses the download).

.EXAMPLE
  pwsh tools/package-windows.ps1 -Version 0.1.0
  pwsh tools/package-windows.ps1 -Version 0.1.0-local -QemuDir "C:\Program Files\qemu"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [string] $QemuDir = '',
    [string] $OutputDir = '',
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- Pinned weilnetz QEMU (Windows x64). To bump: update all three + ADR-0009 + NOTICES. ---
$QemuInstaller = 'qemu-w64-setup-20260501.exe'
$QemuUrl       = "https://qemu.weilnetz.de/w64/2026/$QemuInstaller"
$QemuSha512    = '3d6b996bb904666f3b7ff62bed233b2d21dffe96f512af0a7151cfcc828bc5c8f9b62623cf2a9d363a3ae48111761f4630cce776eacb9b70d83e61e6ae50de47'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'artifacts\win-x64' }
$publishDir = Join-Path $OutputDir 'publish'
$qemuOut    = Join-Path $publishDir 'qemu'
$cacheDir   = Join-Path $repoRoot 'artifacts\_cache'
$appProj    = Join-Path $repoRoot 'src\Boxwright.App\Boxwright.App.csproj'

Write-Host "Boxwright Windows packaging -> $OutputDir (version $Version)"

# 1. Publish self-contained (NOT trimmed, NOT single-file -> Avalonia-safe; see ADR-0009).
if (-not $SkipPublish) {
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    Write-Host 'Publishing self-contained win-x64 ...'
    $publishArgs = @(
        'publish', $appProj, '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:DebugType=none',
        '-o', $publishDir
    )
    dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}

# 2. Assemble qemu/ (unmodified).
if (Test-Path $qemuOut) { Remove-Item $qemuOut -Recurse -Force }
New-Item -ItemType Directory -Path $qemuOut | Out-Null

if ($QemuDir) {
    Write-Host "Copying local QEMU from $QemuDir ..."
    Copy-Item -Path (Join-Path $QemuDir '*') -Destination $qemuOut -Recurse -Force
}
else {
    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    $installer = Join-Path $cacheDir $QemuInstaller
    if (-not (Test-Path $installer)) {
        Write-Host "Downloading $QemuUrl ..."
        Invoke-WebRequest -Uri $QemuUrl -OutFile $installer
    }
    $actual = (Get-FileHash -Algorithm SHA512 $installer).Hash
    if ($actual -ine $QemuSha512) {
        throw "QEMU installer SHA-512 mismatch.`n expected $QemuSha512`n actual   $actual`nRe-pin in this script (and ADR-0009/NOTICES) or investigate."
    }
    # NSIS silent extract: /S = silent, /D=<dir> MUST be the last arg, unquoted, no spaces.
    Write-Host "Extracting QEMU into $qemuOut ..."
    $proc = Start-Process -FilePath $installer -ArgumentList '/S', "/D=$qemuOut" -Wait -PassThru
    if ($proc.ExitCode -ne 0) { throw "QEMU installer extract failed ($($proc.ExitCode))" }
}

# 3. Assert the bundle is complete (guards a partial copy/extract).
$required = @(
    'qemu-system-x86_64.exe',
    'qemu-img.exe',
    'share\edk2-x86_64-code.fd',
    'share\edk2-i386-vars.fd'
)
foreach ($f in $required) {
    if (-not (Test-Path (Join-Path $qemuOut $f))) { throw "Bundled QEMU is missing: qemu\$f" }
}

# 4. License + notices (GPL: QEMU is shipped unmodified with a written source offer).
Copy-Item (Join-Path $repoRoot 'LICENSE') $publishDir -Force
Copy-Item (Join-Path $repoRoot 'packaging\THIRD-PARTY-NOTICES.txt') $publishDir -Force
Copy-Item (Join-Path $repoRoot 'packaging\README-FIRST.txt') $publishDir -Force

# 5. Zip (ZipFile is faster + lighter than Compress-Archive for a large tree).
$zipPath = Join-Path $OutputDir "Boxwright-$Version-win-x64.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
$zipHash = (Get-FileHash -Algorithm SHA256 $zipPath).Hash
Write-Host ''
Write-Host "Created $zipPath ($sizeMB MB)"
Write-Host "SHA-256: $zipHash"
