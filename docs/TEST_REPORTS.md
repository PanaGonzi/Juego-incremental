# Informes de prueba

Tipos separados: **L** lógica · **R** red local con procesos reales · **I** integración con el cliente Godot exportado · **H** uso humano · **N** Internet.
Los resultados de un tipo no demuestran otro. Todo se ejecutó en una sola máquina salvo que se indique. Logs en `logs/` (fuera de Git).

| ID | Fecha | Tipo | Prueba | Resultado |
| --- | --- | --- | --- | --- |
| T-001 | 31-10-2026 | — | `setup.ps1`: .NET 10.0.403, Godot 4.7.2 .NET, plantillas; hashes SHA-512 oficiales | Correcto |
| T-003 | 31-10-2026 | R | xUnit por UDP: rechazo por versión, contraseña, sala llena, sesión duplicada, paquete malformado, velocidad, reconexión, entrada tardía, idempotencia, expulsión | Correcto |
| T-004 | 31-10-2026 | Steam local | `SteamSpike`, AppID de prueba 480: init, 25 relays, 12 clientes y servidor reales por Steam Networking Sockets (pares locales): 996 acciones, 1980 firmas, 0 divergencias, desconexión detectada | Correcto en una máquina y cuenta. **Entre cuentas y por relay: NO VERIFICADO** |
| T-005 | 31-10-2026 | L | xUnit: economía, energía, calor, conservación, conflictos, trenes, reservas, viajes, carga interplanetaria, offline, eventos, guardados reales de 0.1.0, 0.2.0 y 0.3.0, idiomas | Ver CHANGELOG para el recuento vigente |
| T-006 | 31-10-2026 | L | **Campaña completa con comandos válidos: 23/23 objetivos en 176 min de juego** con un bot eficiente | Correcto. Duración humana NO VERIFICADA |
| T-008 | 31-10-2026 | I | `auto-test` en build exportada, 1080p: bucle, eventos reales de teclado y ratón, reasignación de teclas, tren, viaje, Halloween | Correcto en 0.3.0 |
| T-007 | 31-10-2026 | I | `coop-test`: anfitrión + invitado y dedicado + 2 clientes Godot | Correcto: acción compartida, jugador visible, sin divergencias |
| T-009 | 31-10-2026 | I + R | **Anfitrión Godot + 11 procesos cliente**, escenario medio, 1080p | 12 conectados, 0 divergencias. Ver PERFORMANCE |
| T-010 | 31-10-2026 | R | Matriz 4, 8 y 12 clientes con dedicado; jugador 13 `reject_full` | 0 divergencias, 0 ticks tardíos |
| T-011 | 31-10-2026 | R | 12 clientes con relé UDP real: 50 ms/1 %, 100 ms/2 %, 150 ms/3 % con jitter, con viajeros | Pérdida medida 1,0/2,0/3,0 %, 0 divergencias, 0 tardíos. RTT máximo observado 544/739/1006 ms sin investigar |
| T-012 | 31-10-2026 | R | **60 min, 12 clientes, 100 ms/2 %**, escenario medio, sobre 0.2.0 (057afc5): 87 reconexiones, 436 viajes | Memoria 63→65 MB, tick 0,25-0,31 ms, 0 tardíos, 4320 firmas sin divergencia. **Carga económica débil: 1604 acciones aplicadas frente a 21428 rechazadas** |
| T-015 | 31-10-2026 | R | **60 min, 12 clientes, 100 ms/2 % real (30664 de 1511936 datagramas), medio, sobre 0.4.0 (f71efaa)**: 87 reconexiones, 434 viajes, 5638 acciones aplicadas y 8402 rechazadas (bolsa vacía, sin coste, celda ocupada) | Memoria 63→66 MB, tick 0,23-0,33 ms, p99 máx. 38 ms, 0 tardíos, 4320 firmas sin divergencia, sin errores fatales. 0.4.1 solo añade cliente: no repetido sobre ella |
| T-016 | 31-10-2026 | I | Regresión sobre el paquete 0.4.0: bucle, tren, Marte, Halloween, cooperativo; sobre 0.4.1: bucle, Marte, dedicado | Correcto, sin eventos de GPU |
| T-017 | 31-10-2026 | I | Ultrawide 3440×1440 (ventana mayor que el monitor de 1080p: render y disposición, no un monitor real), panel de permisos, aviso de ausencia | Correcto |
| T-013 | 31-10-2026 | R | Dedicado: cierre ordenado, reinicio, `kill` forzado con recuperación del último autosave | Correcto: cargado en tick 133, luego 184 tras el `kill` |
| T-014 | 31-10-2026 | I | 8 tramos de viaje en el cliente | Memoria estable, llegada al planeta correcto, Plutón sigue produciendo |

## Errores que estas pruebas encontraron

- Caída real del servidor al terminar dos jugadores el mismo escombro (**también en 0.1.0**). Probado rojo → verde.
- Los motivos de rechazo no llegaban: el cierre inmediato los descartaba.
- Las filas de latencia iniciales eran **inválidas**: la simulación de LiteNetLib no actúa en release. Sustituidas por relé UDP.
- El `delta` de Godot está suavizado: las primeras cifras de frame eran falsas. Sustituido por reloj real.
- Picos de 180 ms atribuidos primero a shaders: eran mis propias capturas PNG. Precalentamiento retirado.
- Picos de 55 ms reales: regeneración masiva de `Label3D`. Eliminados.
- Ciclo de reinicio del servidor sin guardar por fallo: añadido guardado de emergencia sin pisar el válido.

## Sin verificar

**Una persona jugando** · **todo el audio: generado y reproducido sin errores, pero nadie lo ha escuchado** · dos PC reales · Internet · Steam entre cuentas y relays · Linux · monitor ultrawide físico · instalación limpia en otro equipo · causa de BUG-001.
