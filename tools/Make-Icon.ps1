# Erzeugt das Anwendungs-Icon (app.ico) mit mehreren Aufloesungen.
#
# Wird nur bei Bedarf einmalig ausgefuehrt; das Ergebnis app.ico ist eingecheckt.
# So braucht der eigentliche Build keine System.Drawing-Abhaengigkeit.
#
# Motiv: abgerundetes blaues Quadrat, darauf eine Lupe ueber drei Listenbalken
# (Inventar/Backup, das durchsucht wird).

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'

    $s = [double]$size

    # Hintergrund: abgerundetes Quadrat mit blauem Verlauf.
    $pad = [math]::Round($s * 0.04)
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, ($size - 2*$pad), ($size - 2*$pad))
    $radius = [math]::Max(2, [int]($s * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc(($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
    $path.AddArc(($rect.Right - $d), ($rect.Bottom - $d), $d, $d, 0, 90)
    $path.AddArc($rect.X, ($rect.Bottom - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    $c1 = [System.Drawing.Color]::FromArgb(255, 45, 108, 223)
    $c2 = [System.Drawing.Color]::FromArgb(255, 27, 77, 166)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 90.0)
    $g.FillPath($brush, $path)

    # Drei Listenbalken (Inventar) in Weiss, links unten.
    $barBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
    $barX = $s * 0.22
    $barW = $s * 0.34
    $barH = [math]::Max(1, $s * 0.055)
    $barGap = $s * 0.12
    for ($i = 0; $i -lt 3; $i++) {
        $y = $s * 0.30 + $i * $barGap
        $bx = New-Object System.Drawing.RectangleF($barX, $y, $barW, $barH)
        $g.FillRectangle($barBrush, $bx)
    }

    # Lupe: Ring plus Griff, rechts unten ueberlappend.
    $penW = [math]::Max(1.0, $s * 0.075)
    $ringPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penW)
    $ringPen.StartCap = 'Round'; $ringPen.EndCap = 'Round'
    $ringD = $s * 0.42
    $ringX = $s * 0.40
    $ringY = $s * 0.36
    $g.DrawEllipse($ringPen, $ringX, $ringY, $ringD, $ringD)

    $handlePen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, ($penW * 1.15))
    $handlePen.StartCap = 'Round'; $handlePen.EndCap = 'Round'
    $hx1 = $ringX + $ringD * 0.86
    $hy1 = $ringY + $ringD * 0.86
    $hx2 = $s * 0.86
    $hy2 = $s * 0.86
    $g.DrawLine($handlePen, [single]$hx1, [single]$hy1, [single]$hx2, [single]$hy2)

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngStreams = @()
foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngStreams += ,($ms.ToArray())
    $bmp.Dispose()
}

# ICO-Datei zusammensetzen (Vista+ erlaubt PNG-komprimierte Eintraege).
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0)      # reserved
$bw.Write([UInt16]1)      # type = icon
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $data = $pngStreams[$i]
    $bw.Write([Byte]($(if ($sz -ge 256) { 0 } else { $sz })))   # width
    $bw.Write([Byte]($(if ($sz -ge 256) { 0 } else { $sz })))   # height
    $bw.Write([Byte]0)    # palette
    $bw.Write([Byte]0)    # reserved
    $bw.Write([UInt16]1)  # planes
    $bw.Write([UInt16]32) # bpp
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngStreams) { $bw.Write($data) }
$bw.Flush()

$target = Join-Path $PSScriptRoot "..\src\IobBackupAnalyzer.App\app.ico"
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $target)).Path + "\app.ico", $out.ToArray())
"Icon geschrieben: $((Resolve-Path (Split-Path $target)).Path)\app.ico  ($($out.Length) Bytes)"
