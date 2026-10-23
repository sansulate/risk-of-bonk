param([string]$Vks = "0x57", [int]$Ms = 1500, [int]$TapVk = 0, [int]$TapEveryMs = 0, [int]$AddVk = 0, [int]$AddAfterMs = 0)
# Hold virtual keys (comma-separated hex, e.g. "0x57,0xA2" = W + left Ctrl) for $Ms, optionally tapping $TapVk
# (e.g. 0x20 = Space) every $TapEveryMs while holding, and optionally pressing $AddVk too after $AddAfterMs (held to the end).
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class KH2 { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint c, uint t); }
"@
function Key([byte]$vk, [bool]$up) { [KH2]::keybd_event($vk, [byte][KH2]::MapVirtualKey($vk, 0), $(if ($up) { 0xA } else { 0x8 }), [UIntPtr]::Zero) }
$keys = $Vks.Split(",") | Where-Object { $_ } | ForEach-Object { [byte][Convert]::ToInt32($_.Trim(), 16) }
foreach ($k in $keys) { Key $k $false }
$sw = [Diagnostics.Stopwatch]::StartNew(); $nextTap = 0
$added = $false
while ($sw.ElapsedMilliseconds -lt $Ms) {
  if ($AddVk -and -not $added -and $sw.ElapsedMilliseconds -ge $AddAfterMs) { Key ([byte]$AddVk) $false; $added = $true }
  if ($TapVk -and $TapEveryMs -and $sw.ElapsedMilliseconds -ge $nextTap) {
    Key ([byte]$TapVk) $false; Start-Sleep -Milliseconds 60; Key ([byte]$TapVk) $true
    $nextTap = $sw.ElapsedMilliseconds + $TapEveryMs
  }
  Start-Sleep -Milliseconds 20
}
foreach ($k in $keys) { Key $k $true }
if ($added) { Key ([byte]$AddVk) $true }
