param([string]$SDKArchive)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$version=(Get-Content -LiteralPath (Join-Path $root 'release/version.txt') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid version'}
$manifest=Get-Content -LiteralPath (Join-Path $root 'release/source-sha256.json') -Raw | ConvertFrom-Json -AsHashtable
foreach($relative in $manifest.Keys){
    $p=Join-Path $root $relative
    if((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest[$relative]){throw "Source mismatch: $relative"}
}
$mod=@('__init__.py','core.py','runtime.py','fileio.py','party.py','party_memory.py','party_adapter.py','party_probe.py','party_audit.py','party_trace.py','party_names.py','party_settings.py')
$docs=@('README-RU.md','README-EN.md','CHANGELOG-RU.md','LICENSE.txt','SOURCES.md')
$out=Join-Path $root 'dist-release'
if(Test-Path -LiteralPath $out){throw 'dist-release already exists; use a fresh source folder'}
New-Item -ItemType Directory -Path $out | Out-Null
if(-not $SDKArchive){
    $SDKArchive=Join-Path $out 'oak2-sdk-v0.3.zip'
    Invoke-WebRequest 'https://github.com/bl-sdk/oak2-mod-manager/releases/download/v0.3/oak2-sdk.zip' -OutFile $SDKArchive
}
if((Get-FileHash -LiteralPath $SDKArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne '602675446abed184169fa158be3c8bc81777a71203581e4a248eca8a3d00b5c7'){throw 'SDK checksum mismatch'}
& (Join-Path $root 'build.ps1')
$bundle=Join-Path $out 'BL4-LAN'
$payload=Join-Path $bundle 'sdk_mods/bl4_lan'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'dist/BL4-LAN.exe') -Destination $bundle
Copy-Item -LiteralPath $SDKArchive -Destination (Join-Path $bundle 'oak2-sdk-v0.3.zip')
foreach($name in $mod){Copy-Item -LiteralPath (Join-Path $root "src/bl4_lan/$name") -Destination $payload}
foreach($name in $docs){Copy-Item -LiteralPath (Join-Path $root $name) -Destination $bundle}
$toolDir=Join-Path $bundle 'Tools'
New-Item -ItemType Directory -Path $toolDir | Out-Null
foreach($name in @('Start-Two-Clients.ps1','Two-Clients-RU.md')){Copy-Item -LiteralPath (Join-Path $root "Tools/$name") -Destination $toolDir}
$sumLines=Get-ChildItem -LiteralPath $bundle -Recurse -File | Sort-Object FullName | ForEach-Object {
    $rel=[IO.Path]::GetRelativePath($bundle,$_.FullName).Replace('\','/')
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$rel
}
[IO.File]::WriteAllLines((Join-Path $bundle 'SHA256SUMS.txt'),$sumLines,[Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$binary=Join-Path $out "BL4-LAN-v$version.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($bundle,$binary)
$source=Join-Path $out 'BL4-LAN-source'
New-Item -ItemType Directory -Path $source | Out-Null
foreach($relative in @($manifest.Keys)+@('release/source-sha256.json')){
    $target=Join-Path $source $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
}
$sourceZip=Join-Path $out "BL4-LAN-Source-v$version.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($source,$sourceZip)
$lines=@($binary,$sourceZip) | ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_)}
[IO.File]::WriteAllLines((Join-Path $out 'SHA256SUMS.txt'),$lines,[Text.UTF8Encoding]::new($false))
Write-Output "Prepared $version package and source archive"
