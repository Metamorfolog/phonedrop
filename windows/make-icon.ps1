# Vygeneruje PhoneDrop\Assets\PhoneDrop.ico – stejný motiv jako ikona Android appky
# (zelený kruh s přechodem, bílý telefon se šipkou dolů).
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $k = $s / 48.0
    $rect = New-Object System.Drawing.RectangleF 0, 0, ($s - 0.5), ($s - 0.5)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(70, 204, 116)), ([System.Drawing.Color]::FromArgb(18, 128, 72)), 50.0
    $g.FillEllipse($grad, $rect)
    $white = [System.Drawing.Color]::White
    $pen = New-Object System.Drawing.Pen $white, ([Math]::Max(1.3, 2.6 * $k))
    $pen.LineJoin = 'Round'; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    # Telefon se zaoblenými rohy.
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $x = 15.5 * $k; $y = 10.5 * $k; $w = 17 * $k; $h = 27 * $k; $r = 5 * $k
    $path.AddArc($x, $y, $r, $r, 180, 90); $path.AddArc($x + $w - $r, $y, $r, $r, 270, 90)
    $path.AddArc($x + $w - $r, $y + $h - $r, $r, $r, 0, 90); $path.AddArc($x, $y + $h - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $g.DrawPath($pen, $path)
    # Šipka dolů.
    $g.DrawLine($pen, [single](24 * $k), [single](16 * $k), [single](24 * $k), [single](27 * $k))
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([single](19.5 * $k)), ([single](23 * $k))),
        (New-Object System.Drawing.PointF ([single](24 * $k)), ([single](27.5 * $k))),
        (New-Object System.Drawing.PointF ([single](28.5 * $k)), ([single](23 * $k)))))
    if ($s -ge 32) {
        $brush = New-Object System.Drawing.SolidBrush $white
        $g.FillRectangle($brush, [single](21.5 * $k), [single](32.5 * $k), [single](5 * $k), [single](2 * $k))
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'PhoneDrop\Assets\PhoneDrop.ico'), $out.ToArray())
