# Servidor dedicado en Linux real (B-5): se publica autocontenido para linux-musl-x64 y se ejecuta en una
# distribución WSL2. Los clientes de carga corren en Windows y se conectan por la IP de la distribución.
# Uso: tools\linux-test.ps1 [-Distro docker-desktop] [-Clients 12] [-Seconds 60] [-Extra 1]
# No arranca Docker ni instala nada: usa una distribución WSL ya presente. Datos en memoria (/dev/shm), perfil test.
# Dependencias del servidor en Linux musl: libstdc++ y libgcc. Si la distribución no las trae, -LibDir las carga solo para la prueba.
param(
    [string]$Distro = 'docker-desktop',
    [int]$Clients = 12,
    [int]$Seconds = 60,
    [int]$Extra = 1,
    [int]$Port = 27791,
    [string]$Work = "/dev/shm",
    # Carpeta de Windows con libstdc++.so.6* y libgcc_s.so.1 para distribuciones mínimas que no las traen (docker-desktop).
    [string]$LibDir = ""
)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot -Parent
$env:PATH = "$env:LOCALAPPDATA\PlutonTools\dotnet;$env:PATH"
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\PlutonTools\dotnet"
$env:DOTNET_NOLOGO = '1'
# NuGet necesita estas variables; un proceso lanzador puede no heredarlas.
if (-not $env:ProgramData) { $env:ProgramData = 'C:\ProgramData' }
if (-not ${env:ProgramFiles(x86)}) { ${env:ProgramFiles(x86)} = 'C:\Program Files (x86)' }

$Out = "$Repo\logs\linux-$(Get-Date -Format yyyyMMdd-HHmmss)"
New-Item -ItemType Directory -Force $Out | Out-Null
$Pub = "$Repo\logs\linux-server"
dotnet publish "$Repo\src\Server\Pluton.Server.csproj" -c Release -r linux-musl-x64 --self-contained true -o $Pub -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "No se pudo publicar el servidor para Linux" }
dotnet build "$Repo\tools\BotClient\Pluton.BotClient.csproj" -c Release -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "No se pudo compilar el cliente de carga" }
$Bot = "$Repo\tools\BotClient\bin\Release\net10.0\Pluton.BotClient.exe"

function ToLinux([string]$path) { '/mnt/host/' + $path.Substring(0, 1).ToLower() + ($path.Substring(2) -replace '\\', '/') }
# Las órdenes van en guiones .sh en la carpeta de salida: así no dependen del entrecomillado de PowerShell 5.1 ni de rutas con espacios.
function InLinux([string]$name, [string]$script) {
    $file = Join-Path $Out $name
    [IO.File]::WriteAllText($file, ($script -replace "`r", ''), (New-Object Text.UTF8Encoding $false))
    "sh '$(ToLinux $file)'"
}
function RunLinux([string]$name, [string]$script) {
    $cmd = InLinux $name $script
    # wsl escribe avisos en UTF-16 por stderr (p. ej. disco en solo lectura); se descartan.
    $ErrorActionPreference = 'Continue'
    (wsl.exe -d $Distro -- sh -c $cmd 2>$null) | ForEach-Object { "$_" -replace "`0", '' } | Where-Object { $_ -and $_ -notmatch '^wsl:' }
}
$pubL = ToLinux $Pub
$contentL = ToLinux "$Repo\content"
$outL = ToLinux $Out
$raw = RunLinux 'ip.sh' 'ip -4 -o addr show eth0'
$ip = [regex]::Match(($raw -join ' '), 'inet (\d+\.\d+\.\d+\.\d+)').Groups[1].Value
if (-not $ip) { throw "La distribución $Distro no da IP: $raw" }
"Distribución: $Distro, IP $ip"
RunLinux 'os.sh' 'uname -srm; head -1 /etc/os-release' | Tee-Object "$Out\linux.txt"

# Se copia a $Work (memoria): ejecutar desde el disco de Windows montado es lento, y el disco de la distribución puede estar en solo lectura.
$libs = ''
if ($LibDir) {
    $libs = "mkdir -p $Work/pluton-libs && cp '$(ToLinux $LibDir)'/*.so* $Work/pluton-libs/ && " +
            "for f in $Work/pluton-libs/libstdc++.so.6.*; do ln -sf `$f $Work/pluton-libs/libstdc++.so.6; done; export LD_LIBRARY_PATH=$Work/pluton-libs"
}
$run = @"
rm -rf $Work/pluton-srv $Work/pluton-data $Work/pluton-stop $Work/pluton-libs
cp -r '$pubL' $Work/pluton-srv && chmod +x $Work/pluton-srv/Pluton.Server || exit 1
$libs
cd $Work/pluton-srv
exec ./Pluton.Server --port $Port --world test --profile test --data-root $Work/pluton-data --load-test-unlock --content '$contentL' --autosave 30 --stop-file $Work/pluton-stop --metrics '$outL/metrics.csv' > '$outL/server.log' 2> '$outL/server.err'
"@
$srvCmd = InLinux 'server.sh' $run
$srv = Start-Process wsl.exe -PassThru -WindowStyle Hidden -ArgumentList @('-d', $Distro, '--', 'sh', '-c', "`"$srvCmd`"")
Start-Sleep -Seconds 6
if ($srv.HasExited) { "EL SERVIDOR NO ARRANCÓ"; Get-Content "$Out\server.err" -ErrorAction SilentlyContinue; exit 1 }

$procs = @()
for ($i = 0; $i -lt $Clients; $i++) {
    $behaviour = if ($i % 2 -eq 0) { 'cutter' } else { 'worker' }
    $a = @('--host', $ip, '--port', $Port, '--seconds', $Seconds, '--index', $i, '--behaviour', $behaviour, '--content', "`"$Repo\content`"")
    $procs += Start-Process $Bot -PassThru -NoNewWindow -ArgumentList $a -RedirectStandardOutput "$Out\bot$i.log" -RedirectStandardError "$Out\bot$i.err"
    Start-Sleep -Milliseconds 150
}
Start-Sleep -Seconds 3
$extraProcs = @()
for ($i = 0; $i -lt $Extra; $i++) {
    $n = 100 + $i
    $extraProcs += Start-Process $Bot -PassThru -NoNewWindow -ArgumentList @('--host', $ip, '--port', $Port, '--seconds', '6', '--index', $n, '--content', "`"$Repo\content`"") -RedirectStandardOutput "$Out\extra$n.log" -RedirectStandardError "$Out\extra$n.err"
}
foreach ($p in ($procs + $extraProcs)) { $null = $p.WaitForExit(($Seconds + 40) * 1000); if (-not $p.HasExited) { $p.Kill() } }
Start-Sleep -Seconds 6

# Cierre ordenado: el servidor guarda al ver el archivo de parada; se comprueba que el guardado existe en Linux.
RunLinux 'stop.sh' "touch $Work/pluton-stop" | Out-Null
if (-not $srv.WaitForExit(20000)) { "EL SERVIDOR NO SE CERRÓ"; RunLinux 'kill.sh' 'pkill -f Pluton.Server' | Out-Null }
RunLinux 'saves.sh' "rm -f $Work/pluton-stop; find $Work/pluton-data -name '*.plsave*' -exec ls -l {} \;; cp -r $Work/pluton-data '$outL/data'" | Tee-Object "$Out\saves.txt"
Get-ChildItem $Out -Filter 'bot*.log' | Sort-Object { [int]($_.BaseName -replace '\D') } | ForEach-Object { Get-Content $_.FullName }
Get-ChildItem $Out -Filter 'extra*.log' | ForEach-Object { Get-Content $_.FullName }
Get-ChildItem $Out -Filter '*.err' | Where-Object Length -gt 0 | ForEach-Object { "STDERR $($_.Name):"; Get-Content $_.FullName -TotalCount 10 }
"--- servidor"
Get-Content "$Out\server.log" -Tail 6
"Salida: $Out"
