param([string]$Key, [int]$HoldMs = 80)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class K { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint c, uint t); }
"@
# One character (letter or digit), or a name: space, escape, enter, tab, F1..F12.
$named = @{ "space" = 0x20; "escape" = 0x1B; "enter" = 0x0D; "tab" = 0x09 }
if ($named.ContainsKey($Key.ToLower())) { $vk = [byte]$named[$Key.ToLower()] }
elseif ($Key -match '^[Ff](\d{1,2})$') { $vk = [byte](0x6F + [int]$Matches[1]) }
else { $vk = [byte][char]$Key.ToUpper() }
$sc = [byte][K]::MapVirtualKey($vk, 0)
[K]::keybd_event($vk, $sc, 0x8, [UIntPtr]::Zero); Start-Sleep -Milliseconds $HoldMs; [K]::keybd_event($vk, $sc, 0xA, [UIntPtr]::Zero)
