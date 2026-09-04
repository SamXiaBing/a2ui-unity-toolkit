Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($args[0])
Write-Host "=== dense scan y=140..320 x=970..1610 (card area) ==="
for ($y = 140; $y -lt 320; $y += 10) {
    $row = "y=$y : "
    for ($x = 970; $x -lt 1610; $x += 60) {
        $p = $img.GetPixel($x, $y)
        $row += ("({0},{1},{2}) " -f $p.R, $p.G, $p.B)
    }
    Write-Host $row
}
$img.Dispose()
