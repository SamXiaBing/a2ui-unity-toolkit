param([int]$Port = 3587)
$tcp = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $Port)
$stream = $tcp.GetStream()
$stream.ReadTimeout = 5000

# 读 welcome
$buf = New-Object byte[] 4096
$n = $stream.Read($buf, 0, 4096)
Write-Host ("welcome: " + [System.Text.Encoding]::UTF8.GetString($buf, 0, $n))

# FRAMING=1 可能意味着 newline-delimited JSON（1 = 一行一帧）
$json = '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
$jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($json + "`n")
$stream.Write($jsonBytes, 0, $jsonBytes.Length)
$stream.Flush()
Write-Host ("sent newline-delimited JSON, waiting...")

Start-Sleep -Milliseconds 3000

$allData = New-Object System.Collections.Generic.List[byte]
$buf = New-Object byte[] 65536
try {
    while ($stream.DataAvailable) {
        $n = $stream.Read($buf, 0, 65536)
        for ($i = 0; $i -lt $n; $i++) { $allData.Add($buf[$i]) }
        Start-Sleep -Milliseconds 100
    }
} catch {}

if ($allData.Count -gt 0) {
    $arr = $allData.ToArray()
    $resp = [System.Text.Encoding]::UTF8.GetString($arr, 0, $arr.Count)
    Write-Host ("response (" + $arr.Count + " bytes): " + $resp.Substring(0, [Math]::Min(4000, $resp.Length)))
} else {
    Write-Host "no response data"
}
$tcp.Close()
