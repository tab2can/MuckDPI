$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$out = Join-Path $root "native\WinDivert"
$ver = "2.2.2"
$zipName = "WinDivert-$ver-A.zip"
$url = "https://github.com/basil00/WinDivert/releases/download/v$ver/$zipName"
$zipPath = Join-Path $env:TEMP $zipName

if (Test-Path (Join-Path $out "x64\WinDivert.dll")) {
    Write-Host "WinDivert already present."
    exit 0
}

New-Item -ItemType Directory -Force $out | Out-Null
Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
$extract = Join-Path $env:TEMP "WinDivert-$ver-extract"
if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
Expand-Archive -Path $zipPath -DestinationPath $extract -Force
$inner = Get-ChildItem $extract -Directory | Select-Object -First 1
Copy-Item -Recurse -Force (Join-Path $inner.FullName "*") $out
Write-Host "WinDivert extracted to $out"
