# Draws the app icon and writes Assets\app.ico plus Assets\icon.png.
# Run this only to change the icon; the results are checked in and the normal build doesn't need it.
param([string]$OutDir = (Join-Path $PSScriptRoot '..\src\Flyweight\Assets'))

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-RoundedSquare([float]$size, [float]$radius) {
    $d = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d - 0.01, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d - 0.01, $size - $d - 0.01, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d - 0.01, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'

    $background = New-RoundedSquare $s ([Math]::Max(2, $s * 0.22))
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 22, 158, 145)), ([System.Drawing.Color]::FromArgb(255, 10, 94, 88)), 90
    $g.FillPath($brush, $background)

    $white = [System.Drawing.Brushes]::White
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, $s * 0.075))
    $pen.StartCap = 'Round'
    $pen.EndCap = 'Round'
    $pen.LineJoin = 'Round'

    function P([float]$x, [float]$y) { New-Object System.Drawing.PointF ($x * $s), ($y * $s) }

    if ($s -ge 24) {
        # Play symbol squeezed between two inward chevrons.
        $g.FillPolygon($white, [System.Drawing.PointF[]]@((P 0.385 0.335), (P 0.385 0.665), (P 0.645 0.5)))
        $g.DrawLines($pen, [System.Drawing.PointF[]]@((P 0.135 0.345), (P 0.245 0.5), (P 0.135 0.655)))
        $g.DrawLines($pen, [System.Drawing.PointF[]]@((P 0.865 0.345), (P 0.755 0.5), (P 0.865 0.655)))
    } else {
        # Too small for the chevrons to read, so the play symbol gets the room.
        $g.FillPolygon($white, [System.Drawing.PointF[]]@((P 0.31 0.2), (P 0.31 0.8), (P 0.82 0.5)))
    }

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
