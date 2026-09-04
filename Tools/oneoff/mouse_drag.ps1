Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class MouseDrag {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public const uint LEFTDOWN = 0x0002;
  public const uint LEFTUP = 0x0004;
}
"@

# 参数: startX startY endX endY (屏幕坐标)
$sx = [int]$args[0]; $sy = [int]$args[1]
$ex = [int]$args[2]; $ey = [int]$args[3]

[MouseDrag]::SetCursorPos($sx, $sy) | Out-Null
Start-Sleep -Milliseconds 200

[MouseDrag]::mouse_event([MouseDrag]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 100

# 分步移动（20 步，每步 25ms）模拟真实拖拽
$steps = 20
for ($i = 1; $i -le $steps; $i++) {
    $cx = $sx + [int](($ex - $sx) * $i / $steps)
    $cy = $sy + [int](($ey - $sy) * $i / $steps)
    [MouseDrag]::SetCursorPos($cx, $cy) | Out-Null
    Start-Sleep -Milliseconds 25
}

Start-Sleep -Milliseconds 100
[MouseDrag]::mouse_event([MouseDrag]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Write-Host ("drag: ($sx,$sy) -> ($ex,$ey) done")
