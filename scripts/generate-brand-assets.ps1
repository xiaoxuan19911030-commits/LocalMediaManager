$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$brandRoot = Join-Path $root 'assets\brand'
$pngRoot = Join-Path $brandRoot 'png'
$iconRoot = Join-Path $root 'src-tauri\icons'
$installerRoot = Join-Path $brandRoot 'installer'
New-Item -ItemType Directory -Path $pngRoot, $iconRoot, $installerRoot -Force | Out-Null

function Add-RoundedRect([System.Drawing.Drawing2D.GraphicsPath]$path, [single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
}

function New-MarkBitmap([int]$size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $scale = $size / 256.0
    $bounds = [Drawing.RectangleF]::new(8*$scale, 8*$scale, 240*$scale, 240*$scale)
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    Add-RoundedRect $path $bounds.X $bounds.Y $bounds.Width $bounds.Height (56*$scale)
    $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new($bounds, [Drawing.Color]::FromArgb(22,168,255), [Drawing.Color]::FromArgb(124,58,237), 45)
    $blend = [Drawing.Drawing2D.ColorBlend]::new(3)
    $blend.Colors = @([Drawing.Color]::FromArgb(22,168,255), [Drawing.Color]::FromArgb(37,99,235), [Drawing.Color]::FromArgb(124,58,237))
    $blend.Positions = @([single]0, [single]0.52, [single]1)
    $gradient.InterpolationColors = $blend
    $g.FillPath($gradient, $path)

    $white = [Drawing.Color]::White
    $penL = [Drawing.Pen]::new($white, [single](27*$scale))
    $penL.StartCap = $penL.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $penL.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($penL, @([Drawing.PointF]::new(61*$scale,61*$scale),[Drawing.PointF]::new(61*$scale,187*$scale),[Drawing.PointF]::new(115*$scale,187*$scale)))
    $penM = [Drawing.Pen]::new($white, [single](23*$scale))
    $penM.StartCap = $penM.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $penM.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($penM, @([Drawing.PointF]::new(126*$scale,188*$scale),[Drawing.PointF]::new(126*$scale,91*$scale),[Drawing.PointF]::new(150*$scale,81*$scale),[Drawing.PointF]::new(170*$scale,101*$scale),[Drawing.PointF]::new(190*$scale,81*$scale),[Drawing.PointF]::new(214*$scale,91*$scale),[Drawing.PointF]::new(214*$scale,188*$scale)))
    $penL.Dispose(); $penM.Dispose(); $gradient.Dispose(); $path.Dispose(); $g.Dispose()
    return $bitmap
}

$sizes = @(16,24,32,48,64,128,256)
$pngData = @()
foreach ($size in $sizes) {
    $bitmap = New-MarkBitmap $size
    $path = Join-Path $pngRoot "lmm-$size.png"
    $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $pngData += ,$stream.ToArray()
    $stream.Dispose(); $bitmap.Dispose()
}

$icoPath = Join-Path $brandRoot 'lmm.ico'
$file = [IO.File]::Open($icoPath, [IO.FileMode]::Create)
$writer = [IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$pngData[$i].Length); $writer.Write([uint32]$offset)
    $offset += $pngData[$i].Length
}
foreach ($bytes in $pngData) { $writer.Write($bytes) }
$writer.Dispose(); $file.Dispose()
Copy-Item -LiteralPath $icoPath -Destination (Join-Path $iconRoot 'icon.ico') -Force
Copy-Item -LiteralPath (Join-Path $pngRoot 'lmm-32.png') -Destination (Join-Path $iconRoot '32x32.png') -Force
Copy-Item -LiteralPath (Join-Path $pngRoot 'lmm-128.png') -Destination (Join-Path $iconRoot '128x128.png') -Force
Copy-Item -LiteralPath (Join-Path $pngRoot 'lmm-256.png') -Destination (Join-Path $iconRoot '128x128@2x.png') -Force

function New-InstallerBitmap([int]$width, [int]$height, [string]$path, [bool]$sidebar) {
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::FromArgb(15,17,23))
    $markSize = if ($sidebar) { 96 } else { 44 }
    $mark = New-MarkBitmap $markSize
    $x = if ($sidebar) { [int](($width-$markSize)/2) } else { 8 }
    $y = if ($sidebar) { 42 } else { [int](($height-$markSize)/2) }
    $g.DrawImage($mark, $x, $y, $markSize, $markSize)
    if ($sidebar) {
        $font = [Drawing.Font]::new('Segoe UI', 15, [Drawing.FontStyle]::Bold)
        $small = [Drawing.Font]::new('Segoe UI', 9, [Drawing.FontStyle]::Regular)
        $format = [Drawing.StringFormat]::new(); $format.Alignment = [Drawing.StringAlignment]::Center
        $g.DrawString('Local Media', $font, [Drawing.Brushes]::White, [Drawing.RectangleF]::new(0,158,$width,30), $format)
        $g.DrawString('Manager', $font, [Drawing.Brushes]::White, [Drawing.RectangleF]::new(0,184,$width,30), $format)
        $g.DrawString('LMM', $small, [Drawing.Brushes]::LightGray, [Drawing.RectangleF]::new(0,224,$width,24), $format)
        $format.Dispose(); $font.Dispose(); $small.Dispose()
    } else {
        $font = [Drawing.Font]::new('Segoe UI', 12, [Drawing.FontStyle]::Bold)
        $small = [Drawing.Font]::new('Segoe UI', 7, [Drawing.FontStyle]::Regular)
        $g.DrawString('Local Media Manager', $font, [Drawing.Brushes]::White, 58, 12)
        $g.DrawString('LMM', $small, [Drawing.Brushes]::LightGray, 60, 34)
        $font.Dispose(); $small.Dispose()
    }
    $mark.Dispose(); $g.Dispose(); $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Bmp); $bitmap.Dispose()
}

New-InstallerBitmap 150 57 (Join-Path $installerRoot 'header.bmp') $false
New-InstallerBitmap 164 314 (Join-Path $installerRoot 'sidebar.bmp') $true
Write-Host "Generated LMM brand assets in $brandRoot"
