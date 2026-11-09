$ErrorActionPreference = "Stop"
$certName = "CN=AlfonsoFichas-bit"
$pfxPath = Join-Path $PSScriptRoot "..\MiCertificado.pfx"
$password = ConvertTo-SecureString -String "mipass" -Force -AsPlainText

$cert = New-SelfSignedCertificate -Type Custom -Subject $certName -KeyUsage DigitalSignature -FriendlyName "Invention Dev Cert" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") -NotAfter (Get-Date).AddYears(10)

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password | Out-Null
Write-Host "Created $pfxPath"
