foreach ($p in @(3587, 12508, 9560)) {
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient('127.0.0.1', $p)
        $tcp.Close()
        Write-Host ("port " + $p + ": connectable")
    } catch {
        Write-Host ("port " + $p + ": failed")
    }
}
