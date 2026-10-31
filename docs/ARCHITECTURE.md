# Arquitectura

```text
content/*.json ─► ContentDb (validado, con revisión hash)
                     │
             ┌───────▼────────┐  WorldCodec   ┌──────────────┐
             │ World          │◄─────────────►│ SaveStore    │ atómico + .bak
             │ Apply / Step   │               └──────────────┘
             └───▲─────────┬──┘
     Command     │         │ Dirty*
             ┌───┴─────────▼──┐  ITransport   ┌──────────────┐
             │ GameServer     │◄─────────────►│ GameClient   │ World espejo
             │ ServerRunner   │  UDP/memoria  └──────┬───────┘
             └────────────────┘                      │
       src/Server (dedicado) o hilo local      GameView + Hud (Godot)
                                               BotClient (carga)
```

- **Simulación**: tick fijo a 10 Hz. El espejo del cliente no ejecuta `Step`: solo aplica deltas.
- **Energía**: oferta y demanda por planeta; satisfacción en tantos por mil aplicada al progreso.
- **Producción**: consume al iniciar; lo que está en proceso se devuelve al desmontar.
- **Enlaces**: máquina a máquina, 14 m como máximo. La distancia hace necesario el tren.
- **Escena**: se reconstruye desde el mundo replicado. Mallas fusionadas por material y compartidas, `MultiMesh` para guijarros, colisiones simples.
- **Guardado**: `%APPDATA%\PlutonNovenoPlaneta\<perfil>\saves`. Cabecera, Deflate y SHA-256. Rechaza formatos más nuevos sin tocar el archivo; un archivo dañado recurre al `.bak`.
- **Autosave**: cada 120 s, serializa en el tick y escribe en otro hilo. Pérdida máxima ante un fallo: 120 s.
- **Rendimiento**: tick del servidor y red medidos en `metrics.csv` del dedicado.

## Planetas (previsto)

`Planet` ya está en máquinas, enlaces, escombros, yacimientos y jugadores. Cada cliente representará solo su planeta; el servidor simula todos como datos, sin escena.
