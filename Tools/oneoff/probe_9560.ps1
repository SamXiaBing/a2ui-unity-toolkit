# 探测 9560 端口：可能是 Codely 桥接
param([int]$Port = 9560)
$tcp = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $Port)
$stream = $tcp.GetStream()
$stream.ReadTimeout = 3000

# 先读看有没有 welcome
$buf = New-Object byte[] 4096
try {
    $n = $stream.Read($buf, 0, 4096)
    if ($n -gt 0) {
        Write-Host ("welcome (" + $n + "B): " + [System.Text.Encoding]::UTF8.GetString($buf, 0, $n).Substring(0, [Math]::Min(200, $n)))
    }
} catch { Write-Host "no welcome" }

# 发 HTTP 请求测试
$httpReq = "GET /health HTTP/1.1`r`nHost: 127.0.0.1`r`n`r`n"
$bytes = [System.Text.Encoding]::UTF8.GetBytes($httpReq)
$stream.Write($bytes, 0, $bytes.Length)
$stream.Flush()
Start-Sleep -Milliseconds 1000

try {
    $n = $stream.Read($buf, 0, 4096)
    if ($n -gt 0) {
        Write-Host ("HTTP response (" + $n + "B): " + [System.Text.Encoding]::UTF8.GetString($buf, 0, $n).Substring(0, [Math]::Min(300, $n)))
    }
} catch { Write-Host "no http response" }

# 发 JSON-RPC 测试
$jsonReq = '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
$jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($jsonReq + "`n")
$stream.Write($jsonBytes, 0, $jsonBytes.Length)
$stream.Flush()
Start-Sleep -Milliseconds 1000

try {
    $n = $stream.Read($buf, 0, 4096)
    if ($n -gt 0) {
        Write-Host ("JSON response (" + $n + "B): " + [System.Text.Encoding]::UTF8.GetString($buf, 0, $n).Substring(0, [Math]::Min(500, $n)))
    }
} catch { Write-Host "no json response" }

$tcp.Close()
