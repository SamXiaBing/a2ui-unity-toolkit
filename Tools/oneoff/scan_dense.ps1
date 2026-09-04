Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($args[0])
Write-Host "=== scan x=700..1720 y=140..250 dense ==="
for ($y = 140; $y -lt 250; $y += 10) {
    $row = "y=$y : "
    for ($x = 1050; $x -lt $img.Width; $x += 60) {
        $p = $img.GetPixel($x, $y)
        $row += ("({0},{1},{2}) " -f $p.R, $p.G, $p.B)
    }
    Write-Host $row
}
$img.Dispose()
