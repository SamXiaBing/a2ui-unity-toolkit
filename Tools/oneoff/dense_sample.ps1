Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($args[0])
Write-Host "=== dense sampling y=260..300 x=1010..1650 ==="
for ($y = 260; $y -lt 300; $y += 5) {
    $row = "y=" + $y + ": "
    for ($x = 1010; $x -lt 1650; $x += 80) {
        $p = $img.GetPixel($x, $y)
        $row += ("({0},{1},{2}) " -f $p.R, $p.G, $p.B)
    }
    Write-Host $row
}
$img.Dispose()
