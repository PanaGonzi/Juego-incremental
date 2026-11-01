# Engine arguments for test runs: no sound, and the window on a secondary monitor so it does not
# disturb the owner while working. Dot-source it and call Test-WindowArgs before the '--' separator.
# -Slot places several windows side by side inside the chosen monitor.
function Test-WindowArgs([int]$Width = 960, [int]$Height = 540, [int]$Slot = 0) {
    Add-Type -AssemblyName System.Windows.Forms
    $result = @('--audio-driver', 'Dummy')
    $screen = [System.Windows.Forms.Screen]::AllScreens |
        Where-Object { -not $_.Primary -and $_.WorkingArea.Width -ge $Width -and $_.WorkingArea.Height -ge $Height } |
        Sort-Object { $_.WorkingArea.Width * $_.WorkingArea.Height } -Descending |
        Select-Object -First 1
    if (-not $screen) { Write-Warning 'No secondary monitor fits the window: using the default position.'; return $result }
    $area = $screen.WorkingArea
    $perRow = [Math]::Max(1, [Math]::Floor($area.Width / $Width))
    $x = $area.X + ($Slot % $perRow) * $Width
    $y = $area.Y + 32 + [Math]::Min([Math]::Floor($Slot / $perRow) * $Height, [Math]::Max(0, $area.Height - $Height - 32))
    return $result + @('--position', "$x,$y")
}
