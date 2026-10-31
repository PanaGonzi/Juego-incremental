# Ejecuta el cliente exportado en modo de recorrido automático y resume el resultado.
# Uso: tools\auto-test.ps1 -Build dev -Tag prueba [-Width 1920 -Height 1080] [-Join host:puerto] [-Seconds 10]
param([string]$Build = 'dev', [string]$Tag = 'auto', [int]$Width = 1280, [int]$Height = 720, [string]$Join = '', [string]$HostPort = '', [string]$Profile = 'test', [string]$Scenario = '', [int]$Seconds = 10, [int]$Timeout = 480)
$Repo = Split-Path $PSScriptRoot -Parent
$Out = Join-Path $Repo "logs\$Tag"
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null
$args2 = @('--', '--autotest', '--profile', $Profile, '--outdir', "`"$Out`"", '--width', $Width, '--height', $Height, '--seconds', $Seconds)
if ($Join -ne '') { $args2 += @('--join', $Join) }
if ($HostPort -ne '') { $args2 += @('--host', $HostPort) }
if ($Scenario -ne '') { $args2 += @('--scenario', $Scenario) }
$p = Start-Process (Join-Path $Repo "builds\alpha-$Build\client\Pluton.exe") -PassThru -ArgumentList $args2 -RedirectStandardOutput "$Out\stdout.txt" -RedirectStandardError "$Out\stderr.txt"
$null = $p.Handle
if (-not $p.WaitForExit($Timeout * 1000)) { $p.Kill(); 'TIEMPO AGOTADO' }
"exit=$($p.ExitCode)"
Get-Content "$Out\autotest.log" -Encoding utf8 -ErrorAction SilentlyContinue | Select-Object -Last 40
Get-Content "$Out\stderr.txt" -TotalCount 15 -ErrorAction SilentlyContinue
