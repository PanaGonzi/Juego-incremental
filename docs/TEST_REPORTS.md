# Informes de prueba

Tipos separados: **L** lógica · **R** red local UDP con procesos o sesiones reales · **I** integración con el cliente Godot exportado · **H** uso humano · **N** Internet.
Los resultados de un tipo no demuestran otro. Logs en `logs/` (fuera de Git).

| ID | Fecha | Tipo | Prueba | Resultado |
| --- | --- | --- | --- | --- |
| T-001 | 31-10-2026 | — | `setup.ps1`: .NET 10.0.403, Godot 4.7.2 .NET, plantillas; hashes SHA-512 oficiales | Correcto |
| T-002 | 31-10-2026 | R | `net-test`: dedicado + 2 y + 12 procesos cliente, con reconexiones, plaza 13 y cliente abusivo, 30 s | 0 divergencias, 0 ticks tardíos, 13.º `reject_full`. **Límite: la mayoría de acciones de los bots «worker» se rechazan por alcance; carga económica baja** |
| T-003 | 31-10-2026 | R | xUnit: rechazo por versión, contraseña, sala llena, sesión duplicada, paquete malformado, velocidad, reconexión, entrada tardía, idempotencia | Correcto |
| T-004 | 31-10-2026 | Local Steam | `SteamSpike`, AppID de prueba 480: init, 25 relays, socket pair fiable y no fiable, lobby privado 12 | Correcto en una máquina y cuenta. **Conexión entre cuentas: NO VERIFICADA** |
| T-005 | 31-10-2026 | L | 38 pruebas xUnit: economía, energía, conservación, conflictos entre 2 jugadores, guardado, corrupción, formato nuevo, recorrido completo y ciclo cooperativo | 38/38. Recorrido hasta 8 piezas: 9,4 min de juego con bot |
| T-006 | 31-10-2026 | I | `auto-test` en build exportada, 1080p, con eventos reales de teclado y ratón, herramientas, guardar y reanudar | 6 ejecuciones: 4 correctas; 1 falló por un error de la propia prueba, ya corregido; 1 terminó por pérdida de GPU (BUG-001) |
| T-007 | 31-10-2026 | I | `coop-test`: anfitrión + invitado y dedicado + 2 clientes Godot, misma máquina | Correcto: acción compartida, posiciones interpoladas, jugador visible, sin divergencias |

## Sin verificar

Dos PCs reales · Internet · Steam entre cuentas · juego humano · 4/8 clientes por separado · 12 clientes en anfitrión · red con latencia y pérdida · 60 min · escenarios medio y estrés · Linux · ultrawide.
