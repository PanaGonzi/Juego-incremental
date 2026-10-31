# PLUTON — NOVENO PLANETA

Incremental industrial en primera persona, con cooperativo de hasta 12 jugadores. Godot 4.7.2 .NET + C#.

## Jugar una alfa

`builds\alpha-X.Y.Z\Jugar.bat`. Cada alfa incluye `LEEME.txt` con controles y recorrido sugerido.

## Preparar

```powershell
powershell -ExecutionPolicy Bypass -File tools\setup.ps1
```

Instala en `%LOCALAPPDATA%\PlutonTools`, sin permisos de administrador: .NET SDK 10, Godot .NET 4.7.2, plantillas y Steamworks.NET.

## Probar

```powershell
$env:PATH="$env:LOCALAPPDATA\PlutonTools\dotnet;$env:PATH"; $env:DOTNET_ROOT="$env:LOCALAPPDATA\PlutonTools\dotnet"
dotnet test tests/Pluton.Tests
powershell -ExecutionPolicy Bypass -File tools\net-test.ps1 -Clients 12 -Seconds 60 -Extra 1
powershell -ExecutionPolicy Bypass -File tools\auto-test.ps1 -Tag prueba
powershell -ExecutionPolicy Bypass -File tools\coop-test.ps1 -Mode host
```

## Exportar

```powershell
powershell -ExecutionPolicy Bypass -File tools\export.ps1 -Version 0.1.1
```

Crea `builds\alpha-0.1.1\{client,server}` y se niega a sobrescribir una existente.

## Servidor dedicado

```powershell
Pluton.Server.exe --port 27777 --world mundo [--password clave] [--max-players 12] [--autosave 120] [--metrics metricas.csv]
```

UDP. Guarda en `%APPDATA%\PlutonNovenoPlaneta\server\saves`. Ctrl+C guarda y cierra; `--stop-file RUTA` hace lo mismo cuando aparece ese archivo. El primero en entrar a un mundo nuevo es su propietario.

Más información en [docs/](docs/) y [CLAUDE.md](CLAUDE.md).
