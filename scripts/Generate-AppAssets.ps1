# Generate-AppAssets.ps1
# Takes source logo and generates all required MSIX Assets and AppIcon.ico

Add-Type -AssemblyName System.Drawing

$SourcePath = "d:\vapour vault\vapor vault logo.jpeg"
$AssetsDir = "d:\vapour vault\src\VaporVault.App\Assets"

if (-not (Test-Path $SourcePath)) {
    Write-Error "Source image not found at $SourcePath"
    exit 1
}

$srcImage = [System.Drawing.Image]::FromFile($SourcePath)
Write-Host "Source image loaded: $($srcImage.Width)x$($srcImage.Height)"

# Helper function to resize square logo with high quality
function Resize-Square ($targetWidth, $targetHeight, $outputPath) {
    $bmp = New-Object System.Drawing.Bitmap($targetWidth, $targetHeight)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $g.DrawImage($srcImage, 0, 0, $targetWidth, $targetHeight)
    $bmp.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    
    $g.Dispose()
    $bmp.Dispose()
    Write-Host "Generated: $outputPath ($($targetWidth)x$($targetHeight))"
}

# Helper function for wide aspect ratio assets (centered square logo on dark background)
function Resize-Wide ($targetWidth, $targetHeight, $logoHeight, $outputPath) {
    $bmp = New-Object System.Drawing.Bitmap($targetWidth, $targetHeight)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    # Dark background matching vault theme
    $bgColor = [System.Drawing.Color]::FromArgb(255, 11, 14, 20)
    $g.Clear($bgColor)

    $logoWidth = $logoHeight
    $x = ($targetWidth - $logoWidth) / 2
    $y = ($targetHeight - $logoHeight) / 2

    $g.DrawImage($srcImage, $x, $y, $logoWidth, $logoHeight)
    $bmp.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $g.Dispose()
    $bmp.Dispose()
    Write-Host "Generated Wide: $outputPath ($($targetWidth)x$($targetHeight))"
}

# Generate scaled PNG assets
Resize-Square 88 88 (Join-Path $AssetsDir "Square44x44Logo.scale-200.png")
Resize-Square 44 44 (Join-Path $AssetsDir "Square44x44Logo.png")
Resize-Square 24 24 (Join-Path $AssetsDir "Square44x44Logo.targetsize-24_altform-unplated.png")
Resize-Square 48 48 (Join-Path $AssetsDir "Square44x44Logo.targetsize-48_altform-lightunplated.png")
Resize-Square 300 300 (Join-Path $AssetsDir "Square150x150Logo.scale-200.png")
Resize-Square 150 150 (Join-Path $AssetsDir "Square150x150Logo.png")
Resize-Wide 620 300 240 (Join-Path $AssetsDir "Wide310x150Logo.scale-200.png")
Resize-Wide 310 150 120 (Join-Path $AssetsDir "Wide310x150Logo.png")
Resize-Wide 1240 600 400 (Join-Path $AssetsDir "SplashScreen.scale-200.png")
Resize-Wide 620 300 200 (Join-Path $AssetsDir "SplashScreen.png")
Resize-Square 48 48 (Join-Path $AssetsDir "LockScreenLogo.scale-200.png")
Resize-Square 50 50 (Join-Path $AssetsDir "StoreLogo.png")

# Generate AppIcon.ico using multi-resolution ICO writer
function Export-IconFile ($outputPath) {
    # Generate 256x256 bitmap
    $bmp256 = New-Object System.Drawing.Bitmap(256, 256)
    $g = [System.Drawing.Graphics]::FromImage($bmp256)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($srcImage, 0, 0, 256, 256)
    $g.Dispose()

    # Get Hicon and convert to System.Drawing.Icon
    $hIcon = $bmp256.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($hIcon)

    $fs = [System.IO.File]::Create($outputPath)
    $icon.Save($fs)
    $fs.Close()

    $bmp256.Dispose()
    Write-Host "Generated ICO: $outputPath"
}

Export-IconFile (Join-Path $AssetsDir "AppIcon.ico")

$srcImage.Dispose()
Write-Host "All assets generated successfully."
