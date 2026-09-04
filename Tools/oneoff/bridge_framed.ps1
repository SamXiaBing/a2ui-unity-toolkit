param([int]$Port = 3587)
$tcp = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $Port)
$stream = $tcp.GetStream()
$stream.ReadTimeout = 5000

# 读 welcome
$buf = New-Object byte[] 4096
$n = $stream.Read($buf, 0, 4096)
Write-Host ("welcome: " + [System.Text.Encoding]::UTF8.GetString($buf, 0, $n))

# FRAMING=1 表示长度前缀帧协议
# 发送 JSON-RPC 长度前缀帧
$json = '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
$jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($json)

# 帧: 4字节大端长度 + payload
$lenBytes = [BitConverter]::GetBytes([int]$jsonBytes.Length)
if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($lenBytes) }
$stream.Write($lenBytes, 0, 4)
$stream.Write($jsonBytes, 0, $jsonBytes.Length)
$stream.Flush()
Write-Host ("sent framed request, waiting response...")

Start-Sleep -Milliseconds 2000

try {
    # 读响应帧头
    $hdrBuf = New-Object byte[] 4
    $hdrRead = 0
    while ($hdrRead -lt 4) {
        $r = $stream.Read($hdrBuf, $hdrRead, 4 - $hdrRead)
        if ($r -le 0) { break }
        $hdrRead += $r
    }
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($hdrBuf) }
    $respLen = [BitConverter]::ToInt32($hdrBuf, 0)
    Write-Host ("response length: " + $respLen)

    $respBuf = New-Object byte[] $respLen
    $respRead = 0
    while ($respRead -lt $respLen) {
        $r = $stream.Read($respBuf, $respRead, $respLen - $respRead)
        if ($r -le 0) { break }
        $respRead += $r
    }
    $respText = [System.Text.Encoding]::UTF8.GetString($respBuf, 0, $respRead)
    Write-Host ("response: " + $respText.Substring(0, [Math]::Min(3000, $respText.Length)))
} catch {
    Write-Host ("read error: " + $_.Exception.Message)
}
$tcp.Close()
