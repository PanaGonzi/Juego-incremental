# Anfitrión Godot exportado + N procesos cliente: mide el coste conjunto de servidor, red y render.
# Uso: tools\host-test.ps1 [-Build dev] [-Bots 11] [-Measure medium] [-Seconds 20]
param([string]$Build = 'dev', [int]$Bots = 11, [string]$Measure = 'medium', [int]$Seconds = 20, [int]$Port = 27850)
$Repo = Split-Path $PSScriptRoot -Parent
$env:PATH = "$env:LOCALAPPDATA\PlutonTools\dotnet;$env:PATH"
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\PlutonTools\dotnet"
$env:DOTNET_NOLOGO = '1'
$log = dotnet build "$Repo\tools\BotClient\Pluton.BotClient.csproj" -c Release -v q 2>&1
if ($LASTEXITCODE -ne 0) { $log | Select-Object -Last 10; throw 'Falló la compilación de los bots' }
$Out = Join-Path $Repo "logs\host-$Bots"
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null
$a = @('--', '--autotest', '--profile', 'test-host', '--outdir', "`"$Out`"", '--width', '1920', '--height', '1080', '--seconds', $Seconds, '--host', $Port, '--measure', $Measure, '--expect-players', ($Bots + 1))
$h = Start-Process (Join-Path $Repo "builds\alpha-$Build\client\Pluton.exe") -PassThru -ArgumentList $a -RedirectStandardOutput "$Out\stdout.txt" -RedirectStandardError "$Out\stderr.txt"
$null = $h.Handle
Start-Sleep -Seconds 6
$procs = @()
for ($i = 0; $i -lt $Bots; $i++) {
    $behaviour = if ($i -lt 2) { 'traveler' } else { 'worker' }
    $procs += Start-Process "$Repo\tools\BotClient\bin\Release\net10.0\Pluton.BotClient.exe" -PassThru -NoNewWindow -ArgumentList @('--port', $Port, '--seconds', ($Seconds * 3 + 40), '--index', $i, '--behaviour', $behaviour, '--content', "`"$Repo\content`"") -RedirectStandardOutput "$Out\bot$i.log" -RedirectStandardError "$Out\bot$i.err"
    Start-Sleep -Milliseconds 150
}
if (-not $h.WaitForExit(($Seconds * 3 + 120) * 1000)) { $h.Kill(); 'TIEMPO AGOTADO' }
"anfitrión exit=$($h.ExitCode)"
Get-Content "$Out\autotest.log" -Encoding utf8 -ErrorAction SilentlyContinue
foreach ($p in $procs) { $null = $p.WaitForExit(($Seconds * 3 + 60) * 1000); if (-not $p.HasExited) { $p.Kill() } }
Get-ChildItem $Out -Filter 'bot*.log' | ForEach-Object { Get-Content $_.FullName } | Select-Object -First 3
$sum = 0; Get-ChildItem $Out -Filter 'bot*.log' | ForEach-Object { if ((Get-Content $_.FullName -Raw) -match 'hashMismatch=(\d+)') { $sum += [int]$Matches[1] } }
"divergencias en bots: $sum"
Get-Content "$Out\stderr.txt" -TotalCount 8 -ErrorAction SilentlyContinue
