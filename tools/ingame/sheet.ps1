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
  & "$s\shot.ps1" -Out "$s\tmp.png" | Out-Null
  $img = [System.Drawing.Image]::FromFile("$s\tmp.png")
  $src = New-Object System.Drawing.Rectangle 820, 280, 760, 820
  $dst = New-Object System.Drawing.Rectangle (($i % 5) * $tile), ([math]::Floor($i / 5) * $th), $tile, $th
  $g.DrawImage($img, $dst, $src, [System.Drawing.GraphicsUnit]::Pixel)
  $title = New-Object System.Drawing.Rectangle 300, 300, 230, 40
  $tdst = New-Object System.Drawing.Rectangle (($i % 5) * $tile), ([math]::Floor($i / 5) * $th), 184, 32
  $g.DrawImage($img, $tdst, $title, [System.Drawing.GraphicsUnit]::Pixel)
  $img.Dispose()
  $i++
}
$sheet.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"saved $Out"
