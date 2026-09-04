# 通过 A2UI Host 触发布局树转储：利用现有 TestApi，
# 但菜单在 GUI 编辑器里点；这里改成走 HTTP 推一个特殊样本前无法 dump——
# 所以本脚本改为提示用户点菜单，并轮询 layout_tree.txt 生成时间。
param([int]$TimeoutSec = 60)
$path = "D:\AIWorkSpace\a2ui-unity-toolkit\TestResults\layout_tree.txt"
Write-Host "请在编辑器点菜单: A2UI Scheme A -> 转储布局树 (Layout Tree Dump)"
$start = Get-Date
while (((Get-Date) - $start).TotalSeconds -lt $TimeoutSec) {
    if ((Test-Path $path) -and ((Get-Item $path).LastWriteTime -gt $start)) {
        Write-Host "dump refreshed:"
        Get-Content $path | Select-Object -First 60
        exit 0
    }
    Start-Sleep -Seconds 2
}
Write-Host "timeout waiting for dump"
exit 1
