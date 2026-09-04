Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Write-Host ("screen: " + $bounds.Width + "x" + $bounds.Height)

$bmp = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, 0, 0, 0, $bounds.Size)
$out = "D:\AIWorkSpace\a2ui-unity-toolkit\TestResults\screen_capture.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Host ("saved: " + $out)

# 同时搜索橙色像素
$img = [System.Drawing.Bitmap]::FromFile($out)
$orangeCount = 0
for ($y = 0; $y -lt $img.Height; $y += 3) {
    for ($x = 0; $x -lt $img.Width; $x += 3) {
        $p = $img.GetPixel($x, $y)
        if ($p.R -gt 240 -and $p.G -gt 80 -and $p.G -lt 120 -and $p.B -lt 40) {
            $orangeCount++
        }
    }
}
$img.Dispose()
Write-Host ("orange #FF5C00 pixels: " + $orangeCount)
