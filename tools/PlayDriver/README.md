# PlayDriver

Conductor de QA para Windows: juega la build exportada con entradas normales del sistema (`SendInput`) y captura solo la ventana del juego. La CLI de Claude Code en Windows no ofrece control del ordenador (solo la app Desktop); por eso existe esta herramienta.

Límites:

- Un solo conductor por escritorio (mutex global).
- Solo actúa si la ventana activa es la del juego. **Cambiar de ventana a mano hace que falle la siguiente acción** con código 4, y suelta las teclas.
- Exige perfil `test*`. Nunca toca los guardados del titular.
- Por omisión abre el juego en un monitor secundario donde quepa.

## Compilar

```powershell
$env:PATH="$env:LOCALAPPDATA\PlutonTools\dotnet;$env:PATH"; $env:DOTNET_ROOT="$env:LOCALAPPDATA\PlutonTools\dotnet"
dotnet build tools/PlayDriver -c Release -o logs/playdriver-bin -v q
```

## Comandos

`D` = `logs\playdriver-bin\PlayDriver.exe`. Coordenadas siempre en píxeles de cliente de la ventana.

| Comando | Efecto |
| --- | --- |
| `D start --build builds\alpha-X --profile test-qa [--width 1600 --height 900 --monitor N --log ruta] [-- argumentos del juego]` | Abre el juego y espera a su ventana |
| `D info` | Tamaño, origen, DPI, primer plano y si responde |
| `D shot ruta.png [anchoMax] [x y ancho alto]` | Captura la ventana; avisa si sale casi negra |
| `D key W 800` · `D key Shift+W 1500` · `D key Tab` | Mantiene teclas, o toque breve |
| `D type texto` | Escribe texto Unicode |
| `D move x y` · `D click x y [left\|right] [ms]` · `D drag x1 y1 x2 y2` · `D scroll n` | Ratón absoluto para menús |
| `D press [left\|right] [ms]` | Pulsa sin mover (primera persona: mantener para trabajar) |
| `D look dx dy [ms]` | Ratón **relativo** real (primera persona) |
| `D keydown Shift` … `D click x y` … `D keyup Shift` | Clic con modificador (Mayús, Ctrl) |
| `D wait ms` | Espera |
| `D stop` | Cierra el juego |

`--session nombre` permite otra sesión; sigue habiendo un solo conductor a la vez. Registro de acciones: `%TEMP%\PlutonPlayDriver\<sesión>-acciones.log`, o `--log`.

## Bucle de prueba

Capturar → observar → decidir → actuar → esperar → capturar → evaluar. Una lista de teclas sin mirar la pantalla no es una prueba jugable.
