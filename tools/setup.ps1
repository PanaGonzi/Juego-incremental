# Preparación idempotente del entorno de PLUTON. Instala a nivel de usuario, sin elevación.
# Uso: powershell -ExecutionPolicy Bypass -File tools\setup.ps1
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$GodotVersion = '4.7.2'
$DotnetChannel = '10.0'
$Root = Join-Path $env:LOCALAPPDATA 'PlutonTools'
$DotnetDir = Join-Path $Root 'dotnet'
$GodotDir = Join-Path $Root "godot-$GodotVersion"
$Downloads = Join-Path $Root 'downloads'
$Base = "https://github.com/godotengine/godot/releases/download/$GodotVersion-stable"
$EditorZip = "Godot_v$GodotVersion-stable_mono_win64.zip"
$TemplatesTpz = "Godot_v$GodotVersion-stable_mono_export_templates.tpz"
$TemplatesDir = Join-Path $env:APPDATA "Godot\export_templates\$GodotVersion.stable.mono"

New-Item -ItemType Directory -Force $Root, $Downloads | Out-Null

function Get-Verified($name, $sums) {
    $target = Join-Path $Downloads $name
    $expected = ($sums | Where-Object { $_ -match [regex]::Escape($name) + '$' } | ForEach-Object { ($_ -split '\s+')[0] })
    if (-not $expected) { throw "Sin hash oficial para $name" }
    if (Test-Path $target) {
        if ((Get-FileHash $target -Algorithm SHA512).Hash -eq $expected) { return $target }
        Remove-Item $target
    }
    Write-Host "Descargando $name"
    Invoke-WebRequest "$Base/$name" -OutFile $target
    if ((Get-FileHash $target -Algorithm SHA512).Hash -ne $expected) { throw "Hash incorrecto en $name" }
    return $target
}

# .NET SDK
if (-not (Test-Path (Join-Path $DotnetDir 'dotnet.exe'))) {
    Write-Host "Instalando .NET SDK $DotnetChannel"
    $script = Join-Path $Downloads 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script
    & $script -Channel $DotnetChannel -InstallDir $DotnetDir -NoPath
}

# Godot .NET y plantillas
$sumsFile = Join-Path $Downloads "SHA512-SUMS-$GodotVersion.txt"
if (-not (Test-Path $sumsFile)) { Invoke-WebRequest "$Base/SHA512-SUMS.txt" -OutFile $sumsFile }
$sums = Get-Content $sumsFile

$GodotExe = Join-Path $GodotDir "Godot_v$GodotVersion-stable_mono_win64\Godot_v$GodotVersion-stable_mono_win64_console.exe"
if (-not (Test-Path $GodotExe)) {
    $zip = Get-Verified $EditorZip $sums
    Expand-Archive $zip -DestinationPath $GodotDir -Force
}
if (-not (Test-Path (Join-Path $TemplatesDir 'version.txt'))) {
    $tpz = Get-Verified $TemplatesTpz $sums
    $tmp = Join-Path $Downloads 'templates-extract'
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Copy-Item $tpz "$tpz.zip"
    Expand-Archive "$tpz.zip" -DestinationPath $tmp -Force
    Remove-Item "$tpz.zip"
    New-Item -ItemType Directory -Force $TemplatesDir | Out-Null
    Copy-Item (Join-Path $tmp 'templates\*') $TemplatesDir -Recurse -Force
    Remove-Item $tmp -Recurse -Force
}

# Steamworks.NET: binding + steam_api64 de la misma versión (fuera de Git, tiene licencia propia del SDK)
$SteamworksVersion = '2025.165.0'
$SteamworksDir = Join-Path $Downloads 'steamworksnet'
if (-not (Test-Path (Join-Path $SteamworksDir 'extracted\Windows-x64\steam_api64.dll'))) {
    New-Item -ItemType Directory -Force $SteamworksDir | Out-Null
    $zip = Join-Path $SteamworksDir "Steamworks.NET-Standalone_$SteamworksVersion.zip"
    Invoke-WebRequest "https://github.com/rlabrecque/Steamworks.NET/releases/download/$SteamworksVersion/Steamworks.NET-Standalone_$SteamworksVersion.zip" -OutFile $zip
    Expand-Archive $zip -DestinationPath (Join-Path $SteamworksDir 'extracted') -Force
}

Write-Host "dotnet : $(& (Join-Path $DotnetDir 'dotnet.exe') --version)"
Write-Host "godot  : $(& $GodotExe --version)"
Write-Host "plantillas: $(Get-Content (Join-Path $TemplatesDir 'version.txt'))"
Write-Host "Entorno listo en $Root"
