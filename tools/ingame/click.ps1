param([int]$X, [int]$Y, [string]$Proc = "Risk of Rain 2")
# X/Y are in 1920x1080 reference coordinates; they are scaled to the game window's real client area.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class M {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref PT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
}
"@
$p = Get-Process -Name $Proc -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($p) {
  # A key event first lets this background process bring the game to the front.
  [M]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [M]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
  [M]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  $r = New-Object M+RECT; [M]::GetClientRect($p.MainWindowHandle, [ref]$r) | Out-Null
  $o = New-Object M+PT; [M]::ClientToScreen($p.MainWindowHandle, [ref]$o) | Out-Null
  $X = [int]($o.X + $X * $r.R / 1920.0); $Y = [int]($o.Y + $Y * $r.B / 1080.0)
}
[M]::SetCursorPos($X, $Y) | Out-Null
# Real (absolute) mouse moves too: the game reads raw input and ignores SetCursorPos alone.
$ax = [uint32]($X * 65535 / ([M]::GetSystemMetrics(0) - 1)); $ay = [uint32]($Y * 65535 / ([M]::GetSystemMetrics(1) - 1))
[M]::mouse_event(0x8001, $ax + 300, $ay + 300, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30
[M]::mouse_event(0x8001, $ax, $ay, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 250
[M]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[M]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
