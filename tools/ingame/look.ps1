param([int]$Dx = 0, [int]$Dy = 0, [int]$Steps = 20)
# Turn the in-game camera with relative mouse moves (raw input), spread over $Steps small moves.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class LK { [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e); }
"@
for ($i = 0; $i -lt $Steps; $i++) {
  [LK]::mouse_event(0x0001, [int]($Dx / $Steps), [int]($Dy / $Steps), 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 15
}
