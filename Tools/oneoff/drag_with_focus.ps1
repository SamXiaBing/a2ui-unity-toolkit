Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class DragFull {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}
"@
$pid_ = [int]$args[0]
$sx = [int]$args[1]; $sy = [int]$args[2]
$ex = [int]$args[3]; $ey = [int]$args[4]

$proc = Get-Process -Id $pid_
$hWnd = $proc.MainWindowHandle

# 先把编辑器窗口切到前台
[DragFull]::SetForegroundWindow($hWnd) | Out-Null
Start-Sleep -Milliseconds 500

# 移到起点
[DragFull]::SetCursorPos($sx, $sy) | Out-Null
Start-Sleep -Milliseconds 200

# 按下
[DragFull]::mouse_event([DragFull]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 150

# 分步移动
$steps = 25
for ($i = 1; $i -le $steps; $i++) {
    $cx = $sx + [int](($ex - $sx) * $i / $steps)
    $cy = $sy + [int](($ey - $sy) * $i / $steps)
    [DragFull]::SetCursorPos($cx, $cy) | Out-Null
    Start-Sleep -Milliseconds 30
}

Start-Sleep -Milliseconds 150

# 释放
[DragFull]::mouse_event([DragFull]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 100
Write-Host ("drag: ($sx,$sy) -> ($ex,$ey) with foreground focus")
