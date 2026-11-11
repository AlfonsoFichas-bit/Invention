$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$certPath = Join-Path $repoRoot "MiCertificado.pfx"
$password = "mipass"

$archs = @("x64")
if ($args.Count -gt 0) { $archs = $args }

foreach ($arch in $archs) {
    $platform = $arch
    if ($arch -eq "arm64") { $platform = "ARM64" }
    Write-Host "Building $arch..."
    Push-Location $repoRoot
    dotnet publish -c Release -p:Platform=$platform -p:RuntimeIdentifier=win-$arch -p:PublishProfile=win-$arch.pubxml -p:WindowsPackageType=MSIX -p:EnableMsixTooling=true -p:PackageCertificateKeyFile=$certPath -p:PackageCertificatePassword=$password
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $arch" }
    Pop-Location
}
Write-Host "Done."
