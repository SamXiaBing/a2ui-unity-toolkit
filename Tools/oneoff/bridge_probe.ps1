param([int]$Port, [string]$Msg)

$tcp = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $Port)
$stream = $tcp.GetStream()
$stream.ReadTimeout = 3000

# 发送消息
$bytes = [System.Text.Encoding]::UTF8.GetBytes($Msg + [char]10)
$stream.Write($bytes, 0, $bytes.Length)
$stream.Flush()
Start-Sleep -Milliseconds 2000

# 读响应
$buf = New-Object byte[] 65536
try {
    $n = $stream.Read($buf, 0, 65536)
    $resp = [System.Text.Encoding]::UTF8.GetString($buf, 0, $n)
    Write-Host ("response (" + $n + " bytes): " + $resp.Substring(0, [Math]::Min(2000, $resp.Length)))
} catch {
    Write-Host ("no response: " + $_.Exception.Message)
}
$tcp.Close()
