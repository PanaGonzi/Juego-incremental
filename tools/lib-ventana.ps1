# Engine arguments for test runs: no sound, and the window on a secondary monitor so it does not
# disturb the owner while working. Dot-source it and call Test-WindowArgs before the '--' separator.
# -Slot places several windows side by side inside the chosen monitor.

# Godot reads this file next to the executable once, at start-up: the window then opens without taking the foreground.
# It only lasts while the processes start; Clear-NoFocus removes it, so a delivered build ends up exactly as it was.
function Set-NoFocus([string]$Exe) {
    [IO.File]::WriteAllText((Join-Path (Split-Path $Exe -Parent) 'override.cfg'), "[display]`nwindow/size/no_focus=true`n")
}
function Clear-NoFocus([string]$Exe, [int]$AfterSeconds = 3) {
    Start-Sleep -Seconds $AfterSeconds
    Remove-Item (Join-Path (Split-Path $Exe -Parent) 'override.cfg') -ErrorAction SilentlyContinue
}

function Test-WindowArgs([int]$Width = 960, [int]$Height = 540, [int]$Slot = 0) {
    Add-Type -AssemblyName System.Windows.Forms
    $result = @('--audio-driver', 'Dummy')
    $screen = [System.Windows.Forms.Screen]::AllScreens |
        Where-Object { -not $_.Primary -and $_.WorkingArea.Width -ge $Width -and $_.WorkingArea.Height -ge $Height } |
        Sort-Object { $_.WorkingArea.Width -ge $_.WorkingArea.Height }, { $_.WorkingArea.Width * $_.WorkingArea.Height } -Descending |
        Select-Object -First 1
    if (-not $screen) { Write-Warning 'No secondary monitor fits the window: using the default position.'; return $result }
    $area = $screen.WorkingArea
    $perRow = [Math]::Max(1, [Math]::Floor($area.Width / $Width))
    $x = $area.X + ($Slot % $perRow) * $Width
    $y = $area.Y + 32 + [Math]::Min([Math]::Floor($Slot / $perRow) * $Height, [Math]::Max(0, $area.Height - $Height - 32))
    # Godot cuenta --position desde la esquina del escritorio virtual, no desde el origen de Windows.
    $origin = [System.Windows.Forms.SystemInformation]::VirtualScreen.Location
    return $result + @('--position', "$($x - $origin.X),$($y - $origin.Y)")
}
