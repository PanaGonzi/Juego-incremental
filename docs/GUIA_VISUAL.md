# Guía visual: industrial semirrealista

Vale para todo recurso nuevo. Implementación: `Materials.cs`, `ModelsBuilder.cs`, `ModelsCatalog.cs`, `Scenery.cs`, `UiTheme.cs`, `tools/TexGen`.

## Idea

Máquina cálida sobre mundo frío. El entorno es oscuro y desaturado; la maquinaria se lee por su pintura gris cálida, el acento naranja y las luces ámbar. Si algo importante no se distingue en una captura reducida, está mal aunque el material sea correcto.

## Referencias

Conocimiento general de imágenes públicas, **no consultadas en esta sesión** y sin copiar ningún recurso:

| Referencia | Qué se aprovecha |
| --- | --- |
| New Horizons, Sputnik Planitia (NASA) | Hielo en polígonos de convección con surcos oscuros; tolinas pardas como suelo |
| New Horizons, Caronte | Luna gris con casquete polar rojizo |
| Curiosity y Perseverance (NASA) | Marte: cielo ocre con neblina, suelo mineral, rocas estratificadas |
| Maquinaria minera y ferroviaria real | Torres de perforación cónicas, tolvas, chapa lagrimada, franjas de peligro |
| Satisfactory (citado por el titular) | Jerarquía: silueta legible y bocas de entrada y salida evidentes. Sin copiar su estilo ni piezas |

## Paleta

Valores lineales, en `Materials.cs`.

| Uso | Nombre | Color lineal |
| --- | --- | --- |
| Cuerpo de máquina | `hull`, `panel` | 0,27 · 0,27 · 0,26 y 0,42 · 0,42 · 0,40 |
| Señalización | `signal`, `stripe` | Naranja 0,62 · 0,17 · 0,025; franja negra y amarilla |
| Claro y frío de contraste | `cream`, `navy` | 0,52 · 0,49 · 0,42 y 0,13 · 0,18 · 0,25 |
| Luces | `amber`, `cyan`, `heat`, `green`, `red` | Solo en señales; siempre con forma o texto |
| Suelo de Plutón | tolinas | 0,05 · 0,033 · 0,028 a 0,13 · 0,085 · 0,065 |
| Hielo de Plutón | hielo | 0,62 · 0,72 · 0,86 |
| Suelo de Marte | óxidos | 0,17 · 0,056 · 0,025 a 0,42 · 0,16 · 0,07 |

## Materiales y escala

Una repetición de textura cubre siempre los mismos metros. Las UV se generan en metros, nunca por pieza.

| Capa | Metros por repetición | Respuesta |
| --- | --- | --- |
| Metal pintado | 2 | Dieléctrico; en aristas y rozaduras aparece acero |
| Acero | 1,5 | Metálico, cepillado, rugosidad 0,3-0,55 |
| Goma | 0,8 | Mate, poro fino |
| Roca | 4 | Estratos horizontales, grietas |
| Hielo | 4 | Rugosidad baja con grietas oscuras |
| Regolito | 3 | Mate, grano y terrones |
| Peligro | 1,6 | Franjas diagonales, pintura desgastada |
| Chapa lagrimada | 1,2 | Metálico con relieve |
| Óxido | 2,5 | Rugoso, con picaduras |

Polvo: el material compartido lo acumula abajo y en caras que miran arriba; su color cambia por planeta.

## Escala humana

Celda 2 m. Ojo a 1,62 m. Barandilla 0,9 m, escalón de zapata 0,25 m, boca de cinta 0,82 × 0,58 m a 0,62 m del suelo. Cada máquina cabe en su huella.

## Jerarquía de una máquina

1. **Silueta:** la función se adivina sin textura: torre, horno con chimenea, nave con portón, parábola.
2. **Bocas:** salida al frente (+Z) con flecha y remate naranja, entrada atrás (−Z) con remate claro.
3. **Parte móvil:** gira o se desplaza solo si está en marcha.
4. **Estado:** piloto con color e icono de forma propia sobre la máquina.
5. **Detalle:** costuras, tubos, rejillas, escalas. Solo si se ve a 10 m.

## Presupuestos

Los triángulos por modelo son objetivo de diseño, no medidos uno a uno; los dibujos y tiempos sí están medidos.

| Elemento | Presupuesto |
| --- | --- |
| Máquina | ≤ 8000 triángulos; 1 superficie opaca y, si tiene vidrio, otra |
| Objeto en cinta | ≤ 300 triángulos, en `MultiMesh` |
| Escombro | ≤ 3000 triángulos, 3 variantes compartidas |
| Guijarros | Bloques de 48 m, hasta 180 instancias por bloque, visibles a 95 m, sin sombra |
| Rocas exteriores | 260 instancias en 3 `MultiMesh`, fuera de la zona edificable |
| Luces de máquina | Sin sombra, alcance 7 m, se apagan a 46 m |
| Polvo | 90 partículas (30 con «Reducir efectos») |
| Hitos de Plutón | 4 mallas, ≤ 8000 triángulos cada una, fuera de los muros y sin colisión |
| Actividad de fábrica | 8 emisores de 7 partículas, 4 focos, 6 voces de máquina, 2 de cinta y un fondo, repartidos entre lo más cercano. No crece con la fábrica |
| Terreno | Paso de 2 m hasta ±208 m; después, paso creciente |
| Texturas | 9 capas de 512 px; 2 atlas comprimidos y 1 mapa de variación |

Objetivo de frame: media ≤ 16,7 ms y p99 ≤ 33 ms a 1080p en el escenario medio. Medidas en `PERFORMANCE.md`.

## Terreno y construcción

La colisión sigue siendo el plano `y = 0`. Dentro de la zona jugable el relieve va de 0 a −0,19 m, solo hacia abajo: nada construido queda enterrado y las zapatas tienen faldón. Las normales exageran esa ondulación para que se vea. El relieve grande empieza fuera de los muros (±188 m). Los guijarros de la zona edificable son más bajos que una zapata.

## Zona inicial de Plutón

Cuatro hitos, uno por dirección, reconocibles por silueta y color (`Terrain.Landmarks`, `ModelsLandmarks.cs`): al norte la torre de antena roja y blanca con baliza; al este la grúa amarilla derribada; al oeste la cúpula clara con ventanas ámbar; al sur el pecio azul con la deriva naranja. Una pista apisonada con dos rodadas va de la base a cada uno: se pinta en los canales G y B del color de vértice del terreno, no cambia la colisión ni ocupa celdas.

De cerca, a menos de 8 m, el material industrial y el bastidor de las cintas añaden una segunda escala de textura. Lo que apoya en el suelo usa `soil`, del mismo tono que el suelo de Plutón.

## Interfaz

Tokens en `UiTheme.cs`: espaciado 4, 8, 14, 22; texto 13, 15, 17, 20, 26. Fuente predeterminada de Godot. Hover, pulsado y selección añaden borde izquierdo además de color; foco con contorno ámbar; deshabilitado con texto apagado. Un estado nunca se comunica solo con color: lleva icono o texto. Sin botones decorativos.

## Cómo regenerar

```powershell
dotnet run --project tools/TexGen -c Release -- (Get-Location).Path
```

Sobrescribe PNG e `.import` en `src/Game/Assets/`. El resultado es determinista.
