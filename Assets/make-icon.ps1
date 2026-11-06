Add-Type -AssemblyName System.Drawing

$outDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$master = 512
$bmp = New-Object System.Drawing.Bitmap($master, $master)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

function New-RoundPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# background card
$bgPath = New-RoundPath 16 16 480 480 112
$bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)),
    (New-Object System.Drawing.Point(0, 512)),
    [System.Drawing.Color]::FromArgb(255, 40, 48, 63),
    [System.Drawing.Color]::FromArgb(255, 17, 20, 27))
$g.FillPath($bgBrush, $bgPath)
$borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(46, 255, 255, 255), 5)
$g.DrawPath($borderPen, $bgPath)

# soft cyan glow behind thermometer
$glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$glowPath.AddEllipse(116, 132, 280, 280)
$glowBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowPath)
$glowBrush.CenterColor = [System.Drawing.Color]::FromArgb(70, 56, 189, 248)
$glowBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 56, 189, 248))
$g.FillPath($glowBrush, $glowPath)

$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(242, 238, 244, 252))
$cyan = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 56, 189, 248))
$violet = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 129, 140, 248))

# thermometer bulb
$g.FillEllipse($white, 196, 342, 120, 120)
$g.FillEllipse($cyan, 216, 362, 80, 80)

# thermometer stem
$stemPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(242, 238, 244, 252), 46)
$stemPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$stemPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($stemPen, 256, 118, 256, 372)

# mercury (small rounded inner line)
$innerPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 56, 189, 248), 22)
$innerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$innerPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($innerPen, 256, 268, 256, 388)

# tick marks
$tickPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(150, 238, 244, 252), 9)
$tickPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$tickPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($tickPen, 292, 150, 318, 150)
$g.DrawLine($tickPen, 292, 200, 310, 200)
$g.DrawLine($tickPen, 292, 250, 318, 250)

$g.Dispose()

# save preview
$bmp.Save((Join-Path $outDir "icon-preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)

# build multi-size ico with PNG entries
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($s in $sizes) {
    $sb = New-Object System.Drawing.Bitmap($s, $s)
    $sg = [System.Drawing.Graphics]::FromImage($sb)
    $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $sg.DrawImage($bmp, 0, 0, $s, $s)
    $sg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $sb.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,$ms.ToArray()
    $sb.Dispose()
}
$bmp.Dispose()

$icoPath = Join-Path $outDir "app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
$fs.Close()
"icon written: $icoPath"
