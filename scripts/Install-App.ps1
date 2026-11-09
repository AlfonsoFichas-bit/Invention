$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$msix = Get-ChildItem -Path (Join-Path $repoRoot "bin") -Recurse -Filter "*.msix" | Select-Object -First 1
if (-not $msix) { throw "No .msix found under bin/" }
Add-AppxPackage -Path $msix.FullName
Write-Host "Installed $($msix.FullName)"
