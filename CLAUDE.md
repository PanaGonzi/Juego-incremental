# PLUTON — NOVENO PLANETA

Incremental industrial 3D en primera persona para Steam, con cooperativo online real de 1 a 12 jugadores.
Encargo completo y vigente: [PROMPT_CLAUDE_PLUTON_INCREMENTAL_STEAM.md](PROMPT_CLAUDE_PLUTON_INCREMENTAL_STEAM.md).
Prioridades visuales y de pruebas vigentes: [PROMPT_CLAUDE_CONTINUAR_QA_CINTAS_GRAFICOS.md](PROMPT_CLAUDE_CONTINUAR_QA_CINTAS_GRAFICOS.md). Sustituyen a la dirección visual provisional.
Léelos si vas a tomar una decisión de producto. Comunicación con el titular: español. No esperes su evaluación para seguir con mejoras y pruebas concretas.

## Reglas persistentes

- El servidor es la única autoridad. El cliente envía intenciones (`Command`); nunca acredita nada.
- `src/Simulation` no depende de Godot, red ni disco. Toda regla económica va ahí y se prueba sin escena.
- Reglas y números en `content/*.json`, no en código. Textos en `content/loc/`.
- Cantidades enteras. Todo cambio de estado replicado debe marcar sus conjuntos `Dirty*`.
- Si cambia el formato binario: subir `WorldCodec.FormatVersion` y añadir migración y prueba. Si cambia el protocolo: subir `Protocol.Version`.
- No bajar el límite de 12 jugadores para cerrar un hito. No presentar el AppID 480 como el del producto.
- No marcar nada como verificado sin evidencia. Separar lógica, integración, carga y uso real. Si no se pudo medir: **NO VERIFICADO**.
- Sin botones decorativos: cada texto de interfaz corresponde a una función real.
- Nunca sobrescribir `builds/alpha-X.Y.Z` ya entregada ni guardados del usuario. Las pruebas usan perfiles `test*`.
- Pago, login, publicación o remoto Git: pedir autorización. Sin secretos ni datos de cuenta en Git o informes.

## Comandos (PowerShell, desde la raíz)

```powershell
powershell -ExecutionPolicy Bypass -File tools\setup.ps1          # entorno idempotente
$env:PATH="$env:LOCALAPPDATA\PlutonTools\dotnet;$env:PATH"; $env:DOTNET_ROOT="$env:LOCALAPPDATA\PlutonTools\dotnet"
dotnet test tests/Pluton.Tests                                    # lógica + red UDP local
powershell -ExecutionPolicy Bypass -File tools\export.ps1 -Version dev -Force       # exporta builds/alpha-dev
powershell -ExecutionPolicy Bypass -File tools\auto-test.ps1 -Tag prueba            # recorrido del cliente exportado
powershell -ExecutionPolicy Bypass -File tools\coop-test.ps1 -Mode host             # dos clientes Godot (host|dedicated)
powershell -ExecutionPolicy Bypass -File tools\net-test.ps1 -Clients 12 -Seconds 60 -Extra 1   # procesos independientes
dotnet build tools/PlayDriver -c Release -o logs/playdriver-bin -v q   # conductor de QA: jugar con entradas reales de Windows (ver su README)
```

## Mapa

`src/Simulation` reglas · `src/Persistence` guardados · `src/Networking` protocolo, servidor, cliente, transportes · `src/Server` dedicado · `src/Game` Godot · `content` datos · `tests` · `tools` · `docs`.

Estado actual: [docs/STATUS.md](docs/STATUS.md) · Reanudar: [docs/HANDOFF.md](docs/HANDOFF.md) · Plan: [docs/ROADMAP.md](docs/ROADMAP.md) · Decisiones: [docs/DECISIONS.md](docs/DECISIONS.md).
