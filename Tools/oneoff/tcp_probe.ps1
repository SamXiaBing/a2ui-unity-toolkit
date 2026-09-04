param([int]$Port, [string]$Message)

$tcp = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $Port)
$stream = $tcp.GetStream()
$bytes = [System.Text.Encoding]::UTF8.GetBytes($Message + [char]10)
$stream.Write($bytes, 0, $bytes.Length)
$stream.Flush()
Start-Sleep -Milliseconds 800
$buf = New-Object byte[] 65536
try {
    $n = $stream.Read($buf, 0, 65536)
    Write-Host ([System.Text.Encoding]::UTF8.GetString($buf, 0, $n))
} catch {
    Write-Host "no response: $_"
}
$tcp.Close()
