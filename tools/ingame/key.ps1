param([string]$Key, [int]$HoldMs = 80)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class K { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint c, uint t); }
"@
$vk = [byte][char]$Key.ToUpper()
if ($Key -eq "space") { $vk = 0x20 }
$sc = [byte][K]::MapVirtualKey($vk, 0)
[K]::keybd_event($vk, $sc, 0x8, [UIntPtr]::Zero); Start-Sleep -Milliseconds $HoldMs; [K]::keybd_event($vk, $sc, 0xA, [UIntPtr]::Zero)
