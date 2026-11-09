# Draws the app icon and writes Assets\app.ico plus Assets\icon.png.
# Run this only to change the icon; the results are checked in and the normal build doesn't need it.
# It's a small version of the website's poster: a black wood-type F on the pink-to-yellow sheet.
param([string]$OutDir = (Join-Path $PSScriptRoot '..\src\Flyweight\Assets'))

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

$Pink = [System.Drawing.Color]::FromArgb(255, 255, 47, 142)
$Orange = [System.Drawing.Color]::FromArgb(255, 255, 138, 0)
$Yellow = [System.Drawing.Color]::FromArgb(255, 255, 230, 0)
$Ink = [System.Drawing.Color]::FromArgb(255, 18, 16, 15)

function New-SheetBrush([int]$size) {
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $Pink, $Yellow, 0.0
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = [System.Drawing.Color[]]@($Pink, $Orange, $Yellow)
    $blend.Positions = [single[]]@(0.0, 0.52, 1.0)
    $brush.InterpolationColors = $blend
    return $brush
}

# The letter is all straight edges, snapped to whole pixels so it stays crisp at 16 pixels.
function Get-LetterF([int]$sheet, [int]$offset) {
    function Snap([double]$v) { [Math]::Round($v * $sheet) + $offset }

    $left = 0.27
    $right = 0.73
    $top = 0.1
    $bottom = 0.9
    $height = $bottom - $top
    $stem = $left + ($right - $left) * 0.55
    $armBottom = $top + $height * 0.2
    $midTop = $top + $height * 0.42
    $midBottom = $midTop + $height * 0.19
    $midRight = $left + ($right - $left) * 0.91

    $corners = @(
        @($left, $top), @($right, $top), @($right, $armBottom), @($stem, $armBottom), @($stem, $midTop),
        @($midRight, $midTop), @($midRight, $midBottom), @($stem, $midBottom), @($stem, $bottom), @($left, $bottom)
    )
    $points = New-Object 'System.Drawing.PointF[]' $corners.Count
    for ($i = 0; $i -lt $corners.Count; $i++) {
        $points[$i] = New-Object System.Drawing.PointF (Snap $corners[$i][0]), (Snap $corners[$i][1])
    }
    return , $points
}

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'Half'
    $inkBrush = New-Object System.Drawing.SolidBrush $Ink

    # The hard shadow from the website's cards. Under 32 pixels the room goes to the letter instead.
    $shadow = if ($s -ge 32) { [int][Math]::Max(2, [Math]::Round($s * 0.07)) } else { 0 }
    $sheet = $s - $shadow
    if ($shadow -gt 0) { $g.FillRectangle($inkBrush, $shadow, $shadow, $sheet, $sheet) }
    $g.FillRectangle((New-SheetBrush $sheet), 0, 0, $sheet, $sheet)

    # The white offset behind the big type on the website.
    if ($s -ge 24) {
        $offset = [int][Math]::Max(1, [Math]::Round($s * 0.03))
        $g.FillPolygon([System.Drawing.Brushes]::White, (Get-LetterF $sheet $offset))
    }
    $g.FillPolygon($inkBrush, (Get-LetterF $sheet 0))

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) { $bitmap.Save((Join-Path $OutDir 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bitmap.Dispose()
    , $stream.ToArray()
}

$file = [System.IO.File]::Create((Join-Path $OutDir 'app.ico'))
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$pngs[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $writer.Write([byte[]]$png) }
$writer.Dispose()
$file.Dispose()

Write-Output "Wrote $((Get-Item (Join-Path $OutDir 'app.ico')).Length) bytes to app.ico"
