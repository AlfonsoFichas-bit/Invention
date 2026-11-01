$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$edgePath = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$sourcePath = Join-Path $PSScriptRoot 'logo.svg'
$assetsPath = Join-Path $PSScriptRoot 'Assets'
$tempPath = Join-Path $env:TEMP 'InventionLogoIcons'
$svg = [System.IO.File]::ReadAllText($sourcePath)
New-Item -ItemType Directory -Force -Path $tempPath | Out-Null

function Export-LogoPng([string]$color, [string]$outputPath) {
	$svgText = $script:svg.Replace('.cls-1{fill:#000000}', ".cls-1{fill:$color}")
	$svgPath = Join-Path $script:tempPath "$color.svg"
	$htmlPath = Join-Path $script:tempPath "$color.html"
	$svgText = $svgText -replace '<svg\b', '<svg width="2048" height="2048"'
	[System.IO.File]::WriteAllText($svgPath, $svgText, [System.Text.Encoding]::UTF8)
	$html = "<html><body style='margin:0;background:transparent'><img width='2048' height='2048' src='$(([System.Uri]$svgPath).AbsoluteUri)'></body></html>"
	[System.IO.File]::WriteAllText($htmlPath, $html, [System.Text.Encoding]::UTF8)
	$uri = ([System.Uri]$htmlPath).AbsoluteUri
	$profilePath = Join-Path $script:tempPath "$color-profile"
	$process = Start-Process -FilePath $script:edgePath -ArgumentList @('--headless', '--disable-gpu', '--no-first-run', '--no-default-browser-check', "--user-data-dir=$profilePath", '--window-size=2048,2048', '--default-background-color=00000000', "--screenshot=$outputPath", $uri) -Wait -PassThru -NoNewWindow
	if ($process.ExitCode -ne 0 -or -not (Test-Path $outputPath)) {
		throw "Edge failed to rasterize $color logo. Exit code: $($process.ExitCode)"
	}
	$raster = [System.Drawing.Bitmap]::new($outputPath)
	if ($raster.Width -ne 2048 -or $raster.Height -ne 2048) {
		$raster.Dispose()
		throw "Expected a 2048x2048 raster for $color logo; got $($raster.Width)x$($raster.Height)."
	}
	$raster.Dispose()
}

function Export-ResizedPng([System.Drawing.Bitmap]$source, [int]$size, [string]$outputPath) {
	$bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
	$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
	$graphics.Clear([System.Drawing.Color]::Transparent)
	$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
	$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
	$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
	$graphics.DrawImage($source, 0, 0, $size, $size)
	$bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
	$graphics.Dispose()
	$bitmap.Dispose()
}

function Export-Ico([System.Drawing.Bitmap]$source, [string]$outputPath) {
	$sizes = @(16, 24, 32, 48, 64, 128, 256)
	$pngPaths = @()
	foreach ($size in $sizes) {
		$pngPath = Join-Path $script:tempPath "icon-$size.png"
		Export-ResizedPng $source $size $pngPath
		$pngPaths += $pngPath
	}
	$pngBytes = [System.Collections.Generic.List[byte[]]]::new()
	foreach ($pngPath in $pngPaths) {
		$pngBytes.Add([System.IO.File]::ReadAllBytes($pngPath))
	}
	$stream = [System.IO.File]::Create($outputPath)
	$writer = [System.IO.BinaryWriter]::new($stream)
	$writer.Write([UInt16]0)
	$writer.Write([UInt16]1)
	$writer.Write([UInt16]$sizes.Count)
	$dataOffset = 6 + 16 * $sizes.Count
	for ($index = 0; $index -lt $sizes.Count; $index++) {
		$size = $sizes[$index]
		$writer.Write([Byte]$(if ($size -eq 256) { 0 } else { $size }))
		$writer.Write([Byte]$(if ($size -eq 256) { 0 } else { $size }))
		$writer.Write([Byte]0)
		$writer.Write([Byte]0)
		$writer.Write([UInt16]1)
		$writer.Write([UInt16]32)
		$writer.Write([UInt32]$pngBytes[$index].Length)
		$writer.Write([UInt32]$dataOffset)
		$dataOffset += $pngBytes[$index].Length
	}
	foreach ($bytes in $pngBytes) {
		$writer.Write($bytes)
	}
	$writer.Dispose()
	$stream.Dispose()
}

$lightPngPath = Join-Path $tempPath 'logo-light.png'
$darkPngPath = Join-Path $tempPath 'logo-dark.png'
Export-LogoPng '#000000' $lightPngPath
Export-LogoPng '#FFFFFF' $darkPngPath

$lightBitmap = [System.Drawing.Bitmap]::new($lightPngPath)
$darkBitmap = [System.Drawing.Bitmap]::new($darkPngPath)
Export-Ico $lightBitmap (Join-Path $assetsPath 'InventionLight.ico')
Export-Ico $darkBitmap (Join-Path $assetsPath 'InventionDark.ico')
$darkBitmap.Dispose()

foreach ($scale in @(100, 125, 150, 200, 250, 300, 400)) {
	$square44Size = [Math]::Round(44 * $scale / 100)
	$square150Size = [Math]::Round(150 * $scale / 100)
	Export-ResizedPng $lightBitmap $square44Size (Join-Path $assetsPath "Square44x44Logo.scale-$scale.png")
	Export-ResizedPng $lightBitmap $square150Size (Join-Path $assetsPath "Square150x150Logo.scale-$scale.png")
}
$targetSizes = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
foreach ($size in $targetSizes) {
	$targetPath = Join-Path $assetsPath "Square44x44Logo.targetsize-$size.png"
	Export-ResizedPng $lightBitmap $size $targetPath
	Copy-Item $targetPath (Join-Path $assetsPath "Square44x44Logo.targetsize-${size}_altform-unplated.png") -Force
}
Export-ResizedPng $lightBitmap 150 (Join-Path $assetsPath 'Square150x150Logo.png')
Export-ResizedPng $lightBitmap 44 (Join-Path $assetsPath 'Square44x44Logo.png')
Export-ResizedPng $lightBitmap 24 (Join-Path $assetsPath 'Square44x44Logo.targetsize-24_altform-unplated.png')
Export-ResizedPng $lightBitmap 50 (Join-Path $assetsPath 'StoreLogo.png')
$lightBitmap.Dispose()

Write-Output 'Generated the light/dark SVG rasters and MSIX PNG assets.'
