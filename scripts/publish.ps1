#Requires -Version 5.1
<#
.SYNOPSIS
    Publish the self-contained (full) and framework-dependent (light) win-x64
    builds of dstfarm.exe and print the release-notes checksum block.

.DESCRIPTION
    Builds publish/dstfarm.exe (needs no .NET on the target machine) and
    publish/dstfarm-light.exe (needs the .NET runtime, picked by install.ps1
    when it is present). Prints both SHA-256 hashes in the exact block format
    the release notes must carry: the file name in backticks followed by a
    fenced 64-hex hash, which install.ps1 and the self-updater parse with a
    lazy 80-char regex. Upload both files to the GitHub release and paste the
    printed block into its notes.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [ValidatePattern('^[a-z0-9-]+$')]
    [string] $Runtime = 'win-x64',
    [string] $OutputDirectory = 'publish'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $packageDir = [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    $packageDir = [System.IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
}
$fullPath = Join-Path $packageDir 'dstfarm.exe'
$lightPath = Join-Path $packageDir 'dstfarm-light.exe'
$staging = Join-Path $packageDir ('.publish-' + [guid]::NewGuid().ToString('N'))
$fullDir = Join-Path $staging 'app'
$lightDir = Join-Path $staging 'app-light'
$project = Join-Path $root 'src\DstFarm.Cli\DstFarm.Cli.csproj'

if ((Test-Path -LiteralPath $fullPath) -or (Test-Path -LiteralPath $lightPath)) {
    throw 'A package already exists. Use -OutputDirectory with a new directory; previous artifacts are never removed.'
}
New-Item -ItemType Directory -Path $fullDir -Force | Out-Null
New-Item -ItemType Directory -Path $lightDir -Force | Out-Null

try {
    Write-Host "==> publishing full $project" -ForegroundColor Cyan
    dotnet publish $project `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $fullDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish (full) failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path -LiteralPath (Join-Path $fullDir 'dstfarm.exe'))) {
        throw 'dstfarm.exe is missing from the full publish output'
    }
    Copy-Item -LiteralPath (Join-Path $fullDir 'dstfarm.exe') -Destination $fullPath -Force

    Write-Host "==> publishing light $project" -ForegroundColor Cyan
    # PublishTrimmed=false is required: a framework-dependent single-file
    # publish with trimming enabled fails with NETSDK1102.
    dotnet publish $project `
        -c $Configuration `
        -r $Runtime `
        --self-contained false `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $lightDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish (light) failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path -LiteralPath (Join-Path $lightDir 'dstfarm.exe'))) {
        throw 'dstfarm.exe is missing from the light publish output'
    }
    Copy-Item -LiteralPath (Join-Path $lightDir 'dstfarm.exe') -Destination $lightPath -Force

    $fullHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $lightHash = (Get-FileHash -LiteralPath $lightPath -Algorithm SHA256).Hash.ToLowerInvariant()
} finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host "exe:  $fullPath" -ForegroundColor Green
Write-Host "sha:  $fullHash"
Write-Host "size: $([math]::Round((Get-Item $fullPath).Length / 1MB, 1)) MB"
Write-Host ''
Write-Host "exe:  $lightPath" -ForegroundColor Green
Write-Host "sha:  $lightHash"
Write-Host "size: $([math]::Round((Get-Item $lightPath).Length / 1MB, 1)) MB"
Write-Host ''
Write-Host 'Paste into the release notes:' -ForegroundColor White
Write-Host ''
Write-Host 'SHA-256 `dstfarm.exe`:'
Write-Host ''
Write-Host '```'
Write-Host $fullHash
Write-Host '```'
Write-Host ''
Write-Host 'SHA-256 `dstfarm-light.exe`:'
Write-Host ''
Write-Host '```'
Write-Host $lightHash
Write-Host '```'
