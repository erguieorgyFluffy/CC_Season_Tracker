# checkpng.ps1 - build guard: rooms.png must be a real 8-bit PNG
 $dir = Join-Path $PSScriptRoot "assets"
 $p = Join-Path $dir "rooms.png"
if (-not (Test-Path -LiteralPath $p)) {
    Write-Host "rooms.png missing - the map will use the text fallback" -ForegroundColor Yellow
    exit 0
}
 $b = Get-Content -LiteralPath $p -Encoding Byte -TotalCount 26
if ($b.Count -lt 26) {
    Write-Host "rooms.png is too small to be a PNG - open in Paint, Save As PNG, rebuild" -ForegroundColor Red
    exit 1
}
if (($b[1] -ne 80) -or ($b[2] -ne 78)) {
    Write-Host "rooms.png is NOT a real PNG (bad magic bytes) - open in Paint, Save As PNG, rebuild" -ForegroundColor Red
    exit 1
}
if ($b[24] -ne 8) {
    Write-Host ("rooms.png bit depth is " + $b[24] + " - the game needs 8. Open in Paint, Save As PNG, rebuild") -ForegroundColor Red
    exit 1
}
Write-Host "rooms.png OK (8-bit PNG)" -ForegroundColor DarkGray
exit 0
