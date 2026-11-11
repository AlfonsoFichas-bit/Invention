param(
    [string]$Version = "1.0.0",
    [string]$Arch = "x64",
    [switch]$InstallLocal,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$distDir = Join-Path $repoRoot "dist"
$bucketDir = Join-Path $repoRoot "bucket"

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}
if (-not (Test-Path $bucketDir)) {
    New-Item -ItemType Directory -Path $bucketDir -Force | Out-Null
}

$platform = $Arch
if ($Arch -eq "arm64") { $platform = "ARM64" }

if (-not $SkipBuild) {
    Write-Host "Building unpackaged Release for $Arch..." -ForegroundColor Cyan
    Push-Location $repoRoot
    try {
        dotnet publish Invention.csproj -c Release -p:Platform=$platform -p:RuntimeIdentifier=win-$Arch -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

# Locate publish directory
$candidateDirs = @(
    (Join-Path $repoRoot "bin\Release\net8.0-windows10.0.19041.0\win-$Arch\publish"),
    (Join-Path $repoRoot "bin\$platform\Release\net8.0-windows10.0.19041.0\win-$Arch\publish"),
    (Join-Path $repoRoot "bin\win-$Arch\publish")
)

$publishDir = $null
foreach ($dir in $candidateDirs) {
    if (Test-Path (Join-Path $dir "Invention.exe")) {
        $publishDir = $dir
        break
    }
}

if (-not $publishDir) {
    throw "Could not find publish directory containing Invention.exe in: $($candidateDirs -join ', ')"
}

Write-Host "Found published binary at: $publishDir" -ForegroundColor Green

$zipName = "Invention-v$Version-win-$Arch.zip"
$zipPath = Join-Path $distDir $zipName

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Write-Host "Creating zip package: $zipPath..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

Write-Host "Calculating SHA256 checksum..." -ForegroundColor Cyan
$sha256 = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "SHA256: $sha256" -ForegroundColor Green

# 1. Bucket manifest (for GitHub releases & Scoop bucket distribution)
$bucketManifestJson = @"
{
    "version": "$Version",
    "description": "Modern comic book reader for Windows built with WinUI 3",
    "homepage": "https://github.com/AlfonsoFichas-bit/Invention",
    "license": "MIT",
    "architecture": {
        "64bit": {
            "url": "https://github.com/AlfonsoFichas-bit/Invention/releases/download/v$Version/$zipName",
            "hash": "$sha256"
        }
    },
    "bin": [
        [
            "Invention.exe",
            "invention"
        ]
    ],
    "shortcuts": [
        [
            "Invention.exe",
            "Invention Comics"
        ]
    ],
    "suggest": {
        "vcredist": "extras/vcredist2022"
    },
    "checkver": "github",
    "autoupdate": {
        "architecture": {
            "64bit": {
                "url": "https://github.com/AlfonsoFichas-bit/Invention/releases/download/v`$version/Invention-v`$version-win-x64.zip"
            }
        }
    }
}
"@

$bucketManifestPath = Join-Path $bucketDir "invention.json"
[System.IO.File]::WriteAllText($bucketManifestPath, $bucketManifestJson, [System.Text.Encoding]::UTF8)
Write-Host "Generated Scoop bucket manifest at: $bucketManifestPath" -ForegroundColor Green

# 2. Local manifest (for immediate local installation via Scoop)
$fileUri = "file:///" + $zipPath.Replace("\", "/")
$localManifestJson = @"
{
    "version": "$Version",
    "description": "Modern comic book reader for Windows built with WinUI 3 (Local Package)",
    "homepage": "https://github.com/AlfonsoFichas-bit/Invention",
    "license": "MIT",
    "architecture": {
        "64bit": {
            "url": "$fileUri",
            "hash": "$sha256"
        }
    },
    "bin": [
        [
            "Invention.exe",
            "invention"
        ]
    ],
    "shortcuts": [
        [
            "Invention.exe",
            "Invention Comics"
        ]
    ],
    "suggest": {
        "vcredist": "extras/vcredist2022"
    }
}
"@

$localManifestPath = Join-Path $distDir "invention-local.json"
[System.IO.File]::WriteAllText($localManifestPath, $localManifestJson, [System.Text.Encoding]::UTF8)
Write-Host "Generated Local Scoop manifest at: $localManifestPath" -ForegroundColor Green

if ($InstallLocal) {
    Write-Host "Installing via Scoop..." -ForegroundColor Cyan
    scoop install $localManifestPath
}

Write-Host "`nAll done! To install locally with Scoop, run:" -ForegroundColor Yellow
Write-Host "  scoop install `"$localManifestPath`"`n" -ForegroundColor Yellow
