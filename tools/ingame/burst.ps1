param([string]$Out, [string]$Proc = "Risk of Rain 2", [int]$Count = 6, [int]$EveryMs = 120, [string]$HoldVks = "", [int]$TapVk = 0)
Add-Type -AssemblyName System.Drawing
# The helper is compiled once into %TEMP% (compiling it on every call takes ~50 s while the game is busy).
$dll = Join-Path $env:TEMP "rob_shot_w.dll"
if (Test-Path $dll) { Add-Type -Path $dll } else {
Add-Type -OutputAssembly $dll -OutputType Library @"
using System; using System.Runtime.InteropServices;
public static class W {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
}
"@
Add-Type -Path $dll }
$p = Get-Process -Name $Proc -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$h = $p.MainWindowHandle
[W]::ShowWindow($h, 9) | Out-Null
[W]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 400
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class KB { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr e);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint c, uint t); }
"@
function Key([byte]$vk, [bool]$up) { [KB]::keybd_event($vk, [byte][KB]::MapVirtualKey($vk, 0), $(if ($up) { 0xA } else { 0x8 }), [UIntPtr]::Zero) }
$keys = $HoldVks.Split(",") | Where-Object { $_ } | ForEach-Object { [byte][Convert]::ToInt32($_.Trim(), 16) }
foreach ($k in $keys) { Key $k $false }
Start-Sleep -Milliseconds 400
if ($TapVk) { Key ([byte]$TapVk) $false; Start-Sleep -Milliseconds 60; Key ([byte]$TapVk) $true }
$r = New-Object W+RECT
[W]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hh = $r.B - $r.T
for ($i = 0; $i -lt $Count; $i++) {
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc(); [W]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc)
  $bmp.Save(($Out -replace "\.png$", "_$i.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Start-Sleep -Milliseconds $EveryMs
}
foreach ($k in $keys) { Key $k $true }
"$Count frames -> $Out"
