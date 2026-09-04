Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($args[0])
Write-Host "=== saturated color search across entire image ==="
$satPixels = @{}
for ($y = 150; $y -lt $img.Height - 30; $y += 5) {
    for ($x = 800; $x -lt $img.Width; $x += 5) {
        $p = $img.GetPixel($x, $y)
        $mx = [Math]::Max($p.R, [Math]::Max($p.G, $p.B))
        $mn = [Math]::Min($p.R, [Math]::Min($p.G, $p.B))
        if ($mx -gt 100 -and ($mx - $mn) -gt 40) {
            $key = "({0},{1},{2})" -f $p.R, $p.G, $p.B
            if (-not $satPixels.ContainsKey($key)) { $satPixels[$key] = 0 }
            $satPixels[$key]++
        }
    }
}
$img.Dispose()
Write-Host "top saturated colors:"
$satPixels.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 15 | ForEach-Object {
    Write-Host ("{0}: {1} px" -f $_.Key, $_.Value)
}
if ($satPixels.Count -eq 0) { Write-Host "(no saturated colors found - all gray!)" }
