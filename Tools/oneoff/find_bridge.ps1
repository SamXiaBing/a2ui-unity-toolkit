# 探测 Codely Unity 桥接 TCP 端口：编辑器侧监听端口通常在 1200-1300 或随机高端口
# 已知 metis 用 1213；遍历常见范围 + 从进程连接反查
$targetPid = $args[0]

$conns = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -eq $targetPid }
Write-Host "=== all listeners of pid $targetPid ==="
$conns | ForEach-Object { Write-Host ("  port " + $_.LocalPort) }

# 也试 dotnet 系統里的 codely bridge 端口发现文件
$cfg = "$env:USERPROFILE\.codely-cli\bridges"
if (Test-Path $cfg) {
    Write-Host "=== bridge registry files ==="
    Get-ChildItem $cfg | ForEach-Object { Write-Host ("  " + $_.Name); Get-Content $_.FullName | Select-Object -First 3 }
}
