Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class Win32Cap2 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$proc = Get-Process -Id $args[0] -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "process not found"; exit 1 }
$hWnd = $proc.MainWindowHandle
if ($hWnd -eq [IntPtr]::Zero) { Write-Host "no main window"; exit 1 }

$rect = New-Object Win32Cap2+RECT
[Win32Cap2]::GetWindowRect($hWnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
Write-Host ("window {0}x{1}" -f $w, $h)

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# PW_RENDERFULLCONTENT = 2: 抓取 DirectComposition/UWP 内容（Game View 属于此类）
$ok = [Win32Cap2]::PrintWindow($hWnd, $hdc, 2)
$g.ReleaseHdc($hdc)
Write-Host ("printwindow ok: " + $ok)

$out = $args[1]
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Host ("saved: " + $out)
