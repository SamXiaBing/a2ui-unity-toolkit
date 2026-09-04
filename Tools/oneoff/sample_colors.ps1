Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($args[0])
Write-Host "=== card area color sampling (x=1000..1650, y=200..400) ==="
for ($y = 200; $y -lt 400; $y += 25) {
    $row = ""
    for ($x = 1000; $x -lt 1650; $x += 80) {
        $p = $img.GetPixel($x, $y)
        $row += ("({0},{1})=({2},{3},{4})  " -f $x, $y, $p.R, $p.G, $p.B)
    }
    Write-Host $row
}
$img.Dispose()
