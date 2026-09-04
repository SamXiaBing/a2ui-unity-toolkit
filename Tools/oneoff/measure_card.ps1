Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($args[0])
Write-Host ("image: " + $img.Width + "x" + $img.Height)

# Game View 通常在窗口左下角。扫描每一行，找出"非纯黑背景"的左右边界，
# 定位卡片的实际左右边缘（卡片是深色半透底+白色内容）。
# 采样中部与底部两个横条。
function ScanRow($img, $y) {
    $left = -1; $right = -1
    for ($x = 0; $x -lt $img.Width; $x++) {
        $p = $img.GetPixel($x, $y)
        # 非黑背景判定：任一通道 > 40
        if ($p.R -gt 40 -or $p.G -gt 40 -or $p.B -gt 40) {
            if ($left -lt 0) { $left = $x }
            $right = $x
        }
    }
    return @{ left = $left; right = $right; width = ($right - $left) }
}

# 扫描下半部分（卡片区域），每 40px 一行
for ($y = $img.Height - 500; $y -lt $img.Height - 60; $y += 40) {
    if ($y -lt 0) { continue }
    $r = ScanRow $img $y
    if ($r.width -gt 0) {
        Write-Host ("y={0}: content x=[{1}..{2}] width={3}" -f $y, $r.left, $r.right, $r.width)
    }
}
$img.Dispose()
