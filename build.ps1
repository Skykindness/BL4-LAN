$ErrorActionPreference = 'Stop'
$product = Join-Path $PSScriptRoot 'src'
$assets = Join-Path $PSScriptRoot 'assets'
$dest = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dest -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /optimize+ /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/win32icon:$assets\skykindness.ico" "/resource:$assets\skykindness.png,BL4LAN.Brand.png" "/resource:$assets\skykindness.ico,BL4LAN.Brand.ico" "/out:$dest\BL4-LAN.exe" "$product\Launcher.cs" "$product\Localization.cs" "$product\Installer.cs" "$product\InstallJournal.cs" "$product\GameSettings.cs" "$product\Diagnostics.cs" "$product\DiagnosticMonitor.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Write-Output 'Built dist/BL4-LAN.exe. Keep the matching SDK archive and sdk_mods/bl4_lan payload from the release package beside it for installation.'
