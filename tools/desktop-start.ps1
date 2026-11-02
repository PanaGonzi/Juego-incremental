# Abre el juego para probarlo con agent-desktop (árbol de accesibilidad): sin sonido, en un monitor secundario, sin capturar
# el ratón y con perfil test*. Las acciones semánticas no toman el foco ni mueven el cursor, así que el titular puede seguir usando el equipo.
# Solo para builds/alpha-dev: deja allí un override.cfg que impide a la ventana tomar el foco, y las alfas entregadas no se tocan.
# Uso: tools\desktop-start.ps1 [-Profile testdesktop] [-Width 960] [-Height 540] [-Slot 0]
# No sirve para lo que necesita teclas mantenidas o ratón relativo: eso sigue siendo tools\PlayDriver.
param([string]$Profile = 'testdesktop', [int]$Width = 960, [int]$Height = 540, [int]$Slot = 0)
$ErrorActionPreference = 'Stop'
if ($Profile -notlike 'test*') { throw 'Solo perfiles test*: nunca los guardados del titular.' }
$Repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'lib-ventana.ps1')
$Exe = Join-Path $Repo 'builds\alpha-dev\client\Pluton.exe'
Set-NoFocus $Exe
$window = Test-WindowArgs $Width $Height $Slot
$engine = $window + @('--resolution', "${Width}x${Height}", '--accessibility', 'always', '--', '--profile', $Profile, '--no-capture', '--desktop-test')
$process = Start-Process -FilePath $Exe -ArgumentList $engine -PassThru
# Un clic de accesibilidad activaría la ventana. Con WS_EX_NOACTIVATE responde igual y no quita el foco a nadie.
# Se reaplica mientras arranca, porque el juego rehace el estilo de la ventana al quitar el no_focus inicial.
Add-Type -Namespace PlutonDesktop -Name Win32 -MemberDefinition @'
[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern long Get(System.IntPtr h, int i);
[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern long Set(System.IntPtr h, int i, long v);
'@
$GwlExStyle = -20; $NoActivate = 0x08000000
for ($i = 0; $i -lt 14; $i++) {
    Start-Sleep -Seconds 1
    $process.Refresh()
    $hwnd = $process.MainWindowHandle
    if ($hwnd -eq [IntPtr]::Zero) { continue }
    $style = [PlutonDesktop.Win32]::Get($hwnd, $GwlExStyle)
    if (($style -band $NoActivate) -eq 0) { [void][PlutonDesktop.Win32]::Set($hwnd, $GwlExStyle, $style -bor $NoActivate) }
}
Clear-NoFocus $Exe 0
"PID $($process.Id)"
