# Revisión independiente E-8: rendimiento y calidad con cintas cargadas

Revisión del 2 de noviembre de 2026, de 12:38 a 12:53. Sin cambios en código, contenido ni scripts. Evidencias en `artifacts/qa/0.5-e8/`.

## Veredicto

**Cumple el objetivo en sentido estricto, pero no demuestra lo que se quería demostrar.**

- **Cumple:** escenario medio, cliente único, 1920×1080 reales, Radeon RX 7800 XT, tres tiradas, 34 703 frames. Peor vista: media 5,58 ms (objetivo ≤ 16,7) y p99 11,80 ms (objetivo ≤ 33,3). Ningún frame por encima de 33 ms.
- **No demuestra rendimiento con cintas cargadas a la vista.** Ninguna de las capturas `measure_*.png` encuadra una sola cinta con carga, y el registro no anota cuántos objetos se dibujan. Las cintas del escenario medio son además muy cortas (2 celdas casi todas). El peor caso, una cinta larga saturada delante de la cámara: **NO VERIFICADO**.
- **Posible regresión de tiempo** frente a la última tabla: +12 a +41 % en media y +27 a +109 % en p99, con casi los mismos objetos y dibujos. Sin atribuir: otras condiciones de equipo.
- **Estrés (cota superior):** no cumple en la vista de vías. Media 25,5-27,8 ms, p99 37,5-45,2 ms. El proyecto no promete soportarlo.
- **Defecto real encontrado:** `BeltView.cs:702-708` rehace cada frame la reconciliación de todas las cintas lejanas, justo lo que su comentario dice evitar.

La línea de `docs/PERFORMANCE.md` «Cliente con cintas: NO VERIFICADO» puede pasar a «mundo medio medido, sin cintas cargadas en el encuadre». No debería pasar a verificado sin más.

## Condiciones

| Dato | Valor |
| --- | --- |
| Build | `builds/alpha-dev`. `VERSION.txt` declara `commit 709e7ea (con cambios sin confirmar)`, exportada a las 12:33:41 |
| HEAD del repositorio | `b41255d`, confirmado a las 12:36:03. Ver hallazgo H-12 |
| Resolución real | 1920×1080: registro `ventana (1920, 1080)`, capturas de 1920×1080 y área de cliente 1920×1080 leída de Windows |
| Monitor | Principal de 3440×1440, ventana en (752,125). No en el secundario: ver H-11 |
| Equipo | Ryzen 7 5700X (16 hilos), 32 GB, RX 7800 XT, driver 32.0.32015.7029, Windows 11 Pro 26200, tres monitores |
| Renderizador | Forward+. Perfil nuevo `teste8`, ajustes por defecto, sin registro del preset. VSync desactivada y FPS sin límite por la propia medición |
| Corría a la vez | Equipo en uso por el titular: VS Code, Chrome, Bambu Studio, Discord, iCloud, varios `claude`, `VBCSCompiler`. CPU total del sistema entre 4 y 36 %. Durante la revisión apareció modificado `tests/Pluton.Tests/FirstThirtyMinutesTests.cs`, ajeno a esta revisión: había otra actividad de desarrollo. Las cifras pueden empeorar por ello |
| Otra instancia de Pluton | Ninguna antes de cada tirada, comprobado con `Get-Process` |
| Reinicios del driver | Ninguno entre las 12:37 y el final (eventos 141 y 117). Hoy hubo cinco eventos 117 de 09:53 a 09:55, antes de medir y sin relación con estas tiradas |
| Memoria | Ningún proceso muerto por falta de memoria |

Comando: `tools\auto-test.ps1 -Build dev -Profile teste8 -Measure <escenario> -Width 1920 -Height 1080 -NoCapture -Timeout 400 -Seconds 15`. Se usó `-Seconds 15` para comparar con la última tabla; el valor por defecto del script es 10. `e8-stress-2` usó 40 s para cruzar el autoguardado.

## Medidas

### Escenario medio: 511 máquinas, 25 trenes, 2050 tramos

Ms de frame, 15 s por vista. Cámara quieta.

| Vista | Tirada | Frames | Media | p95 | p99 | Máx. | >33 ms | Objetos | Dibujos | Memoria | VRAM |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Densa | 1 | 5024 | 2,98 | 4,08 | 5,45 | 20,24 | 0 | 2466 | 958 | 740 MB | 397 MB |
| Densa | 2 | 5533 | 2,71 | 3,68 | 5,68 | 29,15 | 0 | 2468 | 960 | 749 MB | 397 MB |
| Densa | 3 | 4857 | 3,09 | 4,32 | 5,83 | 23,57 | 0 | 2468 | 960 | 753 MB | 397 MB |
| Vías | 1 | 2691 | 5,58 | 8,68 | 11,80 | 23,47 | 0 | 4584 | 3717 | 739 MB | 397 MB |
| Vías | 2 | 2939 | 5,10 | 7,40 | 10,06 | 16,82 | 0 | 4582 | 3716 | 757 MB | 397 MB |
| Vías | 3 | 2827 | 5,31 | 7,06 | 8,83 | 16,12 | 0 | 4582 | 3716 | 752 MB | 397 MB |
| Panorámica | 1 | 3690 | 4,07 | 5,85 | 9,20 | 32,21 | 0 | 3962 | 1874 | 744 MB | 398 MB |
| Panorámica | 2 | 3853 | 3,90 | 6,28 | 9,75 | 22,12 | 0 | 3962 | 1874 | 758 MB | 398 MB |
| Panorámica | 3 | 3289 | 4,56 | 7,10 | 10,40 | 25,31 | 0 | 3962 | 1874 | 753 MB | 398 MB |

Con tres tiradas no se calcula un intervalo de confianza creíble: se dan los valores tal cual. El máximo de 32,21 ms queda a 1,1 ms del límite de 33,3.

| Vista | Tirada | gen0 | gen1 | gen2 | Pausa total GC | Última pausa | SyncWorld máx. | Poll máx. | Deltas estructurales |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Densa | 1 / 2 / 3 | 20 / 22 / 19 | 20 / 21 / 19 | 0 / 1 / 0 | 216,5 / 248,5 / 215,5 ms | 12,1 / 10,7 / 13,5 ms | 13,8 / 10,7 / 15,2 ms | 0,8 / 0,7 / 0,8 ms | 0 |
| Vías | 1 / 2 / 3 | 13 / 13 / 12 | 11 / 11 / 10 | 1 / 0 / 0 | 72,8 / 79,0 / 69,7 ms | 5,6 / 6,3 / 5,7 ms | 12,2 / 4,9 / 2,2 ms | 0,9 ms | 0 |
| Panorámica | 1 / 2 / 3 | 15 / 17 / 14 | 13 / 15 / 11 | 0 | 107,4 / 118,9 / 108,1 ms | 6,7 / 6,7 / 6,5 ms | 9,1 / 5,8 / 8,7 ms | 12,3 / 12,1 / 14,2 ms | 1 |

Servidor local: tick medio 0,43-0,46 ms, p99 2,31-2,38, máx. 16,06-22,81, 0 tardíos, 0 divergencias.

### Estrés (cota superior): 2011 máquinas, 100 trenes, 10 200 tramos

| Vista | Tirada | Frames | Media | p95 | p99 | Máx. | >33 ms | >100 ms | Objetos | Dibujos | Memoria | VRAM | gen0/1/2 | Pausa GC | Poll máx. |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Densa | 15 s | 1562 | 9,60 | 12,85 | 17,88 | 43,84 | 3 | 0 | 7653 | 1733 | 970 MB | 816 MB | 35/16/1 | 131,5 ms | 7,1 ms |
| Densa | 40 s | 3680 | 10,87 | 15,29 | 21,37 | 97,62 | 8 | 0 | 7662 | 1732 | 984 MB | 814 MB | 83/40/3 | 388,8 ms | 68,2 ms |
| **Vías** | 15 s | 588 | **25,51** | 29,07 | **37,50** | 45,07 | 13 | 0 | 13 463 | 11 900 | 970 MB | 816 MB | 17/6/1 | 47,5 ms | 6,2 ms |
| **Vías** | 40 s | 1437 | **27,84** | 33,61 | **45,18** | 105,31 | 80 | 1 | 13 712 | 11 934 | 993 MB | 814 MB | 42/13/2 | 143,6 ms | 59,3 ms |
| Panorámica | 15 s | 1471 | 10,20 | 13,28 | 16,73 | 85,64 | 1 | 0 | 6957 | 2438 | 986 MB | 816 MB | 34/11/2 | 98,8 ms | 62,9 ms |
| Panorámica | 40 s | 2799 | 14,30 | 20,25 | 27,84 | 76,21 | 9 | 0 | 6966 | 2443 | 994 MB | 814 MB | 68/32/3 | 256,9 ms | 58,4 ms |

Vías incumple media y p99. Densa y panorámica cumplen en media y p99, con picos de hasta 98 ms. Servidor local: tick medio 1,43-1,51 ms, p99 6,15-6,17, máx. 31-32 ms, 0 tardíos, 0 divergencias.

**Autoguardado durante carga:** una muestra. `autotest-stress.plsave.bak` (262 KB) lleva la hora 12:49:34, t≈121 s de `e8-stress-2`. Ningún frame superó 33 ms entre 119,97 s y 125,97 s. Con un solo cliente local. Con 12 clientes o en un dedicado: **NO VERIFICADO**.

### `long`: 76 máquinas, 30 cintas de 168 celdas, casi sin carga en el encuadre

Una tirada, 15 s por vista.

| Vista | Frames | Media | p95 | p99 | Máx. | >33 ms | Objetos | Dibujos | Memoria | VRAM | gen0/1/2 | Pausa GC | Última pausa |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Densa | 7827 | 1,92 | 3,19 | 4,30 | 76,17 | 2 | 758 | 166 | 681 MB | 386 MB | 7/7/2 | 193,8 ms | 27,5 ms |
| Vías | 4822 | 3,11 | 5,09 | 6,62 | 38,21 | 4 | 1963 | 1285 | 688 MB | 386 MB | 4/4/0 | 131,4 ms | 33,4 ms |
| Panorámica | 6663 | 2,25 | 3,39 | 4,08 | 38,42 | 4 | 1162 | 501 | 693 MB | 386 MB | 5/5/0 | 163,2 ms | 33,2 ms |

### Dónde está el límite

Muestreo externo cada 8 s en `artifacts/qa/0.5-e8/threads-*.txt` y cada 5 s en `sampler-*.csv`:

| Escenario | Hilo principal | Motor 3D de la GPU |
| --- | --- | --- |
| Medio | 94,7-97,9 % de un núcleo | 28-72 % |
| Estrés, vías | 98,3 % de un núcleo | 22-23 % |

El tope es un único hilo de CPU, no la GPU. Sin límite de FPS un hilo tiende a saturarse siempre; lo significativo es que en estrés, a 25 ms por frame, la GPU sigue al 22 %. El muestreador de GPU no se ejecutó sobre `e8-medium-3` ni `e8-stress-2`.

## Comparación con `docs/PERFORMANCE.md`

Contra «Zona inicial, Después»: mismo equipo, misma herramienta y 15 s por vista. Hoy, media de las tres tiradas.

| Vista | Media | p99 | Máx. | Objetos | Dibujos | Memoria | VRAM |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Densa | 2,61 → 2,93 (+12 %) | 4,45 → 5,65 (+27 %) | 17,20 → 24,3 | 2417 → 2467 | 944 → 959 | 731 → ~747 MB | 396 → 397 MB |
| Vías | 3,98 → 5,33 (+34 %) | 6,13 → 10,23 (+67 %) | 15,38 → 18,8 | 4539 → 4583 | 3704 → 3716 | 735 → ~749 MB | 396 → 397 MB |
| Panorámica | 2,96 → 4,18 (+41 %) | 4,67 → 9,78 (+109 %) | 15,53 → 26,5 | 3921 → 3962 | 1862 → 1874 | 735 → ~752 MB | 396 → 398 MB |

**Lectura.** Los objetos y dibujos casi no cambian, así que el aumento no viene de dibujar más. La tabla anterior es una sola tirada, y ahí «seguían otros procesos de desarrollo»; hoy el titular usa el equipo y la ventana está en otro monitor. **No se puede decir si es regresión del juego o ruido del equipo.** El p99 de la panorámica duplicado merece comprobarse. Ver H-5.

Contra tablas más antiguas: los dibujos de la vista densa pasaron de 362 (Arte) a 934 (Zona, Antes). Ya constaba en el documento y no es nuevo de hoy. Estrés de cliente: no hay tabla previa.

## Hallazgos por gravedad

### Alta

**H-1. La medición no mide lo que dice medir.** `AutoTest.cs:794-796` fija tres vistas que no apuntan a cintas. En `medium_measure_denso.png` se ve solo el extremo de una cinta vacía tras la herramienta y el rótulo (`recorte_medium_denso_cinta.png`). En `medium_measure_vias.png` no hay ninguna. En `medium_measure_panoramica.png` quedan a lo lejos. Tampoco se registra `DrawnItems`: `AutoTest.cs:816` anota `existencias`, no objetos en cinta. El mundo medio tiene unas 275 cintas y 725 celdas (cálculo sobre `Scenarios.cs:115-143`), 2 celdas casi todas.
`long` tampoco sirve hoy: con las mejoras activas (`link_rate`, 6,67 huecos/s) la primera carga tarda, por cálculo, unos 50 s en llegar a x=0 y unos 100 s al final, y la medición acaba a los 68 s. La captura de vías es de t≈45 s, antes de que la carga alcance al jugador: `long_measure_vias.png` muestra cintas vacías e «Ingresos/min: 0».
**Corrección:** añadir a `MeasureScenario` una cuarta vista «cintas» que mire a lo largo de una fila saturada; registrar `DrawnItems` y la carga total; esperar en `long` a que la carga llegue; y crear un escenario de cintas largas saturadas, unos 10 000 objetos.

**H-2. Las cintas lejanas se reconcilian cada frame.** En `BeltView.cs:702` se llama a `Reconcile` si `Version != SlotsVersion`, y en `:707` la rama lejana pone `Version = -1`. Por tanto, cada cinta a más de 120 m repite en cada frame `Reconcile`, con su `new VisualItem[Slots.Length]` (`:641`) y su `BeltStatusOf` (`:637`). El comentario de `:705` dice lo contrario. Afecta también al escenario medio: la rejilla llega a más de 120 m de las tres posiciones.
Evidencia: lectura del código, concluyente. Coherente con frames por recolección gen0: ~1200 en `long`, donde nada queda lejos; ~230 en medio; ~40 en estrés. Esa correlación no es prueba aislada y el coste en ms **NO se ha medido por separado**.
**Corrección:** comprobar la distancia antes de reconciliar. Mover la rama `distance > ItemRange` por delante de `:702`. Y en `Reconcile`, reutilizar un búfer en lugar de reservar `result`.

### Media

**H-3. Los dibujos de vías son el límite de frame, y no las cintas.** `GameView.cs:580` crea un `MeshInstance3D` por tramo, sin `VisibilityRangeEnd` y con sombra por defecto. Estrés en vías: 11 900 dibujos, 25-28 ms, hilo principal 98 %, GPU 22 %. Comparando vistas de una misma tirada salen ~0,9 µs por dibujo en medio, ~1,1 en `long` y ~1,6 en estrés; es una estimación entre vistas distintas, no una medida aislada.
**Corrección:** agrupar tramos rectos por zona en una malla o `MultiMesh`, y dar distancia de dibujo a los tramos.

**H-4. Tirón por cada cambio estructural.** Cualquier delta estructural llama a `world.Reindex()` (`GameClient.cs:322`) y rehace la clave de todas las cintas en un `StringBuilder` (`BeltView.cs:201`, `:243-252`). `Poll máx.`: 12-14 ms en medio, 58-68 ms en estrés. Frames resultantes en estrés: 98 ms (25,91 s), 86 ms (55,30 s), 105 ms (85,55 s), 76 ms (115,02 s). Periodo de 30 s, igual que `scrapRenewal.intervalTicks: 300` en `content/balance.json:22`; que sea esa la causa es inferencia por periodo. Otro tanto se espera al construir o desmontar, algo frecuente con 12 jugadores: **NO VERIFICADO**.
**Corrección:** reindexar por incrementos y comparar solo las cintas del delta. La renovación de chatarra no debería marcar cambio estructural de máquinas ni de cintas.

**H-5. Posible regresión de tiempo sin atribuir.** Ver comparación. **Corrección:** medir en la misma sesión y con el mismo método `builds/alpha-dev` y la última entrega anterior a B-8, alternando tres tiradas de cada una. Es la única forma de separar juego y equipo. Fuera de este encargo: solo se autorizó `alpha-dev`.

**H-6. `SyncWorld` recorre todo con cada delta.** Se marca sucio con cada delta recibido (`GameView.cs:241`), hasta 5 veces por segundo con `FlushEveryTicks = 2`. Crea una cadena por máquina (`:510`), varios `HashSet` y recorre chatarra, tramos y trenes. Máximo 15,2 ms en medio y 29,6 ms en estrés. En `e8-stress-2`, vías, muchos picos caen en `x,50 s`: un pulso de 1 Hz que añade de 10 a 25 ms sobre una base de 25 y cruza los 33 ms. Coincide con `SimFlushEveryTicks = 10`; atribuirlo a esto es inferencia, sin perfilador. **Corrección:** marcar sucio solo lo cambiado; comparar con enteros en vez de `$"{m.Def}|{m.X}|{m.Z}|{m.Rot}"`.

**H-7. Pausas de GC que por sí solas agotan el objetivo de p99.** En `long` cada pausa es de 27-33 ms y hay frames de 36-38 ms al ritmo de las recolecciones. Es el mundo más ligero y el de pausas más largas. En medio son de 5-14 ms, y casi todas las gen0 escalan a gen1. Candidata: `node.SetInstanceShaderParameter("offset", …)` (`BeltView.cs:724`), por tramo a menos de 90 m y por frame; en Godot C# la conversión de `string` a `StringName` crea un objeto con finalizador en cada llamada, y eso explicaría la promoción a gen1. Hipótesis sin perfilador: **NO VERIFICADO**. El GC se midió sin límite de FPS: a 60 fps habrá menos recolecciones, y no se puede afirmar si serán más cortas o más largas.
**Corrección:** `static readonly StringName`, y mejor aún animar la banda en el shader con `TIME` y una velocidad por instancia que solo cambie al atascarse. Quita además esa llamada por tramo y frame.

**H-8. Carga sin recorte, con sombra y subida entera cada frame.** `BeltView.cs:627`: `CustomAabb` de 4000×200×4000 m, de modo que el lote nunca se recorta ni en cámara ni en sombras, y sombra activa por defecto: todos los objetos a menos de 120 m se dibujan también detrás del jugador y en cada partición de sombra. `:737`: `b.Multi.Buffer = b.Buffer` copia la capacidad entera, no lo usado, cada frame, aunque nada se mueva. Coste **NO medido**, porque ninguna medición carga el lote de verdad (H-1).
**Corrección:** quitar la sombra a partir de unos 30 m; descartar en `Place` lo que quede detrás de la cámara; subir solo si hay cambios.

### Calidad visual

Solo capturas fijas. **Parpadeos y z-fighting en movimiento: NO VERIFICADO**; ni una captura permite juzgarlos.

**H-9 (media). Muaré en la banda a ángulo rasante.** `recorte_long_vias_cintas.png`, de `long_measure_vias.png`: la misma cinta muestra rayas limpias a la derecha y un punteado a la izquierda. Los tacos se calculan en el shader sin filtrar (`BeltView.cs:46-47`, `fract(along * 8.0)`), con lo que no les afectan los mipmaps. Previsible centelleo al caminar, sin comprobar. **Corrección:** atenuar los tacos con `fwidth(along)` cuando su periodo baje de unos 2 píxeles.

**H-10 (media). Blancos quemados con borde duro.**
- `recorte_medium_denso_blanco.png`, de `medium_measure_denso.png`, también en `stress_measure_denso.png`: manchas blancas saturadas en la plataforma de la ensambladora. Parecen errores, no luz.
- `recorte_long_denso_brillo.png` y `recorte_medium_pano_centro.png`: reflejo solar saturado en el horizonte y en una cresta, que se lee como un segundo sol junto al real. Se ve en `long_measure_denso.png`, `medium_measure_panoramica.png` y `stress_measure_panoramica.png`.
- Las lámparas de estado se ven como bolas luminosas mayores que su pieza (`medium_measure_denso.png`, hacia (250,585) y (1570,390)).

Causa no investigada. **Corrección propuesta:** limitar el especular de esos materiales y el umbral del brillo, y revisar el foco de trabajo.

**Menores:**
- Rayos rojos de estado y puntos ámbar sobre las máquinas (`stress_measure_panoramica.png`, `medium_measure_denso.png`): cumplen su función, pero flotan grandes y lejos de su máquina. Con la energía oscilando entre 606 y 1974 sobre 1930 en el mundo medio, los avisos aparecen y desaparecen en bloque.
- Rótulos «En ruta 35/200» grandes y solapados en fila (`stress_measure_vias.png`).
- Una cinta atascada apenas se distingue sin apuntarla: chapas grises sobre banda gris y flechas rojas tapadas por la carga (`belt_06_atasco.png`).

**Lo que está bien:**
- `belt_05_carga_densa.png`: 104 de 104 huecos, casi todos lingotes, a intervalo regular. Se leen de cerca y hasta el fondo de la cinta, y siguen la esquina. Sin solapes visibles.
- `belt_06_atasco.png`: las pilas de chapas son reconocibles.
- `belt_04_cruce.png`: el puente se lee claro.
- No se aprecian pilas absurdas, z-fighting ni objetos flotando en cintas en ninguna captura.

Estas tres imágenes son de `--scenario belts`, con 3 cintas: **no prueban nada a escala.**

### Herramienta y proceso

**H-11 (media). `-NoCapture` a 1080 de alto abre la ventana en el monitor principal.** `lib-ventana.ps1:19` exige `WorkingArea.Height >= Height`. El secundario da 1032, así que avisa `No secondary monitor fits the window` y deja la posición por defecto: (752,125)-(2688,1244) en el monitor principal, de 3440×1440. Sin quitar foco ni ratón, sí tapó el centro de la pantalla del titular durante las 7 tiradas (unos 11 min). Es lo contrario de lo que promete el encabezado del script. **Corrección:** con `-NoCapture`, aceptar `Bounds` del secundario y no la zona útil, o avisar antes.

**H-12 (baja). La build no se corresponde exactamente con `b41255d`.** `VERSION.txt` dice `709e7ea (con cambios sin confirmar)` y se exportó 2 min 22 s antes de ese commit. La diferencia entre ambos son 9 líneas de interfaz (`GameView.cs`, `Hotbar.cs`, `Ui.cs`), sin relación con el dibujo. Que esos cambios sin confirmar sean exactamente esos: **NO VERIFICADO**. **Corrección:** reexportar antes de la próxima medición de referencia.

**H-13 (baja).**
- `-NoCapture` escribe `override.cfg` en `builds/alpha-dev/client` unos segundos. Se comprobó después de las tiradas que no quedó. Si dos scripts se solapan, uno se lo puede borrar al otro.
- Aviso `ObjectDB instances were leaked at exit` en `e8-medium-2` (12) y `e8-belts` (8), no en todas las tiradas. Sin investigar.

## Coste de cintas y carga: resumen de código

| Concepto | Dónde | Coste | Estado |
| --- | --- | --- | --- |
| Geometría | `BeltView.cs:499-610` | 162 cuadriláteros (324 triángulos) por celda, sin LOD: medio ~280 mil triángulos, `long` ~1,6 millones, antes de sombras | Calculado |
| Dibujos | `BeltView.cs:278-291` | 2 superficies por tramo de 16 celdas, y más por pasadas de sombra | Medido en total |
| Animación | `BeltView.cs:722-724` | 1 llamada al motor por tramo y frame a menos de 90 m | Leído |
| Lejanas | `BeltView.cs:702-708` | 1 reconciliación con reserva por cinta y frame | Leído (H-2) |
| Carga | `BeltView.cs:627`, `:737` | Sin recorte, con sombra y copia entera por frame | Leído, sin medir |

**Primer punto a optimizar en cintas: `BeltView.cs:702-708`.** Es un defecto, se arregla en pocas líneas y escala con el tamaño de la fábrica. Luego `:724` (H-7) y `:627` (H-8). **Primer punto para el frame completo: `GameView.cs:580`, vías (H-3).** No urge en el mundo medio con este equipo; en uno modesto puede ser otra historia.

## Separación por nivel

| Nivel | Resultado |
| --- | --- |
| **Lógica** | No ejecutada en esta revisión: `dotnet test` queda fuera. `Measurement_belts_are_valid_and_carry_real_cargo` existe, pero aquí **NO VERIFICADO** |
| **Integración** | 7 tiradas del cliente exportado con `exit=0` y `RESULTADO: CORRECTO`, 0 divergencias, sin reinicios del driver. `--scenario belts` correcto, sin captura de ratón |
| **Carga** | Tablas de arriba. Cliente único con cámara quieta y ventana sin foco. Sin cintas cargadas en el encuadre. **Anfitrión + 11 clientes con cintas: NO VERIFICADO** |
| **Uso real** | **NO VERIFICADO.** Sin entradas reales, sin caminar ni girar durante la medición |

**NO VERIFICADO, explícitamente:**
- Equipo modesto: GPU integrada o de gama baja, menos núcleos. Solo hay datos de esta RX 7800 XT.
- Sesión con entradas reales: mover, girar, construir mientras se mide.
- Varios PC o red real. Cooperativo con cintas a 12 en cliente.
- Cinta larga saturada delante de la cámara.
- Presets bajo y alto, VSync o límite de 60 fps, GC a 60 fps.
- Parpadeos y z-fighting en movimiento.
- Coste aislado de cada punto de código: no se usó perfilador.
- Linux.

## Seguimiento tras la revisión (mismo día, sin revisor independiente)

Hecho por el desarrollador, no por el revisor. El dictamen de arriba no se ha reescrito.

- **H-1:** escenario `saturated` y vista `cintas`; el registro anota carga y objetos dibujados.
- **H-2:** corregido; una cinta lejana ya no se reconcilia. Coste aislado sin medir.
- **H-5:** A/B alternando `dev` y `0.5.0-qa5` (abajo). Sin regresión de la qa5 a hoy. La diferencia con «Zona, Después» queda sin atribuir: esa tabla es anterior a la qa5 y no se ha medido una build tan antigua.
- **H-7:** parámetros con `StringName` fijo. Efecto en el GC sin aislar.
- **H-10:** parcial. Mismo encuadre antes y después: el hielo se quema menos, pero queda brillo a contraluz.
- **H-11:** corregido; el filtro usa el tamaño total del monitor.
- **H-4:** corregido para escombros: cortar o renovar chatarra ya no reindexa el mundo. Además, `Reindex` deja de ser cuadrático entre nodos y máquinas. Construir y desmontar siguen reindexando y comparando todas las cintas: abierto en ese caso.
- **H-9:** los tacos se apagan al bajar de 2 píxeles. Mejora vista en una captura fija; en movimiento, **NO VERIFICADO**.
- **Abiertos:** H-3, H-6, H-8, parte de H-4 y H-10, y los menores.

Estrés, después de H-4, una sola tirada de 40 s, comparada con `e8-stress-2`, sin alternar builds. Puede influir el estado del equipo; la caída de `Poll` y los deltas estructurales a 0 sí se explican por el cambio:

| Vista | Media | p99 | Máx. | >33 ms | Poll máx. | Pausa GC |
| --- | --- | --- | --- | --- | --- | --- |
| Densa | 10,87 → 10,17 | 21,37 → 18,40 | 97,62 → 55,62 | 8 → 6 | 68,2 → 7,4 ms | 388,8 → 129,3 ms |
| Vías | 27,84 → 25,58 | 45,18 → 43,88 | 105,31 → 59,74 | 80 → 35 | 59,3 → 4,8 ms | 143,6 → 71,4 ms |
| Panorámica | 14,30 → 10,31 | 27,84 → 18,01 | 76,21 → 38,06 | 9 → 1 | 58,4 → 4,0 ms | 256,9 → 100,2 ms |

Vías sigue sin cumplir en estrés: el límite son sus 11 900 dibujos (H-3).

Mediana de 3 tiradas alternas, 1920×1080, 15 s por vista, escenario medio, equipo en uso:

| Vista | Build | Media (rango) | p99 (rango) |
| --- | --- | --- | --- |
| Densa | dev | 2,87 (2,78-2,88) | 5,24 (5,12-6,15) |
| Densa | qa5 | 3,06 (2,88-3,28) | 6,27 (5,88-7,66) |
| Vías | dev | 5,61 (5,23-5,73) | 9,18 (8,65-12,08) |
| Vías | qa5 | 5,70 (5,53-5,96) | 11,78 (9,82-11,80) |
| Panorámica | dev | 3,75 (3,62-4,25) | 6,38 (5,98-8,56) |
| Panorámica | qa5 | 3,78 (3,73-4,62) | 8,02 (7,64-9,15) |

Los rangos se solapan: `dev` no es peor. No demuestra que los arreglos mejoren.

Cintas saturadas (`saturated`, una tirada, `dev` antes del cambio del hielo): 30 cintas de 168 celdas, 20 160 objetos.

| Vista | Dibujados | Media | p99 | Máx. | >33 ms | Dibujos |
| --- | --- | --- | --- | --- | --- | --- |
| Densa | 4478 | 2,17 | 3,81 | 13,82 | 0 | 165 |
| Vías | 9368 | 3,90 | 6,27 | 32,82 | 0 | 1274 |
| Panorámica | 7498 | 3,02 | 5,15 | 24,43 | 0 | 502 |
| Cintas | 12 902 | 3,95 | 6,15 | 20,68 | 0 | 1064 |

Cumple el objetivo en este equipo, con cámara quieta. Equipo modesto, cámara en movimiento y anfitrión con 11 clientes: **NO VERIFICADO**.

## Evidencias

En `artifacts/qa/0.5-e8/`: registros `e8-*.log`; muestreos `sampler-*.csv` y `threads-*.txt`; capturas `medium_*`, `long_*`, `stress_*`, `belt_*`; recortes `recorte_*`. En `sampler-e8-medium-1.csv` la columna de CPU es 0 por un contador mal localizado, corregido en las tiradas siguientes, y la ventana solo se pudo leer en la primera muestra.

Trazas completas en `logs/e8-*`, no versionadas.

Muestreadores temporales en `%TEMP%`, fuera del repositorio.
