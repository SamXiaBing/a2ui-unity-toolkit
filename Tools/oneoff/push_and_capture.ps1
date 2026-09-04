Set-Location $PSScriptRoot\..
$sample = $args[0]
$outPng = $args[1]
$pid_ = $args[2]

$text = [System.IO.File]::ReadAllText("$PWD\Assets\A2UISchemeA\Samples\$sample")
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($text)
$req = [System.Net.HttpWebRequest]::Create('http://127.0.0.1:18766/a2ui')
$req.Method = 'POST'
$req.ContentType = 'application/jsonl; charset=utf-8'
$req.Timeout = 5000
$req.ContentLength = $bodyBytes.Length
$stream = $req.GetRequestStream()
$stream.Write($bodyBytes, 0, $bodyBytes.Length)
$stream.Close()
$resp = $req.GetResponse()
$reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
Write-Host ("push: " + $reader.ReadToEnd())
$resp.Close()

Start-Sleep -Seconds 3
& "$PSScriptRoot\capture_window.ps1" $pid_ $outPng
