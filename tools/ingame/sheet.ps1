param([string]$Out)
$s = Split-Path $MyInvocation.MyCommand.Path
Add-Type -AssemblyName System.Drawing
# Megabonk survivors in the fitted 11-column grid (after the vanilla roster).
$pos = @()
$cols = 133..143 | ForEach-Object { 0 }
for ($i = 0; $i -lt 11; $i++) { $cols[$i] = [int](133 + 54.6 * $i) }
$rows = @(92, 152, 207, 255)
for ($k = 18; $k -lt 38; $k++) { $pos += ,@($cols[$k % 11], $rows[[math]::Floor($k / 11)]) }
$tile = 320; $th = 380
$sheet = New-Object System.Drawing.Bitmap (5 * $tile), (4 * $th)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$i = 0
foreach ($p in $pos) {
  & "$s\click.ps1" -X $p[0] -Y $p[1]
  Start-Sleep -Milliseconds 2600
  & "$s\shot.ps1" -Out "$env:TEMP\rob_tmp.png" | Out-Null
  $img = [System.Drawing.Image]::FromFile("$env:TEMP\rob_tmp.png")
  $f = $img.Width / 1920.0
  $src = New-Object System.Drawing.Rectangle ([int](820*$f)), ([int](280*$f)), ([int](760*$f)), ([int](820*$f))
  $dst = New-Object System.Drawing.Rectangle (($i % 5) * $tile), ([math]::Floor($i / 5) * $th), $tile, $th
  $g.DrawImage($img, $dst, $src, [System.Drawing.GraphicsUnit]::Pixel)
  $title = New-Object System.Drawing.Rectangle ([int](300*$f)), ([int](300*$f)), ([int](230*$f)), ([int](40*$f))
  $tdst = New-Object System.Drawing.Rectangle (($i % 5) * $tile), ([math]::Floor($i / 5) * $th), 184, 32
  $g.DrawImage($img, $tdst, $title, [System.Drawing.GraphicsUnit]::Pixel)
  $img.Dispose()
  $i++
}
$sheet.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"saved $Out"
