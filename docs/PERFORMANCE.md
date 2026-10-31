# Rendimiento

**Equipo de referencia:** Ryzen 7 5700X (8C/16T), 32 GB, Radeon RX 7800 XT (driver 32.0.32015.7029), Windows 11 Pro 26200, monitor de 180 Hz. Forward+ (Vulkan).

## Cliente (build exportada)

| Fecha | Versión | Resolución | Escena | Media | p95 | p99 | Máx. | Memoria | VRAM | Notas |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 31-10-2026 | dev pre-0.1.0 | 1080p | Inicial: 2-3 máquinas construidas, ~150 escombros, 10 s, semilla fija | 5,56 ms | 6,46 | 12,23 | 20,41 | 624 MB | 334 MB | **VSync a 180 Hz: la media es el tope del monitor, no la capacidad.** Escena pequeña: no demuestra el objetivo |

Medido entre llamadas a `_Process` con reloj real; el `delta` de Godot está suavizado y dio cifras falsas al principio. Otras pruebas del mismo día dieron p95 de hasta 13,25 ms: variación sin investigar.

## Servidor dedicado

| Fecha | Clientes | Duración | Tick medio | p99 | Máx. | Tardíos | Envío total | Memoria |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 31-10-2026 | 12 procesos | 30 s | 0,09-0,97 ms | 1,5-14,7 ms | 14,7 ms | 0 | ~300 kbit/s | 41 MB |

Tick presupuestado: 100 ms. **Carga económica baja**, ver T-002: no es el escenario medio.

## Pendiente

Escenarios pequeño, medio y estrés · frame sin VSync · anfitrión + 11 · ampliar vías · cambio de planeta · ensayo de 60 min.
