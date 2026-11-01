# Dos clientes Godot exportados en la misma máquina: anfitrión + invitado, o dedicado + dos clientes.
# Uso: tools\coop-test.ps1 -Mode host|dedicated [-Build dev] [-Seconds 15] [-Scenario coopfactory]
# coopfactory: los dos compiten por la misma fábrica; al final se compara la geometría que ve cada uno.
param([ValidateSet('host', 'dedicated')][string]$Mode = 'host', [string]$Build = 'dev', [int]$Seconds = 15, [int]$Port = 27801, [string]$Scenario = '')
$Repo = Split-Path $PSScriptRoot -Parent
$Exe = Join-Path $Repo "builds\alpha-$Build\client\Pluton.exe"
$Out = Join-Path $Repo "logs\coop-$Mode"
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force "$Out\a", "$Out\b", "$Out\barrier" | Out-Null

. "$PSScriptRoot\lib-ventana.ps1"
$script:Slot = 0
function Client($dir, $profile, $extra) {
    $a = (Test-WindowArgs 960 540 $script:Slot) + @('--', '--autotest', '--profile', $profile, '--outdir', "`"$Out\$dir`"", '--width', '960', '--height', '540', '--seconds', $Seconds) + $extra
    if ($Scenario -ne '') { $a += @('--scenario', $Scenario, '--role', $dir, '--barrier', "`"$Out\barrier`"") }
    $p = Start-Process $Exe -PassThru -ArgumentList $a -RedirectStandardOutput "$Out\$dir\stdout.txt" -RedirectStandardError "$Out\$dir\stderr.txt"
    $null = $p.Handle
    $script:Slot++
    $p
}

$srv = $null
if ($Mode -eq 'dedicated') {
    if ($Scenario -ne '') {
        # El dedicado carga una partida con material preparada por el propio cliente.
        $stage = (Test-WindowArgs 960 540 0) + @('--', '--autotest', '--profile', 'test-a', '--outdir', "`"$Out\stage`"", '--scenario', 'coopstage', '--stage-out', "`"$Out\data\test\saves`"")
        $st = Start-Process $Exe -PassThru -ArgumentList $stage
        $null = $st.Handle
        if (-not $st.WaitForExit(60000)) { $st.Kill(); throw 'La preparación de la partida no terminó' }
        if ($st.ExitCode -ne 0) { throw "La preparación de la partida falló: $($st.ExitCode)" }
    }
    $srv = Start-Process (Join-Path $Repo "builds\alpha-$Build\server\Pluton.Server.exe") -PassThru -WorkingDirectory (Join-Path $Repo "builds\alpha-$Build\server") `
        -ArgumentList @('--port', $Port, '--world', 'coop', '--profile', 'test', '--data-root', "`"$Out\data`"") -RedirectStandardOutput "$Out\server.log" -RedirectStandardError "$Out\server.err"
    Start-Sleep -Seconds 2
    $a = Client 'a' 'test-a' @('--join', "127.0.0.1:$Port")
} else {
    # El anfitrión espera más: al salir termina la sesión de sus invitados.
    $Seconds += 20
    $a = Client 'a' 'test-a' @('--host', $Port)
    $Seconds -= 20
    Start-Sleep -Seconds 4
}
$b = Client 'b' 'test-b' @('--join', "127.0.0.1:$Port")

foreach ($p in $a, $b) { if (-not $p.WaitForExit(300000)) { $p.Kill(); 'TIEMPO AGOTADO' } }
if ($srv) { Stop-Process -Id $srv.Id -Force; Get-Content "$Out\server.log" | Select-Object -Last 8 }
"A exit=$($a.ExitCode)"; Get-Content "$Out\a\autotest.log" -Encoding utf8 -ErrorAction SilentlyContinue
"B exit=$($b.ExitCode)"; Get-Content "$Out\b\autotest.log" -Encoding utf8 -ErrorAction SilentlyContinue
foreach ($f in Get-ChildItem "$Out\b" -Filter 'geom_*.txt') {
    $other = Join-Path "$Out\a" $f.Name
    if (-not (Test-Path $other)) { "GEOMETRIA $($f.Name): falta en A"; continue }
    $same = (Get-Content $f.FullName -Raw) -ceq (Get-Content $other -Raw)
    "GEOMETRIA $($f.Name): $(if ($same) { 'igual' } else { 'DISTINTA' })"
}
