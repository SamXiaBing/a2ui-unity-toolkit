Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($args[0])
Write-Host ("searching for #FF5C00 in " + $img.Width + "x" + $img.Height)

$found = 0
$samples = @()
for ($y = 150; $y -lt $img.Height - 20; $y += 3) {
    for ($x = 800; $x -lt $img.Width; $x += 3) {
        $p = $img.GetPixel($x, $y)
        # #FF5C00 = R>240, G 80-120, B<40
        if ($p.R -gt 240 -and $p.G -gt 80 -and $p.G -lt 120 -and $p.B -lt 40) {
            $found++
            if ($found -le 3) {
                $samples += ("ORANGE at ({0},{1}) rgb=({2},{3},{4})" -f $x, $y, $p.R, $p.G, $p.B)
            }
        }
    }
}
foreach ($s in $samples) { Write-Host $s }
Write-Host ("total orange pixels: " + $found)

# 也搜按钮 secondary 浅色 #F9FAFB
$lightFound = 0
for ($y = 150; $y -lt $img.Height - 20; $y += 5) {
    for ($x = 800; $x -lt $img.Width; $x += 5) {
        $p = $img.GetPixel($x, $y)
        if ($p.R -gt 245 -and $p.G -gt 248 -and $p.B -gt 248) {
            $lightFound++
        }
    }
}
Write-Host ("light surface pixels (#F9FAFB): " + $lightFound)
$img.Dispose()
