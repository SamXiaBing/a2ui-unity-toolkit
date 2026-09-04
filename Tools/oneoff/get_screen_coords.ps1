Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class Win32Pos {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$proc = Get-Process -Id $args[0] -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "no process"; exit 1 }
$rect = New-Object Win32Pos+RECT
[Win32Pos]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
Write-Host ("window rect: L={0} T={1} R={2} B={3}" -f $rect.Left, $rect.Top, $rect.Right, $rect.Bottom)

# 从窗口截图里找卡片位置（用之前的 measure 逻辑）
$img = [System.Drawing.Bitmap]::FromFile($args[1])
# Game View 的卡片在窗口截图坐标 (1107,150) 附近
# 换算到屏幕坐标：加上窗口的 Left/Top
$cardWinX = 1300  # 窗口截图内卡片中心区域
$cardWinY = 200
$screenX = $rect.Left + $cardWinX
$screenY = $rect.Top + $cardWinY
Write-Host ("card center screen coords: ({0},{1})" -f $screenX, $screenY)
Write-Host ("drag target: ({0},{1})" -f ($screenX - 100), ($screenY + 100))
$img.Dispose()
