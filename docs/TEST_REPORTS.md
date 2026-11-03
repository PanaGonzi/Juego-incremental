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
| T-018 | 01-11-2026 | I | Dos clientes exportados, anfitrión y dedicado, un solo PC, `coop-test -Scenario coopfactory`, comandos de la interfaz y no entradas de Windows, mundo preparado: colocar, sacar, enlazar y desmontar a la vez, reconexión, geometría comparada; guardar y cargar solo con anfitrión | Correcto en ambos modos, 0 divergencias. Evidencia en `artifacts/qa/0.5-coop/`. Con dos personas o entre dos PC: **NO VERIFICADO** |
| T-019 | 01-11-2026 | R | 247 pruebas xUnit; 12 clientes 60 s más un 13.º sobre `evolucion-0.5`; anfitrión exportado con 11 bots, escenario medio a 1080p | Correcto: 0 divergencias, 13.º rechazado con `reject_full`, tick p99 máx. 1,5 ms. Anfitrión: media 4,0-6,7 ms, p99 12,8-14,1 ms, máx. 47,9 ms |
| T-020 | 01-11-2026 | I | Regresión del cliente exportado: cintas, cinta larga, gestión, zona, tren, viaje, Marte, Halloween, ausencia, construcción y bucle completo | Correcto, sin eventos de GPU. Construcción y bucle con `-NoCapture`: omiten la captura del ratón y las comprobaciones de entrada, para no interferir con el titular. Esas comprobaciones, y el escenario `fp`, **no repetidas** en esta build |
| T-021 | 01-11-2026 | R | BUG-005, 12 clientes 45 s con relé UDP. Respuesta real = desde enviar la orden hasta recibir su resultado | Ping 1000 ms: 150/0 % RTT 203, resp. máx. 356; 150/3 % RTT 1006, resp. máx. 1560; 50/0 % RTT 105, resp. máx. 251; 50/1 % RTT 602, resp. máx. 652. Ping 500 ms: 150/0 % RTT 203, resp. máx. 376; 150/3 % RTT 514, resp. máx. 1354; 50/1 % RTT 331, resp. máx. 623; 100/2 % RTT 514, resp. máx. 699. Sin divergencias |
| T-022 | 01-11-2026 | I | Sobre el paquete `alpha-0.5.0-qa4` (1f7f047): `coopfactory` con anfitrión y dedicado, bucle con `-NoCapture`; 247 xUnit antes de exportar | Correcto, sin eventos de GPU. T-019 a T-021 son de la build anterior; desde entonces solo cambian el modelo de la plataforma y la versión. No repetidos aquí: `fp`, `InputPaths`, migración `beltsv4`, entradas reales |
| T-023 | 01-11-2026 | R | BUG-005 por etapas, sobre el código exacto de LiteNetLib 2.1.5 (descompilado): 12 clientes, 40 s, relé con 150 ms y jitter 20. Marcas de reloj comunes en cliente, relé y servidor. Tabla abajo | Causa identificada; respuesta real mejorada, picos no eliminados. Ver tabla. Sin divergencias |
| T-024 | 01-11-2026 | I | B-8 sobre el paquete `alpha-0.5.0-qa6` (d3321f1), sin capturar el ratón. 306 xUnit: bloques, apartadero, cruces, estación ocupada, 12 redes al azar con 40 000 ticks, guardado, partida real v5, réplica por UDP con dos clientes y BUG-008. Auto: `signals`, `rail`, `manage`, `beltsv4`, bucle. `coopfactory` con anfitrión y dedicado, con señales. 12 clientes, 40 s y 13.º rechazado. Caudal y coste abajo | Todo correcto, sin divergencias ni eventos de GPU. Un primer intento se detuvo por falta de memoria del equipo, sin relación con el juego. `coopfactory` encontró BUG-008. Los 12 clientes no llevan trenes. Un tren ante una señal con dos clientes: solo xUnit por UDP, no con clientes Godot. No repetidos: `fp`, `InputPaths`, entradas reales |
| T-025 | 02-11-2026 | I | Build `alpha-dev`, sin foco ni captura, con `tools/desktop-start.ps1` y `agent-desktop` (accesibilidad semántica, **no** teclado ni ratón). Alojar y unirse por los menús reales, con nombres escritos, en dos ventanas. Unirse a un puerto sin servidor. Calidad baja y alta y escalas 75, 125 y 150 % a 1600×900, entrando en partida. `manage` al 150 % en 1280×720. Medido en cada paso: ventana en primer plano y recorte del cursor | Correcto: 2/12 en ambas ventanas; el error de conexión se muestra en el menú; primer plano y cursor intactos; sin eventos de GPU. Encontró INC-22, INC-23 y dos fallos de las herramientas de prueba (abajo). Paneles en cooperativo y las pestañas Interfaz, Sonido y Controles: **NO VERIFICADO** por esta vía |
| T-026 | 02-11-2026 | I | Paquete `alpha-0.5.0-qa7` (e0a33f8), sin capturar el ratón. 307 xUnit. Auto: `signals`, `rail`, `manage`, `beltsv4`, bucle. `coopfactory` con anfitrión y dedicado, con las ventanas abiertas sin foco. 12 clientes, 40 s, con `-Extra 1` | Todo correcto, geometría igual en ambos clientes, sin eventos de GPU, sin `override.cfg` residual. No repetidos: `fp`, `InputPaths`, entradas reales |
| T-027 | 02-11-2026 | I | Build `alpha-dev`, posterior a qa7, con H-4 y H-9. 308 xUnit, incluida `Cutting_debris_updates_the_client_index_without_a_structural_delta`. `coopfactory` con anfitrión. Estrés de 40 s por vista y `long` | Correcto, geometría igual, sin eventos de GPU. Cifras en [REVISION_E8](REVISION_E8.md). `coopfactory` con dedicado, 12 clientes y los demás recorridos no se repitieron sobre este cambio |
| T-028 | 02-11-2026 | I | **Teclado y ratón reales de Windows con `PlayDriver`, manejados por un agente**, build `alpha-dev`, perfil `test-real`. Inventario, cintas, divisor y unificador, Mover, Seguir cadena, vías, señales, ramal, energía, cerrar y reabrir, calidad, FOV, escala y ultrawide. `fp` y bucle con captura del ratón, antes de los arreglos | Lo marcado en [PENDIENTE_CONTROLES_REALES](PENDIENTE_CONTROLES_REALES.md). Encontró INC-25 a INC-33; todos corregidos salvo INC-33. Sin eventos de GPU. No es una persona jugando. No hechos: partículas, vapor, tender hacia un divisor, primera experiencia completa |
| T-029 | 02-11-2026 | I + R | Paquete `alpha-0.5.0-qa8` (7ef7bbe), sin capturar el ratón. 318 xUnit. Auto: `signals`, `rail`, `manage`, `beltsv4`, bucle. `coopfactory` con anfitrión y dedicado. 12 clientes, 40 s, y 13.º rechazado con `reject_full`. Medidas `medium`, `long` y `saturated` a 1920×1080, una tirada | Todo correcto, geometría igual en ambos clientes, `hashMismatch=0` en los 12, sin eventos de GPU, sin `override.cfg` residual. Cifras en [REVISION_E8](REVISION_E8.md). No repetidos sobre este paquete: entradas reales, `fp`, `InputPaths` |
| T-030 | 03-11-2026 | L + R + I | Build `alpha-dev` posterior a qa8: reindexado parcial (H-4, H-6), carga quieta sin subir (H-8), distancia de dibujo de vías (B-4), brillo (H-10), avatar con piernas (B-6), nueva expedición (B-7), aviso de material a la venta (B-3). 332 xUnit. Auto sin capturar el ratón: `base` (60 FPS, p99 21,3 ms), `expedition` (CORRECTO, con el panel real y una firma `Check` sin divergencias tras el reinicio), `belts`, `manage`, `signals`, `rail`; medidas `medium` y `stress` a 1080p (estrés denso: media 8,6 ms, p99 15,0; `SyncWorld` máx 6,6/14,7/6,9 ms frente a 19,6/10,0/7,8 de la qa8; máximos con ruido). `coop-test -Mode host` CORRECTO, avatar remoto visible con cuerpo y piernas; zancada en movimiento NO VERIFICADA. **Servidor dedicado en Linux** (`tools/linux-test.ps1`): linux-musl-x64 autocontenido en WSL2 `docker-desktop` (kernel 6.18), 12 clientes de carga desde Windows 45 s, 0 divergencias en 60 firmas, el 13.º rechazado con `reject_full`, cierre ordenado con guardado, y ese guardado carga en el servidor de Windows. La distribución no trae `libstdc++` ni `libgcc`: se cargaron las de Alpine 3.20 solo para la prueba. Destapó INC-35 e INC-36 | Correcto en un PC. Linux real (no WSL) y distribuciones con glibc: NO VERIFICADO |

## T-025: lo que puede y no puede probar `agent-desktop`

Sirve para menús, ajustes de la pestaña Gráficos, alojar, unirse y capturas de la ventana. No sirve para el juego en sí: dentro de la partida el árbol de accesibilidad solo expone la barra de título, y no hay teclas mantenidas, ratón relativo ni pulsaciones sin primer plano. Moverse, mirar, construir y abrir paneles con teclas sigue en [PENDIENTE_CONTROLES_REALES](PENDIENTE_CONTROLES_REALES.md).

**Fallos de las herramientas de prueba, corregidos:**

- `--position` se pasaba en coordenadas de Windows, pero Godot las cuenta desde la esquina del escritorio virtual. Además se elegía el monitor vertical. Medido en este equipo con 960×540: la ventana abría en x=-8, en el borde del monitor principal, no en el secundario. Las pruebas anteriores con ese tamaño pudieron abrir ahí, aunque los documentos dijeran «monitor secundario».
- Medido al abrir sin `-NoCapture` ni arreglo: el juego tomó el primer plano. Ahora `auto-test -NoCapture`, `coop-test` y `desktop-start` no lo toman. `host-test` y `auto-test` con captura **siguen tomándolo**.

## T-024: caudal de una línea con 8 apartaderos

Entregas de 200 unidades en 12 000 ticks (20 min de juego), con la mitad de los trenes en cada sentido. Simulación sin cliente, en un PC.

| Trenes | 1 | 2 | 4 | 8 | 16 | 40 |
| --- | --- | --- | --- | --- | --- | --- |
| Entregas | 22 | 42 | 75 | 76 | 40 | 25 |

Por encima de unos 8 trenes el caudal baja: más trenes esperan ante las señales, aunque ninguno queda atascado. Coste con 40 trenes y 30 tramos, compilación Release: media 0,09 ms por tick, peor tick 9,4 ms.

## T-023: tiempo de orden a resultado, en ms

Cada fila es un ensayo de 40 s con unas 450 órdenes. «>500» cuenta órdenes de más de 500 ms. La pérdida es en el relé, en un solo PC.

| Configuración | Red | p50 | p99 | Máx. | >500 | Ida p50 | Vuelta p50 | Reenvío p50 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Antes | 150/0 % | 278 | 343 | 345 | 0 | 156 | 110 | — |
| Antes | 150/3 % | 279 | 916 | 1156 | 39 | 157 | 110 | 416-418 |
| Antes | 150/3 % | 279 | 980 | 1514 | 34 | 156 | 110 | 409-421 |
| Antes | 150/3 % | 279 | 729 | 947 | 37 | 156 | 110 | 419-420 |
| Lectura cada 5 ms | 150/0 % | 233 | 268 | 281 | 0 | 123 | 110 | — |
| Lectura cada 5 ms | 150/3 % | 234 | 807 | 1074 | 40 | 123 | 110 | 420-422 |
| Lectura cada 5 ms | 150/3 % | 234 | 832 | 1225 | 39 | 123 | 110 | 418-436 |
| Lectura + reenvío 1,3 | 150/0 % | 231 | 265 | 281 | 0 | 122 | 109 | — |
| Lectura + reenvío 1,3 | 150/3 % | 231 | 541 | 623 | 11 | 122 | 109 | 264-266 |
| Lectura + reenvío 1,3 | 150/3 % | 232 | 556 | 1009 | 11 | 123 | 110 | 264-281 |
| Lectura + reenvío 1,3 | 150/3 % | 233 | 623 | 685 | 23 | 123 | 110 | 264-279 |

Lectura de los datos:

- **Sin pérdida no hay picos.** En los tres ensayos «Antes» con pérdida, las 110 órdenes de más de 500 ms tenían un datagrama fiable perdido a menos de 1 s en su sentido; ninguna quedó sin esa pérdida. Esa ventana es amplia: indica coincidencia, no prueba cada caso. La retención por orden no se midió paquete a paquete.
- **El reenvío coincide con la fórmula de la 2.1.5:** `25 + RTT×2,1`, unos 420 ms con 190 ms de RTT. Con 1,3 pasa a unos 265 ms, también la fórmula. Un ping perdido sigue contando como muestra igual a su intervalo: confirmado en `LiteNetPeer` de la 2.1.5.
- **Leer la red solo por tick** añadía de media 33 ms a la ida en cualquier red, con o sin pérdida.
- **Coste:** sin pérdida, los datagramas fiables no aumentan (3044 con 2,1 y 2929 con 1,3). El contador de reenvíos innecesarios de `trace-analyze` da cifras parecidas (unas 800) en todos los ensayos, también con 0 % y con cualquier multiplicador: no distingue copias reales y no sirve como medida.
- **Con redes con mucho jitter** un reenvío más temprano puede duplicar paquetes sin necesidad; no se ha medido.

## Errores que estas pruebas encontraron

- Caída real del servidor al terminar dos jugadores el mismo escombro (**también en 0.1.0**). Probado rojo → verde.
- Los motivos de rechazo no llegaban: el cierre inmediato los descartaba.
- Las filas de latencia iniciales eran **inválidas**: la simulación de LiteNetLib no actúa en release. Sustituidas por relé UDP.
- El `delta` de Godot está suavizado: las primeras cifras de frame eran falsas. Sustituido por reloj real.
- Picos de 180 ms atribuidos primero a shaders: eran mis propias capturas PNG. Precalentamiento retirado.
- Picos de 55 ms reales: regeneración masiva de `Label3D`. Eliminados.
- Ciclo de reinicio del servidor sin guardar por fallo: añadido guardado de emergencia sin pisar el válido.

## Sin verificar

**Una persona jugando** · **todo el audio: generado y reproducido sin errores, pero nadie lo ha escuchado** · dos PC reales · Internet · Steam entre cuentas y relays · Linux fuera de WSL2 · ultrawide a pantalla completa · instalación limpia en otro equipo · causa de BUG-001.
