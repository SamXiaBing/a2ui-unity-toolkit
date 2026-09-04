Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($args[0])
Write-Host "=== scan y=100..200 for Game View bounds ==="
for ($y = 100; $y -lt 200; $y += 10) {
    $row = "y=$y : "
    for ($x = 800; $x -lt $img.Width; $x += 100) {
        $p = $img.GetPixel($x, $y)
        $row += ("({0},{1},{2}) " -f $p.R, $p.G, $p.B)
    }
    Write-Host $row
}
$img.Dispose()
