param([string]$Keys = "W", [int]$HoldMs = 1500, [string]$Out, [switch]$Click)
# Hold keys (and optionally the left mouse button), screenshot the game mid-action, then release.
$s = Split-Path $MyInvocation.MyCommand.Path
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class KH { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint c, uint t);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e); }
"@
$vks = @()
foreach ($k in $Keys.ToCharArray()) { $vks += [byte][char]([string]$k).ToUpper() }
foreach ($vk in $vks) { [KH]::keybd_event($vk, [byte][KH]::MapVirtualKey($vk, 0), 0x8, [UIntPtr]::Zero) }
if ($Click) { [KH]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero) }
Start-Sleep -Milliseconds $HoldMs
& "$s\shot.ps1" -Out $Out
if ($Click) { [KH]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero) }
foreach ($vk in $vks) { [KH]::keybd_event($vk, [byte][KH]::MapVirtualKey($vk, 0), 0xA, [UIntPtr]::Zero) }
