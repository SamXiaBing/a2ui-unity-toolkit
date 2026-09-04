# 检查 18766 A2UI 服务是否在监听（确认 Host 在 Play）
$c = Get-NetTCPConnection -State Listen -LocalPort 18766 -ErrorAction SilentlyContinue
if ($c) {
    Write-Host ("a2ui server listening, pid=" + $c.OwningProcess)
} else {
    Write-Host "18766 NOT listening (host not in play?)"
}

# 列出所有 Codely Unity IPC 管道（TCP 桥接命名管道）
$pipes = [System.IO.Directory]::GetFiles("\\.\pipe\") | Where-Object { $_ -match "CodelyUnityIpc" }
foreach ($p in $pipes) { Write-Host ("pipe: " + $p) }
