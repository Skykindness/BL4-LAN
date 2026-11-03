param(
    [ValidateSet('Both','Host','Guest')][string]$Client = 'Both',
    [string]$GameDirectory = ''
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GameDirectory)) { $GameDirectory = Read-Host 'Borderlands 4 game folder' }
$exe = Join-Path $GameDirectory 'OakGame\Binaries\Win64\Borderlands4.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Borderlands4.exe not found.' }
$profileRoot = Join-Path $PSScriptRoot 'TwoClientProfiles'
$sourceSaves = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Borderlands 4\Saved\SaveGames'
$roles = if ($Client -eq 'Both') { @('Host','Guest') } else { @($Client) }
foreach ($role in $roles) {
    $profile = Join-Path $profileRoot $role
    if (-not (Test-Path -LiteralPath $profile)) {
        if (-not (Test-Path -LiteralPath $sourceSaves -PathType Container)) { throw 'Start the game once and create a character first.' }
        $saveBytes = (Get-ChildItem -LiteralPath $sourceSaves -Recurse -File | Measure-Object -Property Length -Sum).Sum
        if ($saveBytes -gt 64MB) { throw 'Save folder exceeds 64 MB. Create test profiles manually.' }
        $saved = Join-Path $profile 'Saved'
        New-Item -ItemType Directory -Path $saved -Force | Out-Null
        Copy-Item -LiteralPath $sourceSaves -Destination (Join-Path $saved 'SaveGames') -Recurse
    }
    $port = if ($role -eq 'Host') { 7787 } else { 7788 }
    $arguments = @('-NoSplash', ('-UserDir="{0}"' -f $profile), '-SaveToUserDir', '-windowed', '-ResX=960', '-ResY=540', ('-port={0}' -f $port), '-ExecCmds="t.MaxFPS 30"')
    $process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -LiteralPath $exe) -ArgumentList $arguments -WindowStyle Normal -PassThru
    Write-Host ("{0}: PID {1}; profile {2}" -f $role,$process.Id,$profile)
}
Write-Host 'Load a character in BOTH windows using Continue.'
Write-Host 'Host console: bl4lan host'
Write-Host 'Guest console: bl4lan join 127.0.0.1:7787'
Write-Host 'Close the mods menu with Q before entering these commands.'
Write-Host 'Do not launch the same test profile twice. Two clients need extra RAM and VRAM.'
