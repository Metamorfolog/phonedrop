# Vygeneruje PhoneDrop\Assets\PhoneDrop.ico (stejný motiv jako ikona Android appky).
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $k = $s / 48.0
    $g.FillEllipse([System.Drawing.Brushes]::Black, 0, 0, $s - 1, $s - 1)
    $green = [System.Drawing.Color]::FromArgb(127, 209, 139)
    $pen = New-Object System.Drawing.Pen $green, ([Math]::Max(1.2, 2.5 * $k))
    $g.DrawRectangle($pen, [single](15 * $k), [single](10 * $k), [single](18 * $k), [single](28 * $k))
    $brush = New-Object System.Drawing.SolidBrush $green
    $pts = @(
        (New-Object System.Drawing.PointF ([single](22.5 * $k)), ([single](15 * $k))),
        (New-Object System.Drawing.PointF ([single](25.5 * $k)), ([single](15 * $k))),
        (New-Object System.Drawing.PointF ([single](25.5 * $k)), ([single](23 * $k))),
        (New-Object System.Drawing.PointF ([single](29.5 * $k)), ([single](23 * $k))),
        (New-Object System.Drawing.PointF ([single](24 * $k)), ([single](31 * $k))),
        (New-Object System.Drawing.PointF ([single](18.5 * $k)), ([single](23 * $k))),
        (New-Object System.Drawing.PointF ([single](22.5 * $k)), ([single](23 * $k)))
    )
    $g.FillPolygon($brush, $pts)
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
