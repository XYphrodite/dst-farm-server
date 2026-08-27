<#
.SYNOPSIS
    Ставит dstfarm — выделенный сервер Don't Starve Together под идл-фарм дропов Klei.

.DESCRIPTION
    Качает dstfarm.exe из релиза на GitHub, проверяет SHA-256 из описания релиза,
    кладёт в каталог установки и добавляет его в PATH пользователя.

.EXAMPLE
    irm https://raw.githubusercontent.com/XYphrodite/dst-farm-server/main/install.ps1 | iex

.EXAMPLE
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/XYphrodite/dst-farm-server/main/install.ps1))) -InstallDir 'D:\dstfarm'
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    # Куда положить dstfarm.exe. Рядом появятся config.json и .runtime — там же будет сервер, а это ещё около 4.2 ГБ.
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\dstfarm'),

    # Тег релиза, например v0.1.0. По умолчанию последний.
    [string] $Version = 'latest',

    # Не трогать PATH.
    [switch] $NoPath
)

$ErrorActionPreference = 'Stop'
$Repository = 'XYphrodite/dst-farm-server'
$AssetName = 'dstfarm.exe'

# В Windows PowerShell 5.1 прогресс-бар Invoke-WebRequest замедляет скачивание в разы.
$previousProgress = $ProgressPreference
$ProgressPreference = 'SilentlyContinue'

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
} catch {
    Write-Verbose "TLS 1.2 уже включён: $_"
}

function Write-Step {
    param([string] $Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

try {
    if ($Version -eq 'latest') {
        $releaseUrl = "https://api.github.com/repos/$Repository/releases/latest"
    } else {
        $releaseUrl = "https://api.github.com/repos/$Repository/releases/tags/$Version"
    }

    Write-Step "ищу релиз: $Version"
    $headers = @{ 'User-Agent' = 'dstfarm-installer'; 'Accept' = 'application/vnd.github+json' }
    $release = Invoke-RestMethod -Uri $releaseUrl -Headers $headers

    $asset = $release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
    if (-not $asset) {
        throw "в релизе $($release.tag_name) нет файла $AssetName"
    }

    $sizeMb = [math]::Round($asset.size / 1MB, 1)
    Write-Step "качаю $AssetName $($release.tag_name) ($sizeMb МБ)"

    $temp = Join-Path ([IO.Path]::GetTempPath()) ("dstfarm-" + [Guid]::NewGuid().ToString('N').Substring(0, 8) + ".exe")
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $temp -Headers @{ 'User-Agent' = 'dstfarm-installer' }

    # Контрольная сумма лежит в описании релиза строкой вида: SHA-256 `dstfarm.exe`: `<64 hex>`
    $expected = $null
    if ($release.body -match 'SHA-256[^`]*`?dstfarm\.exe`?\s*:\s*`?([0-9a-fA-F]{64})') {
        $expected = $Matches[1].ToLowerInvariant()
    }

    if ($expected) {
        $actual = (Get-FileHash -Path $temp -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            Remove-Item $temp -Force -ErrorAction SilentlyContinue
            throw "SHA-256 не совпал. Ожидалось $expected, получено $actual"
        }
        Write-Step 'SHA-256 совпал'
    } else {
        Write-Warning 'в описании релиза нет SHA-256, проверка контрольной суммы пропущена'
    }

    if (-not (Test-Path -LiteralPath $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }

    $target = Join-Path $InstallDir $AssetName
    $running = Get-Process -Name 'dstfarm' -ErrorAction SilentlyContinue
    if ($running) {
        Remove-Item $temp -Force -ErrorAction SilentlyContinue
        throw "dstfarm уже запущен (pid $($running.Id -join ', ')). Остановите сервер командой 'dstfarm stop' и повторите установку."
    }

    Write-Step "ставлю в $target"
    Move-Item -LiteralPath $temp -Destination $target -Force

    if (-not $NoPath) {
        $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
        if (-not $userPath) { $userPath = '' }
        $entries = $userPath -split ';' | Where-Object { $_ -ne '' }
        $alreadyThere = $entries | Where-Object { $_.TrimEnd('\') -ieq $InstallDir.TrimEnd('\') }

        if (-not $alreadyThere) {
            $newPath = (@($entries) + $InstallDir) -join ';'
            [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
            Write-Step 'каталог добавлен в PATH пользователя'
            Write-Host '    в новых окнах терминала команда dstfarm будет доступна сразу' -ForegroundColor DarkGray
        }

        # Чтобы команда работала прямо в текущем окне.
        if (($env:Path -split ';') -notcontains $InstallDir) {
            $env:Path = "$env:Path;$InstallDir"
        }
    }

    Write-Host ''
    Write-Host "dstfarm $($release.tag_name) установлен: $target" -ForegroundColor Green
    Write-Host ''
    Write-Host 'Дальше:' -ForegroundColor White
    Write-Host '  dstfarm install          ' -NoNewline -ForegroundColor Cyan
    Write-Host 'развернуть сервер DST (~2.9 ГБ загрузки, ~4.2 ГБ на диске)'
    Write-Host '  dstfarm token <ТОКЕН>    ' -NoNewline -ForegroundColor Cyan
    Write-Host 'токен из игры: Account -> Games -> Servers -> Add New Server'
    Write-Host '  dstfarm                  ' -NoNewline -ForegroundColor Cyan
    Write-Host 'полноэкранный интерфейс, клавиша S запускает сервер'
    Write-Host ''
    Write-Host "Документация: https://github.com/$Repository#readme" -ForegroundColor DarkGray
} finally {
    $ProgressPreference = $previousProgress
}
