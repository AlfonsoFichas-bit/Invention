$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$certPath = Join-Path $repoRoot "MiCertificado.pfx"
$password = ConvertTo-SecureString -String "mipass" -Force -AsPlainText

$pfx = Get-PfxCertificate -FilePath $certPath -Password $password
Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Import-PfxCertificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $password | Out-Null
Write-Host "Certificate trusted."
