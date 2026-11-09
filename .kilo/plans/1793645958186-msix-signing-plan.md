# MSIX Signing & Installation Plan

## Goal
Enable the WinUI3 app (`Invention`) to be packaged as a signed MSIX and installed on machines, using a self-signed certificate (free).

## Root Cause / Current State
- `Package.appxmanifest` declares `Publisher="CN=15705535-6EFF-471F-83FE-8D933D684FE6"` (GUID style).
- The user's self-signed cert subject is `CN=AlfonsoFichas-bit`.
- MSIX requires the manifest `Publisher` to exactly match the signing certificate's subject, otherwise install fails with `0x800B0100` / publisher mismatch.

## Decisions
1. Set manifest `Publisher` to `CN=AlfonsoFichas-bit` to match the cert.
2. Keep the cert password `mipass` and file `MiCertificado.pfx` at repo root (gitignored).
3. Do not commit the `.pfx` or password-derived artifacts; add to `.gitignore`.
4. Sign during publish via MSBuild properties (`PackageCertificateKeyFile` + `PackageCertificatePassword`), not by editing csproj secrets; pass on command line in build script.
5. Install requires trusting the cert first (import into `Cert:\LocalMachine\Root` and `TrustedPeople`), then installing the `.msix`.

## Files to change
- `Package.appxmanifest`: `Publisher="CN=AlfonsoFichas-bit"`.
- `.gitignore`: add `*.pfx`, `*.cer`, `bin/`, `obj/` if missing.
- New `scripts/Create-Cert.ps1`: create self-signed cert (10 years), export PFX with password `mipass`.
- New `scripts/Build-Msix.ps1`: `dotnet publish` for x64 (and optionally x86/ARM64) with signing props, output `.msix`.
- New `scripts/Install-Cert.ps1`: import PFX into LocalMachine Root + TrustedPeople (requires admin).
- New `scripts/Install-App.ps1`: `Add-AppxPackage` the built MSIX.

## Implementation steps
1. Edit manifest publisher.
2. Update `.gitignore`.
3. Create `scripts/` directory with the four PowerShell scripts.
4. Run `Create-Cert.ps1` to generate `MiCertificado.pfx`.
5. Run `Build-Msix.ps1` to produce signed MSIX under `bin\<platform>\publish\`.
6. On target machine: run `Install-Cert.ps1` (admin) then `Install-App.ps1` (or double-click MSIX).

## Risks
- Self-signed cert must be installed on every target machine before MSIX install.
- Cert expires in 10 years; reissue and re-sign before expiry.
- Password stored in script is weak; acceptable for dev, not for production/Store. For Store submission, use a trusted CA cert and remove self-signed flow.
- Architecture mismatch: build per platform (x86/x64/ARM64); install matching MSIX.

## Validation
- `dotnet build` succeeds after manifest change.
- `Get-AuthenticodeSignature` on produced `.msix` shows valid signature.
- `Add-AppxPackage` installs without publisher-mismatch error.
- App launches from Start menu.

## Open questions
- None. User provided exact cert commands; follow them.