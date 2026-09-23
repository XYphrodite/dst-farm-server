<#
.SYNOPSIS
    Installs dstfarm - a Don't Starve Together dedicated server for idle Klei drop farming.

.DESCRIPTION
    Downloads dstfarm.exe from the GitHub release, verifies the SHA-256 published in the
    release notes, installs it and adds the install directory to the user PATH.

    When the .NET runtime is installed, the smaller framework-dependent
    dstfarm-light.exe is used instead; -Variant overrides the choice.
    The chosen variant is recorded next to the executable so `dstfarm update`
    keeps installing the same kind of build.

    This file is intentionally ASCII-only: Windows PowerShell 5.1 reads a BOM-less script
    as ANSI, while `irm | iex` chokes on a leading BOM. ASCII keeps both paths working.

.EXAMPLE
    irm https://raw.githubusercontent.com/XYphrodite/dst-farm-server/main/install.ps1 | iex

.EXAMPLE
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/XYphrodite/dst-farm-server/main/install.ps1))) -InstallDir 'D:\dstfarm'

.EXAMPLE
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/XYphrodite/dst-farm-server/main/install.ps1))) -Variant full

.PARAMETER Variant
    auto (default) picks the light package when the .NET runtime is installed,
    full always installs the self-contained package, light always installs the
    framework-dependent package (and fails fast without the runtime).
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    # Where to put dstfarm.exe. config.json and .runtime (the server itself, ~4.2 GB) land next to it.
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\dstfarm'),

    # Release tag, for example v0.1.0. Defaults to the latest release.
    [string] $Version = 'latest',

    [ValidateSet('auto', 'full', 'light')]
    [string] $Variant = 'auto',

    # Leave PATH alone.
    [switch] $NoPath
)

$ErrorActionPreference = 'Stop'
$Repository = 'XYphrodite/dst-farm-server'
$AssetName = 'dstfarm.exe'
$LightAssetName = 'dstfarm-light.exe'
$VariantMarker = '.dstfarm-variant'
# Major of the .NET runtime the light package targets. Bump together with the
# app's target framework; must match DotNetMajor in DstFarmUpdate.
$DotNetMajor = '10'

# In Windows PowerShell 5.1 the Invoke-WebRequest progress bar slows downloads down a lot.
$previousProgress = $ProgressPreference
$ProgressPreference = 'SilentlyContinue'

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
} catch {
    Write-Verbose "TLS 1.2 already enabled: $_"
}

function Write-Step {
    param([string] $Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-DotNetRuntimeLine {
    param([string] $Line, [string] $Major = $DotNetMajor)
    # Console builds run on Microsoft.NETCore.App; the desktop bundle counts too
    # because it includes the base runtime.
    return [bool]($Line -match "^Microsoft\.(NETCore|WindowsDesktop)\.App\s+$Major\.")
}

function Test-DotNetRuntime {
    try {
        $runtimes = & dotnet --list-runtimes 2>$null
    } catch {
        return $false
    }
    foreach ($line in @($runtimes)) {
        if (Test-DotNetRuntimeLine -Line ([string]$line)) { return $true }
    }
    return $false
}

function Select-DstFarmAsset {
    param(
        [ValidateSet('auto', 'full', 'light')]
        [string] $Variant = 'auto',
        [bool] $HasRuntime = $false
    )
    if ($Variant -eq 'light') { return $LightAssetName }
    if ($Variant -eq 'full') { return $AssetName }
    if ($HasRuntime) { return $LightAssetName }
    return $AssetName
}

try {
    if ($Version -eq 'latest') {
        $releaseUrl = "https://api.github.com/repos/$Repository/releases/latest"
    } else {
        $releaseUrl = "https://api.github.com/repos/$Repository/releases/tags/$Version"
    }

    Write-Step "looking up release: $Version"
    $headers = @{ 'User-Agent' = 'dstfarm-installer'; 'Accept' = 'application/vnd.github+json' }
    $release = Invoke-RestMethod -Uri $releaseUrl -Headers $headers

    $hasRuntime = Test-DotNetRuntime
    if ($Variant -eq 'light' -and -not $hasRuntime) {
        throw 'The light package needs the .NET runtime, which was not found. Install the runtime or use -Variant full.'
    }
    $AssetName = Select-DstFarmAsset -Variant $Variant -HasRuntime $hasRuntime
    $variantName = if ($AssetName -eq $LightAssetName) { 'light' } else { 'full' }

    $asset = $release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
    if (-not $asset) {
        throw "release $($release.tag_name) has no asset named $AssetName"
    }

    $sizeMb = [math]::Round($asset.size / 1MB, 1)
    Write-Step "downloading $AssetName $($release.tag_name) ($sizeMb MB)"

    $temp = Join-Path ([IO.Path]::GetTempPath()) ("dstfarm-" + [Guid]::NewGuid().ToString('N').Substring(0, 8) + ".exe")
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $temp -Headers @{ 'User-Agent' = 'dstfarm-installer' }

    # The release notes carry one block per asset, like: SHA-256 `dstfarm.exe`: `<64 hex>`
    # The match is anchored on the backticked asset name, so each variant verifies
    # against its own block. The lazy span matters: the file name itself contains
    # hex letters (d, f, a, e), and a greedy span could reach into the next block.
    $expected = $null
    $hashPattern = 'SHA-256 `' + [regex]::Escape($AssetName) + '`[\s\S]{0,80}?([0-9a-fA-F]{64})'
    if ($release.body -match $hashPattern) {
        $expected = $Matches[1].ToLowerInvariant()
    }

    if ($expected) {
        $actual = (Get-FileHash -Path $temp -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            Remove-Item $temp -Force -ErrorAction SilentlyContinue
            throw "SHA-256 mismatch. Expected $expected, got $actual"
        }
        Write-Step 'SHA-256 verified'
    } else {
        Write-Warning 'release notes carry no SHA-256, skipping checksum verification'
    }

    if (-not (Test-Path -LiteralPath $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }

    $target = Join-Path $InstallDir 'dstfarm.exe'
    $running = Get-Process -Name 'dstfarm' -ErrorAction SilentlyContinue
    if ($running) {
        Remove-Item $temp -Force -ErrorAction SilentlyContinue
        throw "dstfarm is running (pid $($running.Id -join ', ')). Stop it with 'dstfarm stop' and run the installer again."
    }

    Write-Step "installing to $target"
    Move-Item -LiteralPath $temp -Destination $target -Force
    # Records which kind of build this is, so `dstfarm update` keeps the variant.
    [IO.File]::WriteAllText((Join-Path $InstallDir $VariantMarker), $variantName)

    if (-not $NoPath) {
        $separator = [IO.Path]::PathSeparator
        $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
        if (-not $userPath) { $userPath = '' }
        $entries = @($userPath -split $separator | Where-Object { $_ -ne '' })
        $normalized = $InstallDir.TrimEnd([IO.Path]::DirectorySeparatorChar)
        $alreadyThere = $entries | Where-Object { $_.TrimEnd([IO.Path]::DirectorySeparatorChar) -ieq $normalized }

        if (-not $alreadyThere) {
            $newPath = ($entries + $InstallDir) -join $separator
            [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
            Write-Step 'install directory added to the user PATH'
            Write-Host '    new terminal windows will find dstfarm right away' -ForegroundColor DarkGray
        }

        # Make the command usable in the current window too.
        if (@($env:Path -split $separator) -notcontains $InstallDir) {
            $env:Path = $env:Path + $separator + $InstallDir
        }
    }

    Write-Host ''
    Write-Host "dstfarm $($release.tag_name) ($variantName) installed: $target" -ForegroundColor Green
    Write-Host ''
    Write-Host 'Next steps:' -ForegroundColor White
    Write-Host '  dstfarm install       ' -NoNewline -ForegroundColor Cyan
    Write-Host 'deploy the DST server (~2.9 GB download, ~4.2 GB on disk)'
    Write-Host '  dstfarm token <TOKEN> ' -NoNewline -ForegroundColor Cyan
    Write-Host 'cluster token: in game, Account -> Games -> Servers -> Add New Server'
    Write-Host '  dstfarm               ' -NoNewline -ForegroundColor Cyan
    Write-Host 'full-screen interface, press S to start the server'
    Write-Host ''
    Write-Host "Docs: https://github.com/$Repository#readme" -ForegroundColor DarkGray
} finally {
    $ProgressPreference = $previousProgress
}
