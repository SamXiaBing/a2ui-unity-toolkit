$conns = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -eq $args[0] }
if ($conns) {
    $conns | ForEach-Object { Write-Host ("listening: " + $_.LocalPort) }
} else {
    Write-Host "no listeners"
}
