Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($args[0])
Write-Host "=== mouse drag start area (1250..1500, 190..250) ==="
for ($y = 190; $y -lt 250; $y += 10) {
    $row = "y=$y : "
    for ($x = 1250; $x -lt 1500; $x += 50) {
        $p = $img.GetPixel($x, $y)
        $row += ("({0},{1},{2}) " -f $p.R, $p.G, $p.B)
    }
    Write-Host $row
}
$img.Dispose()
