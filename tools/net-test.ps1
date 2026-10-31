# Ensayo con procesos independientes: un servidor dedicado y N clientes con el protocolo real.
# Uso: tools\net-test.ps1 -Clients 12 -Seconds 60 [-Latency 100 -Jitter 10 -Loss 2] [-Reconnect 20] [-Extra 1] [-Tag nombre] [-Abuser]
param(
    [int]$Clients = 2,
    [int]$Seconds = 30,
    [int]$Latency = -1,
    [int]$Jitter = 0,
    [int]$Loss = 0,
    [int]$Reconnect = 0,
    [int]$Extra = 0,
    [int]$Port = 27790,
    [string]$Tag = '',
    [switch]$Abuser
)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot -Parent
$env:PATH = "$env:LOCALAPPDATA\PlutonTools\dotnet;$env:PATH"
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\PlutonTools\dotnet"
$env:DOTNET_NOLOGO = '1'

dotnet build "$Repo\src\Server\Pluton.Server.csproj" -c Release -v q | Out-Null
dotnet build "$Repo\tools\BotClient\Pluton.BotClient.csproj" -c Release -v q | Out-Null

if ($Tag -eq '') { $Tag = "c$Clients-s$Seconds" }
$Out = Join-Path $Repo "logs\net\$(Get-Date -Format 'yyyyMMdd-HHmmss')-$Tag"
New-Item -ItemType Directory -Force $Out | Out-Null
$DataRoot = Join-Path $Out 'data'
$Server = "$Repo\src\Server\bin\Release\net10.0\Pluton.Server.exe"
$Bot = "$Repo\tools\BotClient\bin\Release\net10.0\Pluton.BotClient.exe"

$srv = Start-Process $Server -PassThru -NoNewWindow -RedirectStandardOutput "$Out\server.log" -RedirectStandardError "$Out\server.err" `
    -ArgumentList @('--port', $Port, '--world', 'test', '--profile', 'test', '--data-root', "`"$DataRoot`"", '--metrics', "`"$Out\metrics.csv`"", '--autosave', '30', '--stop-file', "`"$Out\stop.txt`"")
Start-Sleep -Milliseconds 800

$procs = @()
for ($i = 0; $i -lt $Clients; $i++) {
    $behaviour = if ($i % 2 -eq 0) { 'cutter' } else { 'worker' }
    if ($Abuser -and $i -eq 0) { $behaviour = 'abuser' }
    $a = @('--port', $Port, '--seconds', $Seconds, '--index', $i, '--behaviour', $behaviour)
    if ($Latency -ge 0) { $a += @('--latency', $Latency, '--jitter', $Jitter, '--loss', $Loss) }
    if ($Reconnect -gt 0 -and $i % 4 -eq 3) { $a += @('--reconnect-every', $Reconnect) }
    $procs += Start-Process $Bot -PassThru -NoNewWindow -ArgumentList $a -RedirectStandardOutput "$Out\bot$i.log" -RedirectStandardError "$Out\bot$i.err"
    Start-Sleep -Milliseconds 150
}

# Clientes por encima del límite: deben ser rechazados sin molestar a los demás.
$extraProcs = @()
Start-Sleep -Seconds 3
for ($i = 0; $i -lt $Extra; $i++) {
    $n = 100 + $i
    $extraProcs += Start-Process $Bot -PassThru -NoNewWindow -ArgumentList @('--port', $Port, '--seconds', '6', '--index', $n) -RedirectStandardOutput "$Out\extra$n.log" -RedirectStandardError "$Out\extra$n.err"
}

foreach ($p in ($procs + $extraProcs)) { $null = $p.WaitForExit(($Seconds + 40) * 1000); if (-not $p.HasExited) { $p.Kill() } }
Start-Sleep -Seconds 6

# Cierre ordenado: el servidor guarda al detectar el archivo de parada.
"stop" | Set-Content "$Out\stop.txt"
if (-not $srv.WaitForExit(15000)) { "EL SERVIDOR NO SE CERRÓ"; Stop-Process -Id $srv.Id -Force }
Get-ChildItem $Out -Filter 'bot*.log' | Sort-Object { [int]($_.BaseName -replace '\D') } | ForEach-Object { Get-Content $_.FullName }
Get-ChildItem $Out -Filter 'extra*.log' | ForEach-Object { Get-Content $_.FullName }
Get-ChildItem $Out -Filter '*.err' | Where-Object Length -gt 0 | ForEach-Object { "STDERR $($_.Name):"; Get-Content $_.FullName -TotalCount 10 }
"--- servidor"
Get-Content "$Out\server.log" -Tail 4
"--- métricas (últimas 3)"
Get-Content "$Out\metrics.csv" -TotalCount 1
Get-Content "$Out\metrics.csv" -Tail 3
"Salida: $Out"
