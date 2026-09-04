Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($args[0])

# 精确扫描：找 Game View 里卡片的边界。
# 卡片是深色半透底（暗色主题）或浅色底，Game View 背景接近纯黑。
# 先找 Game View 的显示区域：整屏 Game 在窗口右下（减去左侧面板）。
# 策略：对每一行 y，找"连续亮/中亮色段"的最左/最右；输出所有 x=[l..r]。
Write-Host ("img: " + $img.Width + "x" + $img.Height)

# 用中段行密集扫描找卡片的稳定左右边缘
$results = @()
for ($y = 200; $y -lt $img.Height - 30; $y += 5) {
    $left = -1; $right = -1
    for ($x = 760; $x -lt $img.Width; $x++) {
        $p = $img.GetPixel($x, $y)
        if ($p.R -gt 50 -or $p.G -gt 50 -or $p.B -gt 50) {
            if ($left -lt 0) { $left = $x }
            $right = $x
        }
    }
    if ($right -gt $left) {
        $results += [PSCustomObject]@{ Y = $y; L = $left; R = $right; W = ($right - $left) }
    }
}

# 统计出现最多的左右值（众数 = 卡片稳定边缘）
$groups = $results | Group-Object L, R | Sort-Object Count -Descending | Select-Object -First 5
foreach ($g in $groups) {
    $parts = $g.Name -split ', '
    Write-Host ("mode: L=" + $parts[0] + " R=" + $parts[1] + " w=" + ($parts[1] - $parts[0]) + " count=" + $g.Count)
}
$img.Dispose()
