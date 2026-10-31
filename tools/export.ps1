# Genera una alfa versionada sin tocar builds anteriores: builds/alpha-X.Y.Z/{client,server}
# Uso: tools\export.ps1 -Version 0.1.0 [-Force]
param([Parameter(Mandatory)][string]$Version, [switch]$Force)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot -Parent
$Tools = Join-Path $env:LOCALAPPDATA 'PlutonTools'
$env:PATH = "$Tools\dotnet;$env:PATH"
$env:DOTNET_ROOT = "$Tools\dotnet"
$env:DOTNET_NOLOGO = '1'
$Godot = "$Tools\godot-4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"

$Out = Join-Path $Repo "builds\alpha-$Version"
if ((Test-Path $Out) -and -not $Force) { throw "Ya existe ${Out}: no se sobrescribe una build estable" }
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force "$Out\client", "$Out\server" | Out-Null

Push-Location "$Repo\src\Game"
try {
    # Importa recursos y compila antes de exportar para no depender de un estado previo del editor.
    & $Godot --headless --path . --import 2>&1 | Out-Null
    dotnet build Pluton.Game.csproj -c ExportRelease -v q
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación del cliente' }
    $ErrorActionPreference = 'Continue'
    $log = & $Godot --headless --path . --export-release Windows "$Out\client\Pluton.exe" 2>&1 | ForEach-Object { $_.ToString() }
    $ErrorActionPreference = 'Stop'
    $log | Select-Object -Last 5
    if (-not (Test-Path "$Out\client\Pluton.pck")) { $log | Select-Object -Last 40; throw 'La exportación no produjo el ejecutable' }
} finally { Pop-Location }
Copy-Item "$Repo\content" "$Out\client\content" -Recurse

dotnet publish "$Repo\src\Server\Pluton.Server.csproj" -c Release -r win-x64 --self-contained true -o "$Out\server" -v q
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación del servidor' }
Copy-Item "$Repo\content" "$Out\server\content" -Recurse

Copy-Item "$Repo\THIRD_PARTY_NOTICES.txt" "$Out\THIRD_PARTY_NOTICES.txt"
$commit = git -C $Repo rev-parse --short HEAD
$dirty = if (git -C $Repo status --porcelain) { ' (con cambios sin confirmar)' } else { '' }
"PLUTON alfa $Version`ncommit $commit$dirty`nfecha $(Get-Date -Format s)" | Set-Content "$Out\VERSION.txt" -Encoding utf8
"@echo off`r`nstart `"`" `"%~dp0client\Pluton.exe`"" | Set-Content "$Out\Jugar.bat" -Encoding ascii
"@echo off`r`ncd /d `"%~dp0server`"`r`nPluton.Server.exe --port 27777 --world mundo`r`npause" | Set-Content "$Out\Servidor-dedicado.bat" -Encoding ascii
"Exportada en $Out"
