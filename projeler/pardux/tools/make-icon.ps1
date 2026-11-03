# Pardux simgesi (düz, modern): koyu grafit "squircle" zemin, beyaz konuşma balonu yüz.
# Yana taranmış perçem balondan oyulmuş bir boşluk olarak durur; tek göz ve ince bir gülümseme. Gölge/parlaklık yok.
param([string]$Out, [string]$Preview)
Add-Type -AssemblyName System.Drawing

function C([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
function P([double]$x, [double]$y) { New-Object System.Drawing.PointF ([single]$x), ([single]$y) }
function T([double]$px, [double]$py) { P ($script:ox + $px * $script:q * $script:k) ($script:oy + $py * $script:q * $script:k) }

function Squircle([double]$x, [double]$y, [double]$w, [double]$h) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    $n = 5.0; $cx = $x + $w / 2; $cy = $y + $h / 2
    for ($i = 0; $i -lt 360; $i++) {
        $a = $i * [Math]::PI / 180
        $c = [Math]::Cos($a); $s = [Math]::Sin($a)
        $pts.Add((P ($cx + ($w / 2) * [Math]::Sign($c) * [Math]::Pow([Math]::Abs($c), 2 / $n)) ($cy + ($h / 2) * [Math]::Sign($s) * [Math]::Pow([Math]::Abs($s), 2 / $n))))
    }
    $p.AddPolygon($pts.ToArray())
    return $p
}

# Yuvarlatılmış dikdörtgen balon + küçük kuyruk (24'lük ızgarada)
function Bubble {
    $b = New-Object System.Drawing.Drawing2D.GraphicsPath
    $xl = 2.0; $yt = 3.0; $xr = 22.0; $yb = 18.0; $rad = 5.0
    $kk = 0.5523 * $rad
    $b.StartFigure()
    $b.AddLine((T ($xl + $rad) $yt), (T ($xr - $rad) $yt))
    $b.AddBezier((T ($xr - $rad) $yt), (T ($xr - $rad + $kk) $yt), (T $xr ($yt + $rad - $kk)), (T $xr ($yt + $rad)))
    $b.AddLine((T $xr ($yt + $rad)), (T $xr ($yb - $rad)))
    $b.AddBezier((T $xr ($yb - $rad)), (T $xr ($yb - $rad + $kk)), (T ($xr - $rad + $kk) $yb), (T ($xr - $rad) $yb))
    $b.AddLine((T ($xr - $rad) $yb), (T 11.4 $yb))
    $b.AddBezier((T 11.4 $yb), (T 9.6 $yb), (T 7.6 20.2), (T 5.6 21.8))
    $b.AddBezier((T 5.6 21.8), (T 6.2 20.4), (T 6.6 19.0), (T 6.4 $yb))
    $b.AddBezier((T 6.4 $yb), (T 3.9 $yb), (T $xl 16.2), (T $xl ($yb - $rad)))
    $b.AddLine((T $xl ($yb - $rad)), (T $xl ($yt + $rad)))
    $b.AddBezier((T $xl ($yt + $rad)), (T $xl ($yt + $rad - $kk)), (T ($xl + $rad - $kk) $yt), (T ($xl + $rad) $yt))
    $b.CloseFigure()
    return $b
}

# Perçem: balonun sol üstünden çaprazlama inip sol gözü örten sivri uç (balona oyulur)
function Fringe {
    $h = New-Object System.Drawing.Drawing2D.GraphicsPath
    $h.StartFigure()
    $h.AddBezier((T 16.6 4.3), (T 15.0 5.2), (T 13.0 6.6), (T 11.4 8.6))
    $h.AddBezier((T 11.4 8.6), (T 9.8 10.8), (T 7.6 12.8), (T 4.4 13.6))
    $h.AddBezier((T 4.4 13.6), (T 3.4 12.2), (T 3.3 10.2), (T 3.3 8.4))
    $h.AddLine((T 3.3 8.4), (T 3.3 7.6))
    $h.AddBezier((T 3.3 7.6), (T 3.3 5.4), (T 5.2 4.3), (T 7.4 4.3))
    $h.AddLine((T 7.4 4.3), (T 16.6 4.3))
    $h.CloseFigure()
    return $h
}

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = if ($s -ge 48) { $s * 0.06 } else { [Math]::Max(0.5, $s * 0.02) }
    $x = $m; $y = $m; $w = $s - 2 * $m; $h = $w

    # Zemin: koyu grafit, çok hafif dikey geçiş
    $bg = Squircle $x $y $w $h
    $rect = New-Object System.Drawing.RectangleF ([single]$x), ([single]$y), ([single]$w), ([single]$h)
    $base = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C 255 58 58 62), (C 255 24 24 27), 90.0
    $g.FillPath($base, $bg)

    $script:k = $w / 24.0; $script:q = 0.74
    $script:ox = $x + 3.05 * $script:k; $script:oy = $y + 3.6 * $script:k
    $q = $script:q; $k = $script:k

    # Beyaz balon, perçem bölgesi aynı zemin fırçasıyla oyulur
    $g.FillPath([System.Drawing.Brushes]::White, (Bubble))
    $g.FillPath($base, (Fringe))

    # Göz (tek, yuvarlak) ve gülümseme: zemin renginde
    $ink = New-Object System.Drawing.SolidBrush (C 255 30 30 33)
    $e = T 14.9 7.4
    $d = 3.2 * $q * $k
    $g.FillEllipse($ink, $e.X, $e.Y, [single]$d, [single]$d)
    $pen = New-Object System.Drawing.Pen (C 255 30 30 33), ([single]([Math]::Max(1.0, 1.7 * $q * $k)))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawBezier($pen, (T 11.8 13.4), (T 13.4 15.0), (T 15.6 15.0), (T 17.4 13.2))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($Preview -and $s -eq 256) { $bmp.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png) }
    if ($Preview -and $s -eq 32) { $bmp.Save(($Preview -replace '\.png$', '_32.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 256
$frames = $sizes | ForEach-Object { ,(New-Frame $_) }
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $frames[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $bw.Write([byte[]]$f) }
$bw.Close()
