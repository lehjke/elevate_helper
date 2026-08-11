param(
    [string]$OutputDirectory = "Assets"
)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"

$fullOutputDirectory = Join-Path (Get-Location) $OutputDirectory
New-Item -ItemType Directory -Path $fullOutputDirectory -Force | Out-Null

$pngPath = Join-Path $fullOutputDirectory "AppIcon.png"
$icoPath = Join-Path $fullOutputDirectory "AppIcon.ico"

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Rect,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($Rect.X, $Rect.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rect.Right - $diameter, $Rect.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rect.Right - $diameter, $Rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rect.X, $Rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Save-PngFrame {
    param(
        [System.Drawing.Image]$Image,
        [string]$Path
    )

    $Image.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
}

function New-ScaledBitmap {
    param(
        [System.Drawing.Bitmap]$Source,
        [int]$Size
    )

    $scaled = New-Object System.Drawing.Bitmap $Size, $Size
    $graphics = [System.Drawing.Graphics]::FromImage($scaled)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($Source, 0, 0, $Size, $Size)
    $graphics.Dispose()
    return $scaled
}

function New-FittedBitmap {
    param(
        [System.Drawing.Bitmap]$Source,
        [int]$Width,
        [int]$Height,
        [int]$ArtworkSize
    )

    if ($ArtworkSize -gt [Math]::Min($Width, $Height)) {
        throw "ArtworkSize must fit inside the target canvas."
    }

    $fitted = New-Object System.Drawing.Bitmap $Width, $Height
    $graphics = [System.Drawing.Graphics]::FromImage($fitted)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $left = [int](($Width - $ArtworkSize) / 2)
    $top = [int](($Height - $ArtworkSize) / 2)
    $graphics.DrawImage($Source, $left, $top, $ArtworkSize, $ArtworkSize)
    $graphics.Dispose()
    return $fitted
}

$canvasSize = 1024
$bitmap = New-Object System.Drawing.Bitmap $canvasSize, $canvasSize
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.Clear([System.Drawing.Color]::Transparent)

$tileRect = New-Object System.Drawing.RectangleF 64, 64, 896, 896
$tilePath = New-RoundedRectanglePath -Rect $tileRect -Radius 128

$shadowColor = [System.Drawing.Color]::FromArgb(22, 0, 0, 0)
for ($i = 0; $i -lt 18; $i++) {
    $shadowPath = $tilePath.Clone()
    $matrix = New-Object System.Drawing.Drawing2D.Matrix
    $matrix.Translate(0, [float](12 + $i * 2.2))
    $shadowPath.Transform($matrix)
    $graphics.FillPath((New-Object System.Drawing.SolidBrush $shadowColor), $shadowPath)
    $shadowPath.Dispose()
    $matrix.Dispose()
}

$panelBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(250, 250, 250))
$panelPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(235, 235, 235), 3)
$graphics.FillPath($panelBrush, $tilePath)
$graphics.DrawPath($panelPen, $tilePath)

$glyphBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(31, 31, 31))

$leftTriangle = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 230, 414),
    (New-Object System.Drawing.PointF 522, 414),
    (New-Object System.Drawing.PointF 376, 646)
)
$graphics.FillPolygon($glyphBrush, $leftTriangle)

$rightArrow = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 520, 562),
    (New-Object System.Drawing.PointF 744, 562),
    (New-Object System.Drawing.PointF 744, 405),
    (New-Object System.Drawing.PointF 857, 405),
    (New-Object System.Drawing.PointF 677, 225),
    (New-Object System.Drawing.PointF 497, 405),
    (New-Object System.Drawing.PointF 610, 405),
    (New-Object System.Drawing.PointF 610, 518),
    (New-Object System.Drawing.PointF 552, 466)
)
$graphics.FillPolygon($glyphBrush, $rightArrow)

$baseShape = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 368, 753),
    (New-Object System.Drawing.PointF 690, 526),
    (New-Object System.Drawing.PointF 744, 489),
    (New-Object System.Drawing.PointF 744, 718),
    (New-Object System.Drawing.PointF 690, 718),
    (New-Object System.Drawing.PointF 597, 810),
    (New-Object System.Drawing.PointF 376, 810)
)
$graphics.FillPolygon($glyphBrush, $baseShape)

$slashPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White, 56)
$slashPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$slashPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawLine($slashPen, 378, 748, 770, 470)

$innerArrow = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF 580, 430),
    (New-Object System.Drawing.PointF 647, 430),
    (New-Object System.Drawing.PointF 647, 363),
    (New-Object System.Drawing.PointF 714, 363),
    (New-Object System.Drawing.PointF 614, 262),
    (New-Object System.Drawing.PointF 513, 363),
    (New-Object System.Drawing.PointF 580, 363)
)
$graphics.FillPolygon(([System.Drawing.Brushes]::White), $innerArrow)

Save-PngFrame -Image $bitmap -Path $pngPath

$frameSizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = New-Object System.Collections.Generic.List[byte[]]
foreach ($frameSize in $frameSizes) {
    $scaledBitmap = New-ScaledBitmap -Source $bitmap -Size $frameSize
    $memoryStream = New-Object System.IO.MemoryStream
    $scaledBitmap.Save($memoryStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($memoryStream.ToArray())
    $memoryStream.Dispose()
    $scaledBitmap.Dispose()
}

$fileStream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($fileStream)

$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$frames.Count)

$offset = 6 + (16 * $frames.Count)
for ($index = 0; $index -lt $frames.Count; $index++) {
    $size = $frameSizes[$index]
    $bytes = $frames[$index]

    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$bytes.Length)
    $writer.Write([UInt32]$offset)

    $offset += $bytes.Length
}

foreach ($frameBytes in $frames) {
    $writer.Write($frameBytes)
}

$writer.Dispose()
$fileStream.Dispose()

foreach ($mapping in @(
    @{ Name = "Square44x44Logo.png"; Width = 44; Height = 44; ArtworkSize = 44 },
    @{ Name = "Square44x44Logo.scale-200.png"; Width = 88; Height = 88; ArtworkSize = 88 },
    @{ Name = "Square44x44Logo.targetsize-24_altform-unplated.png"; Width = 24; Height = 24; ArtworkSize = 24 },
    @{ Name = "Square150x150Logo.png"; Width = 150; Height = 150; ArtworkSize = 150 },
    @{ Name = "Square150x150Logo.scale-200.png"; Width = 300; Height = 300; ArtworkSize = 300 },
    @{ Name = "Wide310x150Logo.png"; Width = 310; Height = 150; ArtworkSize = 126 },
    @{ Name = "Wide310x150Logo.scale-200.png"; Width = 620; Height = 300; ArtworkSize = 252 },
    @{ Name = "StoreLogo.png"; Width = 50; Height = 50; ArtworkSize = 50 },
    @{ Name = "SplashScreen.png"; Width = 620; Height = 300; ArtworkSize = 220 },
    @{ Name = "SplashScreen.scale-200.png"; Width = 1240; Height = 600; ArtworkSize = 440 },
    @{ Name = "LockScreenLogo.scale-200.png"; Width = 48; Height = 48; ArtworkSize = 48 }
)) {
    $scaled = New-FittedBitmap -Source $bitmap -Width $mapping.Width -Height $mapping.Height -ArtworkSize $mapping.ArtworkSize
    Save-PngFrame -Image $scaled -Path (Join-Path $fullOutputDirectory $mapping.Name)
    $scaled.Dispose()
}

$graphics.Dispose()
$tilePath.Dispose()
$panelBrush.Dispose()
$panelPen.Dispose()
$glyphBrush.Dispose()
$slashPen.Dispose()
$bitmap.Dispose()
