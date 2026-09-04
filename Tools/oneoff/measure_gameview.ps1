Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($args[0])

# 找 Game View 的边界：从截图整体看，左侧有测试发送面板占约 0..700，
# Game View 在右侧。扫描 y=200..900，x=700..1721 区域。
# 目标：找出卡片的左右边缘（卡片是亮色调，Game View 背景是黑/深灰）。
Write-Host "=== scanning Game View area (x>700) ==="
$prevL = -1; $prevR = -1
for ($y = 150; $y -lt $img.Height - 40; $y += 15) {
    $left = -1; $right = -1
    for ($x = 700; $x -lt $img.Width; $x++) {
        $p = $img.GetPixel($x, $y)
        if ($p.R -gt 45 -or $p.G -gt 45 -or $p.B -gt 45) {
            if ($left -lt 0) { $left = $x }
            $right = $x
        }
    }
    if ($right -gt $left -and ($left -ne $prevL -or $right -ne $prevR)) {
        Write-Host ("y={0}: x=[{1}..{2}] w={3}" -f $y, $left, $right, ($right - $left))
        $prevL = $left; $prevR = $right
    }
}
$img.Dispose()
