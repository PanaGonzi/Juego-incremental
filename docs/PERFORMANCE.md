# Rendimiento

**Equipo de referencia:** Ryzen 7 5700X (8C/16T), 32 GB, Radeon RX 7800 XT (driver 32.0.32015.7029), Windows 11 Pro 26200, monitor de 180 Hz, Forward+ (Vulkan). Build exportada; reloj real entre `_Process`, capturas excluidas.

**Objetivo del escenario medio:** media ≤ 16,7 ms y p99 ≤ 33,3 ms a 1080p.

## Cliente, escenario medio (508 máquinas, 25 trenes, 2050 tramos) a 1080p sin VSync, 15-20 s por vista

Commit posterior a 057afc5. Cerca del jugador se dibuja todo el planeta: sin LOD aún.

| Situación | Vista | Media | p99 | Máx. | >33 ms | Objetos | Dibujos | Memoria | VRAM |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Solo | Densa | 1,45 ms | 2,05 | 15,36 | 0 | 2137 | 838 | 869 MB | 340 MB |
| Solo | Vías | 5,69 ms | 9,69 | 13,02 | 0 | 7637 | 6626 | 871 MB | 341 MB |
| Solo | Panorámica | 2,58 ms | 3,80 | 8,79 | 0 | 5423 | 1990 | 873 MB | 341 MB |
| Anfitrión + 11 | Densa | 1,46 ms | 2,11 | 14,26 | 0 | 2546 | 850 | 723 MB | 342 MB |
| Anfitrión + 11 | Vías | 5,77 ms | 9,76 | 16,39 | 0 | 7777 | 6608 | 723 MB | 343 MB |
| Anfitrión + 11 | Panorámica | 2,66 ms | 4,03 | 11,06 | 0 | 6153 | 2078 | 724 MB | 343 MB |

**Cumple el objetivo en este equipo.** Los 6626 dibujos en vías son el primer punto a optimizar (cada tramo es una malla propia); no urge con estos tiempos. Equipos de gama menor: **NO VERIFICADO**.

## Arte de la 0.5, escenario medio a 1080p sin VSync, 15 s por vista

Rama `agente/arte`, preset medio, un solo cliente. Otros agentes usaban el equipo a la vez: las cifras pueden empeorar por ello, no mejorar.

| Vista | Media | p99 | Máx. | >33 ms | Objetos | Dibujos | Memoria | VRAM |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Densa | 1,96 ms | 2,78 | 12,91 | 0 | 1194 | 362 | 626 MB | 362 MB |
| Vías | 3,79 ms | 6,16 | 12,05 | 0 | 4073 | 3545 | 620 MB | 362 MB |
| Panorámica | 2,52 ms | 5,19 | 14,48 | 0 | 3048 | 1203 | 620 MB | 362 MB |

**Cumple el objetivo en este equipo.** Dibujos frente a la tabla anterior: densa 838 → 362, vías 6626 → 3545, panorámica 1990 → 1203, con una superficie opaca compartida por malla. La vista densa sube de 1,45 a 1,96 ms; vías baja de 5,69 a 3,79 ms. Una tirada anterior de la misma rama dio 7,80 ms de media y un pico aislado de 112 ms en vías, con el escritorio ocupado; no se reprodujo en las dos siguientes. Equipos de gama menor, presets bajo y alto, y anfitrión con 11 clientes: **NO VERIFICADO**.

## Servidor dedicado + 12 clientes

| Escenario | Tick medio | p99 máx. | Tardíos | Envío total antes → después | Por cliente | Memoria |
| --- | --- | --- | --- | --- | --- | --- |
| Mundo normal | 0,45 ms | 16,5 | 0 | — → 325 kbit/s | 27 kbit/s | 47 MB |
| Medio | 0,65 ms | 18,5 | 0 | 3145 → 810 kbit/s | 68 kbit/s | 57 MB |
| Estrés (2008 máquinas, 100 trenes, 10200 tramos) | 1,45 ms | 34,1 | 0 | 13366 → 3042 kbit/s | 254 kbit/s | 72 MB |

Tick presupuestado: 100 ms. Las reducciones: compresión compartida, ruta del tren solo al cambiar, estado global y producción a 1 Hz. El estrés solo mide servidor y red, no se promete su soporte.

Anfitrión + 11 en el medio: tick 0,40 ms, p99 3,45, tardíos 0, subida ~660 kbit/s.

## Cintas con carga real (0.5, en desarrollo)

`tools/net-test.ps1`, servidor dedicado + 12 clientes, 60 s (estrés 40 s). Medias con los 12 conectados, sin la primera muestra (reparto de instantáneas). Mismo equipo, con otros procesos de desarrollo a la vez: el tick es orientativo.

| Escenario | Tick medio | p99 máx. | Tardíos | Envío total | Por cliente | Memoria | Divergencias |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Medio, enlaces antiguos (53e1df0) | 0,357 ms | 4,36 | 0 | 702 kbit/s | 58 kbit/s | 58 MB | 0 |
| Medio, cintas (511 máquinas) | 0,358 ms | 4,61 | 0 | 776 kbit/s | 65 kbit/s | 58 MB | 0 |
| Estrés, cintas (2011 máquinas) | 1,056 ms | 9,74 | 0 | 2019 kbit/s | 168 kbit/s | 75 MB | 0 |
| Largo: 30 cintas de 168 celdas, carga escasa (76 máquinas) | 0,201 ms | 2,28 | 0 | 348 kbit/s | 29 kbit/s | 49 MB | 0 |

Los mundos de medición llevan cintas rectas con carga y una línea saturada de cada tres grupos; `Measurement_belts_are_valid_and_carry_real_cargo` comprueba que al menos el 90 % de las cintas mueve carga. Antes y después no son el mismo mundo: el de cintas tiene más máquinas y otra disposición.

**Trazado, medido en `BeltRouteTests` (Release, con otras pruebas en paralelo):**

- Ruta de 337 celdas en campo abierto: 843 expansiones, 4,5 ms. Antes de añadir la curva inevitable a la heurística eran 51 189 expansiones y 143 ms.
- Sin ruta posible, recorriendo todo el planeta: unos 151 500 expansiones, 405 ms repartidos en porciones. El juego reparte 2 ms por frame, así que tarda un momento e indica que está trazando; la porción más lenta medida fue de 7 ms con 300 expansiones.
- El servidor ya no busca al construir: validar una orden es lineal en su longitud. Solo busca en migración y `Move`, con 12 000 expansiones como máximo.
- Comando: ~1 byte por tramo recto; 300 celdas rectas caben en 16 bytes.
- 10 cintas de 168 celdas con entrada irregular: 246 B/s comprimidos por cliente, tick 0,025 ms.

**Cliente con cintas (`--measure medium`): NO VERIFICADO.** Cada cinta es una malla propia con dos superficies, y la carga, un `MultiMesh` por objeto.

## Ensayo sostenido

60 min, 12 clientes, 100 ms/2 %, medio, sobre 0.4.0: memoria 63→66 MB, tick 0,23-0,33 ms, p99 máx. 38 ms, 0 tardíos, envío medio 449-743 kbit/s, picos de hasta 10310 kbit/s al repartir instantáneas. Ver T-015. Ensayo anterior sobre 0.2.0: T-012.

## Viajes

Trayecto de 20 s a 1080p con VSync (180 Hz): media 5,56 ms, p99 ≤ 5,93, máx. ≤ 31 ms sin capturas, salvo un pico de 53 ms en el primer tramo; memoria estable en 8 tramos (~545 MB).

## Pendiente

Autosave durante carga · LOD y dibujos por instancias · frame con menos núcleos o GPU menor · Linux.
