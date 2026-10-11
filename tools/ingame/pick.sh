#!/usr/bin/env bash
# Select Megabonk survivor number N (0-based, characters.json order) on the character select grid, wait, screenshot.
# Usage: pick.sh N out.png [waitSeconds]
I=$(cygpath -w "$(dirname "$0")")
k=$((18 + $1)); col=$((k % 11)); row=$((k / 11))
ys=(92 152 207 255)
x=$(python -c "print(int(133 + 54.6 * $col))")
powershell -ExecutionPolicy Bypass -File "$I\click.ps1" -X $x -Y ${ys[$row]}
sleep ${3:-8}
powershell -ExecutionPolicy Bypass -File "$I\shot.ps1" -Out "$(cygpath -w "$2")"
