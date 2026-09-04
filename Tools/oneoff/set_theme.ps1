param([string]$Theme = "figma-figmaexport")
$body = '{"theme":"' + $Theme + '"}'
Write-Host ("body: " + $body)
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)
$req = [System.Net.HttpWebRequest]::Create('http://127.0.0.1:18766/theme')
$req.Method = 'POST'
$req.ContentType = 'application/json'
$req.Timeout = 5000
$req.ContentLength = $bodyBytes.Length
$s = $req.GetRequestStream()
$s.Write($bodyBytes, 0, $bodyBytes.Length)
$s.Close()
$resp = $req.GetResponse()
$reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
Write-Host $reader.ReadToEnd()
$resp.Close()
